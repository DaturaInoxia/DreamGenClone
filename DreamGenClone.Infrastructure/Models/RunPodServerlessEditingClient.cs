using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// RunPod Serverless source-image editor for the official <c>runpod/worker-comfyui</c> contract.
/// POST <c>{baseUrl}/run</c> with
/// <c>{ "input": { "workflow": &lt;ComfyUI API JSON&gt;, "images": [ { "name", "image" } ] } }</c>,
/// poll <c>/status/{jobId}</c>, then read the base64 <c>output.images[].data</c>. The source image
/// travels inline as a data URI under <c>input.images</c> — serverless has no persistent input dir,
/// so there is no <c>/upload/image</c> (unlike the pod <see cref="ComfyUIImageEditingClient"/>).
/// The edit workflow is the AIO merged-checkpoint graph
/// (<see cref="ComfyUIImageEditingClient.BuildAioMergedCheckpointWorkflow"/>, <c>CheckpointLoaderSimple</c>).
/// </summary>
public sealed class RunPodServerlessEditingClient : IImageEditingClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<RunPodServerlessEditingClient> _logger;

    public RunPodServerlessEditingClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<RunPodServerlessEditingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<byte[]> EditAsync(
        ResolvedImageEditorModel model,
        Stream sourceImage,
        string sourceFileName,
        string instruction,
        CancellationToken cancellationToken = default)
    {
        if (sourceImage is null || !sourceImage.CanRead)
            throw new ImageGenerationException("The source image cannot be read.", model.ProviderName, reasonCode: "source_image_unreadable");
        if (string.IsNullOrWhiteSpace(sourceFileName))
            throw new ImageGenerationException("The source image file name is required.", model.ProviderName, reasonCode: "source_image_name_missing");
        if (string.IsNullOrWhiteSpace(instruction))
            throw new ImageGenerationException("An image edit instruction is required.", model.ProviderName, reasonCode: "instruction_missing");
        if (string.IsNullOrWhiteSpace(model.ApiKeyEncrypted))
            throw new ImageGenerationException(
                $"RunPod serverless image editor provider '{model.ProviderName}' is missing its API key. Configure it in Model Manager (/model-manager).",
                model.ProviderName, reasonCode: "runpod_editor_missing_key");

        var baseUrl = model.ComfyUiUrl.TrimEnd('/');
        var client = _httpClientFactory.CreateClient("CompletionClient");
        client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _encryptionService.Decrypt(model.ApiKeyEncrypted));

        try
        {
            byte[] sourceBytes;
            using (var buffer = new MemoryStream())
            {
                await sourceImage.CopyToAsync(buffer, cancellationToken);
                sourceBytes = buffer.ToArray();
            }

            var imageName = Path.GetFileName(sourceFileName);
            if (string.IsNullOrWhiteSpace(imageName))
            {
                imageName = "source.png";
            }

            // The AIO serverless worker (Qwen-Rapid-AIO-NSFW-v23) accepts only the merged-checkpoint
            // graph (CheckpointLoaderSimple) — the pod split (UNETLoader+CLIPLoader+VAELoader) fails
            // workflow validation on it. Ported from the proven B-101 implementation.
            var workflow = ComfyUIImageEditingClient.BuildAioMergedCheckpointWorkflow(model, imageName, instruction.Trim());
            var dataUri = $"data:{MimeFor(imageName)};base64,{Convert.ToBase64String(sourceBytes)}";
            var payload = new JsonObject
            {
                ["input"] = new JsonObject
                {
                    ["workflow"] = workflow,
                    ["images"] = new JsonArray(
                        new JsonObject { ["name"] = imageName, ["image"] = dataUri })
                }
            };

            _logger.LogInformation(
                "RunPod serverless source-image edit start: Provider={ProviderName}, DiffusionModel={DiffusionModel}, SourceBytes={SourceBytes}, InstructionChars={InstructionChars}",
                model.ProviderName, model.DiffusionModel, sourceBytes.Length, instruction.Length);

            using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/run", payload, cancellationToken);
            if (!submitResponse.IsSuccessStatusCode)
            {
                var errorContent = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new ImageGenerationException(
                    $"RunPod serverless edit submit failed: {(int)submitResponse.StatusCode} {errorContent}",
                    model.ProviderName, (int)submitResponse.StatusCode, "runpod_edit_submit_failed");
            }

            var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var jobId = submitBody?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(jobId))
            {
                throw new ImageGenerationException(
                    "RunPod serverless returned no job id for the image edit.", model.ProviderName, reasonCode: "runpod_edit_no_job_id");
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
                    $"RunPod serverless edit job {jobId} timed out after {model.ProviderTimeoutSeconds}s.",
                    model.ProviderName, reasonCode: "runpod_edit_timeout");
            }

            var finalStatus = statusObj["status"]?.GetValue<string>();
            if (finalStatus != "COMPLETED")
            {
                var detail = statusObj["error"]?.ToJsonString() ?? statusObj["output"]?.ToJsonString() ?? finalStatus;
                throw new ImageGenerationException(
                    $"RunPod serverless edit job {jobId} {finalStatus}: {detail}",
                    model.ProviderName, reasonCode: "runpod_edit_job_failed");
            }

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
                    $"RunPod serverless edit job {jobId} produced no output image.",
                    model.ProviderName, reasonCode: "runpod_edit_no_output");
            }

            if (b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                b64 = b64[(b64.IndexOf(',') + 1)..];
            }

            var bytes = Convert.FromBase64String(b64);
            _logger.LogInformation(
                "RunPod serverless source-image edit completed: Provider={ProviderName}, Bytes={Bytes}, JobId={JobId}",
                model.ProviderName, bytes.Length, jobId);
            return bytes;
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunPod serverless source-image edit failed: Provider={ProviderName}", model.ProviderName);
            throw new ImageGenerationException(
                $"RunPod serverless source-image edit failed: {ex.Message}", model.ProviderName, reasonCode: "runpod_edit_client_error", inner: ex);
        }
    }

    private static string MimeFor(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }
}
