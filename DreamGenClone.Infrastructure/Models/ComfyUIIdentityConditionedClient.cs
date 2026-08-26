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
/// ComfyUI identity-conditioned image client. Implements <see cref="IIdentityConditionedImageClient"/>
/// for the single-actor identity path: uploads the reference image, compiles the pinned IP-Adapter or
/// PuLID workflow, submits, polls, and returns the produced PNG. Separate from the prompt-only
/// <see cref="ComfyUIImageClient"/> — no silent fallback between them.
/// </summary>
public sealed class ComfyUIIdentityConditionedClient : IIdentityConditionedImageClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ComfyUIIdentityConditionedClient> _logger;

    public ComfyUIIdentityConditionedClient(
        IHttpClientFactory httpClientFactory,
        ILogger<ComfyUIIdentityConditionedClient> logger)
    {
        _httpClientFactory = httpClientFactory;
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

            var referenceName = await UploadReferenceImageAsync(client, baseUrl, request, cancellationToken);

            var workflow = model.Mechanism switch
            {
                SceneImageIdentityMechanism.IpAdapter => BuildIpAdapterWorkflow(
                    model.ModelIdentifier, model.AdapterRef, referenceName, request, model.IdentityStrength),
                SceneImageIdentityMechanism.PuLid => BuildPuLidWorkflow(
                    model.ModelIdentifier, model.AdapterRef, referenceName, request, model.IdentityStrength),
                _ => throw new ImageGenerationException(
                    $"Unsupported identity mechanism '{model.Mechanism}'.", model.ProviderName, reasonCode: "unsupported_identity_mechanism")
            };

            var payload = new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = $"dreamgen-identity-{request.CorrelationId}"
            };

            _logger.LogInformation(
                "ComfyUI identity generation start: Provider={ProviderName}, Checkpoint={Checkpoint}, Mechanism={Mechanism}, Reference={Reference}",
                model.ProviderName, model.ModelIdentifier, model.Mechanism, referenceName);

            using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/prompt", payload, cancellationToken);
            if (!submitResponse.IsSuccessStatusCode)
            {
                var errorContent = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new ImageGenerationException(
                    $"ComfyUI identity prompt submission failed: {(int)submitResponse.StatusCode} {errorContent}",
                    model.ProviderName, (int)submitResponse.StatusCode, "comfyui_identity_submit_failed");
            }

            var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var promptId = submitBody?["prompt_id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(promptId))
            {
                throw new ImageGenerationException(
                    "ComfyUI returned no prompt_id for identity render.", model.ProviderName, reasonCode: "comfyui_no_prompt_id");
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
                    $"ComfyUI timed out waiting for identity prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_timeout");
            }

            var status = historyEntry["status"]?["status_str"]?.GetValue<string>();
            if (status != "success")
            {
                throw new ImageGenerationException(
                    $"ComfyUI identity workflow '{status}' for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_identity_error");
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
                    $"ComfyUI produced no output image for identity prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_no_output");
            }

            var query = $"filename={Uri.EscapeDataString(filename)}";
            if (!string.IsNullOrEmpty(subfolder)) query += $"&subfolder={Uri.EscapeDataString(subfolder)}";
            if (!string.IsNullOrEmpty(type)) query += $"&type={Uri.EscapeDataString(type)}";

            using var viewResponse = await client.GetAsync($"{baseUrl}/view?{query}", cancellationToken);
            if (!viewResponse.IsSuccessStatusCode)
            {
                throw new ImageGenerationException(
                    $"ComfyUI identity view failed: {(int)viewResponse.StatusCode}", model.ProviderName, (int)viewResponse.StatusCode, "comfyui_view_failed");
            }

            var bytes = await viewResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation(
                "ComfyUI identity generation completed: Provider={ProviderName}, Bytes={Bytes}, DurationMs={DurationMs}",
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
            _logger.LogError(ex, "ComfyUI identity generation failed for provider {ProviderName} after {DurationMs}ms",
                model.ProviderName, stopwatch.ElapsedMilliseconds);
            throw new ImageGenerationException($"ComfyUI identity generation failed: {ex.Message}", model.ProviderName, reasonCode: "client_error", inner: ex);
        }
    }

    private async Task<string> UploadReferenceImageAsync(
        HttpClient client,
        string baseUrl,
        IdentityControlledImageRequest request,
        CancellationToken cancellationToken)
    {
        var referenceName = $"identity-ref-{request.CorrelationId}.png";

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(request.ReferenceImageBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "image", referenceName);

        using var uploadResponse = await client.PostAsync($"{baseUrl}/upload/image", content, cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var errorContent = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new ImageGenerationException(
                $"ComfyUI reference image upload failed: {(int)uploadResponse.StatusCode} {errorContent}",
                "ComfyUI", (int)uploadResponse.StatusCode, "comfyui_upload_failed");
        }

        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var name = uploadBody?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
        {
            throw new ImageGenerationException(
                "ComfyUI returned no name for the uploaded reference image.", "ComfyUI", reasonCode: "comfyui_upload_no_name");
        }

        _logger.LogDebug("Uploaded identity reference image {Name}", name);
        return name;
    }

    internal static JsonObject BuildIpAdapterWorkflow(
        string checkpointName,
        string preset,
        string referenceName,
        IdentityControlledImageRequest request,
        double strength)
    {
        var (width, height) = ParseSize(request.Size);
        return new JsonObject
        {
            ["4"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = checkpointName }
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
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "IPAdapterUnifiedLoader",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("4", 0), ["preset"] = preset }
            },
            ["11"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = referenceName }
            },
            ["12"] = new JsonObject
            {
                ["class_type"] = "IPAdapter",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("10", 0),
                    ["ipadapter"] = new JsonArray("10", 1),
                    ["image"] = new JsonArray("11", 0),
                    ["weight"] = strength,
                    ["start_at"] = 0.0,
                    ["end_at"] = 1.0,
                    ["weight_type"] = "standard"
                }
            },
            ["3"] = BuildKSampler("12", request.Seed),
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_identity", ["images"] = new JsonArray("8", 0) }
            }
        };
    }

    internal static JsonObject BuildPuLidWorkflow(
        string checkpointName,
        string pulidFile,
        string referenceName,
        IdentityControlledImageRequest request,
        double strength)
    {
        var (width, height) = ParseSize(request.Size);
        return new JsonObject
        {
            ["4"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = checkpointName }
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
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "PulidModelLoader",
                ["inputs"] = new JsonObject { ["pulid_file"] = pulidFile }
            },
            ["11"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = referenceName }
            },
            ["13"] = new JsonObject
            {
                ["class_type"] = "PulidInsightFaceLoader",
                ["inputs"] = new JsonObject { ["provider"] = "CPU" }
            },
            ["14"] = new JsonObject
            {
                ["class_type"] = "PulidEvaClipLoader",
                ["inputs"] = new JsonObject()
            },
            ["12"] = new JsonObject
            {
                ["class_type"] = "ApplyPulid",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("4", 0),
                    ["pulid"] = new JsonArray("10", 0),
                    ["eva_clip"] = new JsonArray("14", 0),
                    ["face_analysis"] = new JsonArray("13", 0),
                    ["image"] = new JsonArray("11", 0),
                    ["method"] = "fidelity",
                    ["weight"] = strength,
                    ["start_at"] = 0.0,
                    ["end_at"] = 1.0
                }
            },
            ["3"] = BuildKSampler("12", request.Seed),
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_identity", ["images"] = new JsonArray("8", 0) }
            }
        };
    }

    private static JsonObject BuildKSampler(string modelNodeId, long? seed) => new()
    {
        ["class_type"] = "KSampler",
        ["inputs"] = new JsonObject
        {
            ["seed"] = seed ?? Random.Shared.Next(0, int.MaxValue),
            ["steps"] = 30,
            ["cfg"] = 5.0,
            ["sampler_name"] = "dpmpp_2m_sde",
            ["scheduler"] = "karras",
            ["denoise"] = 1.0,
            ["model"] = new JsonArray(modelNodeId, 0),
            ["positive"] = new JsonArray("6", 0),
            ["negative"] = new JsonArray("7", 0),
            ["latent_image"] = new JsonArray("5", 0)
        }
    };

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
