using DreamGenClone.Domain.Processing;
using DreamGenClone.Web.Application.RolePlay.Editing;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The mapping that lets the edit workspace stop waiting on a source description that cannot arrive. Work that is
/// still queued or running must keep the wait alive; only a terminal job ends it, and a failed one has to carry its
/// reason — a panel that waits forever with nothing on screen is the dead end reported on 2026-09-24 (debug/071).
/// </summary>
public sealed class ImageEditDescriptionOutcomeTests
{
    [Theory]
    [InlineData(DurableBackgroundJobStatus.Complete, true, null)]
    [InlineData(DurableBackgroundJobStatus.Failed, true, "the provider served 'x:2', configured 'x'")]
    [InlineData(DurableBackgroundJobStatus.Cancelled, true, "The description job ended as Cancelled.")]
    [InlineData(DurableBackgroundJobStatus.Staged, false, null)]
    [InlineData(DurableBackgroundJobStatus.Queued, false, null)]
    [InlineData(DurableBackgroundJobStatus.Processing, false, null)]
    [InlineData(DurableBackgroundJobStatus.RetryScheduled, false, null)]
    public void From_EndsTheWaitOnlyOnATerminalJob(
        DurableBackgroundJobStatus status, bool expectedTerminal, string? expectedFailure)
    {
        var job = new DurableBackgroundJob
        {
            Id = "scene-asset-image-edit-description:session-1",
            JobType = "scene-asset-image-edit-description",
            Status = status,
            ErrorMessage = status == DurableBackgroundJobStatus.Failed
                ? "the provider served 'x:2', configured 'x'"
                : null
        };

        var outcome = ImageEditDescriptionOutcome.From(job);

        Assert.Equal(expectedTerminal, outcome.IsTerminal);
        Assert.Equal(expectedFailure, outcome.FailureMessage);
    }

    /// <summary>An error code alone is still a reason: the operator gets what the worker recorded, not an empty one.</summary>
    [Fact]
    public void From_FallsBackToTheErrorCode_WhenTheFailureHasNoMessage()
    {
        var job = new DurableBackgroundJob
        {
            Status = DurableBackgroundJobStatus.Failed,
            ErrorCode = "durable_handler_unclassified_failure"
        };

        var outcome = ImageEditDescriptionOutcome.From(job);

        Assert.True(outcome.IsTerminal);
        Assert.Equal("durable_handler_unclassified_failure", outcome.FailureMessage);
    }
}
