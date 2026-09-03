using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneIdentityEvaluationRepositoryTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public async Task CasesResultsAndDecision_RoundTripAsFrozenEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cases = new[] { Case("case-2", 1), Case("case-1", 0) };
        await fixture.Repository.CreateCasesAsync(cases);
        await fixture.Repository.AddResultAsync(new SceneIdentityEvaluationResult
        {
            Id = "result-1", EvaluationCaseId = "case-1", AttemptId = "attempt-1",
            OutputSha256 = HashB,
            ConstraintScoresJson = "{\"identityA\":\"Pass\",\"identityB\":\"Fail\",\"composition\":\"NotScored\"}",
            Notes = "Dean did not hold at profile.", Reviewer = "operator",
            ReviewedUtc = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)
        });
        await fixture.Repository.RecordDecisionAsync(Decision());

        var loadedCases = await fixture.Repository.ListCasesAsync("run-1");
        var loadedResult = Assert.Single(await fixture.Repository.ListResultsAsync("run-1"));
        var loadedDecision = Assert.Single(await fixture.Repository.ListDecisionsAsync("pack-1"));

        Assert.Equal(new[] { "case-1", "case-2" }, loadedCases.Select(value => value.Id));
        Assert.Contains("\"identityB\":\"Fail\"", loadedResult.ConstraintScoresJson);
        Assert.Equal(SceneImageIdentityDecisionValue.Deferred, loadedDecision.Decision);
        Assert.Equal("run-1", loadedDecision.EvaluationRunId);
    }

    [Fact]
    public async Task AddResult_RejectsUnknownManualScore()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Repository.CreateCasesAsync([Case("case-1", 0)]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.AddResultAsync(new SceneIdentityEvaluationResult
            {
                Id = "result-1", EvaluationCaseId = "case-1", AttemptId = "attempt-1",
                OutputSha256 = HashB, ConstraintScoresJson = "{\"identityA\":\"Maybe\"}",
                Reviewer = "operator", ReviewedUtc = DateTime.UtcNow
            }));

        Assert.Contains("Pass, Fail, or NotScored", exception.Message);
    }

    [Fact]
    public async Task RecordDecision_RejectsDuplicatePackAndRun()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Repository.RecordDecisionAsync(Decision());

        var duplicate = Decision();
        duplicate.Id = "decision-2";

        await Assert.ThrowsAsync<SqliteException>(() => fixture.Repository.RecordDecisionAsync(duplicate));
    }

    [Fact]
    public async Task RecordDecision_RejectsNestedSecretEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var decision = Decision();
        decision.EvidenceJson = "{\"provider\":{\"apiKey\":\"not-allowed\"}}";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.RecordDecisionAsync(decision));

        Assert.Contains("forbidden secret field 'apiKey'", exception.Message);
    }

    private static SceneIdentityEvaluationCase Case(string id, int ordinal) => new()
    {
        Id = id, EvaluationRunId = "run-1", CapabilityCellId = "cell-1", Ordinal = ordinal,
        CharacterPairJson = "[\"Dean\",\"Becky\"]", PoseKey = "facing", ViewKey = "profile",
        Seed = 1001 + ordinal, PromptHash = HashA, ControlHash = HashB,
        ExpectedConstraintsJson = "{\"identityA\":{\"minimum\":4}}",
        CreatedUtc = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc)
    };

    private static CharacterIdentityDecision Decision() => new()
    {
        Id = "decision-1", IdentityPackId = "pack-1", EvaluationRunId = "run-1",
        Decision = SceneImageIdentityDecisionValue.Deferred,
        EvidenceJson = "{\"failedCells\":[\"C2\",\"C3\"],\"scorecardSha256\":\"abc\"}",
        Rationale = "Near-frontal cells are sufficient; angled identity remains unqualified.",
        CreatedUtc = new DateTime(2026, 9, 2, 13, 0, 0, DateTimeKind.Utc)
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _dbPath;
        public SceneIdentityEvaluationRepository Repository { get; }

        private Fixture(string dbPath, SceneIdentityEvaluationRepository repository)
        {
            _dbPath = dbPath;
            Repository = repository;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"identity-evaluation-{Guid.NewGuid():N}.db");
            var repository = new SceneIdentityEvaluationRepository(Options.Create(new PersistenceOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False",
                SceneImageRoot = Path.Combine(Path.GetTempPath(), $"identity-evaluation-output-{Guid.NewGuid():N}")
            }));
            _ = await repository.ListCasesAsync("schema-init");
            return new Fixture(dbPath, repository);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(_dbPath + suffix); } catch { }
            }
            return ValueTask.CompletedTask;
        }
    }
}