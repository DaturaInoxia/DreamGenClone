using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record GenerateSceneMomentEnrichmentRequest(string MomentSetId, string MomentId);

public sealed record SceneMomentEnrichmentJobPayload(string EnrichmentId, string AttemptId);

public interface ISceneMomentEnrichmentPipelineService
{
    Task<SceneMomentEnrichment> EnqueueAsync(
        GenerateSceneMomentEnrichmentRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneMomentEnrichment> ReplaceAsync(
        GenerateSceneMomentEnrichmentRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneMomentEnrichment> EnqueueRecommendedAsync(
        string momentSetId,
        CancellationToken cancellationToken = default);

    Task<SceneMomentEnrichment?> GetCurrentAsync(
        string momentSetId,
        string momentId,
        CancellationToken cancellationToken = default);

    Task CancelAsync(string enrichmentId, CancellationToken cancellationToken = default);
}
