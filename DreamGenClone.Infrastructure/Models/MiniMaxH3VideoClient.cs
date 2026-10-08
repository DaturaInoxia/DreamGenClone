using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// MiniMax H3 (Ref2VA) video client over the ComfyUI HTTP API: upload the reference images, submit the H3 graph,
/// wait for its history entry, download the produced clip.
/// </summary>
/// <remarks>
/// The same conversation <see cref="ComfyUIImageClient"/> has with ComfyUI - there is no second provider protocol -
/// but the poll is long (a trained-range render is ~25 to ~100 minutes) and the produced artifact is a video, so
/// the output scan looks for the video node's own output key instead of an image list. The base URL always comes
/// from the model's provider row; no host is hardcoded here (the B-150 harness's fixed IP is a harness artifact).
/// </remarks>
public sealed class MiniMaxH3VideoClient : IVideoGenerationClient
{
    private const string ReasonPrefix = "comfyui_video";
    private const string PromptNodeClass = "MiniMaxH3ReferenceToVideo";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<MiniMaxH3VideoClient> _logger;

    public MiniMaxH3VideoClient(
        IHttpClientFactory httpClientFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<MiniMaxH3VideoClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<SceneVideoRenderResult> GenerateAsync(
        ResolvedVideoModel model,
        SceneVideoGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);

        if (!SceneImagePromptMetadata.IsCompatible(model.Family, model.PromptDialect))
        {
            throw new ImageGenerationException(
                $"Video family '{model.Family}' is incompatible with prompt dialect '{model.PromptDialect}'. "
                + "Configure both on the model in Model Manager (/model-manager).",
                model.ProviderName,
                reasonCode: "invalid_video_prompt_metadata");
        }

        var baseUrl = (model.ComfyUiUrl ?? model.ProviderBaseUrl).TrimEnd('/');

        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            // The render budget, not the provider's chat timeout: a trained-range H3 clip runs far longer than any
            // request timeout, and the wait below is bounded by the same configured value.
            client.Timeout = TimeSpan.FromSeconds(model.H3.RenderTimeoutSeconds);

            if (!string.IsNullOrEmpty(model.ApiKeyEncrypted))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _encryptionService.Decrypt(model.ApiKeyEncrypted));
            }

            var uploadedNames = new List<string>(request.References.Count);
            foreach (var reference in request.References)
            {
                if (reference.Content.Length == 0)
                {
                    throw new ImageGenerationException(
                        $"Reference image '{reference.FileName}' carries no bytes.",
                        model.ProviderName,
                        reasonCode: $"{ReasonPrefix}_empty_reference");
                }

                await using var stream = new MemoryStream(reference.Content, writable: false);
                uploadedNames.Add(await ComfyUiWorkflowTransport.UploadImageAsync(
                    client, baseUrl, stream, reference.FileName, model.ProviderName, ReasonPrefix, cancellationToken));
            }

            // A continuation uploads its anchor frame (and, when the operator chose it, the source audio) BEFORE the
            // graph is built, because the graph binds them by uploaded name. A plain render uploads neither and the
            // builder emits no guide node at all.
            string? guideImageName = null;
            string? guideAudioName = null;
            var guideFrameIndex = 0;
            if (request.GuideImage is not null)
            {
                if (request.GuideFrameIndex is not { } index)
                {
                    throw new ImageGenerationException(
                        "A continuation request carries a guide image but no frame index, so the anchor has no "
                        + "position. The composer records the index when it prepares the continuation.",
                        model.ProviderName,
                        reasonCode: $"{ReasonPrefix}_guide_frame_index_missing");
                }

                guideFrameIndex = index;
                guideImageName = await UploadGuideAsync(
                    client, baseUrl, model, request.GuideImage, "guide frame", cancellationToken);
                if (request.GuideAudio is not null)
                {
                    guideAudioName = await UploadGuideAsync(
                        client, baseUrl, model, request.GuideAudio, "guide audio", cancellationToken);
                }
            }

            var graph = MiniMaxH3VideoGraph.Build(
                model.H3,
                request.Prompt,
                uploadedNames,
                request.Width,
                request.Height,
                request.Length,
                request.Steps,
                request.Seed,
                request.RefImageSize,
                request.OutputPrefix,
                request.Loras,
                guideImageName,
                guideAudioName,
                guideFrameIndex);

            _logger.LogInformation(
                "MiniMax H3 render start: Provider={ProviderName}, Model={ModelIdentifier}, References={References}, "
                + "Frames={Frames}, Steps={Steps}, Canvas={Width}x{Height}, Loras={Loras}",
                model.ProviderName,
                model.ModelIdentifier,
                uploadedNames.Count,
                request.Length,
                request.Steps,
                request.Width,
                request.Height,
                request.Loras?.Count ?? 0);

            var promptId = await ComfyUiWorkflowTransport.SubmitPromptAsync(
                client, baseUrl, graph, "dreamgen-app", model.ProviderName, ReasonPrefix, cancellationToken);

            var history = await ComfyUiWorkflowTransport.WaitForHistoryAsync(
                client, baseUrl, promptId, model.H3.RenderTimeoutSeconds, model.ProviderName, ReasonPrefix,
                cancellationToken);

            var (videoBytes, fileName) = await DownloadVideoAsync(
                client, baseUrl, history, promptId, model.ProviderName, cancellationToken);

            _logger.LogInformation(
                "MiniMax H3 render complete: Provider={ProviderName}, File={FileName}, Bytes={Bytes}",
                model.ProviderName, fileName, videoBytes.Length);

            return new SceneVideoRenderResult(videoBytes, fileName);
        }
        catch (ImageGenerationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MiniMax H3 render failed for provider {ProviderName}", model.ProviderName);
            throw new ImageGenerationException(
                $"MiniMax H3 render failed: {ex.Message}", model.ProviderName, reasonCode: $"{ReasonPrefix}_client_error",
                inner: ex);
        }
    }

    /// <summary>
    /// Uploads one continuation guide input (B-156). Fails fast on empty bytes rather than uploading something the
    /// host cannot use as the anchor it is supposed to be.
    /// </summary>
    private static async Task<string> UploadGuideAsync(
        HttpClient client,
        string baseUrl,
        ResolvedVideoModel model,
        SceneVideoGuideImage guide,
        string label,
        CancellationToken cancellationToken)
    {
        if (guide.Content.Length == 0)
        {
            throw new ImageGenerationException(
                $"The continuation {label} '{guide.FileName}' carries no bytes, so there is nothing to anchor to.",
                model.ProviderName,
                reasonCode: $"{ReasonPrefix}_empty_guide");
        }

        var contentType = Path.GetExtension(guide.FileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".mp3" => "audio/mpeg",
            _ => "application/octet-stream"
        };

        await using var stream = new MemoryStream(guide.Content, writable: false);
        return await ComfyUiWorkflowTransport.UploadInputAsync(
            client, baseUrl, stream, guide.FileName, contentType, model.ProviderName, ReasonPrefix, cancellationToken);
    }

    public async Task<(bool Success, string Message)> CheckVideoModelHealthAsync(
        ResolvedVideoModel model,
        CancellationToken cancellationToken = default)    {
        ArgumentNullException.ThrowIfNull(model);
        var baseUrl = (model.ComfyUiUrl ?? model.ProviderBaseUrl).TrimEnd('/');

        try
        {
            var client = _httpClientFactory.CreateClient("CompletionClient");
            client.Timeout = TimeSpan.FromSeconds(30);
            if (!string.IsNullOrEmpty(model.ApiKeyEncrypted))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _encryptionService.Decrypt(model.ApiKeyEncrypted));
            }

            using var stats = await client.GetAsync($"{baseUrl}/system_stats", cancellationToken);
            if (!stats.IsSuccessStatusCode)
            {
                return (false, $"ComfyUI health check failed: HTTP {(int)stats.StatusCode}");
            }

            var nodeInfo = await FetchObjectInfoAsync(client, baseUrl, PromptNodeClass, cancellationToken);
            if (nodeInfo is null)
            {
                return (false,
                    $"ComfyUI is reachable, but the '{PromptNodeClass}' node is not available on this host. "
                    + "Update ComfyUI there (the MiniMax H3 nodes need 0.30.0 or newer).");
            }

            var missing = new List<string>();
            await CheckArtifactAsync(client, baseUrl, "UNETLoader", "unet_name", model.H3.DitName, "DiT", missing, cancellationToken);
            await CheckArtifactAsync(client, baseUrl, "CLIPLoader", "clip_name", model.H3.TextEncoderName, "text encoder", missing, cancellationToken);
            await CheckArtifactAsync(client, baseUrl, "VAELoader", "vae_name", model.H3.VideoVaeName, "video VAE", missing, cancellationToken);
            await CheckArtifactAsync(client, baseUrl, "VAELoader", "vae_name", model.H3.AudioVaeName, "audio VAE", missing, cancellationToken);

            if (missing.Count > 0)
            {
                return (false,
                    $"ComfyUI is reachable and the H3 node is present, but these configured artifacts are not on the "
                    + $"host: {string.Join("; ", missing)}.");
            }

            return (true, $"ComfyUI is reachable, the '{PromptNodeClass}' node is present and every configured artifact is available.");
        }
        catch (Exception ex)
        {
            return (false, $"ComfyUI video health check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads the clip the graph produced. <c>SaveVideo</c> reports its output under a video-shaped key, so the
    /// scan accepts the keys ComfyUI uses for produced media rather than assuming the image one.
    /// </summary>
    private static async Task<(byte[] Bytes, string FileName)> DownloadVideoAsync(
        HttpClient client,
        string baseUrl,
        JsonObject history,
        string promptId,
        string providerName,
        CancellationToken cancellationToken)
    {
        string? filename = null;
        string? subfolder = null;
        string? type = null;

        if (history["outputs"] is JsonObject outputs)
        {
            foreach (var nodeOutput in outputs)
            {
                if (nodeOutput.Value is not JsonObject node) continue;
                foreach (var key in new[] { "video", "videos", "gifs", "images", "files" })
                {
                    if (node[key] is not JsonArray items) continue;
                    if (items.FirstOrDefault() is JsonObject item)
                    {
                        filename = item["filename"]?.GetValue<string>();
                        subfolder = item["subfolder"]?.GetValue<string>();
                        type = item["type"]?.GetValue<string>();
                    }
                    if (!string.IsNullOrWhiteSpace(filename)) break;
                }
                if (!string.IsNullOrWhiteSpace(filename)) break;
            }
        }

        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ImageGenerationException(
                $"ComfyUI produced no video output for prompt {promptId}.", providerName,
                reasonCode: $"{ReasonPrefix}_no_output");
        }

        var query = $"filename={Uri.EscapeDataString(filename)}";
        if (!string.IsNullOrWhiteSpace(subfolder)) query += $"&subfolder={Uri.EscapeDataString(subfolder)}";
        if (!string.IsNullOrWhiteSpace(type)) query += $"&type={Uri.EscapeDataString(type)}";

        using var response = await client.GetAsync($"{baseUrl}/view?{query}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ComfyUiWorkflowTransport.CreateHttpExceptionAsync(
                response, providerName, ReasonPrefix, "view_failed", cancellationToken);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            throw new ImageGenerationException(
                "ComfyUI returned an empty video.", providerName, reasonCode: $"{ReasonPrefix}_empty_output");
        }

        return (bytes, filename);
    }

    private static async Task<JsonObject?> FetchObjectInfoAsync(
        HttpClient client, string baseUrl, string classType, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{baseUrl}/object_info/{classType}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return body?[classType] as JsonObject;
    }

    /// <summary>
    /// Confirms one configured artifact name is present in the node's own combo list. A render is far too
    /// expensive to discover a typo at, so the probe answers it in a second.
    /// </summary>
    private static async Task CheckArtifactAsync(
        HttpClient client,
        string baseUrl,
        string classType,
        string inputName,
        string artifactName,
        string label,
        List<string> missing,
        CancellationToken cancellationToken)
    {
        var info = await FetchObjectInfoAsync(client, baseUrl, classType, cancellationToken);
        var choices = info?["input"]?["required"]?[inputName]?[0] as JsonArray;
        if (choices is null)
        {
            missing.Add($"{label} '{artifactName}' (the host exposes no '{classType}.{inputName}' list)");
            return;
        }

        var present = choices.Any(choice =>
            string.Equals(choice?.GetValue<string>(), artifactName, StringComparison.OrdinalIgnoreCase));
        if (!present)
        {
            missing.Add($"{label} '{artifactName}'");
        }
    }
}
