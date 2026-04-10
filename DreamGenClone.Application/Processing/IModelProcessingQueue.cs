namespace DreamGenClone.Application.Processing;

public interface IModelProcessingQueue
{
    void Enqueue(ModelProcessingTask task);

    /// <summary>
    /// Convenience method: enqueues Summarize, Analyze, and (optionally) Rank tasks for a story.
    /// </summary>
    void EnqueueStoryProcessing(string parsedStoryId, string? storyTitle, string? rankingProfileId = null);

    IReadOnlyList<ModelProcessingTask> GetAllTasks();

    IReadOnlyList<ModelProcessingTask> GetTasksForStory(string parsedStoryId);

    ModelProcessingStatus? GetStoryTaskStatus(string parsedStoryId, ModelProcessingTaskType taskType);

    void ClearCompleted();

    event Action<ModelProcessingTask>? OnStatusChanged;
}
