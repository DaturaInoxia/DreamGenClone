namespace DreamGenClone.Application.Processing;

public sealed class ModelProcessingTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ParsedStoryId { get; set; } = string.Empty;
    public string? StoryTitle { get; set; }
    public ModelProcessingTaskType TaskType { get; set; }
    public ModelProcessingStatus Status { get; set; } = ModelProcessingStatus.Queued;
    public string? ThemeProfileId { get; set; }
    public DateTime EnqueuedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
