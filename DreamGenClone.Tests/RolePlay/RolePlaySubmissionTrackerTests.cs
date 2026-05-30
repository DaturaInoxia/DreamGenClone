using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlaySubmissionTrackerTests
{
    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static RolePlaySubmissionTracker CreateTracker() =>
        new(NullLogger<RolePlaySubmissionTracker>.Instance);

    private static UnifiedPromptSubmission MakeSubmission(string sessionId = "session-1") =>
        new()
        {
            SessionId = sessionId,
            PromptText = "Test prompt",
            Intent = PromptIntent.Message,
            SelectedIdentityId = "char-1"
        };

    private static RolePlayInteraction MakeInteraction() =>
        new() { Content = "Response" };

    /// <summary>
    /// Waits up to <paramref name="timeoutMs"/> milliseconds for <paramref name="condition"/>
    /// to return true, polling every 10 ms.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    // ─── TryBeginSubmission ────────────────────────────────────────────────────

    [Fact]
    public void TryBeginSubmission_FirstCall_ReturnsTrueAndAddsRunningEntry()
    {
        var tracker = CreateTracker();
        var tcs = new TaskCompletionSource<RolePlayInteraction>();

        var result = tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs.Task);

        Assert.True(result);
        var entry = tracker.GetEntry("s1");
        Assert.NotNull(entry);
        Assert.Equal(RolePlaySubmissionStatus.Running, entry.Status);
    }

    [Fact]
    public void TryBeginSubmission_SecondCallForSameSession_ReturnsFalse()
    {
        var tracker = CreateTracker();
        var tcs1 = new TaskCompletionSource<RolePlayInteraction>();
        var tcs2 = new TaskCompletionSource<RolePlayInteraction>();

        tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs1.Task);
        var result = tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs2.Task);

        Assert.False(result);
    }

    [Fact]
    public async Task TryBeginSubmission_WhenTaskCompletes_TransitionsToCompletedAndRemovesEntry()
    {
        var tracker = CreateTracker();
        var tcs = new TaskCompletionSource<RolePlayInteraction>();

        tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs.Task);
        Assert.Equal(RolePlaySubmissionStatus.Running, tracker.GetEntry("s1")!.Status);

        tcs.SetResult(MakeInteraction());

        await WaitForAsync(() => tracker.GetEntry("s1") is null);

        Assert.Null(tracker.GetEntry("s1"));
    }

    [Fact]
    public async Task TryBeginSubmission_WhenTaskFaults_TransitionsToFailedAndRetainsEntry()
    {
        var tracker = CreateTracker();
        var tcs = new TaskCompletionSource<RolePlayInteraction>();

        tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs.Task);

        tcs.SetException(new InvalidOperationException("LLM timed out"));

        await WaitForAsync(() => tracker.GetEntry("s1")?.Status == RolePlaySubmissionStatus.Failed);

        var entry = tracker.GetEntry("s1");
        Assert.NotNull(entry);
        Assert.Equal(RolePlaySubmissionStatus.Failed, entry.Status);
        Assert.Contains("LLM timed out", entry.FailureMessage);
    }

    // ─── AcknowledgeFailure ────────────────────────────────────────────────────

    [Fact]
    public async Task AcknowledgeFailure_FailedEntry_RemovesEntryAndUnblocksSession()
    {
        var tracker = CreateTracker();
        var tcs = new TaskCompletionSource<RolePlayInteraction>();

        tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs.Task);
        tcs.SetException(new Exception("fail"));
        await WaitForAsync(() => tracker.GetEntry("s1")?.Status == RolePlaySubmissionStatus.Failed);

        tracker.AcknowledgeFailure("s1");
        Assert.Null(tracker.GetEntry("s1"));

        // Session is unblocked — a new submission must be accepted.
        var tcs2 = new TaskCompletionSource<RolePlayInteraction>();
        var accepted = tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs2.Task);
        Assert.True(accepted);
    }

    [Fact]
    public void AcknowledgeFailure_RunningEntry_IsNoOp()
    {
        var tracker = CreateTracker();
        var tcs = new TaskCompletionSource<RolePlayInteraction>();

        tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs.Task);

        tracker.AcknowledgeFailure("s1"); // must not remove a Running entry

        var entry = tracker.GetEntry("s1");
        Assert.NotNull(entry);
        Assert.Equal(RolePlaySubmissionStatus.Running, entry.Status);
    }

    [Fact]
    public void AcknowledgeFailure_AbsentEntry_IsNoOp()
    {
        var tracker = CreateTracker();

        // Must not throw.
        tracker.AcknowledgeFailure("does-not-exist");
    }

    // ─── AttachChunkCallback / DetachChunkCallback (T014) ─────────────────────

    [Fact]
    public void AttachChunkCallback_RunningEntry_SwapsInnerCallback()
    {
        var tracker = CreateTracker();
        var tcs = new TaskCompletionSource<RolePlayInteraction>();
        tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs.Task);

        var calls = new List<string>();
        Func<string, Task> callback = chunk =>
        {
            calls.Add(chunk);
            return Task.CompletedTask;
        };

        tracker.AttachChunkCallback("s1", callback);

        var entry = tracker.GetEntry("s1")!;
        _ = entry.ChunkCallbackWrapper.InvokeAsync("hello");

        Assert.Contains("hello", calls);
    }

    [Fact]
    public void AttachChunkCallback_AbsentSession_IsNoOp()
    {
        var tracker = CreateTracker();

        // Must not throw.
        tracker.AttachChunkCallback("no-session", _ => Task.CompletedTask);
    }

    [Fact]
    public void DetachChunkCallback_RunningEntry_SetsInnerToNull()
    {
        var tracker = CreateTracker();
        var tcs = new TaskCompletionSource<RolePlayInteraction>();
        tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs.Task);

        var calls = new List<string>();
        tracker.AttachChunkCallback("s1", chunk => { calls.Add(chunk); return Task.CompletedTask; });
        tracker.DetachChunkCallback("s1");

        var entry = tracker.GetEntry("s1")!;
        _ = entry.ChunkCallbackWrapper.InvokeAsync("should-be-ignored");

        Assert.Empty(calls);
    }

    // ─── OnJobStatusChanged event (T017) ──────────────────────────────────────

    [Fact]
    public async Task OnJobStatusChanged_FiresWithCorrectSessionId_OnCompletion()
    {
        var tracker = CreateTracker();
        var tcs = new TaskCompletionSource<RolePlayInteraction>();
        var firedWith = new List<string>();
        tracker.OnJobStatusChanged += id => firedWith.Add(id);

        tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs.Task);
        tcs.SetResult(MakeInteraction());

        await WaitForAsync(() => firedWith.Count > 0);

        Assert.Single(firedWith);
        Assert.Equal("s1", firedWith[0]);
    }

    [Fact]
    public async Task OnJobStatusChanged_FiresWithCorrectSessionId_OnFailure()
    {
        var tracker = CreateTracker();
        var tcs = new TaskCompletionSource<RolePlayInteraction>();
        var firedWith = new List<string>();
        tracker.OnJobStatusChanged += id => firedWith.Add(id);

        tracker.TryBeginSubmission("s1", MakeSubmission("s1"), tcs.Task);
        tcs.SetException(new Exception("boom"));

        await WaitForAsync(() => firedWith.Count > 0);

        Assert.Single(firedWith);
        Assert.Equal("s1", firedWith[0]);
    }
}
