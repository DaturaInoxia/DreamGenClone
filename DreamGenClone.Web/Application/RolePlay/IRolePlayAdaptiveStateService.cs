using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IRolePlayAdaptiveStateService
{
    sealed record InferredSemanticSignal(
        string EventId,
        decimal Confidence,
        string? ActorName,
        string? TargetCharacterName,
        string? EvidenceSpan);

    Task<AdaptiveScenarioState> UpdateFromInteractionAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken = default);

    Task<AdaptiveScenarioState> ApplyInferredSemanticEvidenceAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        IReadOnlyList<InferredSemanticSignal> inferredSignals,
        CancellationToken cancellationToken = default);

    Task<bool> ApplyManualScenarioOverrideAsync(
        RolePlaySession session,
        string requestedScenarioId,
        CancellationToken cancellationToken = default);

    Task SeedFromScenarioAsync(
        RolePlaySession session,
        Scenario scenario,
        CancellationToken cancellationToken = default);
}