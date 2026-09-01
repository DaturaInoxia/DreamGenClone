using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ICompiledMediaBriefRepository
{
    Task CreateAsync(CompiledMediaBrief brief, CancellationToken cancellationToken = default);

    Task<CompiledMediaBrief?> GetAsync(string briefId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompiledMediaBrief>> ListByMomentEnrichmentAsync(
        string momentEnrichmentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompiledMediaBrief>> ListByBeatProductionPlanAsync(
        string beatProductionPlanId,
        CancellationToken cancellationToken = default);
}

public interface IApprovedMediaDerivativeRepository
{
    Task CreateAsync(ApprovedMediaDerivative derivative, CancellationToken cancellationToken = default);

    Task<ApprovedMediaDerivative?> GetAsync(
        string derivativeId,
        CancellationToken cancellationToken = default);
}