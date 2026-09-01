using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentEnrichmentRepositoryTests
{
    [Fact]
    public async Task CreateRevision_AllocatesRevisionAndSupersedesPriorRevisionAndActiveAttempt()
    {
        var fixture = CreateFixture();
        try
        {
            var (first, firstAttempt) = CreateRevision("first");
            await fixture.Repository.CreateRevisionAsync(first, firstAttempt);
            Assert.Equal(1, first.Revision);
            Assert.True(await fixture.Repository.TryStartAttemptAsync(
                first.Id, firstAttempt.Id, "model-1", "Provider", DateTime.UtcNow));

            var (second, secondAttempt) = CreateRevision("second");
            await fixture.Repository.CreateRevisionAsync(second, secondAttempt);

            Assert.Equal(2, second.Revision);
            var current = await fixture.Repository.GetCurrentAsync(second.MomentSetId, second.MomentId);
            Assert.NotNull(current);
            Assert.Equal(second.Id, current!.Id);
            Assert.Equal("catalogue-1", current.CatalogueId);
            Assert.Equal("beat-1", current.BeatId);
            Assert.Equal("plan-1", current.BeatProductionPlanId);
            Assert.Equal(3, current.BeatProductionPlanVersion);
            Assert.Equal("moment-set-1", current.MomentSetId);
            Assert.Equal(4, current.MomentSetVersion);
            Assert.Equal("moment-1", current.MomentId);

            var superseded = await fixture.Repository.GetAsync(first.Id);
            Assert.Equal(SceneBeatCatalogueStatus.Superseded, superseded!.Status);
            var supersededAttempt = await fixture.Repository.GetAttemptAsync(firstAttempt.Id);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Superseded, supersededAttempt!.Status);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task CompleteCurrentAttempt_PromotesOnlySuccessfulOutputJson()
    {
        var fixture = CreateFixture();
        try
        {
            var (enrichment, attempt) = CreateRevision("complete");
            await fixture.Repository.CreateRevisionAsync(enrichment, attempt);

            var pending = await fixture.Repository.GetAsync(enrichment.Id);
            Assert.Equal(string.Empty, pending!.FrozenStateContractJson);
            Assert.Equal(string.Empty, pending.InstantaneousSoundEventsJson);
            Assert.Equal(string.Empty, pending.VideoKeyStateJson);

            var startedUtc = DateTime.UtcNow;
            Assert.True(await fixture.Repository.TryStartAttemptAsync(
                enrichment.Id, attempt.Id, "enrichment-model", "Provider", startedUtc));
            attempt.RawModelResponse = "{\"schemaVersion\":1}";
            attempt.FinishReason = "stop";
            attempt.ValidationDetailsJson = "{}";
            var data = new SceneMomentEnrichmentData(
                "{\"location\":\"hall\"}",
                "[{\"eventKey\":\"door\"}]",
                "{\"roles\":[\"VideoStart\"]}");

            Assert.True(await fixture.Repository.TryCompleteAttemptAsync(
                enrichment.Id, attempt, data, DateTime.UtcNow));

            var persisted = await fixture.Repository.GetAsync(enrichment.Id);
            Assert.NotNull(persisted);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, persisted!.Status);
            Assert.Equal(data.FrozenStateContractJson, persisted.FrozenStateContractJson);
            Assert.Equal(data.InstantaneousSoundEventsJson, persisted.InstantaneousSoundEventsJson);
            Assert.Equal(data.VideoKeyStateJson, persisted.VideoKeyStateJson);
            Assert.Equal("enrichment-model", persisted.ModelIdentifier);
            Assert.Equal("Provider", persisted.ProviderName);

            var persistedAttempt = await fixture.Repository.GetAttemptAsync(attempt.Id);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Complete, persistedAttempt!.Status);
            Assert.Equal(attempt.RawModelResponse, persistedAttempt.RawModelResponse);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task ReverseOrderCompletion_SupersededAttemptCannotOverwriteCurrentEnrichment_AndCancellationUsesCas()
    {
        var fixture = CreateFixture();
        try
        {
            var (first, firstAttempt) = CreateRevision("first");
            await fixture.Repository.CreateRevisionAsync(first, firstAttempt);
            Assert.True(await fixture.Repository.TryStartAttemptAsync(
                first.Id, firstAttempt.Id, "model-1", "Provider", DateTime.UtcNow));

            var (second, secondAttempt) = CreateRevision("second");
            await fixture.Repository.CreateRevisionAsync(second, secondAttempt);
            firstAttempt.ValidationDetailsJson = "{}";
            var staleData = new SceneMomentEnrichmentData("{\"stale\":true}", "[]", "{}");

            Assert.False(await fixture.Repository.TryCompleteAttemptAsync(
                first.Id, firstAttempt, staleData, DateTime.UtcNow));
            Assert.False(await fixture.Repository.TryCancelCurrentAsync(
                first.Id, firstAttempt.Id, DateTime.UtcNow));
            Assert.False(await fixture.Repository.TryCancelCurrentAsync(
                second.Id, firstAttempt.Id, DateTime.UtcNow));
            Assert.True(await fixture.Repository.TryCancelCurrentAsync(
                second.Id, secondAttempt.Id, DateTime.UtcNow));
            Assert.False(await fixture.Repository.TryCancelCurrentAsync(
                second.Id, secondAttempt.Id, DateTime.UtcNow));

            var stale = await fixture.Repository.GetAsync(first.Id);
            Assert.Equal(SceneBeatCatalogueStatus.Superseded, stale!.Status);
            Assert.Equal(string.Empty, stale.FrozenStateContractJson);
            var cancelled = await fixture.Repository.GetAsync(second.Id);
            Assert.Equal(SceneBeatCatalogueStatus.Cancelled, cancelled!.Status);
            Assert.Equal(string.Empty, cancelled.FrozenStateContractJson);
            var cancelledAttempt = await fixture.Repository.GetAttemptAsync(secondAttempt.Id);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Cancelled, cancelledAttempt!.Status);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static TestFixture CreateFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-moment-enrichment-{Guid.NewGuid():N}.db");
        return new TestFixture(
            new SceneMomentEnrichmentRepository(Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={path}"
            })),
            path);
    }

    private static (SceneMomentEnrichment Enrichment, SceneBeatAnalysisAttempt Attempt) CreateRevision(string suffix)
    {
        var now = DateTime.UtcNow;
        var enrichment = new SceneMomentEnrichment
        {
            Id = $"enrichment-{suffix}",
            CatalogueId = "catalogue-1",
            BeatId = "beat-1",
            BeatProductionPlanId = "plan-1",
            BeatProductionPlanVersion = 3,
            MomentSetId = "moment-set-1",
            MomentSetVersion = 4,
            MomentId = "moment-1",
            SchemaVersion = 1,
            PromptContractVersion = "moment-enrichment-v1",
            MomentSnapshotJson = "{}",
            TurnEvidenceSnapshotJson = "{}",
            ExecutionSettingsJson = "{}",
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = $"attempt-{suffix}",
            OwnerRecordId = enrichment.Id,
            AttemptNumber = 1,
            JobId = $"job-{suffix}",
            SystemPrompt = "system",
            UserPrompt = "user",
            ValidationDetailsJson = "{}",
            InputCharacters = 10,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        enrichment.CurrentAttemptId = attempt.Id;
        return (enrichment, attempt);
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

    private sealed record TestFixture(SceneMomentEnrichmentRepository Repository, string DatabasePath);
}
