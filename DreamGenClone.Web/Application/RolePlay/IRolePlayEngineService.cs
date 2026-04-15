using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Application.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IRolePlayEngineService
{
    Task<RolePlaySession> CreateSessionAsync(
        string title,
        string? scenarioId = null,
        string personaName = "You",
        string personaDescription = "",
        string? personaTemplateId = null,
        string personaGender = "Unknown",
        string personaRole = "Unknown",
        string? personaRelationTargetId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RolePlaySession>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<RolePlaySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<RolePlaySession> OpenSessionAsync(
        string sessionId,
        RolePlaySessionOpenAction action,
        CancellationToken cancellationToken = default);

    Task<RolePlaySession> SaveSessionAsync(RolePlaySession session, CancellationToken cancellationToken = default);

    Task<RolePlaySession> RebuildAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> UpdateBehaviorModeAsync(string sessionId, BehaviorMode mode, CancellationToken cancellationToken = default);

    Task<RolePlayInteraction> AddInteractionAsync(
        string sessionId,
        ContinueAsActor actor,
        string content,
        string? customActorName = null,
        CancellationToken cancellationToken = default);

    Task<RolePlayInteraction> ContinueAsync(
        string sessionId,
        ContinueAsActor actor,
        string? customActorName = null,
        string? instruction = null,
        CancellationToken cancellationToken = default);

    Task<RolePlayInteraction> SubmitPromptAsync(
        UnifiedPromptSubmission submission,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default);

    Task<ContinueAsResult> ContinueAsAsync(
        ContinueAsRequest request,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default);

    Task<RolePlayPendingDecisionPrompt?> GetPendingDecisionPromptAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<DecisionOutcome?> ApplyDecisionAsync(
        string sessionId,
        string decisionPointId,
        string optionId,
        string? customResponseText = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
