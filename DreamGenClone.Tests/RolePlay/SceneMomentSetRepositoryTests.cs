using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentSetRepositoryTests
{
    [Fact]
    public async Task CompleteCurrentSet_PersistsRecommendationAndOrderedMoments()
    {
        var fixture = CreateFixture();
        try
        {
            var (momentSet, attempt) = CreateVersion(0, "first");
            await fixture.Repository.CreateVersionAsync(momentSet, attempt);
            Assert.Equal(1, momentSet.Version);
            Assert.True(await fixture.Repository.TryStartAttemptAsync(
                momentSet.Id, attempt.Id, "analyzer-model", "Provider", DateTime.UtcNow));
            attempt.RawModelResponse = "{\"schemaVersion\":1}";
            attempt.ValidationDetailsJson = "{}";

            Assert.True(await fixture.Repository.TryCompleteAttemptAsync(
                momentSet.Id, attempt, CreateData(momentSet.Id), DateTime.UtcNow));

            var persisted = await fixture.Repository.GetAsync(momentSet.Id);
            Assert.NotNull(persisted);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, persisted!.Status);
            Assert.Equal("m2", persisted.RecommendedMomentId);
            Assert.Equal(["m1", "m2"], persisted.Moments.Select(moment => moment.MomentId));
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task ReverseOrderCompletion_SupersededAttemptCannotPromoteMoments()
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
            secondAttempt.ValidationDetailsJson = "{}";
            Assert.True(await fixture.Repository.TryCompleteAttemptAsync(
                second.Id, secondAttempt, CreateData(second.Id), DateTime.UtcNow));

            firstAttempt.ValidationDetailsJson = "{}";
            Assert.False(await fixture.Repository.TryCompleteAttemptAsync(
                first.Id, firstAttempt, CreateData(first.Id), DateTime.UtcNow));

            var current = await fixture.Repository.GetCurrentAsync(second.BeatProductionPlanId);
            Assert.Equal(second.Id, current!.Id);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, current.Status);
            var stale = await fixture.Repository.GetAsync(first.Id);
            Assert.Equal(SceneBeatCatalogueStatus.Superseded, stale!.Status);
            Assert.Empty(stale.Moments);
            var staleAttempt = await fixture.Repository.GetAttemptAsync(firstAttempt.Id);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Superseded, staleAttempt!.Status);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task CompleteSet_RejectsFewerThanTwoMoments()
    {
        var fixture = CreateFixture();
        try
        {
            var (momentSet, attempt) = CreateVersion(1, "first");
            await fixture.Repository.CreateVersionAsync(momentSet, attempt);
            Assert.True(await fixture.Repository.TryStartAttemptAsync(
                momentSet.Id, attempt.Id, "model-1", "Provider", DateTime.UtcNow));
            var data = CreateData(momentSet.Id);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Repository.TryCompleteAttemptAsync(
                    momentSet.Id,
                    attempt,
                    new SceneMomentSetData("m1", data.Moments.Take(1).ToList()),
                    DateTime.UtcNow));

            Assert.Contains("2 to 4 Moments", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static TestFixture CreateFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-moment-set-{Guid.NewGuid():N}.db");
        return new TestFixture(
            new SceneMomentSetRepository(Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={path}"
            })),
            path);
    }

    private static (SceneMomentSet MomentSet, SceneBeatAnalysisAttempt Attempt) CreateVersion(int version, string suffix)
    {
        var now = DateTime.UtcNow;
        var momentSet = new SceneMomentSet
        {
            Id = $"moment-set-{suffix}",
            CatalogueId = "catalogue-1",
            BeatId = "b1",
            BeatProductionPlanId = "plan-1",
            BeatProductionPlanVersion = 1,
            Version = version,
            SchemaVersion = 1,
            PromptContractVersion = "moment-discovery-v1",
            BeatSnapshotJson = "{}",
            TurnEvidenceSnapshotJson = "{}",
            ExecutionSettingsJson = "{}",
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = $"attempt-{suffix}",
            OwnerRecordId = momentSet.Id,
            AttemptNumber = 1,
            JobId = $"job-{suffix}",
            SystemPrompt = "system",
            UserPrompt = "user",
            ValidationDetailsJson = "{}",
            InputCharacters = 10,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        momentSet.CurrentAttemptId = attempt.Id;
        return (momentSet, attempt);
    }

    private static SceneMomentSetData CreateData(string momentSetId)
        => new("m2",
        [
            CreateMoment(momentSetId, "m1", 1, "Threshold", "[\"VideoStart\"]"),
            CreateMoment(momentSetId, "m2", 2, "Exchanged look", "[\"StillCandidate\",\"VideoEnd\"]")
        ]);

    private static SceneMoment CreateMoment(
        string momentSetId,
        string momentId,
        int order,
        string label,
        string rolesJson)
        => new()
        {
            MomentSetId = momentSetId,
            MomentId = momentId,
            Order = order,
            Label = label,
            TemporalAnchor = $"instant {order}",
            FrozenState = $"frozen state {order}",
            VisibleAction = "holding position",
            ParticipantSummaryJson = "[]",
            CompositionRationale = "Clear state transition.",
            ProductionRolesJson = rolesJson,
            EvidenceInteractionIdsJson = "[\"interaction-1\"]"
        };

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

    private sealed record TestFixture(SceneMomentSetRepository Repository, string DatabasePath);
}