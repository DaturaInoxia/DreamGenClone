using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// ComfyUI pose-conditioned image client. Implements <see cref="IPoseConditionedImageClient"/> for
/// the local ComfyUI path: uploads the pose skeleton image, compiles the pinned OpenPose ControlNet
/// (OpenPoseXL2) workflow against the configured checkpoint, submits, polls, and returns the produced
/// PNG. Separate from <see cref="ComfyUIImageClient"/> (prompt-only) and
/// <see cref="ComfyUIIdentityConditionedClient"/> (identity) — no silent fallback between them.
/// Node-graph template mirrors the proven pose proof workflow
/// <c>helpers/runpod/workflows/juggernaut-fellatio-openpose.json</c>.
/// </summary>
public sealed class ComfyUIPoseConditionedImageClient : IPoseConditionedImageClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ComfyUIPoseConditionedImageClient> _logger;

    public ComfyUIPoseConditionedImageClient(
        IHttpClientFactory httpClientFactory,
        ILogger<ComfyUIPoseConditionedImageClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<byte[]> GenerateAsync(
        ResolvedPoseImageModel model,
        PoseConditionedImageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Strength is <= 0 or > 1)
        {
            throw new ImageGenerationException(
                $"Pose ControlNet strength must be in (0, 1], but was {request.Strength}.",
                model.ProviderName,
                reasonCode: "pose_strength_out_of_range");
        }
        if (request.PoseImageBytes.Length == 0)
        {
            throw new ImageGenerationException(
                "Pose-conditioned render requires pose image bytes, but none were supplied.",
                model.ProviderName,
                reasonCode: "pose_image_missing");
        }
        if (string.IsNullOrWhiteSpace(model.ControlNetAdapterRef))
        {
            throw new ImageGenerationException(
                "Pose-conditioned render requires a configured ControlNet adapter reference.",
                model.ProviderName,
                reasonCode: "pose_adapter_missing");
        }

        var stopwatch = Stopwatch.StartNew();
        var baseUrl = model.ProviderBaseUrl.TrimEnd('/');

        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);

            var referenceName = await UploadPoseImageAsync(client, baseUrl, request.PoseImageBytes, request.CorrelationId, cancellationToken);
            var workflow = BuildOpenPoseSdxlWorkflow(
                model.ModelIdentifier,
                model.ControlNetAdapterRef,
                referenceName,
                request);

            var payload = new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = $"dreamgen-pose-{request.CorrelationId}"
            };

            _logger.LogInformation(
                "ComfyUI pose generation start: Provider={ProviderName}, Checkpoint={Checkpoint}, ControlNet={Adapter}, Strength={Strength}",
                model.ProviderName, model.ModelIdentifier, model.ControlNetAdapterRef, request.Strength);

            using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/prompt", payload, cancellationToken);
            if (!submitResponse.IsSuccessStatusCode)
            {
                var errorContent = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new ImageGenerationException(
                    $"ComfyUI pose prompt submission failed: {(int)submitResponse.StatusCode} {errorContent}",
                    model.ProviderName, (int)submitResponse.StatusCode, "comfyui_pose_submit_failed");
            }

            var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var promptId = submitBody?["prompt_id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(promptId))
            {
                throw new ImageGenerationException(
                    "ComfyUI returned no prompt_id for pose render.", model.ProviderName, reasonCode: "comfyui_no_prompt_id");
            }

            var deadline = DateTime.UtcNow.AddSeconds(model.ProviderTimeoutSeconds);
            JsonObject? historyEntry = null;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(2000, cancellationToken);
                using var histResponse = await client.GetAsync($"{baseUrl}/history/{promptId}", cancellationToken);
                if (histResponse.IsSuccessStatusCode)
                {
                    var hist = await histResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
                    if (hist is not null && hist.ContainsKey(promptId))
                    {
                        historyEntry = hist[promptId] as JsonObject;
                        break;
                    }
                }
            }

            if (historyEntry is null)
            {
                throw new ImageGenerationException(
                    $"ComfyUI timed out waiting for pose prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_timeout");
            }

            var status = historyEntry["status"]?["status_str"]?.GetValue<string>();
            if (status != "success")
            {
                throw new ImageGenerationException(
                    $"ComfyUI pose workflow '{status}' for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_pose_error");
            }

            string? filename = null;
            string? subfolder = null;
            string? type = null;
            if (historyEntry["outputs"] is JsonObject outputs)
            {
                foreach (var nodeOut in outputs)
                {
                    if (nodeOut.Value is JsonObject nodeObj && nodeObj["images"] is JsonArray images)
                    {
                        foreach (var imgNode in images)
                        {
                            if (imgNode is JsonObject img)
                            {
                                filename = img["filename"]?.GetValue<string>();
                                subfolder = img["subfolder"]?.GetValue<string>();
                                type = img["type"]?.GetValue<string>();
                                break;
                            }
                        }
                    }
                    if (filename is not null) break;
                }
            }

            if (string.IsNullOrEmpty(filename))
            {
                throw new ImageGenerationException(
                    $"ComfyUI produced no output image for pose prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_no_output");
            }

            var query = $"filename={Uri.EscapeDataString(filename)}";
            if (!string.IsNullOrEmpty(subfolder)) query += $"&subfolder={Uri.EscapeDataString(subfolder)}";
            if (!string.IsNullOrEmpty(type)) query += $"&type={Uri.EscapeDataString(type)}";

            using var viewResponse = await client.GetAsync($"{baseUrl}/view?{query}", cancellationToken);
            if (!viewResponse.IsSuccessStatusCode)
            {
                throw new ImageGenerationException(
                    $"ComfyUI pose view failed: {(int)viewResponse.StatusCode}", model.ProviderName, (int)viewResponse.StatusCode, "comfyui_view_failed");
            }

            var bytes = await viewResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation(
                "ComfyUI pose generation completed: Provider={ProviderName}, Bytes={Bytes}, DurationMs={DurationMs}",
                model.ProviderName, bytes.Length, stopwatch.ElapsedMilliseconds);
            return bytes;
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "ComfyUI pose generation failed for provider {ProviderName} after {DurationMs}ms",
                model.ProviderName, stopwatch.ElapsedMilliseconds);
            throw new ImageGenerationException($"ComfyUI pose generation failed: {ex.Message}", model.ProviderName, reasonCode: "client_error", inner: ex);
        }
    }

    private async Task<string> UploadPoseImageAsync(
        HttpClient client,
        string baseUrl,
        byte[] imageBytes,
        string nameStem,
        CancellationToken cancellationToken)
    {
        var poseName = $"pose_{nameStem}.png";

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "image", poseName);

        using var uploadResponse = await client.PostAsync($"{baseUrl}/upload/image", content, cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var errorContent = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new ImageGenerationException(
                $"ComfyUI pose image upload failed: {(int)uploadResponse.StatusCode} {errorContent}",
                "ComfyUI", (int)uploadResponse.StatusCode, "comfyui_upload_failed");
        }

        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var name = uploadBody?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
        {
            throw new ImageGenerationException(
                "ComfyUI returned no name for the uploaded pose image.", "ComfyUI", reasonCode: "comfyui_upload_no_name");
        }

        _logger.LogDebug("Uploaded pose image {Name}", name);
        return name;
    }

    /// <summary>
    /// SDXL-family OpenPose ControlNet workflow (node ids 4/1/6/7/11/12/5/3/8/9). Mirrors the proven
    /// pose proof graph (<c>juggernaut-fellatio-openpose.json</c>) with the app's SDXL sampler
    /// defaults (DPM++ 2M SDE / karras / 30 steps / CFG 5, no CLIP skip). The checkpoint, ControlNet
    /// weight and pose image name are injected at call time. The OpenPose conditioning is applied
    /// between the CLIP encodings and the sampler (positive/negative outputs of
    /// <c>ControlNetApplyAdvanced</c> feed KSampler).
    /// </summary>
    internal static JsonObject BuildOpenPoseSdxlWorkflow(
        string checkpointName,
        string controlNetRef,
        string poseImageName,
        PoseConditionedImageRequest request)
    {
        var (width, height) = ParseSize(request.Size);
        return new JsonObject
        {
            ["4"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = checkpointName }
            },
            ["1"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = poseImageName }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = request.PositivePrompt, ["clip"] = new JsonArray("4", 1) }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = request.NegativePrompt, ["clip"] = new JsonArray("4", 1) }
            },
            ["11"] = new JsonObject
            {
                ["class_type"] = "ControlNetLoader",
                ["inputs"] = new JsonObject { ["control_net_name"] = controlNetRef }
            },
            ["12"] = new JsonObject
            {
                ["class_type"] = "ControlNetApplyAdvanced",
                ["inputs"] = new JsonObject
                {
                    ["positive"] = new JsonArray("6", 0),
                    ["negative"] = new JsonArray("7", 0),
                    ["control_net"] = new JsonArray("11", 0),
                    ["image"] = new JsonArray("1", 0),
                    ["strength"] = request.Strength,
                    ["start_percent"] = 0.0,
                    ["end_percent"] = 1.0,
                    ["vae"] = new JsonArray("4", 2)
                }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["seed"] = request.Seed ?? Random.Shared.Next(0, int.MaxValue),
                    ["steps"] = 30,
                    ["cfg"] = 5.0,
                    ["sampler_name"] = "dpmpp_2m_sde",
                    ["scheduler"] = "karras",
                    ["denoise"] = 1.0,
                    ["model"] = new JsonArray("4", 0),
                    ["positive"] = new JsonArray("12", 0),
                    ["negative"] = new JsonArray("12", 1),
                    ["latent_image"] = new JsonArray("5", 0)
                }
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_app_pose", ["images"] = new JsonArray("8", 0) }
            }
        };
    }

    private static (int Width, int Height) ParseSize(string? size)
    {
        if (!string.IsNullOrWhiteSpace(size))
        {
            var parts = size.Split('x', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2
                && int.TryParse(parts[0], out var w)
                && int.TryParse(parts[1], out var h)
                && w > 0 && h > 0)
            {
                return (w, h);
            }
        }
        return (1024, 1024);
    }
}
