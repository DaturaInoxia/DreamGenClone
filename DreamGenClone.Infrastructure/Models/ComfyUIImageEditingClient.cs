using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>ComfyUI client for the persisted Qwen source-image editing workflow.</summary>
public sealed class ComfyUIImageEditingClient : IImageEditingClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ComfyUIImageEditingClient> _logger;

    public ComfyUIImageEditingClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<ComfyUIImageEditingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    internal static JsonObject BuildWorkflow(ResolvedImageEditorModel model, string sourceImageName, string instruction)
    {
        return new JsonObject
        {
            ["1"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = sourceImageName }
            },
            ["2"] = new JsonObject
            {
                ["class_type"] = "FluxKontextImageScale",
                ["inputs"] = new JsonObject { ["image"] = new JsonArray("1", 0) }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("14", 0),
                    ["positive"] = new JsonArray("12", 0),
                    ["negative"] = new JsonArray("13", 0),
                    ["latent_image"] = new JsonArray("8", 0),
                    ["seed"] = Random.Shared.NextInt64(long.MaxValue),
                    ["steps"] = model.Steps,
                    ["cfg"] = model.Cfg,
                    ["sampler_name"] = model.Sampler,
                    ["scheduler"] = model.Scheduler,
                    ["denoise"] = model.Denoise
                }
            },
            ["4"] = new JsonObject
            {
                ["class_type"] = "UNETLoader",
                ["inputs"] = new JsonObject { ["unet_name"] = model.DiffusionModel, ["weight_dtype"] = "default" }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "ModelSamplingAuraFlow",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("4", 0), ["shift"] = model.AuraFlowShift }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImageEditPlus",
                ["inputs"] = new JsonObject
                {
                    ["clip"] = new JsonArray("10", 0),
                    ["vae"] = new JsonArray("11", 0),
                    ["image1"] = new JsonArray("2", 0),
                    ["prompt"] = instruction
                }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImageEditPlus",
                ["inputs"] = new JsonObject
                {
                    ["clip"] = new JsonArray("10", 0),
                    ["vae"] = new JsonArray("11", 0),
                    ["image1"] = new JsonArray("2", 0),
                    ["prompt"] = string.Empty
                }
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEEncode",
                ["inputs"] = new JsonObject { ["pixels"] = new JsonArray("2", 0), ["vae"] = new JsonArray("11", 0) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["images"] = new JsonArray("15", 0), ["filename_prefix"] = "dreamgen_app/qwen-edit" }
            },
            ["10"] = new JsonObject
            {
                ["class_type"] = "CLIPLoader",
                ["inputs"] = new JsonObject { ["clip_name"] = model.TextEncoder, ["type"] = "qwen_image", ["device"] = "default" }
            },
            ["11"] = new JsonObject
            {
                ["class_type"] = "VAELoader",
                ["inputs"] = new JsonObject { ["vae_name"] = model.Vae }
            },
            ["12"] = new JsonObject
            {
                ["class_type"] = "FluxKontextMultiReferenceLatentMethod",
                ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray("6", 0), ["reference_latents_method"] = "index_timestep_zero" }
            },
            ["13"] = new JsonObject
            {
                ["class_type"] = "FluxKontextMultiReferenceLatentMethod",
                ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray("7", 0), ["reference_latents_method"] = "index_timestep_zero" }
            },
            ["14"] = new JsonObject
            {
                ["class_type"] = "CFGNorm",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("5", 0), ["strength"] = model.CfgNormStrength }
            },
            ["15"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("11", 0) }
            }
        };
    }

    /// <summary>
    /// Builds the Qwen-Image-Edit workflow for the merged AIO checkpoint
    /// (e.g. <c>Qwen-Rapid-AIO-NSFW-v23.safetensors</c>) which bundles model+clip+vae in one file
    /// (B-101 MODEL DECISION). Uses <c>CheckpointLoaderSimple</c> (model/clip/vae together) instead
    /// of the pod split (<c>UNETLoader</c>+<c>CLIPLoader</c>+<c>VAELoader</c>) — the AIO serverless
    /// worker validates/accepts only this graph. The checkpoint name comes from the resolved
    /// <c>DiffusionModel</c> field; sampler settings come from the resolved model, never hardcoded.
    /// </summary>
    internal static JsonObject BuildAioMergedCheckpointWorkflow(ResolvedImageEditorModel model, string sourceImageName, string instruction)
    {
        return new JsonObject
        {
            ["1"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = sourceImageName }
            },
            ["2"] = new JsonObject
            {
                ["class_type"] = "FluxKontextImageScale",
                ["inputs"] = new JsonObject { ["image"] = new JsonArray("1", 0) }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "KSampler",
                ["inputs"] = new JsonObject
                {
                    ["model"] = new JsonArray("14", 0),
                    ["positive"] = new JsonArray("12", 0),
                    ["negative"] = new JsonArray("13", 0),
                    ["latent_image"] = new JsonArray("8", 0),
                    ["seed"] = Random.Shared.NextInt64(long.MaxValue),
                    ["steps"] = model.Steps,
                    ["cfg"] = model.Cfg,
                    ["sampler_name"] = model.Sampler,
                    ["scheduler"] = model.Scheduler,
                    ["denoise"] = model.Denoise
                }
            },
            ["5"] = new JsonObject
            {
                ["class_type"] = "ModelSamplingAuraFlow",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("16", 0), ["shift"] = model.AuraFlowShift }
            },
            ["6"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImageEditPlus",
                ["inputs"] = new JsonObject
                {
                    ["clip"] = new JsonArray("16", 1),
                    ["vae"] = new JsonArray("16", 2),
                    ["image1"] = new JsonArray("2", 0),
                    ["prompt"] = instruction
                }
            },
            ["7"] = new JsonObject
            {
                ["class_type"] = "TextEncodeQwenImageEditPlus",
                ["inputs"] = new JsonObject
                {
                    ["clip"] = new JsonArray("16", 1),
                    ["vae"] = new JsonArray("16", 2),
                    ["image1"] = new JsonArray("2", 0),
                    ["prompt"] = string.Empty
                }
            },
            ["8"] = new JsonObject
            {
                ["class_type"] = "VAEEncode",
                ["inputs"] = new JsonObject { ["pixels"] = new JsonArray("2", 0), ["vae"] = new JsonArray("16", 2) }
            },
            ["9"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject { ["images"] = new JsonArray("15", 0), ["filename_prefix"] = "dreamgen_app/qwen-edit" }
            },
            ["12"] = new JsonObject
            {
                ["class_type"] = "FluxKontextMultiReferenceLatentMethod",
                ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray("6", 0), ["reference_latents_method"] = "index_timestep_zero" }
            },
            ["13"] = new JsonObject
            {
                ["class_type"] = "FluxKontextMultiReferenceLatentMethod",
                ["inputs"] = new JsonObject { ["conditioning"] = new JsonArray("7", 0), ["reference_latents_method"] = "index_timestep_zero" }
            },
            ["14"] = new JsonObject
            {
                ["class_type"] = "CFGNorm",
                ["inputs"] = new JsonObject { ["model"] = new JsonArray("5", 0), ["strength"] = model.CfgNormStrength }
            },
            ["15"] = new JsonObject
            {
                ["class_type"] = "VAEDecode",
                ["inputs"] = new JsonObject { ["samples"] = new JsonArray("3", 0), ["vae"] = new JsonArray("16", 2) }
            },
            ["16"] = new JsonObject
            {
                ["class_type"] = "CheckpointLoaderSimple",
                ["inputs"] = new JsonObject { ["ckpt_name"] = model.DiffusionModel }
            }
        };
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

        var baseUrl = model.ComfyUiUrl.TrimEnd('/');
        var client = _httpClientFactory.CreateClient("CompletionClient");
        client.Timeout = TimeSpan.FromSeconds(model.ProviderTimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(model.ApiKeyEncrypted))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _encryptionService.Decrypt(model.ApiKeyEncrypted));
        }

        try
        {
            var uploadedName = await UploadAsync(client, baseUrl, sourceImage, sourceFileName, model.ProviderName, cancellationToken);
            var workflow = BuildWorkflow(model, uploadedName, instruction.Trim());
            var payload = new JsonObject { ["prompt"] = workflow, ["client_id"] = "dreamgen-app" };

            _logger.LogInformation("ComfyUI source-image edit start: Provider={ProviderName}, DiffusionModel={DiffusionModel}, InstructionChars={InstructionChars}", model.ProviderName, model.DiffusionModel, instruction.Length);
            using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/prompt", payload, cancellationToken);
            if (!submitResponse.IsSuccessStatusCode)
                throw await CreateHttpExceptionAsync(submitResponse, model.ProviderName, "comfyui_edit_submit_failed", cancellationToken);

            var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            var promptId = submitBody?["prompt_id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(promptId))
                throw new ImageGenerationException("ComfyUI returned no prompt_id for the image edit.", model.ProviderName, reasonCode: "comfyui_edit_no_prompt_id");

            var history = await WaitForHistoryAsync(client, baseUrl, promptId, model, cancellationToken);
            return await DownloadOutputAsync(client, baseUrl, history, promptId, model.ProviderName, cancellationToken);
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ComfyUI source-image edit failed: Provider={ProviderName}", model.ProviderName);
            throw new ImageGenerationException($"ComfyUI source-image edit failed: {ex.Message}", model.ProviderName, reasonCode: "comfyui_edit_client_error", inner: ex);
        }
    }

    private static async Task<string> UploadAsync(HttpClient client, string baseUrl, Stream sourceImage, string sourceFileName, string providerName, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var imageContent = new StreamContent(sourceImage);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(imageContent, "image", sourceFileName);
        using var response = await client.PostAsync($"{baseUrl}/upload/image", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpExceptionAsync(response, providerName, "comfyui_edit_upload_failed", cancellationToken);
        var upload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var name = upload?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
            throw new ImageGenerationException("ComfyUI returned no uploaded source image name.", providerName, reasonCode: "comfyui_edit_upload_no_name");
        var subfolder = upload?["subfolder"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(subfolder) ? name : $"{subfolder}/{name}";
    }

    private static async Task<JsonObject> WaitForHistoryAsync(HttpClient client, string baseUrl, string promptId, ResolvedImageEditorModel model, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(model.ProviderTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            using var response = await client.GetAsync($"{baseUrl}/history/{promptId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                continue;
            var history = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            if (history?[promptId] is not JsonObject entry)
                continue;
            var status = entry["status"]?["status_str"]?.GetValue<string>();
            if (status == "success")
                return entry;
            if (status == "error")
                throw new ImageGenerationException($"ComfyUI workflow error for edit prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_edit_error");
        }
        throw new ImageGenerationException($"ComfyUI timed out waiting for edit prompt {promptId}.", model.ProviderName, reasonCode: "comfyui_edit_timeout");
    }

    private static async Task<byte[]> DownloadOutputAsync(HttpClient client, string baseUrl, JsonObject history, string promptId, string providerName, CancellationToken cancellationToken)
    {
        var image = history["outputs"]?.AsObject().FirstOrDefault(x => x.Value?["images"] is JsonArray).Value?["images"]?.AsArray().FirstOrDefault()?.AsObject();
        var filename = image?["filename"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(filename))
            throw new ImageGenerationException($"ComfyUI produced no output image for edit prompt {promptId}.", providerName, reasonCode: "comfyui_edit_no_output");
        var query = $"filename={Uri.EscapeDataString(filename)}";
        var subfolder = image?["subfolder"]?.GetValue<string>();
        var type = image?["type"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(subfolder)) query += $"&subfolder={Uri.EscapeDataString(subfolder)}";
        if (!string.IsNullOrWhiteSpace(type)) query += $"&type={Uri.EscapeDataString(type)}";
        using var response = await client.GetAsync($"{baseUrl}/view?{query}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpExceptionAsync(response, providerName, "comfyui_edit_view_failed", cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
            throw new ImageGenerationException("ComfyUI returned an empty edited image.", providerName, reasonCode: "comfyui_edit_empty_output");
        return bytes;
    }

    private static async Task<ImageGenerationException> CreateHttpExceptionAsync(HttpResponseMessage response, string providerName, string reasonCode, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ImageGenerationException($"ComfyUI image edit request failed: {(int)response.StatusCode} {body}", providerName, (int)response.StatusCode, reasonCode);
    }
}