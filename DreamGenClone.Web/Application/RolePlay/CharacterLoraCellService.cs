using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The render half of the cell workspace.
///
/// <para>
/// It decides two things and delegates the rest. It resolves the identity reference the cell's own reference rule
/// names — from the dataset's stored plan and its approved pack, never from the caller — and it resolves the
/// container the dataset's attempts live in. The prompt and the model are the caller's, because those are the
/// operator's choices.
/// </para>
///
/// <para>
/// It renders through the identity-aware generation path (<see cref="ISceneAssetService.AddGeneratedImageAsync"/>)
/// rather than the prompt-only one. That is not a preference: the prompt-only path carries no conditioning at all,
/// so a cell rendered through it is not the character.
/// </para>
/// </summary>
public sealed class CharacterLoraCellService : ICharacterLoraCellService
{
    private readonly ISceneAssetService _assets;
    private readonly IImageWorkflowTemplateService _settings;
    private readonly ICharacterLoraRepository _datasets;
    private readonly ICharacterImageIdentityService _identity;

    public CharacterLoraCellService(
        ISceneAssetService assets,
        IImageWorkflowTemplateService settings,
        ICharacterLoraRepository datasets,
        ICharacterImageIdentityService identity)
    {
        _assets = assets;
        _settings = settings;
        _datasets = datasets;
        _identity = identity;
    }

    /// <summary>
    /// Prefix identifying a LoRA cell's attempts. Distinct from the front and angle batches, so a cell's deck can
    /// never show another pipeline's images.
    /// </summary>
    public const string CellBatchPrefix = "lora-cell-";

    public string CellBatchIdFor(string datasetId, string cellKey)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
        {
            throw new InvalidOperationException("A dataset id is required to identify a coverage cell.");
        }

        if (string.IsNullOrWhiteSpace(cellKey))
        {
            throw new InvalidOperationException("A cell key is required to identify a coverage cell.");
        }

