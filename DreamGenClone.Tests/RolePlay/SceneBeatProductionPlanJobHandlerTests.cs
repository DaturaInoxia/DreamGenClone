using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatProductionPlanJobHandlerTests
{
    [Fact]
    public async Task Handle_ValidOutputCompletesPlanAndPersistsProjections()
    {
        var fixture = await CreateFixtureAsync(SceneBeatProductionParserTests.ValidResponse);
        try
        {
            await fixture.Handler.HandleAsync(fixture.Job);

            var plan = await fixture.Repository.GetAsync(fixture.PlanId);
            Assert.Equal(SceneBeatCatalogueStatus.Complete, plan!.Status);
            Assert.Single(plan.DialogueCues);
            Assert.Equal(2, plan.SoundCues.Count);
            Assert.Single(plan.VideoCoveragePlans);
            var attempt = await fixture.Repository.GetAttemptAsync(fixture.AttemptId);
            Assert.Equal(SceneBeatAnalysisAttemptStatus.Complete, attempt!.Status);
            Assert.Equal("stop", attempt.FinishReason);
        }
        finally { Cleanup(fixture.Path); }
    }

    [Fact]
    public async Task Handle_InvalidOutputPreservesRawResponseAndFailsPlan()
    {
        const string raw = "{\"schemaVersion\":1}";
        var fixture = await CreateFixtureAsync(raw);
        try
        {
            var error = await Assert.ThrowsAsync<DurableJobFailureException>(() => fixture.Handler.HandleAsync(fixture.Job));

            Assert.False(error.IsTransient);
            Assert.Equal("scene_beat_production_output_invalid", error.ErrorCode);
            var plan = await fixture.Repository.GetAsync(fixture.PlanId);
            Assert.Equal(SceneBeatCatalogueStatus.Failed, plan!.Status);
            var attempt = await fixture.Repository.GetAttemptAsync(fixture.AttemptId);
            Assert.Equal(raw, attempt!.RawModelResponse);
            Assert.Equal("scene_beat_production_output_invalid", attempt.ValidationCode);
        }
        finally { Cleanup(fixture.Path); }
    }

    private static async Task<Fixture> CreateFixtureAsync(string response)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-production-handler-{Guid.NewGuid():N}.db");
        var repository = new SceneBeatProductionPlanRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={path}"
        }));
        var now = DateTime.UtcNow;
        var planId = "plan-1";
        var attemptId = "attempt-1";
        var execution = new SceneBeatAnalyzerExecutionSnapshot(
            "default-1", "model-1", "provider-1", "https://provider.example", "/v1/chat/completions",
            30, false, "analyzer", 0.2, 0.9, 4096, "Provider", true, ThinkingMode.Disabled,
            StructuredOutputMode.StrictJsonSchema, 32768, 4096, 2, 120, 250, [5, 30], 30, 8);
        var plan = new SceneBeatProductionPlan
        {
            Id = planId, CatalogueId = "catalogue-1", BeatId = "b1", CatalogueVersion = 1,
            Version = 1, CurrentAttemptId = attemptId, SchemaVersion = 1,
            PromptContractVersion = SceneBeatProductionContract.ContractVersion,
            SourceSnapshotJson = JsonSerializer.Serialize(
                SceneBeatProductionParserTests.CreateSnapshot(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ExecutionSettingsJson = JsonSerializer.Serialize(execution, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedUtc = now, UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = attemptId, OwnerRecordId = planId, AttemptNumber = 1, JobId = "job-1",
            SystemPrompt = "system", UserPrompt = "user", ValidationDetailsJson = "{}",
            InputCharacters = 10, CreatedUtc = now, UpdatedUtc = now
        };
        await repository.CreateVersionAsync(plan, attempt);
        var job = new DurableBackgroundJob
        {
            Id = "job-1", JobType = SceneBeatProductionPipelineService.JobType,
            Lane = DurableJobLane.TextAnalysis,
            PayloadJson = JsonSerializer.Serialize(new SceneBeatProductionPlanJobPayload(planId, attemptId), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            DedupeKey = "production-test", Status = DurableBackgroundJobStatus.Processing,
            AttemptCount = 1, MaxAttempts = 3, LeaseOwner = "test", CreatedUtc = now, UpdatedUtc = now
        };
        var handler = new SceneBeatProductionPlanJobHandler(
            repository, new ProviderRepository(), new CompletionClient(response),
            new SceneBeatProductionParser(), TimeProvider.System);
        return new Fixture(handler, repository, job, planId, attemptId, path);
    }

    private static void Cleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(path + suffix)) File.Delete(path + suffix); } catch { }
        }
    }

    private sealed record Fixture(
        SceneBeatProductionPlanJobHandler Handler,
        SceneBeatProductionPlanRepository Repository,
        DurableBackgroundJob Job,
        string PlanId,
        string AttemptId,
        string Path);

    private sealed class CompletionClient(string response) : IStructuredTextCompletionClient
    {
        public Task<StructuredTextCompletionResult> GenerateAsync(
            ResolvedSceneBeatAnalyzer analyzer,
            StructuredTextCompletionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new StructuredTextCompletionResult(response, analyzer.Model.ModelIdentifier, "stop", TimeSpan.FromMilliseconds(20)));
    }

    private sealed class ProviderRepository : IProviderRepository
    {
        public Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}