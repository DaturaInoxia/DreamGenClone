using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Turns one wardrobe item's short description into a full reference prompt GEARED TO the chosen model, stores it on
/// a new image row, and hands that row to the ordinary asset render.
/// </summary>
/// <remarks>
/// Two stages, one job, because the compiled prompt has to BE the image's prompt: the render path uses
/// <c>SceneAssetImages.Prompt</c> verbatim whenever the row names a <c>PromptCompilerId</c>, so writing the compiled
/// text there is what makes "compile for this model" and "render with it" the same fact rather than two that can
/// disagree. It also means the long prompt is visible and editable on the finished card, and re-rendering a corrected
/// prompt needs no second compilation.
/// </remarks>
public sealed class WardrobeItemPromptGenerationJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneAssetRepository _assets;
    private readonly IModelResolutionService _modelResolution;
    private readonly IWardrobeItemPromptCompilerRegistry _compilers;
    private readonly IWardrobeItemService _wardrobe;
    private readonly ICompletionClient _completionClient;
    private readonly ILogger<WardrobeItemPromptGenerationJobHandler> _logger;

    public WardrobeItemPromptGenerationJobHandler(
        ISceneAssetRepository assets,
        IModelResolutionService modelResolution,
        IWardrobeItemPromptCompilerRegistry compilers,
        IWardrobeItemService wardrobe,
        ICompletionClient completionClient,
        ILogger<WardrobeItemPromptGenerationJobHandler> logger)
    {
        _assets = assets;
        _modelResolution = modelResolution;
        _compilers = compilers;
        _wardrobe = wardrobe;
        _completionClient = completionClient;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.WardrobeItemPromptGeneration;

    public async Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Deserialize<WardrobeItemPromptJobPayload>(job.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Wardrobe item prompt payload is missing or invalid.");
        if (string.IsNullOrWhiteSpace(payload.AssetId))
        {
            throw new InvalidOperationException("Wardrobe item prompt payload requires an AssetId.");
        }

        if (string.IsNullOrWhiteSpace(payload.ItemDescription))
        {
            throw new InvalidOperationException("Wardrobe item prompt payload requires the garment description.");
        }

        if (string.IsNullOrWhiteSpace(payload.ModelId) || string.IsNullOrWhiteSpace(payload.ImageSize))
        {
            throw new InvalidOperationException("Wardrobe item prompt payload requires an exact ModelId and ImageSize.");
        }

        var image = await _assets.GetImageAsync(payload.ImageId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Wardrobe item image '{payload.ImageId}' was not found, so there is no row to compile a prompt onto.");
        if (!string.Equals(image.AssetId, payload.AssetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Wardrobe item image '{image.Id}' does not belong to asset '{payload.AssetId}'.");
        }

        if (image.Status == SceneAssetStatus.Complete)
        {
            // Already rendered: re-compiling would rewrite the prompt of an image that exists.
            return;
        }

        try
        {
            await CompileAndQueueRenderAsync(image, payload, cancellationToken);
        }
        catch (Exception exception)
        {
            // The row exists, so the failure is written WHERE THE OPERATOR IS LOOKING. Without this the tab showed
            // "No images yet" after a failed compile, which is how a real error looks identical to a dead button.
            image.Status = SceneAssetStatus.Failed;
            image.ErrorMessage = exception.Message;
            image.UpdatedUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
            await _assets.UpsertImageAsync(image, cancellationToken);
            throw;
        }
    }

    private async Task CompileAndQueueRenderAsync(
        SceneAssetImage image,
        WardrobeItemPromptJobPayload payload,
        CancellationToken cancellationToken)
    {
        var asset = await _assets.GetAsync(payload.AssetId, cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe item '{payload.AssetId}' was not found.");
        if (asset.Type != SceneAssetType.Wardrobe)
        {
            throw new InvalidOperationException($"Asset '{asset.Id}' is not a wardrobe item.");
        }

        // The compiler is chosen from the TARGET model's own family and dialect, so "geared to the model" is structural.
        var imageModel = await _modelResolution.ResolveImageModelByIdAsync(payload.ModelId, cancellationToken);
        var compiler = _compilers.Require(imageModel.SceneImageModelFamily, imageModel.PromptDialect);

        var promptModel = await _modelResolution.ResolveImagePromptModelAsync(
            sessionOverrideId: null,
            cancellationToken);
        var (systemPrompt, userPrompt) = compiler.BuildMessages(
            new WardrobeItemPromptRequest(payload.ItemDescription, payload.ImageSize));
        var (rawResponse, _) = await _completionClient.GenerateWithReasoningAsync(
            systemPrompt,
            userPrompt,
            promptModel,
            cancellationToken);
        var compiledPrompt = compiler.ParseOutput(rawResponse);
        var compilerId = CompilerIdFor(imageModel.SceneImageModelFamily, imageModel.PromptDialect);

        // The prompt lands on the row that ALREADY EXISTS, with the compiler that authored it. That row is what the
        // render path reads, and a named compiler is what makes it use this text verbatim instead of compiling it again.
        await _assets.SetImagePromptAsync(
            image.Id,
            compiledPrompt,
            compilerId,
            negativePrompt: null,
            associationMetadataJson: JsonSerializer.Serialize(new
            {
                source = "wardrobe-item",
                stage = "compiled",
                semanticDescription = payload.ItemDescription.Trim(),
                promptCompilerId = compilerId,
                compilerFamily = imageModel.SceneImageModelFamily.ToString(),
                compilerDialect = imageModel.PromptDialect.ToString(),
                promptModelIdentifier = promptModel.ModelIdentifier,
                requestedModelId = payload.ModelId,
                imageSize = payload.ImageSize
            }, JsonOptions),
            cancellationToken);

        if (!payload.RenderWhenCompiled)
        {
            // The prompt is the deliverable this time: hold the row so the operator can read it, correct it, and render
            // it when they decide to.
            await _wardrobe.MarkPromptReadyAsync(image.Id, cancellationToken);
        }
        else
        {
            await _wardrobe.RenderExistingImageAsync(image.Id, cancellationToken);
        }
        _logger.LogInformation(
            "Compiled wardrobe item prompt: AssetId={AssetId}, ImageId={ImageId}, Compiler={CompilerId}, Length={Length}, "
            + "SemanticDescription={SemanticDescription}",
            asset.Id, image.Id, compilerId, compiledPrompt.Length, payload.ItemDescription.Trim());
    }

    /// <summary>
    /// The stable id recorded on the image row. It names the family and dialect the prompt was written FOR, which is
    /// what makes a stored prompt self-describing when it is read back months later.
    /// </summary>
    private static string CompilerIdFor(SceneImageModelFamily family, SceneImagePromptDialect dialect)
    {
        var familySlug = family switch
        {
            SceneImageModelFamily.QwenImage21 => "qwen-image-21",
            SceneImageModelFamily.Sdxl => "sdxl",
            SceneImageModelFamily.Flux => "flux",
            SceneImageModelFamily.Api => "api",
            _ => throw new InvalidOperationException($"Model family '{family}' has no wardrobe-item compiler id.")
        };

        var dialectSlug = dialect switch
        {
            SceneImagePromptDialect.NaturalLanguage => "natural-language",
            SceneImagePromptDialect.SdxlNaturalLanguage => "sdxl-natural-language",
            SceneImagePromptDialect.FluxNaturalLanguage => "flux-natural-language",
            _ => throw new InvalidOperationException($"Prompt dialect '{dialect}' has no wardrobe-item compiler id.")
        };

        return $"wardrobe-item-{familySlug}-{dialectSlug}";
    }
}
