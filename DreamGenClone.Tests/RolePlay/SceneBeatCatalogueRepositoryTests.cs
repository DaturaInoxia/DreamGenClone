using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatCatalogueRepositoryTests
{
    [Fact]
    public async Task CurrentAttempt_AloneCanCompleteCatalogueAndPersistEntries()
    {
        var fixture = CreateFixture();
        try
        {
            var (catalogue, attempt) = CreateVersion(1);
            await fixture.Repository.CreateVersionAsync(catalogue, attempt);

            Assert.True(await fixture.Repository.TryStartAttemptAsync(
                catalogue.Id,
                attempt.Id,
                "catalogue-model",
                "catalogue-provider",
                "{\"temperature\":0.2}",
                DateTime.UtcNow));

            var staleAttempt = CreateAttempt(catalogue.Id, "stale-attempt", 2);
            Assert.False(await fixture.Repository.TryCompleteAttemptAsync(
                catalogue.Id,
                staleAttempt,
                [CreateEntry(catalogue.Id)],
                DateTime.UtcNow));

            attempt.RawModelResponse = "{\"beats\":[]}";
            attempt.ValidationDetailsJson = "{}";
            Assert.True(await fixture.Repository.TryCompleteAttemptAsync(
                catalogue.Id,
                attempt,
                [CreateEntry(catalogue.Id)],
                DateTime.UtcNow));

            var persisted = await fixture.Repository.GetAsync(catalogue.Id);
            Assert.NotNull(persisted);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, persisted!.Status);
            Assert.Equal("catalogue-model", persisted.ModelIdentifier);
            Assert.Single(persisted.Entries);
            Assert.Equal("Arrival", persisted.Entries[0].Label);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task CreateVersion_SupersedesPriorCurrentCatalogue()
    {
        var fixture = CreateFixture();
        try
        {
            var (first, firstAttempt) = CreateVersion(1);
            var (second, secondAttempt) = CreateVersion(2);
            await fixture.Repository.CreateVersionAsync(first, firstAttempt);
            await fixture.Repository.CreateVersionAsync(second, secondAttempt);

            var oldVersion = await fixture.Repository.GetAsync(first.Id);
            var current = await fixture.Repository.GetCurrentByTurnAsync(second.SessionId, second.TurnId);

            Assert.Equal(SceneBeatCatalogueStatus.Superseded, oldVersion!.Status);
            Assert.Equal(second.Id, current!.Id);
            Assert.Equal(2, current.Version);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static TestFixture CreateFixture()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scene-beat-catalogue-{Guid.NewGuid():N}.db");
        var repository = new SceneBeatCatalogueRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={databasePath}"
        }));
        return new TestFixture(repository, databasePath);
    }

    private static (SceneBeatCatalogue Catalogue, SceneBeatAnalysisAttempt Attempt) CreateVersion(int version)
    {
        var now = DateTime.UtcNow;
        var catalogue = new SceneBeatCatalogue
        {
            Id = $"catalogue-{version}",
            SessionId = "session-1",
            TurnId = "turn-1",
            Version = version,
            SchemaVersion = 1,
            PromptContractVersion = "catalogue-v1",
            InputSnapshotJson = "{}",
            ExecutionSettingsJson = "{}",
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = CreateAttempt(catalogue.Id, $"attempt-{version}", 1);
        catalogue.CurrentAttemptId = attempt.Id;
        return (catalogue, attempt);
    }

    private static SceneBeatAnalysisAttempt CreateAttempt(string ownerId, string attemptId, int number)
    {
        var now = DateTime.UtcNow;
        return new SceneBeatAnalysisAttempt
        {
            Id = attemptId,
            OwnerRecordId = ownerId,
            AttemptNumber = number,
            JobId = $"job-{attemptId}",
            SystemPrompt = "system",
            UserPrompt = "user",
            ValidationDetailsJson = "{}",
            InputCharacters = 10,
            CreatedUtc = now,
            UpdatedUtc = now
        };
    }

    private static SceneBeatCatalogueEntry CreateEntry(string catalogueId) => new()
    {
        CatalogueId = catalogueId,
        BeatId = "b1",
        Order = 1,
        Label = "Arrival",
        BeatSynopsis = "She arrives in the hall.",
        PrimaryLocation = "hall",
        ParticipantSummaryJson = "[{\"name\":\"Wife\",\"role\":\"active\"}]",
        EvidenceInteractionIdsJson = "[\"narrative-1\"]",
        ContentTagsJson = "[]"
    };

    private static void Cleanup(string databasePath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix);
            }
            catch
            {
            }
        }
    }

    private sealed record TestFixture(SceneBeatCatalogueRepository Repository, string DatabasePath);
}