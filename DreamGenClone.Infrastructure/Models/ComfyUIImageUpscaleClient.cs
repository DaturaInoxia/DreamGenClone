using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// Local ComfyUI upscaler. The graph is the one validated by the identity test runner
/// (<c>identity-two-character/runners/run_upscale.py</c>): load the image, load the upscale model, run it,
/// save the result. Nothing else is added here — no sampler, no text encoder — so enhancing cannot change
/// anything except the pixels' resolution.
/// </summary>
public sealed class ComfyUIImageUpscaleClient : IImageUpscaleClient
{
    /// <summary>Failure-code prefix for this client; the shared transport composes its codes from it.</summary>
    private const string UpscaleReasonPrefix = "comfyui_upscale";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ComfyUIImageUpscaleClient> _logger;

    public ComfyUIImageUpscaleClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<ComfyUIImageUpscaleClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>The proven four-node graph. Exposed for the graph test that pins its shape.</summary>
    internal static JsonObject BuildWorkflow(string sourceImageName, string upscalerModelName)
    {
        if (string.IsNullOrWhiteSpace(upscalerModelName))
            throw new InvalidOperationException("An upscale model name is required.");
        if (string.IsNullOrWhiteSpace(sourceImageName))
            throw new InvalidOperationException("An uploaded source image name is required.");

        return new JsonObject
        {
            ["1"] = new JsonObject
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new JsonObject { ["image"] = sourceImageName }
            },
            ["2"] = new JsonObject
            {
                ["class_type"] = "UpscaleModelLoader",
                ["inputs"] = new JsonObject { ["model_name"] = upscalerModelName }
            },
            ["3"] = new JsonObject
            {
                ["class_type"] = "ImageUpscaleWithModel",
                ["inputs"] = new JsonObject
                {
                    ["upscale_model"] = new JsonArray("2", 0),
                    ["image"] = new JsonArray("1", 0)
                }
            },
            ["4"] = new JsonObject
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new JsonObject
                {
                    ["filename_prefix"] = "dg_enhance",
                    ["images"] = new JsonArray("3", 0)
                }
            }
        };
    }

    public async Task<byte[]> UpscaleAsync(
        ResolvedImageEditorModel endpoint,
        string upscalerModelName,
        Stream sourceImage,
        string sourceFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (string.IsNullOrWhiteSpace(upscalerModelName))
            throw new InvalidOperationException(
                "Missing required configuration 'UpscalerModelName': the ComfyUI upscale model to use must be "
                + "configured before an image can be enhanced.");
        if (sourceImage is null || !sourceImage.CanRead)
            throw new InvalidOperationException("The image to enhance cannot be read.");
        if (string.IsNullOrWhiteSpace(sourceFileName))
            throw new InvalidOperationException("The image file name is required to enhance an image.");

        var baseUrl = endpoint.ComfyUiUrl.TrimEnd('/');
        var client = _httpClientFactory.CreateClient("CompletionClient");
        client.Timeout = TimeSpan.FromSeconds(endpoint.ProviderTimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKeyEncrypted))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _encryptionService.Decrypt(endpoint.ApiKeyEncrypted));
        }

        try
        {
            var uploadedName = await ComfyUiWorkflowTransport.UploadImageAsync(
                client, baseUrl, sourceImage, sourceFileName, endpoint.ProviderName, UpscaleReasonPrefix, cancellationToken);
            var workflow = BuildWorkflow(uploadedName, upscalerModelName.Trim());

            _logger.LogInformation(
                "ComfyUI upscale start: Provider={ProviderName}, Upscaler={Upscaler}", endpoint.ProviderName, upscalerModelName);
            var promptId = await ComfyUiWorkflowTransport.SubmitPromptAsync(
                client, baseUrl, workflow, "dreamgen-app", endpoint.ProviderName, UpscaleReasonPrefix, cancellationToken);

            var history = await ComfyUiWorkflowTransport.WaitForHistoryAsync(
                client, baseUrl, promptId, endpoint.ProviderTimeoutSeconds, endpoint.ProviderName, UpscaleReasonPrefix, cancellationToken);
            var bytes = await ComfyUiWorkflowTransport.DownloadOutputAsync(
                client, baseUrl, history, promptId, endpoint.ProviderName, UpscaleReasonPrefix, cancellationToken);

            _logger.LogInformation(
                "ComfyUI upscale completed: Provider={ProviderName}, Upscaler={Upscaler}, Bytes={Bytes}",
                endpoint.ProviderName, upscalerModelName, bytes.Length);
            return bytes;
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ComfyUI upscale failed: Provider={ProviderName}", endpoint.ProviderName);
            throw new ImageGenerationException(
                $"ComfyUI upscale failed: {ex.Message}", endpoint.ProviderName, reasonCode: "comfyui_upscale_client_error", inner: ex);
        }
    }
}
