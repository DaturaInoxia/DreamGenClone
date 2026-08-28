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
/// RunPod Serverless image generation client for the official <c>runpod/worker-comfyui</c> contract:
/// POST <c>https://api.runpod.ai/v2/{endpointId}/run</c> with
/// <c>{ "input": { "workflow": &lt;ComfyUI API JSON&gt; } }</c>, poll <c>/status/{jobId}</c>, then read
/// the base64 <c>output.images[].data</c>. There are no <c>/prompt</c>, <c>/history</c>,
/// <c>/view</c> or <c>/upload/image</c> endpoints — results come back inline (base64) in the job
/// output. The provider <c>BaseUrl</c> must be <c>https://api.runpod.ai/v2/{endpointId}</c> and the
/// API key is the RunPod API key (Bearer, resolved via Model Manager's encrypted store or the
/// git-ignored ModelManagerSecrets).
/// </summary>
public sealed class RunPodServerlessImageClient : IImageGenerationClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<RunPodServerlessImageClient> _logger;

    public RunPodServerlessImageClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<RunPodServerlessImageClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<byte[]?> GenerateAsync(
        ResolvedImageModel model,
        string prompt,
        string? size,
        string? negativePrompt = null,
        long? seed = null,
        CancellationToken cancellationToken = default,
        SceneImageGenerationOptions? options = null)
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

            // Checkpoint name: the model identifier is used verbatim when it looks like a ComfyUI
            // checkpoint filename. Any other identifier is a misconfiguration — fail fast, never a
            // silent default model.
            if (!LooksLikeFilename(model.ModelIdentifier))
            {
                throw new ImageGenerationException(
                    $"Model identifier '{model.ModelIdentifier}' is not a ComfyUI checkpoint filename. Configure the checkpoint filename as the model identifier in Model Manager.",
                    model.ProviderName,
                    reasonCode: "invalid_checkpoint_identifier");
            }

            var checkpoint = model.ModelIdentifier;

            // Model-family aware baseline negative: SDXL/Juggernaut needs a heavier guard set than
            // Pony. When a deterministic per-scene negative is provided it takes precedence.
            var family = SceneImageModelFamilyResolver.Classify(checkpoint);
            var baselineNegative = family == SceneImageModelFamily.Sdxl
                ? "deformed, bad anatomy, extra limbs, extra legs, four legs, fused legs, extra fingers, extra arms, missing limbs, malformed hands, malformed feet, blurry genitals, featureless genitals, censored, cartoon, anime, illustration, painting, sketch, watermark, text, low quality, oversaturated, plastic skin"
                : "extra penis, multiple penises, two penises, duplicate anatomy, blurry, low quality, ugly, deformed, extra limbs, bad anatomy, watermark, text, censored, mosaic, airbrushed, plastic skin";
            var effectiveNegative = string.IsNullOrWhiteSpace(negativePrompt)
                ? baselineNegative
                : negativePrompt.Trim();

            // Reuse the same workflow builders as the pod ComfyUI client (same assembly).
            var workflow = family switch
            {
                SceneImageModelFamily.Pony => ComfyUIImageClient.BuildDefaultWorkflow(checkpoint, prompt, effectiveNegative, size, seed),
                SceneImageModelFamily.Sdxl => ComfyUIImageClient.BuildSdxlWorkflow(checkpoint, prompt, effectiveNegative, size, seed, options),
                _ => throw new ImageGenerationException(
                    $"Unsupported scene-image checkpoint '{checkpoint}'. Register a Pony or SDXL/Juggernaut model in Model Manager.",
                    model.ProviderName,
                    reasonCode: "unsupported_checkpoint")
            };

            var payload = new JsonObject
            {
                ["input"] = new JsonObject { ["workflow"] = workflow }
            };

            _logger.LogInformation(
                "RunPod serverless image generation start: Provider={ProviderName}, Checkpoint={Checkpoint}, PromptChars={PromptChars}",
                model.ProviderName, checkpoint, prompt.Length);

            // Submit asynchronously (official worker-comfyui contract).
            using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/run", payload, cancellationToken);
            if (!submitResponse.IsSuccessStatusCode)
            {
                var errorContent = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new ImageGenerationException(
                    $"RunPod serverless submit failed: {(int)submitResponse.StatusCode} {errorContent}",
                    model.ProviderName, (int)submitResponse.StatusCode, "runpod_submit_failed");
            }

            var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var jobId = submitBody?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(jobId))
            {
                throw new ImageGenerationException(
                    "RunPod serverless returned no job id.", model.ProviderName, reasonCode: "runpod_no_job_id");
            }

            // Poll /status/{jobId} until a terminal state or the provider timeout elapses.
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
                    $"RunPod serverless job {jobId} timed out after {model.ProviderTimeoutSeconds}s.",
                    model.ProviderName, reasonCode: "runpod_timeout");
            }

            var finalStatus = statusObj["status"]?.GetValue<string>();
            if (finalStatus != "COMPLETED")
            {
                var detail = statusObj["error"]?.ToJsonString()
                             ?? statusObj["output"]?.ToJsonString()
                             ?? finalStatus;
                throw new ImageGenerationException(
                    $"RunPod serverless job {jobId} {finalStatus}: {detail}",
                    model.ProviderName, reasonCode: "runpod_job_failed");
            }

            // Extract the first base64 image from output.images[].
            string? b64 = null;
            if (statusObj["output"] is JsonObject output && output["images"] is JsonArray images)
            {
                foreach (var node in images)
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
                    $"RunPod serverless job {jobId} produced no output image.",
                    model.ProviderName, reasonCode: "runpod_no_output");
            }

            if (b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                b64 = b64[(b64.IndexOf(',') + 1)..];
            }
            var bytes = Convert.FromBase64String(b64);

            stopwatch.Stop();
            _logger.LogInformation(
                "RunPod serverless image generation completed: Provider={ProviderName}, Bytes={Bytes}, JobId={JobId}, DurationMs={DurationMs}",
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
            _logger.LogError(ex, "RunPod serverless image generation failed for provider {ProviderName} after {DurationMs}ms",
                model.ProviderName, stopwatch.ElapsedMilliseconds);
            throw new ImageGenerationException(
                $"RunPod serverless image generation failed: {ex.Message}", model.ProviderName, reasonCode: "client_error", inner: ex);
        }
    }

    public async Task<(bool Success, string Message)> CheckImageModelHealthAsync(
        string providerBaseUrl,
        string imageGenerationPath,
        int timeoutSeconds,
        string? decryptedApiKey,
        string modelIdentifier,
        ImageContentPolicy contentPolicy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            if (!string.IsNullOrEmpty(decryptedApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedApiKey);
            }

            var baseUrl = providerBaseUrl.TrimEnd('/');
            using var response = await client.GetAsync($"{baseUrl}/health", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, "RunPod serverless endpoint is reachable and healthy.");
            }
            return (false, $"RunPod serverless health check failed: HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"RunPod serverless health check failed: {ex.Message}");
        }
    }

    private static bool LooksLikeFilename(string value)
        => value.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)
           || value.EndsWith(".ckpt", StringComparison.OrdinalIgnoreCase);
}
