using System.Reflection;
using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class StoryPresentationImportServiceTests
{
    [Fact]
    public async Task Import_PreservesExplicitOrderAndExactCurrentProductionFactsWithStableChecksum()
    {
        var first = Fixture.Create("plan-1", "beat-1", 1);
        var second = Fixture.Create("plan-2", "beat-2", 2);
        var service = CreateService(first, second);

        var result = await service.ImportAsync(new StoryPresentationImportRequest("session-1", [second.Plan.Id, first.Plan.Id]));
        var repeated = await service.ImportAsync(new StoryPresentationImportRequest("session-1", [second.Plan.Id, first.Plan.Id]));

        Assert.True(result.Success);
        Assert.Empty(result.Findings);
        Assert.Equal([second.Plan.Id, first.Plan.Id], result.Snapshot!.ProductionPlans.Select(plan => plan.Id));
        Assert.Equal(result.CanonicalSnapshotJson, repeated.CanonicalSnapshotJson);
        Assert.Equal(result.SourceChecksum, repeated.SourceChecksum);
        Assert.Matches("^[0-9a-f]{64}$", result.SourceChecksum!);

        var imported = result.Snapshot.ProductionPlans[0];
        Assert.Equal(second.Plan.DialogueCues[0], imported.DialogueCues[0]);
        Assert.Equal(second.Plan.SoundCues[0], imported.SoundCues[0]);
        Assert.Equal(second.Plan.VideoCoveragePlans[0], imported.VideoCoveragePlans[0]);
        Assert.Equal(second.Plan.ActionArcJson, imported.ActionArcJson);
        Assert.Equal(second.Plan.AmbiencePlanJson, imported.AmbiencePlanJson);
        Assert.Equal(second.Plan.MusicPlanJson, imported.MusicPlanJson);
        Assert.Equal(second.Moment.FrozenState, imported.MomentSet.Moments[0].FrozenState);
        Assert.Equal(second.Enrichment.FrozenStateContractJson, imported.MomentSet.Enrichments[0].FrozenStateContractJson);
        Assert.DoesNotContain("sourceSnapshotJson", result.CanonicalSnapshotJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("turnEvidenceSnapshotJson", result.CanonicalSnapshotJson!, StringComparison.OrdinalIgnoreCase);

        using var parsed = JsonDocument.Parse(result.CanonicalSnapshotJson!);
        Assert.Equal("plan-2", parsed.RootElement.GetProperty("productionPlans")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Import_MissingMomentEnrichment_ReturnsTypedSourceMissingWithoutSnapshot()
    {
        var fixture = Fixture.Create("plan-1", "beat-1", 1);
        var service = new StoryPresentationImportService(
            new CatalogueRepository([fixture]),
            new PlanRepository([fixture]),
            new MomentSetRepository([fixture]),
            new EnrichmentRepository([]));

        var result = await service.ImportAsync(new StoryPresentationImportRequest("session-1", [fixture.Plan.Id]));

        Assert.False(result.Success);
        Assert.Null(result.Snapshot);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(StoryPresentationImportFailureCode.SourceMissing, finding.Code);
        Assert.Equal([fixture.Moment.MomentId], finding.AffectedIds);
    }

    [Fact]
    public async Task Import_StaleEnrichmentLineage_ReturnsTypedSourceVersionStaleWithoutRepair()
    {
        var fixture = Fixture.Create("plan-1", "beat-1", 1);
        fixture.Enrichment.MomentSetVersion++;
        var service = CreateService(fixture);

        var result = await service.ImportAsync(new StoryPresentationImportRequest("session-1", [fixture.Plan.Id]));

        Assert.False(result.Success);
        Assert.Null(result.CanonicalSnapshotJson);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(StoryPresentationImportFailureCode.SourceVersionStale, finding.Code);
        Assert.Contains(fixture.Enrichment.Id, finding.AffectedIds);
        Assert.Equal(fixture.MomentSet.Version + 1, fixture.Enrichment.MomentSetVersion);
    }

    [Fact]
    public void ImportBoundary_HasNoModelSessionInteractionPromptRawProseOrProviderDependency()
    {
        var forbidden = new[]
        {
            "ICompletionClient", "IModelResolutionService", "RolePlaySession", "RolePlayInteraction",
            "Prompt", "RawProse", "ProviderClient"
        };
        var publicTypes = new[]
        {
            typeof(IStoryPresentationImportService), typeof(StoryPresentationImportRequest),
            typeof(StoryPresentationImportResult), typeof(StoryPresentationImportSnapshot),
            typeof(StoryPresentationProductionPlanSnapshot)
        };
        var contractSurface = publicTypes.SelectMany(type =>
                type.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType)
                    .Concat(type.GetProperties().Select(property => property.PropertyType))
                    .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .SelectMany(method => method.GetParameters()).Select(parameter => parameter.ParameterType)))
            .Select(type => type.FullName ?? type.Name);
        var constructorSurface = typeof(StoryPresentationImportService).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name);

        Assert.DoesNotContain(contractSurface.Concat(constructorSurface),
            name => forbidden.Any(item => name.Contains(item, StringComparison.Ordinal)));
    }

    private static StoryPresentationImportService CreateService(params Fixture[] fixtures) => new(
        new CatalogueRepository(fixtures),
        new PlanRepository(fixtures),
        new MomentSetRepository(fixtures),
        new EnrichmentRepository(fixtures));

    private sealed class Fixture
    {
        public required SceneBeatCatalogue Catalogue { get; init; }
        public required SceneBeatProductionPlan Plan { get; init; }
        public required SceneMomentSet MomentSet { get; init; }
        public required SceneMoment Moment { get; init; }
        public required SceneMomentEnrichment Enrichment { get; init; }

        public static Fixture Create(string planId, string beatId, int order)
        {
            var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
            var window = new ProductionTimeWindow(0, 2, "e1", "e1", "two seconds",
                ProductionWindowPrecision.Exact, ProductionOverlapPolicy.Disallow);
            var performance = new VoicePerformanceIntent("character-1", "en", "en-US", "calm", "low", "measured", null, [], null, [], []);
            var dialogue = new SceneBeatDialogueCue($"dialogue-{order}", planId, 1, SceneBeatDialogueKind.Dialogue, "e1",
                "Exact words.", "Exact words.", "Exact words.", "none", "1", $"interaction-{order}", 0, 12,
                "character-1", [], performance, window, true, ProductionReviewStatus.Validated, null);
            var sound = new SceneBeatSoundCue($"sound-{order}", planId, 1, SceneBeatSoundKind.Ambience, "e1", "hall", null,
                null, "room tone", "steady", true, "stereo", window, true, null, "hall-tone", ProductionReviewStatus.Validated, null);
            var coverage = new SceneVideoCoveragePlan($"coverage-{order}", planId, $"v{order}", SceneVideoCoverageKind.MomentHold,
                window, ["e1"], ["start"], [], "locked medium", "50mm", "none", "hold", [], [dialogue.Id], [sound.Id], ["m1"],
                [new(dialogue.Id, "ExternalMix")], false, "hold", "strict", ProductionReviewStatus.Validated, null);
            var plan = new SceneBeatProductionPlan
            {
                Id = planId, CatalogueId = $"catalogue-{order}", BeatId = beatId, CatalogueVersion = 1, Version = order,
                Status = SceneBeatCatalogueStatus.Complete, NarrativeArcJson = "[{\"eventKey\":\"e1\"}]",
                TimelineJson = "{\"durationIntent\":\"two seconds\"}", NarrationCuesJson = "[]", DialogueCuesJson = "[{\"cueKey\":\"d1\"}]",
                AmbiencePlanJson = "{\"location\":\"hall\"}", SoundEventCuesJson = "[]", MusicPlanJson = "[{\"sectionKey\":\"m1\"}]",
                ActionArcJson = "[{\"action\":\"turns\"}]", StartContinuityJson = "{\"location\":\"hall\"}",
                EndContinuityJson = "{\"location\":\"hall\"}", TypedReferencesJson = "[]", VideoCoveragePlansJson = "[{\"kind\":\"MomentHold\"}]",
                DialogueCues = [dialogue], SoundCues = [sound], VideoCoveragePlans = [coverage], CreatedUtc = now, CompletedUtc = now, UpdatedUtc = now
            };
            var entry = new SceneBeatCatalogueEntry
            {
                CatalogueId = plan.CatalogueId, BeatId = beatId, Order = order, Label = $"Beat {order}", BeatSynopsis = "A canonical event.",
                PrimaryLocation = "hall", ParticipantSummaryJson = "[]", EvidenceInteractionIdsJson = $"[\"interaction-{order}\"]", ContentTagsJson = "[]"
            };
            var catalogue = new SceneBeatCatalogue
            {
                Id = plan.CatalogueId, SessionId = "session-1", TurnId = $"turn-{order}", Version = 1,
                Status = SceneBeatCatalogueStatus.Complete, Entries = [entry], CreatedUtc = now, CompletedUtc = now, UpdatedUtc = now
            };
            var moment = new SceneMoment
            {
                MomentSetId = $"set-{order}", MomentId = $"moment-{order}", Order = 1, Label = "Frozen instant",
                TemporalAnchor = "at one second", FrozenState = "Character is still.", VisibleAction = "turning",
                ParticipantSummaryJson = "[]", CompositionRationale = "Clear state", ProductionRolesJson = "[\"StillCandidate\"]",
                EvidenceInteractionIdsJson = $"[\"interaction-{order}\"]"
            };
            var momentSet = new SceneMomentSet
            {
                Id = moment.MomentSetId, CatalogueId = plan.CatalogueId, BeatId = beatId, BeatProductionPlanId = planId,
                BeatProductionPlanVersion = plan.Version, Version = order, Status = SceneBeatCatalogueStatus.Complete,
                RecommendedMomentId = moment.MomentId, Moments = [moment], CreatedUtc = now, CompletedUtc = now, UpdatedUtc = now
            };
            var enrichment = new SceneMomentEnrichment
            {
                Id = $"enrichment-{order}", CatalogueId = plan.CatalogueId, BeatId = beatId, BeatProductionPlanId = planId,
                BeatProductionPlanVersion = plan.Version, MomentSetId = momentSet.Id, MomentSetVersion = momentSet.Version,
                MomentId = moment.MomentId, Revision = order, Status = SceneBeatCatalogueStatus.Complete,
                FrozenStateContractJson = "{\"location\":\"hall\",\"action\":\"turning\"}",
                InstantaneousSoundEventsJson = $"[{{\"cueKey\":\"{sound.Id}\"}}]", VideoKeyStateJson = "{\"roles\":[\"StillCandidate\"]}",
                CreatedUtc = now, CompletedUtc = now, UpdatedUtc = now
            };
            return new Fixture { Catalogue = catalogue, Plan = plan, MomentSet = momentSet, Moment = moment, Enrichment = enrichment };
        }
    }

    private sealed class CatalogueRepository(IEnumerable<Fixture> fixtures) : ISceneBeatCatalogueRepository
    {
        private readonly Fixture[] values = fixtures.ToArray();
        public Task<SceneBeatCatalogue?> GetAsync(string catalogueId, CancellationToken cancellationToken = default) =>
            Task.FromResult(values.SingleOrDefault(value => value.Catalogue.Id == catalogueId)?.Catalogue);
        public Task<SceneBeatCatalogue?> GetCurrentByTurnAsync(string sessionId, string turnId, CancellationToken cancellationToken = default) =>
            Task.FromResult(values.SingleOrDefault(value => value.Catalogue.SessionId == sessionId && value.Catalogue.TurnId == turnId)?.Catalogue);
        public Task CreateVersionAsync(SceneBeatCatalogue catalogue, SceneBeatAnalysisAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> GetNextVersionAsync(string sessionId, string turnId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryStartAttemptAsync(string catalogueId, string attemptId, string modelIdentifier, string providerName, string executionSettingsJson, DateTime startedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAttemptAsync(string catalogueId, SceneBeatAnalysisAttempt attempt, IReadOnlyList<SceneBeatCatalogueEntry> entries, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryFailAttemptAsync(string catalogueId, SceneBeatAnalysisAttempt attempt, string errorCode, string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelCurrentAsync(string catalogueId, string attemptId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PlanRepository(IEnumerable<Fixture> fixtures) : ISceneBeatProductionPlanRepository
    {
        private readonly Fixture[] values = fixtures.ToArray();
        public Task<SceneBeatProductionPlan?> GetAsync(string planId, CancellationToken cancellationToken = default) => Task.FromResult(values.SingleOrDefault(value => value.Plan.Id == planId)?.Plan);
        public Task<SceneBeatProductionPlan?> GetCurrentAsync(string catalogueId, string beatId, CancellationToken cancellationToken = default) => Task.FromResult(values.SingleOrDefault(value => value.Plan.CatalogueId == catalogueId && value.Plan.BeatId == beatId)?.Plan);
        public Task CreateVersionAsync(SceneBeatProductionPlan plan, SceneBeatAnalysisAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryStartAttemptAsync(string planId, string attemptId, string modelIdentifier, string providerName, DateTime startedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAttemptAsync(string planId, SceneBeatAnalysisAttempt attempt, SceneBeatProductionPlanData data, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryFailAttemptAsync(string planId, SceneBeatAnalysisAttempt attempt, string errorCode, string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelCurrentAsync(string planId, string attemptId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MomentSetRepository(IEnumerable<Fixture> fixtures) : ISceneMomentSetRepository
    {
        private readonly Fixture[] values = fixtures.ToArray();
        public Task<SceneMomentSet?> GetCurrentAsync(string beatProductionPlanId, CancellationToken cancellationToken = default) => Task.FromResult(values.SingleOrDefault(value => value.Plan.Id == beatProductionPlanId)?.MomentSet);
        public Task<SceneMomentSet?> GetAsync(string momentSetId, CancellationToken cancellationToken = default) => Task.FromResult(values.SingleOrDefault(value => value.MomentSet.Id == momentSetId)?.MomentSet);
        public Task CreateVersionAsync(SceneMomentSet momentSet, SceneBeatAnalysisAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryStartAttemptAsync(string momentSetId, string attemptId, string modelIdentifier, string providerName, DateTime startedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAttemptAsync(string momentSetId, SceneBeatAnalysisAttempt attempt, SceneMomentSetData data, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryFailAttemptAsync(string momentSetId, SceneBeatAnalysisAttempt attempt, string errorCode, string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelCurrentAsync(string momentSetId, string attemptId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EnrichmentRepository(IEnumerable<Fixture> fixtures) : ISceneMomentEnrichmentRepository
    {
        private readonly Fixture[] values = fixtures.ToArray();
        public Task<SceneMomentEnrichment?> GetCurrentAsync(string momentSetId, string momentId, CancellationToken cancellationToken = default) => Task.FromResult(values.SingleOrDefault(value => value.MomentSet.Id == momentSetId && value.Moment.MomentId == momentId)?.Enrichment);
        public Task<SceneMomentEnrichment?> GetAsync(string enrichmentId, CancellationToken cancellationToken = default) => Task.FromResult(values.SingleOrDefault(value => value.Enrichment.Id == enrichmentId)?.Enrichment);
        public Task CreateRevisionAsync(SceneMomentEnrichment enrichment, SceneBeatAnalysisAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SceneBeatAnalysisAttempt?> GetAttemptAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryStartAttemptAsync(string enrichmentId, string attemptId, string modelIdentifier, string providerName, DateTime startedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCompleteAttemptAsync(string enrichmentId, SceneBeatAnalysisAttempt attempt, SceneMomentEnrichmentData data, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryFailAttemptAsync(string enrichmentId, SceneBeatAnalysisAttempt attempt, string errorCode, string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryCancelCurrentAsync(string enrichmentId, string attemptId, DateTime cancelledUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}