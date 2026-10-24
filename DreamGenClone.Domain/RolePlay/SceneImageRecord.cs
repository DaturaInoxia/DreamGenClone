using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>Lifecycle of a rendered scene image.</summary>
public enum SceneImageStatus
{
    /// <summary>Job enqueued; image model not yet called.</summary>
    Pending = 0,

    /// <summary>Worker has started the image call.</summary>
    Generating = 1,

    /// <summary>Image bytes saved to disk.</summary>
    Complete = 2,

    /// <summary>Image call failed or the provider rejected the prompt.</summary>
    Failed = 3,

    /// <summary>Image work was cancelled before the durable job completed.</summary>
    Cancelled = 4
}

/// <summary>How a scene image was created.</summary>
public enum SceneImageOperation
{
    /// <summary>Initial text-to-image render or a regeneration.</summary>
    Generate = 0,

    /// <summary>Source-image edit performed by the configured image editor.</summary>
    Edit = 1,

    /// <summary>
    /// Deterministic operation on a source image (crop). No image model was involved, so the record
    /// carries no model identity and no compiler provenance.
    /// </summary>
    Crop = 2,

    /// <summary>
    /// Enhances a source image through the configured upscaler. Like a crop this involves no editor model
    /// and no prompt, so the record carries the upscaler and target edge instead of a model identity.
    /// </summary>
    Enhance = 3
}

public enum SceneImageFinishChangeClass
{
    Cosmetic = 1,
    Geometry = 2
}

/// <summary>Render mode for a scene image: prompt-only, identity-controlled, or native-reference.</summary>
public enum SceneImageRenderMode
{
    /// <summary>Existing Phase 1 path; no continuity guarantee.</summary>
    PromptOnly = 0,

    /// <summary>Identity-conditioned render using an approved identity pack. No prompt-only fallback.</summary>
    IdentityControlled = 1,

    /// <summary>
    /// Native multi-reference render: the references themselves (character faces AND/OR approved scene
    /// assets such as a location) condition the generation in ONE call, with no identity mechanism and
    /// no editing pass. Only a family that implements reference-conditioned generation can serve it
    /// (Qwen-Image-2.1); anything else fails fast rather than dropping the references.
    /// </summary>
    NativeReference = 2
}

