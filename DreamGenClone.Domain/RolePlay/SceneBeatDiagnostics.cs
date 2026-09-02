namespace DreamGenClone.Domain.RolePlay;

public enum SceneBeatPipelineStage
{
    Catalogue = 0,
    BeatProduction = 1,
    MomentDiscovery = 2,
    MomentEnrichment = 3
}

public sealed record SceneBeatStageStatusCounts(
    int Queued,
    int Processing,
    int Complete,
    int Failed,
    int Superseded,
    int Cancelled);

public sealed record SceneBeatStageMetrics(
    SceneBeatPipelineStage Stage,
    int AttemptCount,
    SceneBeatStageStatusCounts StatusCounts,
    double? AverageDurationMs,
    long? MaximumDurationMs,
    long TotalInputCharacters,
    long TotalOutputCharacters,
    DateTime? OldestAttemptUtc,
    DateTime? NewestAttemptUtc,
    int RawResponseRetainedCount,
    int ReasoningRetainedCount);

public sealed record SceneBeatDiagnosticAttemptSummary(
    SceneBeatPipelineStage Stage,
    string OwnerRecordId,
    string AttemptId,
    string JobId,
    int AttemptNumber,
    SceneBeatAnalysisAttemptStatus Status,
    string? ModelIdentifier,
    string? ProviderName,
    string? FinishReason,
    string? ValidationCode,
    long? DurationMs,
    int InputCharacters,
    int? OutputCharacters,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    DateTime UpdatedUtc,
    bool RawResponseRetained,
    bool ReasoningRetained);

public sealed record SceneBeatDiagnosticsPruneRun(
    string Id,
    string FunctionDefaultId,
    int RetentionDays,
    DateTime CutoffUtc,
    DateTime PrunedUtc,
    string Actor,
    int CataloguePrunedCount,
    int BeatProductionPrunedCount,
    int MomentDiscoveryPrunedCount,
    int MomentEnrichmentPrunedCount)
{
    public int TotalPrunedCount => CataloguePrunedCount
        + BeatProductionPrunedCount
        + MomentDiscoveryPrunedCount
        + MomentEnrichmentPrunedCount;
}