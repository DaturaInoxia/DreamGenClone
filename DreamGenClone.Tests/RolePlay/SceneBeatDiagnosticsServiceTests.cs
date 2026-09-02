using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatDiagnosticsServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PruneExpired_UsesExactResolvedFunctionConfigurationAndClock()
    {
        var repository = new RepositoryStub();
        var service = new SceneBeatDiagnosticsService(
            repository, new ResolverStub(CreateAnalyzer(retentionDays: 37)), new FixedTimeProvider(Now));

        await service.PruneExpiredAsync(" operator ");

        Assert.Equal("function-default-37", repository.FunctionDefaultId);
        Assert.Equal(37, repository.RetentionDays);
        Assert.Equal(Now.AddDays(-37), repository.CutoffUtc);
        Assert.Equal(Now, repository.PrunedUtc);
        Assert.Equal("operator", repository.Actor);
    }

    [Fact]
    public async Task PruneExpired_PropagatesResolverFailureWithoutCallingRepository()
    {
        var repository = new RepositoryStub();
        var expected = new InvalidOperationException("required analyzer configuration missing");
        var service = new SceneBeatDiagnosticsService(
            repository, new ResolverStub(expected), new FixedTimeProvider(Now));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PruneExpiredAsync("operator"));

        Assert.Same(expected, actual);
        Assert.False(repository.PruneCalled);
    }

    private static ResolvedSceneBeatAnalyzer CreateAnalyzer(int retentionDays)
        => new(
            $"function-default-{retentionDays}", "model-id", "provider-id",
            new ResolvedModel("https://provider.example", "/v1/chat/completions", 30, null,
                "model", 0.2, 0.9, 1000, "provider", false),
            StructuredOutputMode.StrictJsonSchema, 10000, 1000, 1, 30, 1000, [5], retentionDays, 8);

    private sealed class ResolverStub : ISceneBeatAnalyzerResolver
    {
        private readonly ResolvedSceneBeatAnalyzer? _analyzer;
        private readonly Exception? _failure;

        public ResolverStub(ResolvedSceneBeatAnalyzer analyzer) => _analyzer = analyzer;
        public ResolverStub(Exception failure) => _failure = failure;

        public Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default)
            => _failure is null
                ? Task.FromResult(_analyzer!)
                : Task.FromException<ResolvedSceneBeatAnalyzer>(_failure);
    }

    private sealed class RepositoryStub : ISceneBeatDiagnosticsRepository
    {
        public bool PruneCalled { get; private set; }
        public string? FunctionDefaultId { get; private set; }
        public int RetentionDays { get; private set; }
        public DateTime CutoffUtc { get; private set; }
        public DateTime PrunedUtc { get; private set; }
        public string? Actor { get; private set; }

        public Task<SceneBeatStageMetrics> GetMetricsAsync(SceneBeatPipelineStage stage, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneBeatDiagnosticAttemptSummary>> GetRecentDiagnosticsAsync(
            SceneBeatPipelineStage stage, int limit, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SceneBeatDiagnosticsPruneRun> PruneRawDiagnosticsAsync(
            string functionDefaultId, int retentionDays, DateTime cutoffUtc, DateTime prunedUtc,
            string actor, CancellationToken cancellationToken = default)
        {
            PruneCalled = true;
            FunctionDefaultId = functionDefaultId;
            RetentionDays = retentionDays;
            CutoffUtc = cutoffUtc;
            PrunedUtc = prunedUtc;
            Actor = actor;
            return Task.FromResult(new SceneBeatDiagnosticsPruneRun(
                "run", functionDefaultId, retentionDays, cutoffUtc, prunedUtc, actor, 0, 0, 0, 0));
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}