        return $"{CellBatchPrefix}{datasetId.Trim()}::{cellKey.Trim()}";
    }

    public async Task<IReadOnlyList<SceneAssetImage>> ListCellAttemptsAsync(
        string datasetId, string cellKey, CancellationToken cancellationToken = default)
    {
        var batchId = CellBatchIdFor(datasetId, cellKey);
        var images = await _assets.ListImagesByCandidateBatchAsync(batchId, cancellationToken);
        return images.OrderByDescending(image => image.CreatedUtc).ToList();
    }

    public async Task<string> DescribeIdentityReferenceAsync(
        string datasetId, string cellKey, CancellationToken cancellationToken = default)
    {
        var (dataset, record) = await LoadCellAsync(datasetId, cellKey, cancellationToken);
        var packAssets = await _identity.ListAssetsAsync(dataset.IdentityPackId, cancellationToken);
        var conditioning = ResolveIdentityConditioning(record, dataset.IdentityPackId, packAssets);
        if (conditioning is null)
        {
            return string.Empty;
        }

        return $"face {record.FaceCanonicalSlot} from pack {Shorten(dataset.IdentityPackId)} "
            + $"({Shorten(conditioning.FaceAssetId)})";
    }

    public async Task<string> DescribeBodyReferenceAsync(
        string datasetId, string cellKey, CancellationToken cancellationToken = default)
    {
        var (dataset, record) = await LoadCellAsync(datasetId, cellKey, cancellationToken);
        var packAssets = await _identity.ListAssetsAsync(dataset.IdentityPackId, cancellationToken);
        var conditioning = ResolveBodyConditioning(record, dataset.IdentityPackId, packAssets);
        return $"body {record.BodyCanonicalSlot}/{record.BodyState} from pack {Shorten(dataset.IdentityPackId)} "
            + $"({Shorten(conditioning.BodyAssetId)})";
    }

    public async Task<SceneAssetImage> RenderCellAsync(
        string datasetId,
        string cellKey,
        string prompt,
        string modelId,
        string aspect,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException(
                "A coverage cell cannot be rendered without a prompt; the cell's prompt could not be composed.");
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException(
                "An exact image model is required to render a coverage cell. Choose one in the cell workspace.");
        }

        if (string.IsNullOrWhiteSpace(aspect))
        {
            throw new InvalidOperationException("The coverage cell has no render aspect, so it cannot be rendered.");
        }

        var (dataset, record) = await LoadCellAsync(datasetId, cellKey, cancellationToken);
        if (dataset.Status != CharacterLoraDatasetStatus.Draft)
        {
            throw new InvalidOperationException(
                $"LoRA dataset '{dataset.Id}' is {dataset.Status}; only a draft dataset can be shot.");
        }

        // The face reference comes from the cell's own rule, so an angled cell is conditioned on the angled
        // reference rather than on whatever face happened to be canonical.
        var packAssets = await _identity.ListAssetsAsync(dataset.IdentityPackId, cancellationToken);
        var conditioning = ResolveIdentityConditioning(record, dataset.IdentityPackId, packAssets);

        // The build reference comes from the same rule and is state-matched: this cell's own state decides which
        // approved body reference travels, so a clothed cell can never be conditioned on the unclothed one.
        var bodyConditioning = ResolveBodyConditioning(record, dataset.IdentityPackId, packAssets);

        var container = await EnsureContainerAsync(dataset, cancellationToken);

        return await _assets.AddGeneratedImageAsync(
            container.Id,
            prompt.Trim(),
            modelId.Trim(),
            aspect.Trim(),
            cancellationToken,
            candidateBatchId: CellBatchIdFor(dataset.Id, record.Key),
            options: new SceneAssetImageGenerationOptions
            {
                // No negative, deliberately. The families this pipeline renders carry none: SDXL / Juggernaut /
                // BigLust resolve to an EMPTY negative by model-author research, Pony takes only the short guard
                // set its own compiler authors, and FLUX has no negative field at all. A cell that set one would
                // also be setting a dead field — the render path compiles a cell prompt (no compiler id is set)
                // and authors no negative, so a value here would never reach the model.
                Identity = conditioning,
                BodyReference = bodyConditioning
            });
    }

    public async Task DiscardAttemptAsync(string imageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            throw new InvalidOperationException("An attempt image id is required.");
        }

        await _assets.DeleteImageAsync(imageId.Trim(), cancellationToken);
    }

    public async Task<string?> ResolveCellModelAsync(string characterId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return null;
        }

        var resolved = await _settings.ResolveSettingsAsync(characterId.Trim(), cancellationToken);
        return resolved.LoraCellModelId;
    }

    public async Task SaveCellModelAsync(
        string characterId, string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            throw new InvalidOperationException("A character is required to save the coverage cell model.");
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("A coverage cell model is required.");
        }

        // Read the resolved row and write it back with one field changed, so no other setting is reset to a
        // default by saving this one. The character row is created on first save and overrides the global row
        // from then on.
        var resolved = await _settings.ResolveSettingsAsync(characterId.Trim(), cancellationToken);
        await _settings.SaveSettingsAsync(
            new ReferenceWorkflowSettings
            {
                CharacterProfileId = characterId.Trim(),
                EditorModelId = resolved.EditorModelId,
                FrontModelId = resolved.FrontModelId,
                UpscalerModelName = resolved.UpscalerModelName,
                EnhanceTargetLongEdge = resolved.EnhanceTargetLongEdge,
                EyeGateMaxAbsIrisDyPercent = resolved.EyeGateMaxAbsIrisDyPercent,
                AngleYawMinAbsPercent = resolved.AngleYawMinAbsPercent,
                QualityGateMinSharpness = resolved.QualityGateMinSharpness,
                CropHeadroomPercent = resolved.CropHeadroomPercent,
                CropTargetAspect = resolved.CropTargetAspect,
                DeriveByMirrorThreeQuarterRight = resolved.DeriveByMirrorThreeQuarterRight,
                DeriveByMirrorProfileRight = resolved.DeriveByMirrorProfileRight,
                DeriveByMirrorThreeQuarterLeft = resolved.DeriveByMirrorThreeQuarterLeft,
                DeriveByMirrorProfileLeft = resolved.DeriveByMirrorProfileLeft,
                EyeToolPythonPath = resolved.EyeToolPythonPath,
                BodyModelId = resolved.BodyModelId,
                BodyImageSize = resolved.BodyImageSize,
                LoraCellModelId = modelId.Trim()
            },
            cancellationToken);
    }

    /// <summary>An id is for the operator to recognise, not a second prompt.</summary>
    private static string Shorten(string value)
        => value.Length <= 8 ? value : $"{value[..8]}…";

    /// <summary>
    /// One container per dataset, created on the first attempt and remembered on the dataset. It is created here
    /// rather than in the UI so a dataset can never have two containers and split its own attempts in half.
    /// </summary>
    private async Task<SceneAsset> EnsureContainerAsync(
        CharacterLoraDataset dataset, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(dataset.ContainerAssetId))
        {
            return await _assets.GetAssetAsync(dataset.ContainerAssetId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"The LoRA dataset's container asset '{dataset.ContainerAssetId}' was not found, so its attempts "
                    + "cannot be filed. It was deleted outside this pipeline.");
        }

        var container = await _assets.CreateAssetAsync(
            name: $"{dataset.TriggerToken} · LoRA dataset v{dataset.Version}",
            type: SceneAssetType.Character,
            characterProfileId: dataset.CharacterTemplateId,
            cancellationToken: cancellationToken);
        await _datasets.SetDatasetContainerAsync(dataset.Id, container.Id, cancellationToken);
        return container;
    }

    /// <summary>
    /// The identity conditioning one cell is rendered with, decided from the cell's own reference rule and the
    /// pack's approved references. Pure and static on purpose: this is the decision that makes a training image the
    /// character, so it is the one thing here that must be provable without a database.
    /// <para>
    /// A cell that shows no face gets NO conditioning — a view from behind has no face to hold, and naming one
    /// would be a lie the render path would act on. A cell that shows a face must find its exact-angle approved
    /// reference: falling back to another angle, or to the canonical face, would train the set on a face that does
    /// not match the view.
    /// </para>
    /// </summary>
    public static SceneAssetIdentityConditioning? ResolveIdentityConditioning(
        CoverageRecord record,
        string identityPackId,
        IReadOnlyList<SceneImageReferenceAsset> packAssets)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(packAssets);
        if (string.IsNullOrWhiteSpace(identityPackId))
        {
            throw new InvalidOperationException("A dataset without an identity pack cannot condition a render on identity.");
        }

        if (record.FaceCanonicalSlot is not { } slot)
        {
            return null;
        }

        var matches = packAssets
            .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.Face && asset.FaceView == slot)
            .Where(asset => asset.IsApproved)
            .OrderByDescending(asset => asset.CreatedUtc)
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Identity pack '{identityPackId}' has no approved {slot} face reference, so a cell at that angle "
                + "cannot be rendered as this character. Shoot or approve that view on the Faces tab first.");
        }

        return new SceneAssetIdentityConditioning(identityPackId.Trim(), matches[0].Id);
    }

    /// <summary>
    /// The BODY reference one cell is rendered with — the build axis, decided from the cell's own rule and the
    /// pack's approved full-body references. Pure and static for the same reason as the face decision.
    /// <para>
    /// The match is on the canonical slot the plan recorded AND the state the cell depicts. Matching on the slot
    /// alone would hand a clothed cell the pack's unclothed reference, and a reference IMAGE carries its state with
    /// it: measured 2026-09-23, a bare-shouldered reference made a clothed render come out unclothed. Every cell
    /// states a body reference, including a view from behind (the back slot exists for exactly that).
    /// </para>
    /// <para>
    /// Nothing is substituted: a missing reference fails fast naming the pack, the slot and the state, because a
    /// cell rendered without its build reference is a cell the dataset cannot be trusted on.
    /// </para>
    /// </summary>
    public static SceneAssetBodyReferenceConditioning ResolveBodyConditioning(
        CoverageRecord record,
        string identityPackId,
        IReadOnlyList<SceneImageReferenceAsset> packAssets)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(packAssets);
        if (string.IsNullOrWhiteSpace(identityPackId))
        {
            throw new InvalidOperationException("A dataset without an identity pack cannot condition a render on the body.");
        }

        var matches = packAssets
            .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.FullBody && asset.IsApproved)
            .Where(asset => asset.BodyView == record.BodyCanonicalSlot && asset.BodyState == record.BodyState)
            .OrderByDescending(asset => asset.CreatedUtc)
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Identity pack '{identityPackId}' has no approved {record.BodyCanonicalSlot}/{record.BodyState} body "
                + $"reference, so this cell cannot be rendered on the character's build. Shoot or approve that body "
                + "slot in that state on the Body tab first.");
        }

        return new SceneAssetBodyReferenceConditioning(identityPackId.Trim(), matches[0].Id);
    }

    private async Task<(CharacterLoraDataset Dataset, CoverageRecord Record)> LoadCellAsync(
        string datasetId, string cellKey, CancellationToken cancellationToken)
    {
        var dataset = await RequireDatasetAsync(datasetId, cancellationToken);
        var plan = CoveragePlan.FromJson(dataset.CoveragePlanJson);
        var record = plan.FindRecord(cellKey)
            ?? throw new InvalidOperationException(
                $"Coverage cell '{cellKey}' is not in dataset '{dataset.Id}'s plan, so it cannot be shot.");
        return (dataset, record);
    }

    private async Task<CharacterLoraDataset> RequireDatasetAsync(
        string datasetId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
        {
            throw new InvalidOperationException("A dataset id is required.");
        }

        return await _datasets.GetDatasetAsync(datasetId.Trim(), cancellationToken)
            ?? throw new InvalidOperationException($"LoRA dataset '{datasetId}' was not found.");
    }
}
