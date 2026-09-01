using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatProductionPlanRepositoryTests
{
    [Fact]
    public async Task CompleteCurrentPlan_PersistsCanonicalJsonAndQueryableProjections()
    {
        var fixture = CreateFixture();
        try
        {
            var (plan, attempt) = CreateVersion(0, "first");
            await fixture.Repository.CreateVersionAsync(plan, attempt);
            Assert.Equal(1, plan.Version);
            Assert.True(await fixture.Repository.TryStartAttemptAsync(
                plan.Id, attempt.Id, "analyzer-model", "Provider", DateTime.UtcNow));
            attempt.RawModelResponse = "{\"schemaVersion\":1}";
            attempt.FinishReason = "stop";
            attempt.ValidationDetailsJson = "{}";

            Assert.True(await fixture.Repository.TryCompleteAttemptAsync(
                plan.Id, attempt, CreateData(plan.Id), DateTime.UtcNow));

            var persisted = await fixture.Repository.GetAsync(plan.Id);
            Assert.NotNull(persisted);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, persisted!.Status);
            Assert.Equal("{\"events\":[]}", persisted.NarrativeArcJson);
            Assert.Single(persisted.DialogueCues);
            Assert.Single(persisted.SoundCues);
            Assert.Single(persisted.VideoCoveragePlans);
            Assert.Equal("dialogue-1", persisted.DialogueCues[0].Id);
            Assert.Equal(SceneVideoCoverageKind.MomentTransition, persisted.VideoCoveragePlans[0].CoverageKind);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task ReverseOrderCompletion_OlderSupersededAttemptCannotOverwriteCurrentPlan()
    {
        var fixture = CreateFixture();
        try
        {
            var (first, firstAttempt) = CreateVersion(1, "first");
            await fixture.Repository.CreateVersionAsync(first, firstAttempt);
            Assert.True(await fixture.Repository.TryStartAttemptAsync(
                first.Id, firstAttempt.Id, "model-1", "Provider", DateTime.UtcNow));

            var (second, secondAttempt) = CreateVersion(2, "second");
            await fixture.Repository.CreateVersionAsync(second, secondAttempt);
            Assert.True(await fixture.Repository.TryStartAttemptAsync(
                second.Id, secondAttempt.Id, "model-2", "Provider", DateTime.UtcNow));
            secondAttempt.RawModelResponse = "{\"new\":true}";
            secondAttempt.ValidationDetailsJson = "{}";
            Assert.True(await fixture.Repository.TryCompleteAttemptAsync(
                second.Id, secondAttempt, CreateData(second.Id), DateTime.UtcNow));

            firstAttempt.RawModelResponse = "{\"old\":true}";
            firstAttempt.ValidationDetailsJson = "{}";
            Assert.False(await fixture.Repository.TryCompleteAttemptAsync(
                first.Id, firstAttempt, CreateData(first.Id), DateTime.UtcNow));

            var current = await fixture.Repository.GetCurrentAsync(second.CatalogueId, second.BeatId);
            Assert.Equal(second.Id, current!.Id);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, current.Status);
            var staleAttempt = await fixture.Repository.GetAttemptAsync(firstAttempt.Id);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Superseded, staleAttempt!.Status);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task FailPendingPlan_RecordsStableErrorWithoutStartingModel()
    {
        var fixture = CreateFixture();
        try
        {
            var (plan, attempt) = CreateVersion(1, "first");
            await fixture.Repository.CreateVersionAsync(plan, attempt);
            attempt.ValidationCode = "production_snapshot_invalid";
            attempt.ValidationDetailsJson = "{\"reason\":\"invalid\"}";

            Assert.True(await fixture.Repository.TryFailAttemptAsync(
                plan.Id, attempt, attempt.ValidationCode, "Snapshot invalid.", DateTime.UtcNow));

            var persisted = await fixture.Repository.GetAsync(plan.Id);
            Assert.Equal(SceneBeatCatalogueStatus.Failed, persisted!.Status);
            Assert.Equal("production_snapshot_invalid", persisted.ErrorCode);
            var persistedAttempt = await fixture.Repository.GetAttemptAsync(attempt.Id);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Failed, persistedAttempt!.Status);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static TestFixture CreateFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-beat-production-{Guid.NewGuid():N}.db");
        return new TestFixture(
            new SceneBeatProductionPlanRepository(Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={path}"
            })),
            path);
    }

    private static (SceneBeatProductionPlan Plan, SceneBeatAnalysisAttempt Attempt) CreateVersion(int version, string suffix)
    {
        var now = DateTime.UtcNow;
        var plan = new SceneBeatProductionPlan
        {
            Id = $"plan-{suffix}",
            CatalogueId = "catalogue-1",
            BeatId = "b1",
            CatalogueVersion = 1,
            Version = version,
            SchemaVersion = 1,
            PromptContractVersion = "beat-production-v1",
            SourceSnapshotJson = "{}",
            ExecutionSettingsJson = "{}",
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = $"attempt-{suffix}",
            OwnerRecordId = plan.Id,
            AttemptNumber = 1,
            JobId = $"job-{suffix}",
            SystemPrompt = "system",
            UserPrompt = "user",
            ValidationDetailsJson = "{}",
            InputCharacters = 10,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        plan.CurrentAttemptId = attempt.Id;
        return (plan, attempt);
    }

    private static SceneBeatProductionPlanData CreateData(string planId)
    {
        var window = new ProductionTimeWindow(
            0m, 2m, "e1", "e2", "short", ProductionWindowPrecision.Estimated, ProductionOverlapPolicy.Allow);
        var performance = new VoicePerformanceIntent(
            "character-1", "en", null, "calm", "low", "measured", null, [], null, [], []);
        var dialogue = new SceneBeatDialogueCue(
            "dialogue-1", planId, 1, SceneBeatDialogueKind.Dialogue, "e1", "Hello.", "Hello.", "Hello.",
            "identity", "1", "interaction-1", 0, 6, "character-1", [], performance, window, true,
            ProductionReviewStatus.Validated, null);
        var sound = new SceneBeatSoundCue(
            "sound-1", planId, 1, SceneBeatSoundKind.SoundEffect, "e1", "hall", "character-1", null,
            "footstep", "brief", true, "center", window, false, null, "hall-room-tone",
            ProductionReviewStatus.Validated, null);
        var video = new SceneVideoCoveragePlan(
            "video-1", planId, "v1", SceneVideoCoverageKind.MomentTransition, window, ["e1", "e2"],
            ["start", "end"], ["action"], "wide", "normal", "track", "measured", [], [dialogue.Id],
            [sound.Id], [], [new(dialogue.Id, "ExternalMix")], true, "preserve delivery", "fit-to-window",
            ProductionReviewStatus.Validated, null);
        return new SceneBeatProductionPlanData(
            "{\"events\":[]}",
            "{\"windows\":[]}",
            "[]",
            "[]",
            "{}",
            "[]",
            "[]",
            "[]",
            "{}",
            "{}",
            "[]",
            "[]",
            [dialogue],
            [sound],
            [video]);
    }

    private static void Cleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
            }
            catch
            {
            }
        }
    }

    private sealed record TestFixture(SceneBeatProductionPlanRepository Repository, string DatabasePath);
}