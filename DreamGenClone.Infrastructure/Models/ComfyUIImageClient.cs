using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// ComfyUI image generation client. Implements <see cref="IImageGenerationClient"/> using the
/// ComfyUI HTTP API: POST a workflow to <c>/prompt</c>, poll <c>/history/{prompt_id}</c> until
/// success, then fetch the produced PNG via <c>/view</c>.
/// </summary>
public sealed class ComfyUIImageClient : IImageGenerationClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ComfyUIImageClient> _logger;

    public ComfyUIImageClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<ComfyUIImageClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>
    /// Default PonyV6 text-to-image workflow (node ids 4/10/6/7/3/5/8/9). The checkpoint name and
    /// positive/negative prompts are injected at call time. CLIP skip 2 via CLIPSetLastLayer.
    /// When <paramref name="seed"/> is provided the KSampler uses that fixed seed (reproducible);
    /// otherwise it draws a random seed each call.
    /// </summary>
    internal static JsonObject BuildDefaultWorkflow(string checkpointName, string prompt, string negative, string? size, long? seed)
    {
        var (width, height) = ParseSize(size);
        var wf = new JsonObject
        {
            ["4"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = checkpointName }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "CLIPSetLastLayer",
                ["inputs"] = new JsonObject { ["stop_at_clip_layer"] = -2, ["clip"] = new JsonArray("4", 1) }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = prompt, ["clip"] = new JsonArray("10", 0) }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "CLIPTextEncode",
                ["inputs"] = new JsonObject { ["text"] = negative, ["clip"] = new JsonArray("10", 0) }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["seed"] = seed ?? Random.Shared.Next(0, int.MaxValue),
                    ["steps"] = 25,
                    ["cfg"] = 7.0,
                    ["sampler_name"] = "euler_ancestral",
                    ["scheduler"] = "normal",
                    ["denoise"] = 1.0,
                    ["model"] = new JsonArray("4", 0),
                    ["positive"] = new JsonArray("6", 0),
                    ["negative"] = new JsonArray("7", 0),
                    ["latent_image"] = new JsonArray("5", 0)
                }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "EmptyLatentImage",
                ["inputs"] = new JsonObject { ["width"] = width, ["height"] = height, ["batch_size"] = 1 }
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("4", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["filename_prefix"] = "dreamgen_app", ["images"] = new JsonArray("8", 0) }
            }
        };
        return wf;
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

    public async Task<byte[]?> GenerateAsync(
        ResolvedImageModel model,
        string prompt,
        string? size,
        string? negativePrompt = null,
        long? seed = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var baseUrl = (model.ComfyUiUrl ?? model.ProviderBaseUrl).TrimEnd('/');

        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);

            if (!string.IsNullOrEmpty(model.ApiKeyEncrypted))
            {
                var decryptedKey = _encryptionService.Decrypt(model.ApiKeyEncrypted);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", decryptedKey);
            }

            // Checkpoint name: use the model identifier if it looks like a filename, else default.
            var checkpoint = LooksLikeFilename(model.ModelIdentifier)
                ? model.ModelIdentifier
                : "ponyDiffusionV6XL_v6.safetensors";

            // Use the deterministic per-scene negative when provided; otherwise the baseline guard.
            // Order matters: anti-duplication reset token must be near the start so the encoder
            // attends to it strongly. Matches the reference PonyV6 negative.
            var effectiveNegative = string.IsNullOrWhiteSpace(negativePrompt)
                ? "extra penis, multiple penises, two penises, duplicate anatomy, blurry, low quality, ugly, deformed, extra limbs, bad anatomy, watermark, text, censored, mosaic, airbrushed, plastic skin"
                : negativePrompt.Trim();

            var workflow = BuildDefaultWorkflow(checkpoint, prompt, effectiveNegative, size, seed);

            var payload = new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = "dreamgen-app"
            };

            _logger.LogInformation(
                "ComfyUI image generation start: Provider={ProviderName}, Checkpoint={Checkpoint}, PromptChars={PromptChars}",
                model.ProviderName, checkpoint, prompt.Length);

            using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/prompt", payload, cancellationToken);
            if (!submitResponse.IsSuccessStatusCode)
            {
                var errorContent = await submitResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new ImageGenerationException(
                    $"ComfyUI prompt submission failed: {(int)submitResponse.StatusCode} {errorContent}",
                    model.ProviderName, (int)submitResponse.StatusCode, "comfyui_submit_failed");
            }

            var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var promptId = submitBody?["prompt_id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(promptId))
            {
                throw new ImageGenerationException(
                    "ComfyUI returned no prompt_id.", model.ProviderName, reasonCode: "comfyui_no_prompt_id");
            }

            // Poll /history/{promptId} until success/error or timeout.
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
                    $"ComfyUI timed out waiting for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_timeout");
            }

            var status = historyEntry["status"]?["status_str"]?.GetValue<string>();
            if (status == "error")
            {
                throw new ImageGenerationException(
                    $"ComfyUI workflow error for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_error");
            }
            if (status != "success")
            {
                throw new ImageGenerationException(
                    $"ComfyUI unexpected status '{status}' for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_status");
            }

            // Extract first output image filename.
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
                    $"ComfyUI produced no output image for prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_no_output");
            }

            var query = $"filename={Uri.EscapeDataString(filename)}";
            if (!string.IsNullOrEmpty(subfolder)) query += $"&subfolder={Uri.EscapeDataString(subfolder)}";
            if (!string.IsNullOrEmpty(type)) query += $"&type={Uri.EscapeDataString(type)}";

            using var viewResponse = await client.GetAsync($"{baseUrl}/view?{query}", cancellationToken);
            if (!viewResponse.IsSuccessStatusCode)
            {
                throw new ImageGenerationException(
                    $"ComfyUI view failed: {(int)viewResponse.StatusCode}", model.ProviderName, (int)viewResponse.StatusCode, "comfyui_view_failed");
            }

            var bytes = await viewResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation(
                "ComfyUI image generation completed: Provider={ProviderName}, Bytes={Bytes}, DurationMs={DurationMs}",
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
            _logger.LogError(ex, "ComfyUI image generation failed for provider {ProviderName} after {DurationMs}ms",
                model.ProviderName, stopwatch.ElapsedMilliseconds);
            throw new ImageGenerationException($"ComfyUI image generation failed: {ex.Message}", model.ProviderName, reasonCode: "client_error", inner: ex);
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
            using var response = await client.GetAsync($"{baseUrl}/system_stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (true, "ComfyUI is reachable and responding.");
            }
            return (false, $"ComfyUI health check failed: HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"ComfyUI health check failed: {ex.Message}");
        }
    }

    private static bool LooksLikeFilename(string value)
        => value.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase)
           || value.EndsWith(".ckpt", StringComparison.OrdinalIgnoreCase);
}
