using System.Collections.Concurrent;
using System.Threading.Channels;
using DreamGenClone.Application.Processing;

namespace DreamGenClone.Infrastructure.Processing;

public sealed class ModelProcessingQueue : IModelProcessingQueue
{
    private readonly Channel<ModelProcessingTask> _channel = Channel.CreateUnbounded<ModelProcessingTask>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, ModelProcessingTask> _tasks = new();

    public event Action<ModelProcessingTask>? OnStatusChanged;

    internal ChannelReader<ModelProcessingTask> Reader => _channel.Reader;

    public void Enqueue(ModelProcessingTask task)
    {
        // Deduplication: skip if same story + task type is already Queued or Processing
        var isDuplicate = _tasks.Values.Any(t =>
            t.ParsedStoryId == task.ParsedStoryId &&
            t.TaskType == task.TaskType &&
            t.Status is ModelProcessingStatus.Queued or ModelProcessingStatus.Processing);

        if (isDuplicate) return;

        _tasks[task.Id] = task;
        _channel.Writer.TryWrite(task);
        OnStatusChanged?.Invoke(task);
    }

    public void EnqueueStoryProcessing(string parsedStoryId, string? storyTitle, string? rankingProfileId = null)
    {
        Enqueue(new ModelProcessingTask
        {
            ParsedStoryId = parsedStoryId,
            StoryTitle = storyTitle,
            TaskType = ModelProcessingTaskType.Summarize
        });

        Enqueue(new ModelProcessingTask
        {
            ParsedStoryId = parsedStoryId,
            StoryTitle = storyTitle,
            TaskType = ModelProcessingTaskType.Analyze
        });

        if (rankingProfileId is not null)
        {
            Enqueue(new ModelProcessingTask
            {
                ParsedStoryId = parsedStoryId,
                StoryTitle = storyTitle,
                TaskType = ModelProcessingTaskType.Rank,
                RankingProfileId = rankingProfileId
            });
        }
    }

    public IReadOnlyList<ModelProcessingTask> GetAllTasks()
    {
        return _tasks.Values
            .OrderByDescending(t => t.EnqueuedUtc)
            .ToList();
    }

    public IReadOnlyList<ModelProcessingTask> GetTasksForStory(string parsedStoryId)
    {
        return _tasks.Values
            .Where(t => t.ParsedStoryId == parsedStoryId)
            .OrderByDescending(t => t.EnqueuedUtc)
            .ToList();
    }

    public ModelProcessingStatus? GetStoryTaskStatus(string parsedStoryId, ModelProcessingTaskType taskType)
    {
        // Return the most recent task of this type for this story
        return _tasks.Values
            .Where(t => t.ParsedStoryId == parsedStoryId && t.TaskType == taskType)
            .OrderByDescending(t => t.EnqueuedUtc)
            .Select(t => (ModelProcessingStatus?)t.Status)
            .FirstOrDefault();
    }

    public void ClearCompleted()
    {
        var completedIds = _tasks.Values
            .Where(t => t.Status is ModelProcessingStatus.Completed or ModelProcessingStatus.Failed)
            .Select(t => t.Id)
            .ToList();

        foreach (var id in completedIds)
        {
            _tasks.TryRemove(id, out _);
        }
    }

    internal void UpdateTaskStatus(string taskId, ModelProcessingStatus status, string? errorMessage = null)
    {
        if (!_tasks.TryGetValue(taskId, out var task)) return;

        task.Status = status;
        if (status == ModelProcessingStatus.Processing)
            task.StartedUtc = DateTime.UtcNow;
        if (status is ModelProcessingStatus.Completed or ModelProcessingStatus.Failed)
            task.CompletedUtc = DateTime.UtcNow;
        if (errorMessage is not null)
            task.ErrorMessage = errorMessage;

        OnStatusChanged?.Invoke(task);
    }
}
