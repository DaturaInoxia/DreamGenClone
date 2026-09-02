using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record StoryPresentationImportRequest(
    string SessionId,
    IReadOnlyList<string> OrderedBeatProductionPlanIds);

public enum StoryPresentationImportFailureCode
{
    SourceMissing = 1,
    SourceVersionStale = 2,
    ProductionBriefIncomplete = 3
}

public sealed record StoryPresentationImportFinding(
    StoryPresentationImportFailureCode Code,
    IReadOnlyList<string> AffectedIds,
    string Details);

public sealed record StoryPresentationCatalogueEntrySnapshot(
    string CatalogueId,
    int CatalogueVersion,
    string TurnId,
    string BeatId,
    int Order,
    string Label,
    string BeatSynopsis,
    string PrimaryLocation,
    string ParticipantSummaryJson,
    string EvidenceInteractionIdsJson,
    string ContentTagsJson);

public sealed record StoryPresentationMomentEnrichmentSnapshot(
    string Id,
    int Revision,
    string MomentId,
    string FrozenStateContractJson,
    string InstantaneousSoundEventsJson,
    string VideoKeyStateJson);

public sealed record StoryPresentationMomentSnapshot(
    string MomentSetId,
    string MomentId,
    int Order,
    string Label,
    string TemporalAnchor,
    string FrozenState,
    string VisibleAction,
    string ParticipantSummaryJson,
    string CompositionRationale,
    string ProductionRolesJson,
    string EvidenceInteractionIdsJson);

public sealed record StoryPresentationMomentSetSnapshot(
    string Id,
    int Version,
    string RecommendedMomentId,
    IReadOnlyList<StoryPresentationMomentSnapshot> Moments,
    IReadOnlyList<StoryPresentationMomentEnrichmentSnapshot> Enrichments);

public sealed record StoryPresentationProductionPlanSnapshot(
    string Id,
    int Version,
    StoryPresentationCatalogueEntrySnapshot CatalogueEntry,
    string NarrativeArcJson,
    string TimelineJson,
    string NarrationCuesJson,
    string DialogueCuesJson,
    string AmbiencePlanJson,
    string SoundEventCuesJson,
    string MusicPlanJson,
    string ActionArcJson,
    string StartContinuityJson,
    string EndContinuityJson,
    string TypedReferencesJson,
    string VideoCoveragePlansJson,
    IReadOnlyList<SceneBeatDialogueCue> DialogueCues,
    IReadOnlyList<SceneBeatSoundCue> SoundCues,
    IReadOnlyList<SceneVideoCoveragePlan> VideoCoveragePlans,
    StoryPresentationMomentSetSnapshot MomentSet);

public sealed record StoryPresentationImportSnapshot(
    string SessionId,
    IReadOnlyList<StoryPresentationProductionPlanSnapshot> ProductionPlans);

public sealed record StoryPresentationImportResult(
    bool Success,
    StoryPresentationImportSnapshot? Snapshot,
    string? CanonicalSnapshotJson,
    string? SourceChecksum,
    IReadOnlyList<StoryPresentationImportFinding> Findings);

