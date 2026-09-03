using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ISceneIdentityEvaluationRepository
{
    Task CreateCasesAsync(
        IReadOnlyList<SceneIdentityEvaluationCase> cases,
        CancellationToken cancellationToken = default);

    Task<SceneIdentityEvaluationCase?> GetCaseAsync(
        string caseId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneIdentityEvaluationCase>> ListCasesAsync(
        string evaluationRunId,
        CancellationToken cancellationToken = default);

    Task AddResultAsync(
        SceneIdentityEvaluationResult result,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneIdentityEvaluationResult>> ListResultsAsync(
        string evaluationRunId,
        CancellationToken cancellationToken = default);

    Task RecordDecisionAsync(
        CharacterIdentityDecision decision,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityDecision>> ListDecisionsAsync(
        string identityPackId,
        CancellationToken cancellationToken = default);
}