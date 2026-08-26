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
    Failed = 3
}

/// <summary>How a scene image was created.</summary>
public enum SceneImageOperation
{
    /// <summary>Initial text-to-image render or a regeneration.</summary>
    Generate = 0,

    /// <summary>Source-image edit performed by the configured image editor.</summary>
    Edit = 1
}

/// <summary>Render mode for a scene image: prompt-only or identity-controlled.</summary>
public enum SceneImageRenderMode
{
    /// <summary>Existing Phase 1 path; no continuity guarantee.</summary>
    PromptOnly = 0,

    /// <summary>Identity-conditioned render using an approved identity pack. No prompt-only fallback.</summary>
    IdentityControlled = 1
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
    /// <see cref="SceneImageRenderMode.IdentityControlled"/>. Null for prompt-only renders.</summary>
    public string? IdentityPackId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
