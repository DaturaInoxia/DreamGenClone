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

    public SceneImageStatus Status { get; set; } = SceneImageStatus.Pending;

    /// <summary>Relative path under the scene-image root, e.g. "{sessionId}/{imageId}.png".</summary>
    public string? FileRelativePath { get; set; }

    public string? ModelIdentifier { get; set; }
    public string? ProviderName { get; set; }
    public ImageContentPolicy ContentPolicy { get; set; } = ImageContentPolicy.Unknown;
    public string? ImageSize { get; set; }
    public string? Style { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Parent image id when this record is a regenerate of another.</summary>
    public string? RegenerateOfId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
