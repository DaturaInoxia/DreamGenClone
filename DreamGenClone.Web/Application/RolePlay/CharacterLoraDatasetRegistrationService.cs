using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// One accepted cell image, resolved and ready to become a dataset member: the operator's accepted attempt for a
/// cell, the member row it will produce, and the exact bytes it shares with that row.
/// </summary>
public sealed record LoraCellAcceptance(
    string CellKey,
    int Ordinal,
    LoraCoverageCellRole CellRole,
    CharacterLoraDatasetSplit Split,
    CharacterLoraDatasetMemberRole MemberRole,
    string Caption,
    string CoverageJson,
    SceneAssetImage Image);

/// <summary>
/// A cell that cannot be registered, with the reason. Kept as a LIST rather than a count so the operator is told
/// which cell and why, instead of a total that hides the cause.
/// </summary>
public sealed record LoraCellRefusal(string CellKey, string Reason);

public sealed record LoraCellRegistrationPreview(
    IReadOnlyList<LoraCellAcceptance> Ready,
    IReadOnlyList<LoraCellRefusal> Refused,
    IReadOnlyList<LoraCellRefusal> Pending,
    IReadOnlyList<LoraCellRefusal> Registered);

/// <summary>
/// The facts a production approval has to record, collected ONCE for the whole set. They are the same fields
/// <c>ProductionApprovalForm</c> collects for a single image; none of them is inferable from the render, so none of
/// them is defaulted here.
/// </summary>
public sealed record LoraDatasetApprovalFacts(
    string SourceProvenanceJson,
    SceneAssetConsentState ConsentState,
    SceneAssetLicenseState LicenseState,
    string LicenseLabel,
    SceneAssetApprovedUseScope ApprovedUseScope,
    string ContentPolicyKey,
    string CompatibilityMetadataJson,
    string ReviewedBy);

public sealed record LoraCellRegistrationOutcome(
    int RegisteredCount,
    IReadOnlyList<string> RegisteredCellKeys,
    IReadOnlyList<LoraCellRefusal> Pending);

public interface ICharacterLoraDatasetRegistrationService
{
    /// <summary>
    /// What would be registered right now, cell by cell, without writing anything. A cell is READY when it has
    /// exactly one accepted attempt, PENDING when none has been accepted yet, and REFUSED when more than one has
    /// (which would put two near-identical images in the training set under one cell's identity).
    /// </summary>
    Task<LoraCellRegistrationPreview> PreviewAsync(string datasetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Turn every ready cell into a dataset member: promote a complete Scene Asset that SHARES the accepted image's
    /// bytes and checksum, approve that asset for production with the supplied facts, then register the member. The
    /// dataset must be a Draft.
    /// </summary>
    Task<LoraCellRegistrationOutcome> RegisterAsync(
        string datasetId, LoraDatasetApprovalFacts facts, CancellationToken cancellationToken = default);
}

/// <summary>
/// The step that turns accepted cell images into an actual training set.
///
/// <para>
/// It exists because "accepted" and "in the dataset" are two different facts, and only the second one trains. The
/// cell deck writes <see cref="SceneAssetImage.CandidateDecision"/>; a dataset member is a Scene Asset with its own
/// version and checksum, and freezing verifies that the asset is complete, approved, approved FOR
/// <see cref="SceneAssetApprovedUseScope.CharacterLoraTraining"/>, and carries the exact version and checksum the
/// member claims. Nothing wrote those rows, so a fully-rendered and fully-curated dataset still could not be frozen.
/// </para>
///
/// <para>
/// The promotion reuses the ordinary asset writers rather than the "promoted approved frame" path: that path keys
/// its uniqueness on a roleplay approval decision id, which a LoRA cell attempt does not have. What it does share is
/// the important property — the asset shares the source file and checksum instead of copying bytes.
/// </para>
/// </summary>
public sealed class CharacterLoraDatasetRegistrationService : ICharacterLoraDatasetRegistrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICharacterLoraRepository _datasets;
    private readonly ICharacterLoraCellService _cells;
    private readonly ISceneAssetRepository _assets;
    private readonly IImageWorkflowTemplateService _templates;

