namespace DreamGenClone.Application.RolePlay;

public interface ISemanticInteractionAnalysisRepository
{
    Task UpsertAsync(SemanticInteractionAnalysisState state, CancellationToken cancellationToken = default);

    Task<SemanticInteractionAnalysisState?> GetBySessionAndInteractionAsync(
        string sessionId,
        string interactionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SemanticInteractionAnalysisState>> ListBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<SemanticInteractionAnalysisState?> GetLatestBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<SemanticInteractionAnalysisState?> GetLatestBySessionAndCharacterAsync(
        string sessionId,
        string characterId,
        CancellationToken cancellationToken = default);
}