/// <summary>
/// A rendered image for an interaction. Each render is a distinct record (regenerate creates a new
/// one with <see cref="RegenerateOfId"/> pointing at the parent). Files live on disk; this holds
/// metadata only.
/// </summary>
public sealed class SceneImageRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;

    /// <summary>FK to <see cref="SceneImagePromptRecord.Id"/> whose prompt was rendered.</summary>
    public string PromptRecordId { get; set; } = string.Empty;

    /// <summary>Exact prompt text sent to the image model (regenerate/audit).</summary>
    public string PromptSnapshot { get; set; } = string.Empty;

    /// <summary>
    /// The deterministic negative prompt captured at render-enqueue time from the beat + POV.
    /// Consumed by backends that support a separate negative (e.g. ComfyUI). Null/empty for
    /// providers that only accept a single prompt.
    /// </summary>
    public string? NegativePromptSnapshot { get; set; }

    public SceneImageStatus Status { get; set; } = SceneImageStatus.Pending;

    /// <summary>Whether this record was generated from text or edited from a source image.</summary>
    public SceneImageOperation Operation { get; set; } = SceneImageOperation.Generate;

    /// <summary>Required parent image when <see cref="Operation"/> is <see cref="SceneImageOperation.Edit"/>.</summary>
    public string? SourceImageId { get; set; }

    public string? EditSessionId { get; set; }
    public string? EditCompilationAttemptId { get; set; }
    public string? EditPromptRevisionId { get; set; }
    public string? EditIntentSnapshot { get; set; }
    public string? EditCompilerProvenanceJson { get; set; }

    /// <summary>Relative path under the scene-image root, e.g. "{sessionId}/{imageId}.png".</summary>
    public string? FileRelativePath { get; set; }

    public string? ModelIdentifier { get; set; }

    /// <summary>Registered image model id the user pinned for this render. Null = resolve the
    /// configured default for <c>RolePlaySceneImage</c>.</summary>
    public string? RequestedModelId { get; set; }

    public string? ProviderName { get; set; }
    public ImageContentPolicy ContentPolicy { get; set; } = ImageContentPolicy.Unknown;
    public string? ImageSize { get; set; }
    public string? Style { get; set; }

    /// <summary>Snapshot of the full <c>SceneImageStudioSettings</c> (style, size, aspect ratio,
    /// explicitness) used for this render. Enables "continue from this image" — the studio can
    /// restore the exact settings that produced the image.</summary>
    public string SettingsJson { get; set; } = "{}";

    public string? ErrorMessage { get; set; }

    /// <summary>Parent image id when this record is a regenerate of another.</summary>
    public string? RegenerateOfId { get; set; }

    /// <summary>The beat id this image depicts (CR-006 P5), e.g. "b1". Null for legacy images.</summary>
    public string? BeatId { get; set; }

    /// <summary>The POV framing this image was rendered from (CR-006 P5), e.g. "Omniscient", "Becky".
    /// Null for legacy images.</summary>
    public string? Pov { get; set; }

    /// <summary>Render mode: prompt-only or identity-controlled.</summary>
    public SceneImageRenderMode RenderMode { get; set; } = SceneImageRenderMode.PromptOnly;

    /// <summary>Approved identity pack version used when <see cref="RenderMode"/> is
    /// <see cref="SceneImageRenderMode.IdentityControlled"/>. Null for prompt-only renders. For
    /// multi-character renders this is the first selected pack; the full list lives in
    /// <see cref="IdentityPacksJson"/>.</summary>
    public string? IdentityPackId { get; set; }

    /// <summary>
    /// JSON list of identity pack selections for a multi-character identity-controlled render, each
    /// <c>{ packId, characterLabel, strength? }</c>. Null/empty for prompt-only or single-actor
    /// renders. Mirrors the first entry into <see cref="IdentityPackId"/> for gallery compat.
    /// </summary>
    public string? IdentityPacksJson { get; set; }

    public string? ProductionGroupId { get; set; }
    public string? CompiledMediaBriefId { get; set; }
    public SceneImageProductionStage? ProductionStage { get; set; }
    public SceneImageAttemptDisposition? Disposition { get; set; }
    public DateTime? DispositionUpdatedUtc { get; set; }
    public string? CatalogueId { get; set; }
    public string? BeatProductionPlanId { get; set; }
    public int? BeatProductionPlanVersion { get; set; }
    public string? MomentSetId { get; set; }
    public int? MomentSetVersion { get; set; }
    public string? MomentId { get; set; }
    public string? MomentEnrichmentId { get; set; }
    public int? MomentEnrichmentRevision { get; set; }
    public string? TypedReferenceSnapshotJson { get; set; }
    public SceneImageFinishChangeClass? FinishChangeClass { get; set; }
    public bool IdentityStale { get; set; }
    public string? IdentityReferenceBindingsJson { get; set; }
    public string? AppliedReferenceBindingsJson { get; set; }

    /// <summary>
    /// The instruction the image editor ACTUALLY received for this row, when it differs from what was compiled.
    ///
    /// <para>
    /// It exists because the reference role clause — the <c>&lt;imageN&gt;</c> tags and the sentences naming each
    /// reference — is appended at SEND time, from the references that survived validation, and never rewrote
    /// <see cref="PromptSnapshot"/>. So the row could not answer the first question anyone asks of a bad edit: "what
    /// did the model actually read?" (reported 2026-10-15: a bound face reference was composited into the frame as a
    /// second face, and whether the prompt had ever named that image was not answerable from the row at all).
    /// </para>
    ///
    /// <para>
    /// <see cref="PromptSnapshot"/> stays exactly what it was — the accepted prompt revision — because its checksum is
    /// recorded in the compiler provenance and validated before an edit runs. This column is the record of what was
    /// built ON TOP of it. Null for a row no editor ran on (a crop, an enhance, or a render).
    /// </para>
    /// </summary>
    public string? SentInstructionSnapshot { get; set; }
    public string? Sha256 { get; set; }

    /// <summary>
    /// The operator's own name for this rendered image — "the trailer at dusk", "Front", anything that tells it apart
    /// from its siblings in a list. Null means unnamed, and a blank write clears it back to null so "unnamed" has one
    /// representation.
    /// </summary>
    /// <remarks>
    /// Carried on the image rather than on a production attempt because a rendered image's identity is the image: a
    /// name is how an operator retrieves it, and this one is read by the reference resolver when it describes a
    /// location reference in the prompt.
    /// </remarks>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The tags this image carries, as <c>ImageTagCatalog</c> JSON — the same vocabulary and the same shape an asset
    /// image's tags are stored in, so one search and one editor serve both stores.
    /// </summary>
    /// <remarks>
    /// A tag is a claim about a picture, and a rendered image is as findable as a library one: the operator judges both
    /// in a review deck and looks for both by the same words. Null means untagged, which is a different fact from an
    /// empty list only in that nothing has ever been written.
    /// </remarks>
    public string? TagsJson { get; set; }
    public DateTime? BytesPurgedUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
