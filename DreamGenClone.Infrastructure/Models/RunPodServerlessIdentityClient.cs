using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// RunPod Serverless identity-conditioned image client for the official
/// <c>runpod/worker-comfyui</c> contract. Same workflow builders as
/// <see cref="ComfyUIIdentityConditionedClient"/> (IP-Adapter / PuLID single-actor, or the
/// multi-character chained IP-Adapter with regional masks), but the reference/mask images travel
/// inline as base64 in <c>input.images</c> instead of a pod-only <c>/upload/image</c> POST. Results
/// come back base64 in the job output — there are no <c>/prompt</c>, <c>/history</c> or
/// <c>/view</c> endpoints. The provider <c>BaseUrl</c> must be
/// <c>https://api.runpod.ai/v2/{endpointId}</c> and the API key is the RunPod API key (Bearer,
/// resolved via Model Manager's encrypted store or the git-ignored ModelManagerSecrets).
/// </summary>
public sealed class RunPodServerlessIdentityClient : IIdentityConditionedImageClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<RunPodServerlessIdentityClient> _logger;

    public RunPodServerlessIdentityClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<RunPodServerlessIdentityClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<byte[]> GenerateAsync(
        ResolvedIdentityImageModel model,
        IdentityControlledImageRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var baseUrl = model.ProviderBaseUrl.TrimEnd('/');

        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);

            if (!string.IsNullOrEmpty(model.ApiKeyEncrypted))
            {
                var decryptedKey = _encryptionService.Decrypt(model.ApiKeyEncrypted);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedKey);
            }

            if (!LooksLikeFilename(model.ModelIdentifier))
            {
                throw new ImageGenerationException(
                    $"Model identifier '{model.ModelIdentifier}' is not a ComfyUI checkpoint filename. Configure the checkpoint filename as the model identifier in Model Manager.",
                    model.ProviderName,
                    reasonCode: "invalid_checkpoint_identifier");
            }

            // Build the workflow with deterministic image names, then attach the actual bytes to
            // input.images under those same names. The worker-comfyui handler intercepts LoadImage /
            // LoadImageMask and substitutes the matching base64 entries.
            var isMulti = request.References.Count > 1;
            if (isMulti && model.Mechanism != SceneImageIdentityMechanism.IpAdapter)
            {
                throw new ImageGenerationException(
                    $"Identity mechanism '{model.Mechanism}' does not support multi-character rendering; use IP-Adapter.",
                    model.ProviderName, reasonCode: "unsupported_identity_mechanism_multi");
            }

            var images = new JsonArray();
            JsonObject workflow;
            if (isMulti)
            {
                var (width, height) = ComfyUIIdentityConditionedClient.ParseSize(request.Size);
                var named = new List<(string ReferenceName, string MaskName, IdentityReferenceInput Reference)>();
                for (var i = 0; i < request.References.Count; i++)
                {
                    var reference = request.References[i];
                    var refName = $"ref-{i}.png";
                    var maskBytes = ComfyUIIdentityConditionedClient.ResolveMaskBytes(reference, width, height);
                    var maskName = $"mask-{i}.png";
                    AddImage(images, refName, reference.ReferenceImageBytes);
                    AddImage(images, maskName, maskBytes);
                    named.Add((refName, maskName, reference));
                }

                workflow = ComfyUIIdentityConditionedClient.BuildMultiIpAdapterWorkflow(
                    model.ModelIdentifier, model.AdapterRef, named, request, model.IdentityStrength);
            }
            else
            {
                var referenceBytes = request.References.Count == 1
                    ? request.References[0].ReferenceImageBytes
                    : request.ReferenceImageBytes;
                const string refName = "ref-0.png";
                AddImage(images, refName, referenceBytes);

                workflow = model.Mechanism switch
                {
                    SceneImageIdentityMechanism.IpAdapter => ComfyUIIdentityConditionedClient.BuildIpAdapterWorkflow(
                        model.ModelIdentifier, model.AdapterRef, refName, request, model.IdentityStrength),
                    SceneImageIdentityMechanism.PuLid => ComfyUIIdentityConditionedClient.BuildPuLidWorkflow(
                        model.ModelIdentifier, model.AdapterRef, refName, request, model.IdentityStrength),
                    _ => throw new ImageGenerationException(
                        $"Unsupported identity mechanism '{model.Mechanism}'.", model.ProviderName, reasonCode: "unsupported_identity_mechanism")
                };
            }

            var payload = new JsonObject
            {
                ["input"] = new JsonObject
                {
                    ["workflow"] = workflow,
                    ["images"] = images
                }
            };

            _logger.LogInformation(
                "RunPod serverless identity generation start: Provider={ProviderName}, Checkpoint={Checkpoint}, Mechanism={Mechanism}, References={ReferenceCount}",
                model.ProviderName, model.ModelIdentifier, model.Mechanism, request.References.Count > 1 ? request.References.Count : 1);

            using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/run", payload, cancellationToken);
            if (!submitResponse.IsSuccessStatusCode)
            {
                var errorContent = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new ImageGenerationException(
                    $"RunPod serverless identity submit failed: {(int)submitResponse.StatusCode} {errorContent}",
                    model.ProviderName, (int)submitResponse.StatusCode, "runpod_identity_submit_failed");
            }

            var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var jobId = submitBody?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(jobId))
            {
                throw new ImageGenerationException(
                    "RunPod serverless returned no job id.", model.ProviderName, reasonCode: "runpod_no_job_id");
            }

            var deadline = DateTime.UtcNow.AddSeconds(model.ProviderTimeoutSeconds);
            JsonObject? statusObj = null;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(5000, cancellationToken);
                using var statusResponse = await client.GetAsync($"{baseUrl}/status/{jobId}", cancellationToken);
                if (!statusResponse.IsSuccessStatusCode)
                {
                    continue;
                }
                var st = await statusResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
                var status = st?["status"]?.GetValue<string>();
                if (status is "COMPLETED" or "FAILED" or "CANCELLED" or "TIMED_OUT")
                {
                    statusObj = st;
                    break;
                }
            }

            if (statusObj is null)
            {
                throw new ImageGenerationException(
                    $"RunPod serverless identity job {jobId} timed out after {model.ProviderTimeoutSeconds}s.",
                    model.ProviderName, reasonCode: "runpod_timeout");
            }

            var finalStatus = statusObj["status"]?.GetValue<string>();
            if (finalStatus != "COMPLETED")
            {
                var detail = statusObj["error"]?.ToJsonString()
                             ?? statusObj["output"]?.ToJsonString()
                             ?? finalStatus;
                throw new ImageGenerationException(
                    $"RunPod serverless identity job {jobId} {finalStatus}: {detail}",
                    model.ProviderName, reasonCode: "runpod_identity_job_failed");
            }

            string? b64 = null;
            if (statusObj["output"] is JsonObject output && output["images"] is JsonArray outImages)
            {
                foreach (var node in outImages)
                {
                    if (node is JsonObject img
                        && img["data"] is not null
                        && img["type"]?.GetValue<string>() == "base64")
                    {
                        b64 = img["data"]!.GetValue<string>();
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(b64))
            {
                throw new ImageGenerationException(
                    $"RunPod serverless identity job {jobId} produced no output image.",
                    model.ProviderName, reasonCode: "runpod_no_output");
            }

            if (b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                b64 = b64[(b64.IndexOf(',') + 1)..];
            }
            var bytes = Convert.FromBase64String(b64);

            stopwatch.Stop();
            _logger.LogInformation(
                "RunPod serverless identity generation completed: Provider={ProviderName}, Bytes={Bytes}, JobId={JobId}, DurationMs={DurationMs}",
                model.ProviderName, bytes.Length, jobId, stopwatch.ElapsedMilliseconds);
            return bytes;
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "RunPod serverless identity generation failed for provider {ProviderName} after {DurationMs}ms",
                model.ProviderName, stopwatch.ElapsedMilliseconds);
            throw new ImageGenerationException(
                $"RunPod serverless identity generation failed: {ex.Message}", model.ProviderName, reasonCode: "client_error", inner: ex);
        }
    }

    private static void AddImage(JsonArray images, string name, byte[] bytes)
    {
        images.Add(new JsonObject
        {
            ["name"] = name,
            ["image"] = Convert.ToBase64String(bytes)
        });
    }

    private static bool LooksLikeFilename(string identifier)
    {
        return identifier.Contains('.', StringComparison.Ordinal)
               && !identifier.Contains('/', StringComparison.Ordinal)
               && !identifier.Contains('\\', StringComparison.Ordinal)
               && !identifier.Contains(' ', StringComparison.Ordinal);
    }
}
