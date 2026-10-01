using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>How a scene asset was produced.</summary>
public enum SceneAssetKind
{
    /// <summary>Text-to-image render by the configured image model (Juggernaut).</summary>
    PromptGenerated = 1,

    /// <summary>Directly uploaded by the user.</summary>
    Uploaded = 2,

    /// <summary>Source-image edit (Qwen) of another asset. The original is untouched.</summary>
    Edited = 3,

    /// <summary>The front (identity anchor) view of a generated profile pack.</summary>
    ProfilePackFront = 4,

    /// <summary>One of the four non-front views (3/4L, 3/4R, profL, profR) of a generated profile pack.</summary>
    ProfilePackFace = 5,

    /// <summary>An explicitly promoted approved production frame. The source file is shared.</summary>
    PromotedApprovedFrame = 6
}

/// <summary>Async generation lifecycle for a scene asset.</summary>
public enum SceneAssetStatus
{
    /// <summary>Generation/editing job enqueued; model not yet called.</summary>
    Pending = 1,

    /// <summary>Bytes saved to disk; the asset is ready to view/download/reference.</summary>
    Complete = 2,

    /// <summary>Generation/editing failed; ErrorMessage holds the reason.</summary>
    Failed = 3
}

public enum SceneAssetType
{
    Location = 1,
    Wardrobe = 2,
    Prop = 3,
    Style = 4,
    CharacterFace = 5,
    CharacterBody = 6,
    ProductionFrame = 7,

    /// <summary>Owner-level character group (a whole character, not one view).</summary>
    Character = 8,

    /// <summary>
    /// One character rendered in a specific pose and wardrobe state — the reusable "character pose asset" (B-130 D8).
    /// </summary>
    /// <remarks>
    /// Appended, never inserted: the <c>SceneAssets.Type</c> column persists this enum's NAME (the reconciliation
    /// service parses it with <c>Enum.TryParse</c>), so a new member cannot disturb an existing row. A pose asset
    /// being a SceneAsset is what lets it reuse the approval / production-version / sha256 validation and the
    /// reference-binding channel the render already revalidates, instead of inventing a second reference mechanism.
    /// </remarks>
    CharacterPose = 9,

    /// <summary>
    /// A scratch container for images an operator makes in the Image Playground (B-135). It is a real asset so the
    /// playground inherits the whole existing loop — the one composer, the edit workspace, the review deck and the
    /// lineage chain — instead of growing a second pipeline, but it is its OWN type so playground scratch images never
    /// appear among production assets that carry approval and licensing meaning.
    /// </summary>
    Playground = 10
}

public enum SceneAssetCandidateDecision
{
    Undecided = 0,
    Accepted = 1,
    Rejected = 2,
    Shortlisted = 3,
    Revoked = 4
}

public enum SceneAssetProductionApprovalStatus
{
    Draft = 1,
    Approved = 2,
    Superseded = 3,
    Revoked = 4
}

public enum SceneAssetConsentState
{
    Unknown = 1,
    Confirmed = 2,
    NotApplicable = 3
}

public enum SceneAssetLicenseState
{
    Unknown = 1,
    Confirmed = 2,
    NotApplicable = 3
}

[Flags]
public enum SceneAssetApprovedUseScope
{
    CharacterIdentity = 1,
    CharacterBody = 2,
    CharacterWardrobe = 4,
    Location = 8,
    Control = 16,
    ProductionSource = 32,
    CharacterLoraTraining = 64
}

