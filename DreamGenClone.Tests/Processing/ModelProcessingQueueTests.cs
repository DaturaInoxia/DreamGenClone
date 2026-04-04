using DreamGenClone.Application.Processing;
using DreamGenClone.Infrastructure.Processing;

namespace DreamGenClone.Tests.Processing;

public class ModelProcessingQueueTests
{
    private ModelProcessingQueue CreateQueue() => new();

    [Fact]
    public void Enqueue_AddsTask_And_TracksFIFO()
    {
        var queue = CreateQueue();

        var task1 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        var task2 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Analyze };

        queue.Enqueue(task1);
        queue.Enqueue(task2);

        var all = queue.GetAllTasks();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void Enqueue_Deduplicates_SameStoryAndType_WhenQueued()
    {
        var queue = CreateQueue();

        var task1 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        var task2 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };

        queue.Enqueue(task1);
        queue.Enqueue(task2);

        var all = queue.GetAllTasks();
        Assert.Single(all);
    }

    [Fact]
    public void Enqueue_AllowsSameType_ForDifferentStories()
    {
        var queue = CreateQueue();

        queue.Enqueue(new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize });
        queue.Enqueue(new ModelProcessingTask { ParsedStoryId = "s2", TaskType = ModelProcessingTaskType.Summarize });

        Assert.Equal(2, queue.GetAllTasks().Count);
    }

    [Fact]
    public void Enqueue_AllowsDifferentTypes_ForSameStory()
    {
        var queue = CreateQueue();

        queue.Enqueue(new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize });
        queue.Enqueue(new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Analyze });
        queue.Enqueue(new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Rank, RankingProfileId = "p1" });

        Assert.Equal(3, queue.GetAllTasks().Count);
    }

    [Fact]
    public void Enqueue_AllowsReEnqueue_AfterCompleted()
    {
        var queue = CreateQueue();

        var task1 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        queue.Enqueue(task1);
        queue.UpdateTaskStatus(task1.Id, ModelProcessingStatus.Completed);

        var task2 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        queue.Enqueue(task2);

        Assert.Equal(2, queue.GetAllTasks().Count);
    }

    [Fact]
    public void EnqueueStoryProcessing_Enqueues_ThreeTasksWithProfile()
    {
        var queue = CreateQueue();
        queue.EnqueueStoryProcessing("s1", "Test Story", "profile1");

        var tasks = queue.GetTasksForStory("s1");
        Assert.Equal(3, tasks.Count);
        Assert.Contains(tasks, t => t.TaskType == ModelProcessingTaskType.Summarize);
        Assert.Contains(tasks, t => t.TaskType == ModelProcessingTaskType.Analyze);
        Assert.Contains(tasks, t => t.TaskType == ModelProcessingTaskType.Rank && t.RankingProfileId == "profile1");
    }

    [Fact]
    public void EnqueueStoryProcessing_Enqueues_TwoTasksWithoutProfile()
    {
        var queue = CreateQueue();
        queue.EnqueueStoryProcessing("s1", "Test Story");

        var tasks = queue.GetTasksForStory("s1");
        Assert.Equal(2, tasks.Count);
        Assert.DoesNotContain(tasks, t => t.TaskType == ModelProcessingTaskType.Rank);
    }

    [Fact]
    public void GetStoryTaskStatus_ReturnsNull_WhenNoTask()
    {
        var queue = CreateQueue();
        Assert.Null(queue.GetStoryTaskStatus("s1", ModelProcessingTaskType.Summarize));
    }

    [Fact]
    public void GetStoryTaskStatus_ReturnsCorrectStatus()
    {
        var queue = CreateQueue();
        var task = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Analyze };
        queue.Enqueue(task);

        Assert.Equal(ModelProcessingStatus.Queued, queue.GetStoryTaskStatus("s1", ModelProcessingTaskType.Analyze));

        queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Processing);
        Assert.Equal(ModelProcessingStatus.Processing, queue.GetStoryTaskStatus("s1", ModelProcessingTaskType.Analyze));
    }

    [Fact]
    public void UpdateTaskStatus_SetsTimestamps()
    {
        var queue = CreateQueue();
        var task = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        queue.Enqueue(task);

        Assert.Null(task.StartedUtc);
        Assert.Null(task.CompletedUtc);

        queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Processing);
        Assert.NotNull(task.StartedUtc);
        Assert.Null(task.CompletedUtc);

        queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Completed);
        Assert.NotNull(task.CompletedUtc);
    }

    [Fact]
    public void UpdateTaskStatus_SetsErrorMessage_OnFailed()
    {
        var queue = CreateQueue();
        var task = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        queue.Enqueue(task);

        queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Failed, "LLM timeout");
        Assert.Equal("LLM timeout", task.ErrorMessage);
        Assert.Equal(ModelProcessingStatus.Failed, task.Status);
    }

    [Fact]
    public void ClearCompleted_RemovesCompletedAndFailed()
    {
        var queue = CreateQueue();

        var t1 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        var t2 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Analyze };
        var t3 = new ModelProcessingTask { ParsedStoryId = "s2", TaskType = ModelProcessingTaskType.Summarize };

        queue.Enqueue(t1);
        queue.Enqueue(t2);
        queue.Enqueue(t3);

        queue.UpdateTaskStatus(t1.Id, ModelProcessingStatus.Completed);
        queue.UpdateTaskStatus(t2.Id, ModelProcessingStatus.Failed, "err");

        queue.ClearCompleted();

        var remaining = queue.GetAllTasks();
        Assert.Single(remaining);
        Assert.Equal(t3.Id, remaining[0].Id);
    }

    [Fact]
    public void OnStatusChanged_FiresOnEnqueue()
    {
        var queue = CreateQueue();
        ModelProcessingTask? notifiedTask = null;
        queue.OnStatusChanged += t => notifiedTask = t;

        var task = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        queue.Enqueue(task);

        Assert.NotNull(notifiedTask);
        Assert.Equal(task.Id, notifiedTask!.Id);
    }

    [Fact]
    public void OnStatusChanged_FiresOnStatusUpdate()
    {
        var queue = CreateQueue();
        var task = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        queue.Enqueue(task);

        var notifications = new List<ModelProcessingStatus>();
        queue.OnStatusChanged += t => notifications.Add(t.Status);

        queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Processing);
        queue.UpdateTaskStatus(task.Id, ModelProcessingStatus.Completed);

        Assert.Equal(2, notifications.Count);
        Assert.Equal(ModelProcessingStatus.Processing, notifications[0]);
        Assert.Equal(ModelProcessingStatus.Completed, notifications[1]);
    }

    [Fact]
    public void OnStatusChanged_DoesNotFire_OnDuplicateEnqueue()
    {
        var queue = CreateQueue();
        var count = 0;
        queue.OnStatusChanged += _ => count++;

        queue.Enqueue(new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize });
        queue.Enqueue(new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize }); // duplicate

        Assert.Equal(1, count);
    }

    [Fact]
    public void Reader_YieldsEnqueuedTasks_InOrder()
    {
        var queue = CreateQueue();

        var t1 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Summarize };
        var t2 = new ModelProcessingTask { ParsedStoryId = "s1", TaskType = ModelProcessingTaskType.Analyze };

        queue.Enqueue(t1);
        queue.Enqueue(t2);

        // Reader should yield in FIFO order
        Assert.True(queue.Reader.TryRead(out var read1));
        Assert.Equal(t1.Id, read1!.Id);
        Assert.True(queue.Reader.TryRead(out var read2));
        Assert.Equal(t2.Id, read2!.Id);
    }
}