    public CharacterLoraDatasetRegistrationService(
        ICharacterLoraRepository datasets,
        ICharacterLoraCellService cells,
        ISceneAssetRepository assets,
        IImageWorkflowTemplateService templates)
    {
        _datasets = datasets;
        _cells = cells;
        _assets = assets;
        _templates = templates;
    }

    public async Task<LoraCellRegistrationPreview> PreviewAsync(
        string datasetId, CancellationToken cancellationToken = default)
    {
        var dataset = await _datasets.GetDatasetAsync(datasetId, cancellationToken)
            ?? throw new InvalidOperationException($"LoRA dataset '{datasetId}' was not found.");
        if (dataset.Status != CharacterLoraDatasetStatus.Draft)
        {
            throw new InvalidOperationException(
                $"LoRA dataset '{dataset.Id}' is {dataset.Status}; members can only be registered while it is a Draft.");
        }

        var plan = CoveragePlan.FromStoredJson(dataset.CoveragePlanJson, out var staleReason);
        if (!string.IsNullOrWhiteSpace(staleReason))
        {
            throw new InvalidOperationException(
                $"LoRA dataset '{dataset.Id}' holds a coverage plan that cannot be registered against: {staleReason}");
        }

        var captionTemplateBody = (await _templates.ResolveAsync(
            LoraCellWorkflowKeys.Caption, dataset.CharacterTemplateId, cancellationToken)).Body;

        // Cells that are already members. Without this the page offers them again, and a second press promotes a
        // second asset per cell - the same image twice in the training set, under one cell's identity.
        var alreadyRegistered = (await _datasets.ListDatasetMembersAsync(dataset.Id, cancellationToken))
            .Select(member => member.Ordinal)
            .ToHashSet();

        var ready = new List<LoraCellAcceptance>();
        var refused = new List<LoraCellRefusal>();
        var pending = new List<LoraCellRefusal>();
        var registered = new List<LoraCellRefusal>();
        var identitySeedTaken = false;

        for (var ordinal = 0; ordinal < plan.Records.Count; ordinal++)
        {
            var record = plan.Records[ordinal];
            if (alreadyRegistered.Contains(ordinal))
            {
                registered.Add(new LoraCellRefusal(record.Key, "Already a member of this dataset."));
                continue;
            }

            var attempts = await _cells.ListCellAttemptsAsync(dataset.Id, record.Key, cancellationToken);
            var accepted = attempts
                .Where(attempt => attempt.CandidateDecision == SceneAssetCandidateDecision.Accepted)
                .ToList();

            if (accepted.Count == 0)
            {
                pending.Add(new LoraCellRefusal(record.Key, "No attempt of this cell has been accepted yet."));
                continue;
            }

            if (accepted.Count > 1)
            {
                refused.Add(new LoraCellRefusal(
                    record.Key,
                    $"{accepted.Count} attempts of this cell are accepted. A cell contributes exactly one training "
                    + "image, so revoke all but the one you want before registering."));
                continue;
            }

            var image = accepted[0];
            if (string.IsNullOrWhiteSpace(image.FileRelativePath)
                || string.IsNullOrWhiteSpace(image.Sha256)
                || image.ByteLength <= 0)
            {
                refused.Add(new LoraCellRefusal(
                    record.Key, "The accepted attempt has no stored bytes or checksum to promote."));
                continue;
            }

            // The identity seed is the set's anchor image: the first face-visible TRAIN cell in plan order. It is
            // derived from the plan rather than picked by hand so a re-registration of the same dataset produces the
            // same roles, and the frozen set always has the one IdentitySeed member a freeze requires.
            var memberRole = record.Split != CharacterLoraDatasetSplit.Train
                ? CharacterLoraDatasetMemberRole.Validation
                : !identitySeedTaken && record.FaceVisible
                    ? CharacterLoraDatasetMemberRole.IdentitySeed
                    : CharacterLoraDatasetMemberRole.Training;
            if (memberRole == CharacterLoraDatasetMemberRole.IdentitySeed)
            {
                identitySeedTaken = true;
            }

            ready.Add(new LoraCellAcceptance(
                record.Key,
                ordinal,
                record.Role,
                record.Split,
                memberRole,
                LoraCellPromptComposer.ComposeCaption(plan, record, captionTemplateBody, lightingTagOverride: null),
                CoverageJson(record),
                image));
        }

        return new LoraCellRegistrationPreview(ready, refused, pending, registered);
    }