/// <summary>
/// A persisted, app-wide generated asset (prompt-generated, uploaded, or edited). Files live under
/// the git-ignored scene-image root and are served at <c>/scene-images/assets/...</c>. This is the
/// reusable asset library that character identity packs, locations, and wardrobe packs build on.
/// </summary>
public sealed class SceneAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>User-settable display label.</summary>
    public string Name { get; set; } = string.Empty;

    public SceneAssetKind Kind { get; set; } = SceneAssetKind.PromptGenerated;

    public SceneAssetStatus Status { get; set; } = SceneAssetStatus.Pending;

    public SceneAssetType? Type { get; set; }

    /// <summary>True when this row is an asset container; image lifecycle data lives in child rows.</summary>
    public bool IsContainerOnly { get; set; }

    public string? AssociationMetadataJson { get; set; }

    /// <summary>The generation or edit prompt that produced this asset (empty for uploads).</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Parent asset when this is an edit revision.</summary>
    public string? SourceAssetId { get; set; }

    /// <summary>Snapshot of the resolved model used to produce this asset (informational).</summary>
    public string? ModelSnapshotJson { get; set; }

    public string? FileRelativePath { get; set; }

    public string MediaType { get; set; } = string.Empty;

    public int? Width { get; set; }
    public int? Height { get; set; }
    public long ByteLength { get; set; }

    /// <summary>Uppercase SHA-256 of the stored bytes.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Face view when this asset is one view of a generated profile pack.</summary>
    public SceneImageReferenceFaceView? FaceView { get; set; }

    /// <summary>Canonical body-slot contract; null for non-body assets and extended body views.</summary>
    public SceneImageReferenceBodyView? BodyView { get; set; }

    /// <summary>Required exactly when this asset is a full-body reference.</summary>
    public SceneImageReferenceBodyState? BodyState { get; set; }

    /// <summary>Serialized <see cref="ReferenceViewDescriptor"/>; carries the fine-grained/extended view data.</summary>
    public string? ViewDescriptorJson { get; set; }

    /// <summary>Identity pack this asset belongs to when generated by a profile pack run.</summary>
    public string? IdentityPackId { get; set; }

    /// <summary>Character the generated profile pack belongs to (characterProfileId).</summary>
    public string? CharacterProfileId { get; set; }

    public string? SourceApprovalDecisionId { get; set; }

    public string? SourceSceneImageId { get; set; }

    public string? SourceSha256 { get; set; }

    public string? SourceProvenanceJson { get; set; }

    public string? CandidateBatchId { get; set; }

    public SceneAssetCandidateDecision? CandidateDecision { get; set; }

    public string? CandidateNotes { get; set; }

    public string? CandidateSourceAssetId { get; set; }

    /// <summary>
    /// Production governance is nullable so historical assets never acquire implicit approval.
    /// </summary>
    public SceneAssetProductionApprovalStatus? ProductionApprovalStatus { get; set; }

    public SceneAssetConsentState? ConsentState { get; set; }

    public SceneAssetLicenseState? LicenseState { get; set; }

    public string? LicenseLabel { get; set; }

    public SceneAssetApprovedUseScope? ApprovedUseScope { get; set; }

    public string? ContentPolicyKey { get; set; }

    public string? CompatibilityMetadataJson { get; set; }

    public int? ProductionVersion { get; set; }

    public string? SupersedesAssetId { get; set; }

    public DateTime? ProductionApprovedUtc { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// An immutable generated, uploaded, or edited image owned by a reusable scene asset. It records the prompt that
/// produced it, the compiler that authored that prompt (<see cref="PromptCompilerId"/>) and the negative it was
/// rendered with, so a candidate can always be explained after the fact.
/// </summary>
public sealed class SceneAssetImage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AssetId { get; set; } = string.Empty;
    public SceneAssetKind Kind { get; set; }
    public SceneAssetStatus Status { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? SourceImageId { get; set; }
    public string? ModelSnapshotJson { get; set; }
    public string? AssociationMetadataJson { get; set; }
    public string? FileRelativePath { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? SourceProvenanceJson { get; set; }
    public SceneAssetProductionApprovalStatus? ProductionApprovalStatus { get; set; }
    public SceneAssetConsentState? ConsentState { get; set; }
    public SceneAssetLicenseState? LicenseState { get; set; }
    public string? LicenseLabel { get; set; }
    public SceneAssetApprovedUseScope? ApprovedUseScope { get; set; }
    public string? ContentPolicyKey { get; set; }
    public string? CompatibilityMetadataJson { get; set; }
    public int? ProductionVersion { get; set; }
    public string? CandidateBatchId { get; set; }
    public SceneAssetCandidateDecision? CandidateDecision { get; set; }
    public string? CandidateNotes { get; set; }

    /// <summary>
    /// The eye-gate result measured for this image, as <see cref="SceneAssetImageValidation"/> JSON.
    /// It belongs to the image, not to the build that produced it: every candidate passes or fails on
    /// its own (B-121 note 001).
    /// </summary>
    public string? ValidationResultJson { get; set; }

    /// <summary>
    /// The front-pipeline steps that produced this image, as <see cref="SceneAssetImagePipeline"/> JSON.
    /// Done-or-skipped is a property of the image itself: one de-clothe/crop/enhance run produces several
    /// attempts, and only the approved one's chain says what was actually done to it (B-121 note 002).
    /// </summary>
    public string? PipelineStepsJson { get; set; }

    /// <summary>
    /// The negative prompt this image was rendered with, or null when its prompt was never compiled by a prompt
    /// compiler. Null is the honest value for a legacy or uploaded image: it records that no negative was authored,
    /// which is a different fact from "the author decided the negative should be empty" (the empty string).
    /// </summary>
    public string? NegativePrompt { get; set; }

    /// <summary>
    /// The prompt compiler that authored <see cref="Prompt"/>, or null when the stored text is still a semantic
    /// description awaiting compilation at render time.
    ///
    /// This is the discriminator the render path reads. It exists so that "compile this description for the model"
    /// and "this text IS the model-ready prompt" are two STATED cases rather than a guess about the text's shape —
    /// and so a compiled prompt is never compiled twice.
    /// </summary>
    public string? PromptCompilerId { get; set; }

    /// <summary>
    /// The sampler seed this render ACTUALLY used, or null for a row written before the seed was recorded.
    ///
    /// <para>
    /// Recorded whichever way the seed was chosen. A run can PIN one (a catalog position declares a seed, so a re-run
    /// reproduces the image) or ask for a fresh one (exploration), and in both cases the number that reached the
    /// sampler is written here — which is what turns a good result into something reproducible instead of a one-off.
    /// </para>
    ///
    /// <para>
    /// Null therefore means "not recorded", never "no seed": every render has one, because the workflow builder draws
    /// a random value when it is given none.
    /// </para>
    /// </summary>
    public long? Seed { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The eye-gate outcome for one image, stored on that image. <see cref="Verdict"/> is the gate decision;
/// the measured values and the raw tool output are kept so the decision can be re-checked later without
/// running the tool again.
/// </summary>
public sealed record SceneAssetImageValidation(
    CharacterIdentityValidationVerdict Verdict,
    double? IrisDyPercent,
    double? EyeDyPercent,
    double? InterocularPixels,
    double? HeadHeightPx,
    double ThresholdPercent,
    string? BlockReason,
    string? RawToolOutput,
    DateTime MeasuredUtc);

/// <summary>
/// Which steps of the front pipeline (de-clothe → crop → enhance) produced one approved image, with the
/// artifact each step contributed to that image's chain. A step the image's own lineage does not contain is
/// recorded as <see cref="CharacterIdentityBuildStepStatus.Skipped"/> with no artifact: it was not run for
/// this image, and recording that is the point (B-121 note 002).
/// </summary>
public sealed record SceneAssetImagePipeline(
    string FrontArtifactId,
    IReadOnlyList<SceneAssetImagePipelineStep> Steps,
    DateTime RecordedUtc);

/// <summary>
/// One step of a <see cref="SceneAssetImagePipeline"/>. <see cref="Outcome"/> is
/// <see cref="CharacterIdentityBuildStepStatus.Complete"/> or
/// <see cref="CharacterIdentityBuildStepStatus.Skipped"/> — the two answers the user asked to be recorded.
/// <see cref="ArtifactId"/> is null for a skipped step: it produced nothing.
/// </summary>
public sealed record SceneAssetImagePipelineStep(
    CharacterIdentityBuildStep Step,
    CharacterIdentityBuildStepStatus Outcome,
    string InputArtifactId,
    string? ArtifactId);