public interface IStoryPresentationImportService
{
    Task<StoryPresentationImportResult> ImportAsync(
        StoryPresentationImportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StoryPresentationImportService(
    ISceneBeatCatalogueRepository catalogueRepository,
    ISceneBeatProductionPlanRepository productionPlanRepository,
    ISceneMomentSetRepository momentSetRepository,
    ISceneMomentEnrichmentRepository enrichmentRepository) : IStoryPresentationImportService
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<StoryPresentationImportResult> ImportAsync(
        StoryPresentationImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
        ArgumentNullException.ThrowIfNull(request.OrderedBeatProductionPlanIds);

        var snapshots = new List<StoryPresentationProductionPlanSnapshot>(request.OrderedBeatProductionPlanIds.Count);
        foreach (var planId in request.OrderedBeatProductionPlanIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(planId);
            var outcome = await ImportPlanAsync(request.SessionId, planId, cancellationToken);
            if (outcome.Finding is not null)
            {
                return Failure(outcome.Finding);
            }

            snapshots.Add(outcome.Snapshot!);
        }

        var snapshot = new StoryPresentationImportSnapshot(request.SessionId, snapshots.ToArray());
        var canonicalJson = JsonSerializer.Serialize(snapshot, CanonicalJsonOptions);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
        return new StoryPresentationImportResult(true, snapshot, canonicalJson, checksum, []);
    }

    private async Task<PlanImportOutcome> ImportPlanAsync(
        string sessionId,
        string planId,
        CancellationToken cancellationToken)
    {
        var plan = await productionPlanRepository.GetAsync(planId, cancellationToken);
        if (plan is null)
        {
            return Missing(planId, $"Beat Production Plan '{planId}' does not exist.");
        }

        if (plan.Status != SceneBeatCatalogueStatus.Complete || !HasCompleteProductionFacts(plan))
        {
            return Incomplete(planId, $"Beat Production Plan '{planId}' is not complete with all canonical production facts.");
        }

        var currentPlan = await productionPlanRepository.GetCurrentAsync(plan.CatalogueId, plan.BeatId, cancellationToken);
        if (currentPlan is null)
        {
            return Missing(planId, $"No current Beat Production Plan exists for catalogue '{plan.CatalogueId}' beat '{plan.BeatId}'.");
        }

        if (currentPlan.Id != plan.Id || currentPlan.Version != plan.Version)
        {
            return Stale([plan.Id, currentPlan.Id], $"Beat Production Plan '{plan.Id}' version {plan.Version} is not current.");
        }

        var catalogue = await catalogueRepository.GetAsync(plan.CatalogueId, cancellationToken);
        if (catalogue is null)
        {
            return Missing(plan.CatalogueId, $"Catalogue '{plan.CatalogueId}' does not exist.");
        }

        if (catalogue.SessionId != sessionId)
        {
            return Stale([plan.Id, catalogue.Id, sessionId], $"Catalogue '{catalogue.Id}' does not belong to session '{sessionId}'.");
        }

        var currentCatalogue = await catalogueRepository.GetCurrentByTurnAsync(sessionId, catalogue.TurnId, cancellationToken);
        if (catalogue.Status != SceneBeatCatalogueStatus.Complete || currentCatalogue?.Id != catalogue.Id || currentCatalogue.Version != catalogue.Version)
        {
            return Stale([catalogue.Id, currentCatalogue?.Id ?? string.Empty], $"Catalogue '{catalogue.Id}' version {catalogue.Version} is not the current completed catalogue.");
        }

        var entry = catalogue.Entries.SingleOrDefault(candidate => candidate.BeatId == plan.BeatId);
        if (entry is null)
        {
            return Missing(plan.BeatId, $"Catalogue '{catalogue.Id}' does not contain beat '{plan.BeatId}'.");
        }

        var momentSet = await momentSetRepository.GetCurrentAsync(plan.Id, cancellationToken);
        if (momentSet is null)
        {
            return Missing(plan.Id, $"Beat Production Plan '{plan.Id}' has no current Moment Set.");
        }

        if (momentSet.Status != SceneBeatCatalogueStatus.Complete ||
            momentSet.BeatProductionPlanId != plan.Id ||
            momentSet.BeatProductionPlanVersion != plan.Version ||
            string.IsNullOrWhiteSpace(momentSet.RecommendedMomentId) ||
            momentSet.Moments.Count == 0)
        {
            return Stale([plan.Id, momentSet.Id], $"Moment Set '{momentSet.Id}' is not a completed current descendant of Beat Production Plan '{plan.Id}'.");
        }

        var enrichments = new List<StoryPresentationMomentEnrichmentSnapshot>(momentSet.Moments.Count);
        foreach (var moment in momentSet.Moments.OrderBy(candidate => candidate.Order))
        {
            var enrichment = await enrichmentRepository.GetCurrentAsync(momentSet.Id, moment.MomentId, cancellationToken);
            if (enrichment is null)
            {
                return Missing(moment.MomentId, $"Moment '{moment.MomentId}' has no current enrichment.");
            }

            if (enrichment.Status != SceneBeatCatalogueStatus.Complete ||
                enrichment.CatalogueId != plan.CatalogueId ||
                enrichment.BeatId != plan.BeatId ||
                enrichment.BeatProductionPlanId != plan.Id ||
                enrichment.BeatProductionPlanVersion != plan.Version ||
                enrichment.MomentSetId != momentSet.Id ||
                enrichment.MomentSetVersion != momentSet.Version ||
                enrichment.MomentId != moment.MomentId)
            {
                return Stale([plan.Id, momentSet.Id, moment.MomentId, enrichment.Id],
                    $"Moment enrichment '{enrichment.Id}' is not the current descendant for moment '{moment.MomentId}'.");
            }

            if (string.IsNullOrWhiteSpace(enrichment.FrozenStateContractJson) ||
                string.IsNullOrWhiteSpace(enrichment.InstantaneousSoundEventsJson) ||
                string.IsNullOrWhiteSpace(enrichment.VideoKeyStateJson))
            {
                return Incomplete(enrichment.Id, $"Moment enrichment '{enrichment.Id}' is missing canonical production facts.");
            }

            enrichments.Add(new StoryPresentationMomentEnrichmentSnapshot(
                enrichment.Id,
                enrichment.Revision,
                enrichment.MomentId,
                enrichment.FrozenStateContractJson,
                enrichment.InstantaneousSoundEventsJson,
                enrichment.VideoKeyStateJson));
        }

        var catalogueEntry = new StoryPresentationCatalogueEntrySnapshot(
            catalogue.Id,
            catalogue.Version,
            catalogue.TurnId,
            entry.BeatId,
            entry.Order,
            entry.Label,
            entry.BeatSynopsis,
            entry.PrimaryLocation,
            entry.ParticipantSummaryJson,
            entry.EvidenceInteractionIdsJson,
            entry.ContentTagsJson);
        var momentSetSnapshot = new StoryPresentationMomentSetSnapshot(
            momentSet.Id,
            momentSet.Version,
            momentSet.RecommendedMomentId,
            momentSet.Moments.OrderBy(candidate => candidate.Order).Select(moment =>
                new StoryPresentationMomentSnapshot(
                    moment.MomentSetId,
                    moment.MomentId,
                    moment.Order,
                    moment.Label,
                    moment.TemporalAnchor,
                    moment.FrozenState,
                    moment.VisibleAction,
                    moment.ParticipantSummaryJson,
                    moment.CompositionRationale,
                    moment.ProductionRolesJson,
                    moment.EvidenceInteractionIdsJson)).ToArray(),
            enrichments.ToArray());
        return new PlanImportOutcome(new StoryPresentationProductionPlanSnapshot(
            plan.Id,
            plan.Version,
            catalogueEntry,
            plan.NarrativeArcJson,
            plan.TimelineJson,
            plan.NarrationCuesJson,
            plan.DialogueCuesJson,
            plan.AmbiencePlanJson,
            plan.SoundEventCuesJson,
            plan.MusicPlanJson,
            plan.ActionArcJson,
            plan.StartContinuityJson,
            plan.EndContinuityJson,
            plan.TypedReferencesJson,
            plan.VideoCoveragePlansJson,
            plan.DialogueCues.ToArray(),
            plan.SoundCues.ToArray(),
            plan.VideoCoveragePlans.ToArray(),
            momentSetSnapshot), null);
    }

    private static bool HasCompleteProductionFacts(SceneBeatProductionPlan plan) =>
        RequiredJson(plan).All(value => !string.IsNullOrWhiteSpace(value));

    private static IEnumerable<string> RequiredJson(SceneBeatProductionPlan plan)
    {
        yield return plan.NarrativeArcJson;
        yield return plan.TimelineJson;
        yield return plan.NarrationCuesJson;
        yield return plan.DialogueCuesJson;
        yield return plan.AmbiencePlanJson;
        yield return plan.SoundEventCuesJson;
        yield return plan.MusicPlanJson;
        yield return plan.ActionArcJson;
        yield return plan.StartContinuityJson;
        yield return plan.EndContinuityJson;
        yield return plan.TypedReferencesJson;
        yield return plan.VideoCoveragePlansJson;
    }

    private static PlanImportOutcome Missing(string affectedId, string details) =>
        Finding(StoryPresentationImportFailureCode.SourceMissing, [affectedId], details);

    private static PlanImportOutcome Stale(IReadOnlyList<string> affectedIds, string details) =>
        Finding(StoryPresentationImportFailureCode.SourceVersionStale, affectedIds.Where(id => id.Length > 0).ToArray(), details);

    private static PlanImportOutcome Incomplete(string affectedId, string details) =>
        Finding(StoryPresentationImportFailureCode.ProductionBriefIncomplete, [affectedId], details);

    private static PlanImportOutcome Finding(
        StoryPresentationImportFailureCode code,
        IReadOnlyList<string> affectedIds,
        string details) => new(null, new StoryPresentationImportFinding(code, affectedIds, details));

    private static StoryPresentationImportResult Failure(StoryPresentationImportFinding finding) =>
        new(false, null, null, null, [finding]);

    private sealed record PlanImportOutcome(
        StoryPresentationProductionPlanSnapshot? Snapshot,
        StoryPresentationImportFinding? Finding);
}