    public async Task<LoraCellRegistrationOutcome> RegisterAsync(
        string datasetId, LoraDatasetApprovalFacts facts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var dataset = await _datasets.GetDatasetAsync(datasetId, cancellationToken)
            ?? throw new InvalidOperationException($"LoRA dataset '{datasetId}' was not found.");

        // Every fact is required, and the LoRA scope is required ON TOP of whatever else is granted: an asset that
        // is approved for identity but not for training cannot be a training member, and freezing would refuse it
        // later with a message about a member that looks fine.
        if (string.IsNullOrWhiteSpace(facts.SourceProvenanceJson)) throw new InvalidOperationException("Source provenance JSON is required.");
        if (string.IsNullOrWhiteSpace(facts.LicenseLabel)) throw new InvalidOperationException("License label is required.");
        if (string.IsNullOrWhiteSpace(facts.ContentPolicyKey)) throw new InvalidOperationException("Content policy key is required.");
        if (string.IsNullOrWhiteSpace(facts.CompatibilityMetadataJson)) throw new InvalidOperationException("Compatibility metadata JSON is required.");
        if (string.IsNullOrWhiteSpace(facts.ReviewedBy)) throw new InvalidOperationException("Reviewer id is required.");
        if (facts.ConsentState == SceneAssetConsentState.Unknown)
            throw new InvalidOperationException("Consent must be Confirmed or Not applicable; it was never recorded.");
        if (facts.LicenseState == SceneAssetLicenseState.Unknown)
            throw new InvalidOperationException("License state must be Confirmed or Not applicable; it was never recorded.");
        if (!facts.ApprovedUseScope.HasFlag(SceneAssetApprovedUseScope.CharacterLoraTraining))
        {
            throw new InvalidOperationException(
                "The approved use scope must include CharacterLoraTraining. A dataset member is a training image; "
                + "approving the set for anything else would leave every member refused at freeze time.");
        }

        var preview = await PreviewAsync(datasetId, cancellationToken);
        if (preview.Refused.Count > 0)
        {
            throw new InvalidOperationException(
                $"Registration was refused because {preview.Refused.Count} cell(s) are ambiguous: "
                + string.Join(" | ", preview.Refused.Select(item => $"{item.CellKey}: {item.Reason}")));
        }

        if (preview.Ready.Count == 0)
        {
            // Already done. A repeated press is a no-op with a truthful outcome, not a refusal: the operator pressed
            // the button again because they could not tell whether the first press worked, and answering with a red
            // error is exactly what makes that uncertainty worse.
            if (preview.Registered.Count > 0)
            {
                return new LoraCellRegistrationOutcome(0, [], preview.Pending);
            }

            throw new InvalidOperationException(
                "No cell has exactly one accepted attempt, so there is nothing to register yet.");
        }

        var registered = new List<string>();
        foreach (var acceptance in preview.Ready)
        {
            var approved = await PromoteAndApproveAsync(dataset, acceptance, facts, cancellationToken);
            await _datasets.AddDatasetMemberAsync(new CharacterLoraDatasetMember
            {
                Id = Guid.NewGuid().ToString("N"),
                DatasetId = dataset.Id,
                Ordinal = acceptance.Ordinal,
                SceneAssetId = approved.Id,
                SceneAssetVersion = approved.ProductionVersion
                    ?? throw new InvalidOperationException(
                        $"Promoted asset '{approved.Id}' has no production version after approval."),
                AssetSha256 = approved.Sha256,
                Role = acceptance.MemberRole,
                Split = acceptance.Split,
                Caption = acceptance.Caption,
                CaptionRevision = 1,
                CoverageJson = acceptance.CoverageJson,
                GenerationAttemptId = acceptance.Image.Id,
                CurationStatus = CharacterLoraCurationStatus.Accepted,

                // Honest by construction: the operator accepted this image by eye and NO measurement has run for it.
                // Recording "no findings" here would read as "measured and clean", which is a claim nobody made.
                CurationFindingsJson = CurationFindings.NotScorable(
                    "cell-not-measured",
                    "Accepted by the operator while reviewing the cell; no identity, duplicate or anatomy measurement "
                    + "has been run against this image.").ToJson(),
                ReviewedBy = facts.ReviewedBy.Trim(),
                ReviewedUtc = DateTime.UtcNow
            }, cancellationToken);
            registered.Add(acceptance.CellKey);
        }

        return new LoraCellRegistrationOutcome(registered.Count, registered, preview.Pending);
    }

