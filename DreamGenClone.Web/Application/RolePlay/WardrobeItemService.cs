using System.Text.Json;
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
/// Default wardrobe library: shared garment items, one-click reference readiness, and the render that compiles a
/// short description into a full reference prompt first.
/// </summary>
public sealed class WardrobeItemService : IWardrobeItemService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The provenance and license wording this path records for an image the operator marked usable. Named here,
    /// once, because the operator is not asked for it: it states what the image IS (a locally produced, internally
    /// used garment reference) rather than pretending a human reviewed a licence.
    /// </summary>
    public const string UseAsReferenceLicenseLabel = "AI-generated, internal use";

    public const string UseAsReferencePolicyKey = "wardrobe-item-reference";

    public const int MaximumImagesPerRequest = 8;

    private readonly ISceneAssetRepository _assets;
    private readonly ISceneAssetService _assetService;
    private readonly IModelResolutionService _modelResolution;
    private readonly IFunctionDefaultRepository _functionDefaults;
    private readonly IWardrobeItemPromptCompilerRegistry _compilers;
    private readonly IDurableBackgroundJobQueue _backgroundJobQueue;
    private readonly ISceneBeatAnalyzerResolver _durableSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WardrobeItemService> _logger;

    public WardrobeItemService(
        ISceneAssetRepository assets,
        ISceneAssetService assetService,
        IModelResolutionService modelResolution,
        IFunctionDefaultRepository functionDefaults,
        IWardrobeItemPromptCompilerRegistry compilers,
        IDurableBackgroundJobQueue backgroundJobQueue,
        ISceneBeatAnalyzerResolver durableSettings,
        TimeProvider timeProvider,
        ILogger<WardrobeItemService> logger)
    {
        _assets = assets;
        _assetService = assetService;
        _modelResolution = modelResolution;
        _functionDefaults = functionDefaults;
        _compilers = compilers;
        _backgroundJobQueue = backgroundJobQueue;
        _durableSettings = durableSettings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WardrobeItem>> ListItemsAsync(CancellationToken cancellationToken = default)
    {
        var assets = (await _assets.ListAsync(cancellationToken))
            .Where(asset => asset.Type == SceneAssetType.Wardrobe)
            .OrderBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<WardrobeItem>(assets.Count);
        foreach (var asset in assets)
        {
            var images = await _assets.ListImagesAsync(asset.Id, cancellationToken);
            items.Add(new WardrobeItem(
                asset,
                images
                    .OrderByDescending(image => image.CreatedUtc)
                    .Select(image => new WardrobeItemImage(
                        image,
                        image.CandidateNotes?.Trim() ?? string.Empty,
                        IsInUse(image),
                        IsUnrenderedPromptDraft(image)))
                    .ToList()));
        }

        return items;
    }

    public async Task<SceneAsset> CreateItemAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("A wardrobe item needs a name (for example \"Yellow sundress\").");
        }

        var trimmed = name.Trim();
        var existing = (await _assets.ListAsync(cancellationToken))
            .FirstOrDefault(asset => asset.Type == SceneAssetType.Wardrobe
                && string.Equals(asset.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"A wardrobe item named '{trimmed}' already exists. Open it and add images to it, or give this one a different name.");
        }

        // No character owner, on purpose: the same garment is worn by whoever the scene calls for, so an item belongs
        // to the library rather than to one character.
        return await _assetService.CreateAssetAsync(trimmed, SceneAssetType.Wardrobe, characterProfileId: null, cancellationToken);
    }

    public async Task<SceneAssetImage> SetImageInUseAsync(
        string imageId,
        bool inUse,
        CancellationToken cancellationToken = default)
    {
        var image = await _assets.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe image '{imageId}' was not found.");

        if (inUse)
        {
            if (IsInUse(image))
            {
                return image;
            }

            var provenance = JsonSerializer.Serialize(new
            {
                source = "wardrobe-item",
                markedInUseUtc = _timeProvider.GetUtcNow().UtcDateTime,
                note = "Marked usable as a reference image from the character Wardrobe tab."
            }, JsonOptions);

            return await _assets.ApproveImageForProductionAsync(
                image.Id,
                provenance,
                SceneAssetConsentState.NotApplicable,
                SceneAssetLicenseState.NotApplicable,
                UseAsReferenceLicenseLabel,
                SceneAssetApprovedUseScope.CharacterWardrobe | SceneAssetApprovedUseScope.ProductionSource,
                UseAsReferencePolicyKey,
                "{}",
                cancellationToken);
        }

        // Stopping is written on the image itself. Kept (not deleted) so the operator can start using it again, and so
        // the row remains the record of what the render once used.
        image.ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Draft;
        image.UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _assets.UpsertImageAsync(image, cancellationToken);
        return image;
    }

    public async Task SetImageLabelAsync(
        string imageId,
        string? label,
        CancellationToken cancellationToken = default)
    {
        var image = await _assets.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe image '{imageId}' was not found.");

        // The label lives in the image's own notes column: it is a property of THIS image of the item, and the
        // reference picker reads it to name the option.
        var text = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (string.Equals(image.CandidateNotes, text, StringComparison.Ordinal))
        {
            return;
        }

        image.CandidateNotes = text;
        image.UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _assets.UpsertImageAsync(image, cancellationToken);
    }

    public async Task DeleteImageAsync(string imageId, CancellationToken cancellationToken = default)
    {
        // The shared service refuses an image that is in use as a reference, so the rule has one home and this call
        // cannot drift from the asset pages' behaviour.
        await _assetService.DeleteImageAsync(imageId, cancellationToken);
    }

    public async Task<IReadOnlyList<SceneImageModelChoice>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await _modelResolution.ListSceneImageModelsAsync(identityCapableOnly: false, cancellationToken);
        var eligible = models
            .Where(model => _compilers.TryResolve(model.Family, model.Dialect, out _))
            .ToList();

        if (eligible.Count == 0)
        {
            var supported = _compilers.Compilers
                .Select(compiler => compiler.Family.ToString())
                .Distinct(StringComparer.Ordinal);
            throw new InvalidOperationException(
                "No enabled image model can be driven from the wardrobe tab. It compiles a garment reference prompt for "
                + $"these model families: {string.Join(", ", supported)}. Enable one in Model Manager (/model-manager).");
        }

        return eligible;
    }

    public async Task<string?> DefaultModelIdAsync(CancellationToken cancellationToken = default)
    {
        var configured = await _functionDefaults.GetByFunctionAsync(AppFunction.RolePlayWardrobeItem, cancellationToken);
        return string.IsNullOrWhiteSpace(configured?.ModelId) ? null : configured!.ModelId;
    }

    public async Task EnqueueItemImagesAsync(
        string assetId,
        string itemDescription,
        string modelId,
        string imageSize,
        int outputCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemDescription))
        {
            throw new InvalidOperationException(
                "Describe the garment in a few words (for example \"a yellow sundress\"); the prompt is compiled from it.");
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("Choose the image model this garment will be rendered with.");
        }

        if (string.IsNullOrWhiteSpace(imageSize))
        {
            throw new InvalidOperationException("Choose the image size.");
        }

        if (outputCount < 1 || outputCount > MaximumImagesPerRequest)
        {
            throw new InvalidOperationException($"Image count must be between 1 and {MaximumImagesPerRequest}.");
        }

        var asset = await _assets.GetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe item '{assetId}' was not found.");
        if (asset.Type != SceneAssetType.Wardrobe)
        {
            throw new InvalidOperationException($"Asset '{assetId}' is not a wardrobe item.");
        }

        // Fail BEFORE anything is queued when the chosen model has no wardrobe compiler: the job would fail anyway, and
        // the operator would have spent a queue slot to learn what the tab could have told them immediately.
        var resolved = await _modelResolution.ResolveImageModelByIdAsync(modelId, cancellationToken);
        _compilers.Require(resolved.SceneImageModelFamily, resolved.PromptDialect);

        // ONE ROW PER REQUESTED IMAGE, created NOW rather than by the job. Two reasons, both about the operator seeing
        // what is happening: a card appears the moment they ask for it instead of nothing at all while a prompt is
        // drafted, and a compile that fails has a row to carry the reason (it used to leave the tab looking as if the
        // button had done nothing).
        for (var index = 0; index < outputCount; index++)
        {
            await CreatePendingRowAsync(assetId, itemDescription, modelId, imageSize, renderWhenCompiled: true, cancellationToken);
        }

        _logger.LogInformation(
            "Enqueued wardrobe item images: AssetId={AssetId}, ModelId={ModelId}, Count={Count}",
            asset.Id, modelId, outputCount);
    }

    /// <summary>
    /// Whether an image is currently usable as a reference image.
    /// </summary>
    private static bool IsInUse(SceneAssetImage image)
        => image.Status == SceneAssetStatus.Complete
            && image.ProductionApprovalStatus == SceneAssetProductionApprovalStatus.Approved
            && image.ProductionVersion is not null
            && !string.IsNullOrWhiteSpace(image.Sha256);

    /// <summary>The compiler id recorded for a prompt the OPERATOR wrote, rather than one a compiler drafted.</summary>
    public const string HandEditedPromptCompilerId = "wardrobe-item-hand-edited";

    public async Task<SceneAssetImage> EnqueuePromptDraftAsync(
        string assetId,
        string itemDescription,
        string modelId,
        string imageSize,
        CancellationToken cancellationToken = default)
    {
        var asset = await _assets.GetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe item '{assetId}' was not found.");

        // ONE live draft per item: "Generate prompt" replaces the prompt the operator has not rendered yet, so pressing
        // it twice leaves one text to read rather than a pile of drafts nobody asked to keep. A draft that HAS been
        // rendered is left alone - it is the record of an image.
        foreach (var stale in (await _assets.ListImagesAsync(asset.Id, cancellationToken)).Where(IsUnrenderedPromptDraft))
        {
            await _assets.DeleteImageAsync(stale.Id, cancellationToken);
        }

        var row = await CreatePendingRowAsync(
            assetId, itemDescription, modelId, imageSize, renderWhenCompiled: false, cancellationToken);
        _logger.LogInformation(
            "Enqueued wardrobe item prompt draft: AssetId={AssetId}, ImageId={ImageId}", assetId, row.Id);
        return row;
    }

    /// <summary>
    /// Whether a row is a PROMPT waiting for the operator rather than a picture: the compile ran (or is running) and no
    /// render was ever asked for. This is what the tab hides from its image list, because a drafted prompt is not an
    /// attempt at an image.
    /// </summary>
    private static bool IsUnrenderedPromptDraft(SceneAssetImage image)
        => string.IsNullOrWhiteSpace(image.FileRelativePath)
            && image.ProductionApprovalStatus is null
            && (StageOf(image) is "compiling" or "prompt-ready");

    /// <summary>The stage recorded on a row, or empty when it recorded none.</summary>
    private static string StageOf(SceneAssetImage image)
    {
        if (string.IsNullOrWhiteSpace(image.AssociationMetadataJson) || image.AssociationMetadataJson.Trim().Length <= 2)
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(image.AssociationMetadataJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("stage", out var stage)
                && stage.ValueKind == JsonValueKind.String
                    ? stage.GetString() ?? string.Empty
                    : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Creates the row the operator will see, and queues the compile job that fills its prompt in. One row per
    /// requested image: it appears the moment they ask for it rather than after a prompt has been drafted, and a
    /// compile that fails has somewhere to report itself.
    /// </summary>
    private async Task<SceneAssetImage> CreatePendingRowAsync(
        string assetId,
        string itemDescription,
        string modelId,
        string imageSize,
        bool renderWhenCompiled,
        CancellationToken cancellationToken)
    {
        var asset = await _assets.GetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe item '{assetId}' was not found.");
        if (asset.Type != SceneAssetType.Wardrobe)
        {
            throw new InvalidOperationException($"Asset '{assetId}' is not a wardrobe item.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var image = new SceneAssetImage
        {
            Id = Guid.NewGuid().ToString("N"),
            AssetId = asset.Id,
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Pending,
            // The operator's own words, so the card says what was asked for. PromptCompilerId stays NULL until the
            // prompt is drafted: a null compiler is what tells the render path this text is not model-ready yet.
            Prompt = itemDescription.Trim(),
            PromptCompilerId = null,
            MediaType = "image/png",
            AssociationMetadataJson = JsonSerializer.Serialize(new
            {
                source = "wardrobe-item",
                stage = "compiling",
                semanticDescription = itemDescription.Trim(),
                requestedModelId = modelId.Trim(),
                imageSize = imageSize.Trim()
            }, JsonOptions),
            CreatedUtc = now,
            UpdatedUtc = now
        };
        await _assets.UpsertImageAsync(image, cancellationToken);

        var payload = new WardrobeItemPromptJobPayload
        {
            AssetId = asset.Id,
            ImageId = image.Id,
            ItemDescription = itemDescription.Trim(),
            ModelId = modelId.Trim(),
            ImageSize = imageSize.Trim(),
            RenderWhenCompiled = renderWhenCompiled
        };
        await EnqueueAsync(
            BackgroundJobTypes.WardrobeItemPromptGeneration,
            DurableJobLane.PromptCompilation,
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken);

        return image;
    }

    public async Task<SceneAssetImage> RenderPromptAsync(
        string assetId,
        string prompt,
        string modelId,
        string imageSize,
        string promptCompilerId,
        string? semanticDescription = null,
        string? handEditedFromImageId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException("A prompt is required to render a wardrobe image.");
        }

        if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(imageSize))
        {
            throw new InvalidOperationException("Rendering a wardrobe prompt requires an exact ModelId and ImageSize.");
        }

        if (string.IsNullOrWhiteSpace(promptCompilerId))
        {
            throw new InvalidOperationException(
                "A wardrobe prompt must name what authored it, so the render uses the text verbatim rather than "
                + "compiling a model-ready prompt a second time.");
        }

        var asset = await _assets.GetAsync(assetId, cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe item '{assetId}' was not found.");
        if (asset.Type != SceneAssetType.Wardrobe)
        {
            throw new InvalidOperationException($"Asset '{assetId}' is not a wardrobe item.");
        }

        // Resolved even though the prompt is already written, because an unresolvable or disabled model must fail here
        // rather than in a queued job nobody is watching.
        await _modelResolution.ResolveImageModelByIdAsync(modelId, cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var image = new SceneAssetImage
        {
            Id = Guid.NewGuid().ToString("N"),
            AssetId = asset.Id,
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Pending,
            Prompt = prompt.Trim(),
            PromptCompilerId = promptCompilerId.Trim(),
            // Null, not empty: this compiler authors no negative (2.1 runs cfg 1 where it is inert), and "no negative
            // was authored" must stay distinguishable from "the author chose an empty one".
            NegativePrompt = null,
            SourceImageId = string.IsNullOrWhiteSpace(handEditedFromImageId) ? null : handEditedFromImageId.Trim(),
            MediaType = "image/png",
            AssociationMetadataJson = JsonSerializer.Serialize(new
            {
                source = "wardrobe-item",
                semanticDescription = string.IsNullOrWhiteSpace(semanticDescription) ? null : semanticDescription.Trim(),
                promptCompilerId = promptCompilerId.Trim(),
                handEdited = !string.IsNullOrWhiteSpace(handEditedFromImageId),
                handEditedFromImageId,
                requestedModelId = modelId.Trim(),
                imageSize = imageSize.Trim()
            }, JsonOptions),
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _assets.UpsertImageAsync(image, cancellationToken);
        await RenderExistingImageAsync(image.Id, cancellationToken);

        return image;
    }

    public async Task RenderExistingImageAsync(string imageId, CancellationToken cancellationToken = default)
    {
        var image = await _assets.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe image '{imageId}' was not found.");
        if (string.IsNullOrWhiteSpace(image.Prompt))
        {
            throw new InvalidOperationException($"Wardrobe image '{imageId}' has no prompt to render.");
        }

        if (string.IsNullOrWhiteSpace(image.PromptCompilerId))
        {
            throw new InvalidOperationException(
                $"Wardrobe image '{imageId}' does not name the compiler that authored its prompt, so the render could "
                + "not tell a model-ready prompt from a description awaiting compilation. Refusing rather than guessing.");
        }

        var metadata = string.IsNullOrWhiteSpace(image.AssociationMetadataJson) || image.AssociationMetadataJson.Trim().Length <= 2
            ? $"{{\"source\":\"wardrobe-item\",\"stage\":\"compiled\"}}"
            : image.AssociationMetadataJson;

        await SetStageAsync(image, "rendering", cancellationToken);

        var payload = new SceneAssetGenerationJobPayload
        {
            AssetId = image.AssetId,
            ImageId = image.Id,
            ModelId = RequestedModelId(metadata),
            ImageSize = RequestedImageSize(metadata)
        };
        await EnqueueAsync(
            BackgroundJobTypes.SceneAssetGeneration,
            DurableJobLane.ImageRender,
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken);
    }

    public async Task MarkPromptReadyAsync(string imageId, CancellationToken cancellationToken = default)
    {
        var image = await _assets.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Wardrobe image '{imageId}' was not found.");
        await SetStageAsync(image, "prompt-ready", cancellationToken);
    }

    /// <summary>
    /// Records what a row is currently doing. The tab reads this to say it out loud, and it is the only way a row whose
    /// prompt is written but deliberately not rendered can be told apart from one that is still being drafted.
    /// </summary>
    private async Task SetStageAsync(SceneAssetImage image, string stage, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source"] = "wardrobe-item",
            ["stage"] = stage
        };

        if (!string.IsNullOrWhiteSpace(image.AssociationMetadataJson) && image.AssociationMetadataJson.Trim().Length > 2)
        {
            using var document = JsonDocument.Parse(image.AssociationMetadataJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    fields[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => property.Value.GetRawText()
                    };
                }
            }
        }

        fields["stage"] = stage;
        image.AssociationMetadataJson = JsonSerializer.Serialize(fields, JsonOptions);
        image.UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _assets.UpsertImageAsync(image, cancellationToken);
    }

    /// <summary>
    /// The model and frame a row was requested with, read back from its own metadata. The render must use the values the
    /// image was ASKED for rather than "whatever the form says now": the operator may have changed the model picker
    /// since, and a prompt that no longer matches its model is exactly what the compiler id is meant to prevent.
    /// </summary>
    private static string RequestedModelId(string metadataJson)
    {
        using var document = JsonDocument.Parse(metadataJson);
        return document.RootElement.TryGetProperty("requestedModelId", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException(
                "This wardrobe image does not record which model it was requested for, so its render cannot be dispatched.");
    }

    private static string RequestedImageSize(string metadataJson)
    {
        using var document = JsonDocument.Parse(metadataJson);
        return document.RootElement.TryGetProperty("imageSize", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException(
                "This wardrobe image does not record which frame it was requested at, so its render cannot be dispatched.");
    }

    /// <summary>
    /// One job per requested image, each with its own key: the images are deliberately independent attempts of the
    /// same item, so collapsing them into one job (and one image) would silently render fewer than the operator asked
    /// for.
    /// </summary>
    private async Task EnqueueAsync(
        string jobType,
        DurableJobLane lane,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var settings = await _durableSettings.ResolveAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var key = $"{jobType}:{Guid.NewGuid():N}";
        var job = new DurableBackgroundJob
        {
            Id = key,
            JobType = jobType,
            Lane = lane,
            PayloadJson = payloadJson,
            DedupeKey = key,
            MaxAttempts = settings.RetryDelaysSeconds.Count + 1,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        if (!await _backgroundJobQueue.TryEnqueueAsync(job, cancellationToken))
        {
            throw new InvalidOperationException($"A durable '{jobType}' job with key '{key}' is already active.");
        }
    }
}
