using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>Lifecycle of a composed scene video (B-152).</summary>
public enum SceneVideoStatus
{
    /// <summary>Job enqueued or staged; the video model has not been called.</summary>
    Pending = 0,

    /// <summary>The worker has claimed the job and the render is running.</summary>
    Rendering = 1,

    /// <summary>The clip (and its normalized audio) is stored.</summary>
    Complete = 2,

    /// <summary>The render failed or the host rejected the graph.</summary>
    Failed = 3,

    /// <summary>Cancelled before the render completed.</summary>
    Cancelled = 4
}

/// <summary>
/// Where a composition came from. The origin is OPTIONAL by design: the composer opens standalone, and a
/// composition may be seeded from a scene image, an Asset Manager image, or a B-100 video coverage plan.
/// </summary>
public enum SceneVideoOriginKind
{
    /// <summary>Opened directly with no seed image.</summary>
    Standalone = 0,

    /// <summary>Seeded from a rendered scene image (its file is the default first reference).</summary>
    SceneImage = 1,

    /// <summary>Seeded from an Asset Manager image.</summary>
    AssetImage = 2,

    /// <summary>
    /// Seeded from a B-100 <c>SceneVideoCoveragePlan</c>, which keeps video INTENT in B-100's hands: the
    /// plan's own prompt cues and its <c>VideoFirstFrame</c> typed reference seed the composition.
    /// </summary>
    VideoCoveragePlan = 3
}

/// <summary>
/// The role one ordered reference image plays in the compiled prompt. Order is semantic (it is the
/// <c>&lt;Picture i&gt;</c> numbering the node requires), so the role travels with the position.
/// </summary>
public enum SceneVideoReferenceRole{
    /// <summary>A concrete frame anchor: it gets its own <c>&lt;Picture N&gt;</c> line.</summary>
    FrameAnchor = 1,

    /// <summary>
    /// Defines a subject (person, wardrobe, prop, style). It is cited INSIDE the <c>&lt;Subject N&gt;</c> line it
    /// defines and never gets a standalone <c>&lt;Picture N&gt;</c> line.
    /// </summary>
    SubjectDefinition = 2,

    /// <summary>A multi-panel character sheet: it gets its own <c>&lt;Picture N&gt;</c> entry naming its panels.</summary>
    CharacterSheet = 3
}

/// <summary>Which storage root a reference's bytes live under. Explicit, never guessed: two roots exist and a
/// silent search across them would hide a misconfigured reference.</summary>
public enum SceneVideoReferenceStorageRoot
{
    /// <summary>The scene-image root (scene images, asset images, identity images). The default.</summary>
    SceneImage = 0,

    /// <summary>The scene-video root - composed clips and the frames extracted from them (B-156).</summary>
    SceneVideo = 1
}

/// <summary>How a continuation carries its source clip forward (B-156).</summary>
public enum SceneVideoContinuationKind
{
    /// <summary>The source's final frame anchors the new clip's first frame.</summary>
    FrameOnly = 1,

    /// <summary>
    /// The final frame AND the source's audio are carried forward, so the model continues the previous track
    /// instead of starting a fresh one. Fails fast when the source has no readable audio.
    /// </summary>
    FrameAndAudio = 2
}

/// <summary>
/// One ordered reference image on a composition: where its bytes live, what role it plays, and the operator's
/// description of the subject it defines.
/// </summary>
/// <param name="Role">How the compiled prompt may cite it.</param>
/// <param name="FileRelativePath">Relative path under the root named by <paramref name="StorageRoot"/>.</param>
/// <param name="FileName">The name the image is uploaded to ComfyUI under.</param>
/// <param name="SubjectDescription">Required when the role defines a subject.</param>
/// <param name="RetentionMarker">Closed-set retention token; resolved by the compiler when null.</param>
/// <param name="DerivedFromReferenceIndex">
/// When this reference was derived from another (the one-click subject crop), the source position. Recorded so
/// the manifest explains where the crop came from.
/// </param>
/// <param name="StorageRoot">
/// Which root <paramref name="FileRelativePath"/> is relative to. Defaults to the scene-image root so every
/// existing composition keeps reading exactly as before; a continuation's extracted anchor frame names the
/// scene-video root explicitly.
/// </param>
public sealed record SceneVideoReference(
    SceneVideoReferenceRole Role,
    string FileRelativePath,
    string FileName,
    string? SubjectDescription = null,
    string? RetentionMarker = null,
    int? DerivedFromReferenceIndex = null,
    SceneVideoReferenceStorageRoot StorageRoot = SceneVideoReferenceStorageRoot.SceneImage);

