namespace DreamGenClone.Domain.RolePlay;

/// <summary>Lifecycle of an editable image prompt produced by the pre-processor model.</summary>
public enum SceneImagePromptStatus
{
    /// <summary>Job enqueued; pre-processor not yet run.</summary>
    Pending = 0,

    /// <summary>Pre-processor returned a usable prompt.</summary>
    Complete = 1,

    /// <summary>Pre-processor call failed or returned unusable output.</summary>
    Failed = 2
}

/// <summary>
/// The editable image prompt draft for an interaction. One record per prompt generation/refine
/// attempt. The studio shows <see cref="OutputPrompt"/> in an editable textarea; renders reference
/// this record by id but snapshot the exact prompt text sent to the image model.
/// </summary>
public sealed class SceneImagePromptRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string InteractionId { get; set; } = string.Empty;
    public string BeatAnalysisId { get; set; } = string.Empty;
    public string BeatSnapshotJson { get; set; } = "{}";
    public string? ProductionGroupId { get; set; }
    public string? CompiledMediaBriefId { get; set; }
    public string Pov { get; set; } = string.Empty;

    /// <summary>Snapshot of the studio settings used to build this prompt (style/size/explicitness).</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>The passage pulled from the interaction that this prompt depicts.</summary>
    public string InputExcerpt { get; set; } = string.Empty;

    /// <summary>The editable image prompt produced by the pre-processor.</summary>
    public string OutputPrompt { get; set; } = string.Empty;

    /// <summary>Optional user instruction threaded into the pre-processor on a refine pass.
    /// Persisted so the job handler can apply it — not editable after enqueue.</summary>
    public string? RefineInstruction { get; set; }

    public SceneImagePromptStatus Status { get; set; } = SceneImagePromptStatus.Pending;
    public string? ModelIdentifier { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
