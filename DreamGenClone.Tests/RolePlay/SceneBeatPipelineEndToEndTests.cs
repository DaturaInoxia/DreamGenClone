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
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatPipelineEndToEndTests
{
    private const string CatalogueResponse = """
        {"schemaVersion":1,"beats":[{"beatId":"b1","order":1,"label":"Conversation","beatSynopsis":"Dean speaks to Becky.","primaryLocation":"entry hall","participants":[{"name":"Becky","involvement":"active"},{"name":"Dean","involvement":"active"}],"evidenceKeys":["n0","c1"]}]}
        """;

    [Fact]
    public async Task EnqueueAndHandle_AllFourStagesThenCompileStill_PersistsExactCurrentLineage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-beat-e2e-{Guid.NewGuid():N}.db");
        try
        {
            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={path}" });
            var catalogues = new SceneBeatCatalogueRepository(options);
            var plans = new SceneBeatProductionPlanRepository(options);
            var momentSets = new SceneMomentSetRepository(options);
            var enrichments = new SceneMomentEnrichmentRepository(options);
            var queue = new RecordingQueue();
            var resolver = new AnalyzerResolver();
            var providerRepository = new ProviderRepository();
            var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
            var session = CreateSession(now);
            var turn = CreateTurn(now);

            var catalogueBuilder = new SceneBeatCatalogueSnapshotBuilder();
            var catalogueService = new SceneBeatPipelineService(
                new SessionReader(session), new TurnReader(turn), new ScenarioReader(), resolver,
                catalogueBuilder, new SceneBeatCatalogueContract(catalogueBuilder), catalogues, queue, TimeProvider.System);
            var catalogue = await catalogueService.EnqueueCatalogueAsync(new(session.Id, turn.TurnId));
            await new SceneBeatCatalogueJobHandler(
                    catalogues, providerRepository, new CompletionClient(CatalogueResponse),
                    new SceneBeatCatalogueContract(catalogueBuilder), TimeProvider.System)
                .HandleAsync(queue.TakeLast(SceneBeatPipelineService.CatalogueJobType));
            catalogue = (await catalogues.GetCurrentByTurnAsync(session.Id, turn.TurnId))!;

            Assert.Equal(SceneBeatCatalogueStatus.Complete, catalogue.Status);
            var entry = Assert.Single(catalogue.Entries);
            Assert.Equal("b1", entry.BeatId);
            Assert.Equal("[\"narrative-1\",\"dialogue-1\"]", entry.EvidenceInteractionIdsJson);

            var productionBuilder = new SceneBeatProductionSnapshotBuilder();
            var productionService = new SceneBeatProductionPipelineService(
                catalogues, plans, resolver, productionBuilder, new SceneBeatProductionContract(), queue, TimeProvider.System);
            var plan = await productionService.EnqueueAsync(new(catalogue.Id, entry.BeatId));
            await new SceneBeatProductionPlanJobHandler(
                    plans, providerRepository, new CompletionClient(SceneBeatProductionParserTests.ValidResponse),
                    new SceneBeatProductionParser(), TimeProvider.System)
                .HandleAsync(queue.TakeLast(SceneBeatProductionPipelineService.JobType));
            plan = (await plans.GetCurrentAsync(catalogue.Id, entry.BeatId))!;

            Assert.Equal(SceneBeatCatalogueStatus.Complete, plan.Status);
            Assert.Equal(catalogue.Id, plan.CatalogueId);
            Assert.Equal(catalogue.Version, plan.CatalogueVersion);
            Assert.Equal("character-dean", Assert.Single(plan.DialogueCues).SpeakerCharacterId);
            Assert.Equal(SceneVideoCoverageKind.MomentTransition, Assert.Single(plan.VideoCoveragePlans).CoverageKind);

            var discoveryBuilder = new SceneMomentDiscoverySnapshotBuilder();
            var discoveryService = new SceneMomentDiscoveryPipelineService(
                plans, momentSets, resolver, discoveryBuilder, new SceneMomentDiscoveryContract(), queue, TimeProvider.System);
            var momentSet = await discoveryService.EnqueueAsync(new(plan.Id));
            var discoveryResponse = SceneBeatMomentDiscoveryJobHandlerTests.ValidResponse.Replace(
                "[\"StillCandidate\",\"VideoEnd\"]",
                "[\"StillCandidate\",\"VideoEnd\",\"SoundEventAnchor\"]",
                StringComparison.Ordinal);
            await new SceneBeatMomentDiscoveryJobHandler(
                    momentSets, providerRepository, new CompletionClient(discoveryResponse),
                    discoveryBuilder, new SceneMomentDiscoveryParser(), TimeProvider.System)
                .HandleAsync(queue.TakeLast(SceneMomentDiscoveryPipelineService.JobType));
            momentSet = (await momentSets.GetCurrentAsync(plan.Id))!;

            Assert.Equal(SceneBeatCatalogueStatus.Complete, momentSet.Status);
            Assert.Equal(plan.Id, momentSet.BeatProductionPlanId);
            Assert.Equal(plan.Version, momentSet.BeatProductionPlanVersion);
            Assert.Equal("m2", momentSet.RecommendedMomentId);
            Assert.Equal(["m1", "m2"], momentSet.Moments.Select(moment => moment.MomentId));
            var recommendedMomentId = Assert.IsType<string>(momentSet.RecommendedMomentId);

            var enrichmentBuilder = new SceneMomentEnrichmentSnapshotBuilder();
            var enrichmentService = new SceneMomentEnrichmentPipelineService(
                momentSets, plans, enrichments, resolver, enrichmentBuilder,
                new SceneMomentEnrichmentContract(), queue, TimeProvider.System);
            var enrichment = await enrichmentService.EnqueueRecommendedAsync(momentSet.Id);
            var enrichmentResponse = SceneMomentEnrichmentJobHandlerTests.ValidResponse.Replace(
                "\"profileKey\": \"p1\", \"involvement\": \"observer\"",
                "\"profileKey\": \"p1\", \"involvement\": \"active\"",
                StringComparison.Ordinal);
            await new SceneMomentEnrichmentJobHandler(
                    enrichments, providerRepository, new CompletionClient(enrichmentResponse),
                    enrichmentBuilder, new SceneMomentEnrichmentParser(), TimeProvider.System)
                .HandleAsync(queue.TakeLast(SceneMomentEnrichmentPipelineService.JobType));
            enrichment = (await enrichments.GetCurrentAsync(momentSet.Id, recommendedMomentId))!;

            Assert.Equal(SceneBeatCatalogueStatus.Complete, enrichment.Status);
            Assert.Equal(plan.Id, enrichment.BeatProductionPlanId);
            Assert.Equal(plan.Version, enrichment.BeatProductionPlanVersion);
            Assert.Equal(momentSet.Id, enrichment.MomentSetId);
            Assert.Equal(momentSet.Version, enrichment.MomentSetVersion);
            Assert.Equal(recommendedMomentId, enrichment.MomentId);
            Assert.Contains("character-becky", enrichment.FrozenStateContractJson, StringComparison.Ordinal);

            var target = new MediaCompilerTargetProfile(
                "still-profile", "1", MediaProductionKind.StillImage, "canonical", "deterministic", "1",
                new HashSet<MediaCompilerCapability>
                {
                    MediaCompilerCapability.FrozenVisualState,
                    MediaCompilerCapability.TypedMediaReferences
                },
                "canonical-request-v1");
            var compiler = new DeterministicMultimodalMediaCompiler(new MediaCompilerDescriptor(
                target.MediaKind, target.FamilyKey, target.CompilerKey, target.CompilerVersion, target.Capabilities));
            var briefRepository = new CapturingBriefRepository();
            var compilationService = new MultimodalMediaCompilationService(
                new MultimodalMediaCompilerRegistry([compiler]), briefRepository, plans, momentSets, enrichments, TimeProvider.System);
            var selectedMoment = Assert.Single(momentSet.Moments, moment => moment.MomentId == recommendedMomentId);

            var brief = await compilationService.CompileAndPersistAsync(new CompileMediaBriefRequest(
                plan, momentSet, selectedMoment, enrichment, target, null, [], [], [], null, null));

            Assert.Equal(MediaCompilerStatus.Complete, brief.Status);
            Assert.Same(brief, Assert.Single(briefRepository.Created));
            Assert.Equal(catalogue.Id, brief.Lineage.CatalogueId);
            Assert.Equal(plan.Id, brief.Lineage.BeatProductionPlanId);
            Assert.Equal(plan.Version, brief.Lineage.BeatProductionPlanVersion);
            Assert.Equal(momentSet.Id, brief.Lineage.MomentSetId);
            Assert.Equal(momentSet.Version, brief.Lineage.MomentSetVersion);
            Assert.Equal(selectedMoment.MomentId, brief.Lineage.MomentId);
            Assert.Equal(enrichment.Id, brief.Lineage.MomentEnrichmentId);
            Assert.Equal(enrichment.Revision, brief.Lineage.MomentEnrichmentRevision);
        }
        finally
        {
            Cleanup(path);
        }
    }

    private static RolePlaySession CreateSession(DateTime now) => new()
    {
        Id = "session-1",
        PersonaCharacterId = "character-becky",
        PersonaName = "Becky",
        PersonaRole = "Wife",
        PersonaGender = "Female",
        ScenarioId = "scenario-1",
        Interactions =
        [
            Interaction("dialogue-1", "Dean", "You're still awake.", InteractionType.User, now),
            Interaction("narrative-1", "Narrative", "Dean speaks to Becky.", InteractionType.System, now.AddSeconds(1))
        ]
    };

    private static RolePlayTurn CreateTurn(DateTime now) => new()
    {
        TurnId = "turn-1", SessionId = "session-1", TurnIndex = 1, TurnKind = "SubmitPrompt",
        TriggerSource = "User", InputInteractionId = "dialogue-1", OutputInteractionIds = ["narrative-1"],
        StartedUtc = now, CompletedUtc = now.AddSeconds(1), Status = RolePlayTurnStatus.Completed
    };

    private static RolePlayInteraction Interaction(string id, string actor, string content, InteractionType type, DateTime createdAt) =>
        new() { Id = id, ActorName = actor, Content = content, InteractionType = type, CreatedAt = createdAt };

    private static ResolvedSceneBeatAnalyzer Analyzer() => new(
        "default-1", "model-1", "provider-1",
        new ResolvedModel("https://provider.example", "/v1/chat/completions", 30, "secret", "analyzer", 0.2, 0.9, 4096, "Provider", false)
        { SupportsThinkingControl = true, ThinkingMode = ThinkingMode.Disabled },
        StructuredOutputMode.StrictJsonSchema, 32768, 4096, 2, 120, 250, [5, 30], 30, 8);

    private static void Cleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(path + suffix)) File.Delete(path + suffix); } catch { }
        }
    }

    private sealed class SessionReader(RolePlaySession session) : ISceneBeatSessionReader
    {
        public Task<RolePlaySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RolePlaySession?>(session.Id == sessionId ? session : null);
    }

    private sealed class TurnReader(RolePlayTurn turn) : IRolePlayTurnReader
    {
        public Task<RolePlayTurn?> GetTurnAsync(string sessionId, string turnId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RolePlayTurn?>(turn.SessionId == sessionId && turn.TurnId == turnId ? turn : null);
    }

    private sealed class ScenarioReader : ISceneBeatScenarioReader
    {
        public Task<IReadOnlyList<Character>?> GetCharactersAsync(string scenarioId) =>
            Task.FromResult<IReadOnlyList<Character>?>(
            [
                new Character { Id = "character-becky", Name = "Becky", Role = "Wife", Gender = "Female" },
                new Character { Id = "character-dean", Name = "Dean", Role = "Husband", Gender = "Male" }
            ]);
    }

    private sealed class AnalyzerResolver : ISceneBeatAnalyzerResolver
    {
        public Task<ResolvedSceneBeatAnalyzer> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Analyzer());
    }

    private sealed class CompletionClient(string response) : IStructuredTextCompletionClient
    {
        public Task<StructuredTextCompletionResult> GenerateAsync(
            ResolvedSceneBeatAnalyzer analyzer,
            StructuredTextCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StructuredTextCompletionResult(response, analyzer.Model.ModelIdentifier, "stop", TimeSpan.FromMilliseconds(20)));
    }

    private sealed class RecordingQueue : IDurableBackgroundJobQueue
    {
        public List<DurableBackgroundJob> Jobs { get; } = [];

        public DurableBackgroundJob TakeLast(string jobType) =>
            Jobs.Last(job => job.JobType == jobType);

        public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        {
            Jobs.Add(job);
            return Task.FromResult(true);
        }

        public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs.SingleOrDefault(job => job.Id == jobId));

        public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task WaitForWorkAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ProviderRepository : IProviderRepository
    {
        public Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Provider?>(id == "provider-1"
                ? new Provider { Id = id, Name = "Provider", ApiKeyEncrypted = "secret" }
                : null);
        public Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingBriefRepository : ICompiledMediaBriefRepository
    {
        public List<CompiledMediaBrief> Created { get; } = [];
        public Task CreateAsync(CompiledMediaBrief brief, CancellationToken cancellationToken = default)
        {
            Created.Add(brief);
            return Task.CompletedTask;
        }
        public Task<CompiledMediaBrief?> GetAsync(string briefId, CancellationToken cancellationToken = default) => Task.FromResult<CompiledMediaBrief?>(null);
        public Task<IReadOnlyList<CompiledMediaBrief>> ListByMomentEnrichmentAsync(string momentEnrichmentId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CompiledMediaBrief>>([]);
        public Task<IReadOnlyList<CompiledMediaBrief>> ListByBeatProductionPlanAsync(string beatProductionPlanId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CompiledMediaBrief>>([]);
    }
}