/// <summary>
/// A composed clip. Files live on disk; this holds the metadata, the full configuration snapshot and the
/// verification evidence, so a render is reproducible and a silent or truncated clip is a visible failure.
/// </summary>
public sealed class SceneVideoRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Composition title, shown in the queue history and the recent list.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Null for the standalone and asset origins - there is no session to hang the clip on.</summary>
    public string? SessionId { get; set; }

    public string? InteractionId { get; set; }

    public SceneVideoStatus Status { get; set; } = SceneVideoStatus.Pending;

    public SceneVideoOriginKind OriginKind { get; set; } = SceneVideoOriginKind.Standalone;

    /// <summary>Scene-image id or asset-image id for the image origins.</summary>
    public string? OriginImageId { get; set; }

    /// <summary>B-100 <c>SceneVideoCoveragePlan</c> id for the coverage origin.</summary>
    public string? OriginCoveragePlanId { get; set; }

    /// <summary>Relative path (under <c>SceneVideoRoot</c>) of the produced mp4.</summary>
    public string? FileRelativePath { get; set; }

    /// <summary>
    /// Relative path of the loudness-normalized deliverable. H3's native audio is ~12 dB below delivery level, so
    /// this - not the raw render - is what the player serves.
    /// </summary>
    public string? NormalizedFileRelativePath { get; set; }

    /// <summary>The compiled prompt as submitted (or the operator's manual edit, flagged below).</summary>
    public string PromptSnapshot { get; set; } = string.Empty;

    /// <summary>True when the operator replaced the compiled prompt by hand.</summary>
    public bool PromptManuallyEdited { get; set; }

    /// <summary>The compiler's key and version, so a render names the rule set that produced its prompt.</summary>
    public string? CompilerKey { get; set; }

    public string? CompilerVersion { get; set; }

    /// <summary>The full configuration snapshot (intent, audio, sampling, dropdowns) as JSON.</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>The ordered reference manifest as JSON (role, path, label, retention).</summary>
    public string? ReferencesJson { get; set; }

    /// <summary>The ordered LoRA stack as JSON (file, strength, trigger token).</summary>
    public string? LoraStackJson { get; set; }

    public string? ModelIdentifier { get; set; }

    /// <summary>Registered video model id the operator pinned. Null = resolve the function default.</summary>
    public string? RequestedModelId { get; set; }

    public string? ProviderName { get; set; }

    public long Seed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }
    public int Steps { get; set; }
    public int Fps { get; set; }
    public string RefImageSize { get; set; } = string.Empty;

    /// <summary>The durable job that renders (or rendered) this composition.</summary>
    public string? JobId { get; set; }

    public string? ErrorMessage { get; set; }

    // ---- Verification (S6): a silent or truncated clip must be a VISIBLE failure ----
    /// <summary>Whether the stored file carries a video stream (measured, not assumed).</summary>
    public bool? VideoStreamPresent { get; set; }

    /// <summary>Whether the stored file carries an audio stream (measured, not assumed).</summary>
    public bool? AudioStreamPresent { get; set; }

    /// <summary>Measured integrated loudness of the delivered file, in LUFS.</summary>
    public double? MeasuredLoudnessLufs { get; set; }

    /// <summary>The loudness target this clip was normalized to, in LUFS.</summary>
    public double? LoudnessTargetLufs { get; set; }

    /// <summary>Measured duration in seconds, from the stored file.</summary>
    public double? MeasuredDurationSeconds { get; set; }

    /// <summary>Any verification warnings (for example "audio stream present but silent").</summary>
    public string? VerificationNotes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    // ---- Continuation (B-156): a chain is a lineage, never an edit ----
    /// <summary>
    /// The clip this one continues, or null when it is an original render. Several continuations may point at
    /// the same source (there is no unique constraint), and a deleted source leaves this pointing at a row that
    /// no longer exists - which the UI must surface as broken lineage rather than treat as standalone.
    /// </summary>
    public string? SourceVideoId { get; set; }

    /// <summary>How the source clip was carried forward. Null iff <see cref="SourceVideoId"/> is null.</summary>
    public SceneVideoContinuationKind? ContinuationKind { get; set; }

    /// <summary>
    /// The frame index the guide pinned in the target clip (0 = the first frame). Configured, recorded, never
    /// inferred: it is what makes the seam reproducible.
    /// </summary>
    public int? GuideFrameIndex { get; set; }

    /// <summary>Relative path of the extracted source frame, under the SCENE-VIDEO root.</summary>
    public string? SourceFrameRelativePath { get; set; }

    /// <summary>The extracted frame's SHA-256; a mismatch means the anchor is not the frame it claims to be.</summary>
    public string? SourceFrameSha256 { get; set; }

    /// <summary>
    /// Per-block drift metrics as JSON (B-156 C-14), measured after the render. Null when not measured - a
    /// continuation draft or an in-flight render has none, and measurement is evidence rather than a gate.
    /// </summary>
    public string? DriftMetricsJson { get; set; }
}