    /// <summary>
    /// Give the accepted image its own Scene Asset and approve it for production.
    ///
    /// The asset SHARES the image's file, byte length and checksum instead of copying bytes: the dataset member's
    /// checksum check is what ties the member to the exact pixels that were accepted, and a copy would be free to
    /// drift from them.
    /// </summary>
    private async Task<SceneAsset> PromoteAndApproveAsync(
        CharacterLoraDataset dataset,
        LoraCellAcceptance acceptance,
        LoraDatasetApprovalFacts facts,
        CancellationToken cancellationToken)
    {
        var image = acceptance.Image;
        var now = DateTime.UtcNow;
        var asset = new SceneAsset
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{dataset.TriggerToken} {acceptance.CellKey}",
            Kind = SceneAssetKind.PromotedApprovedFrame,
            Status = SceneAssetStatus.Complete,
            Type = SceneAssetType.ProductionFrame,
            Prompt = image.Prompt ?? string.Empty,
            FileRelativePath = image.FileRelativePath,
            MediaType = image.MediaType,
            Width = image.Width,
            Height = image.Height,
            ByteLength = image.ByteLength,
            Sha256 = image.Sha256,
            CharacterProfileId = dataset.CharacterTemplateId,
            AssociationMetadataJson = JsonSerializer.Serialize(new
            {
                source = "lora-cell-acceptance",
                datasetId = dataset.Id,
                datasetVersion = dataset.Version,
                cellKey = acceptance.CellKey,
                cellRole = acceptance.CellRole.ToString(),
                split = acceptance.Split.ToString(),
                memberRole = acceptance.MemberRole.ToString(),
                sourceImageId = image.Id,
                sourceContainerAssetId = image.AssetId,
                sourceModel = image.ModelSnapshotJson
            }, JsonOptions),
            CreatedUtc = now,
            CompletedUtc = image.CompletedUtc ?? now,
            UpdatedUtc = now
        };
        await _assets.UpsertAsync(asset, cancellationToken);

        return await _assets.ApproveForProductionAsync(
            asset.Id,
            facts.SourceProvenanceJson,
            facts.ConsentState,
            facts.LicenseState,
            facts.LicenseLabel,
            facts.ApprovedUseScope,
            facts.ContentPolicyKey,
            facts.CompatibilityMetadataJson,
            cancellationToken);
    }

    /// <summary>The cell's own axes, recorded with the member so a training image can be traced back to its rule.</summary>
    private static string CoverageJson(CoverageRecord record) => JsonSerializer.Serialize(new
    {
        cellKey = record.Key,
        cellRole = record.Role.ToString(),
        angleFamily = record.AngleFamily.ToString(),
        angleYawDeg = record.AngleYawDeg,
        faceVisible = record.FaceVisible,
        faceCanonicalSlot = record.FaceCanonicalSlot?.ToString(),
        bodyCanonicalSlot = record.BodyCanonicalSlot.ToString(),
        bodyState = record.BodyState.ToString(),
        distance = record.Distance.ToString(),
        wardrobeState = record.WardrobeState.ToString(),
        poseClass = record.PoseClass?.ToString(),
        expressionKey = record.ExpressionKey,
        lightingKey = record.LightingKey,
        backgroundKey = record.BackgroundKey,
        outfitKey = record.OutfitKey,
        split = record.Split.ToString(),
        seed = record.Seed
    }, JsonOptions);
}
