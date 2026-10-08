namespace DreamGenClone.Domain.Processing;

public enum DurableJobLane
{
    TextAnalysis = 1,
    PromptCompilation = 2,
    ImageRender = 3,
    ImageEdit = 4,

    /// <summary>
    /// Video render (MiniMax H3, B-152). Its own lane because one clip occupies the single GPU for ~25 to ~100
    /// minutes: its concurrency, lease and polling bounds come from the <c>RolePlaySceneVideo</c> function default
    /// rather than the image lanes' settings. Appended, never renumbered - the integer is persisted.
    /// </summary>
    VideoRender = 5
}

public enum DurableBackgroundJobStatus
{
    Staged = 1,
    Queued = 2,
    Processing = 3,
    RetryScheduled = 4,
    Complete = 5,
    Failed = 6,
    Cancelled = 7
}

public sealed class DurableBackgroundJob
{
    public string Id { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public DurableJobLane Lane { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public DurableBackgroundJobStatus Status { get; set; } = DurableBackgroundJobStatus.Queued;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}