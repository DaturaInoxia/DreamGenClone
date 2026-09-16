using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface ICharacterIdentityBuildRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task UpsertBuildAsync(CharacterIdentityBuild build, CancellationToken cancellationToken = default);

    Task<CharacterIdentityBuild?> GetBuildAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityBuild>> ListBuildsAsync(string characterProfileId, CancellationToken cancellationToken = default);

    Task UpsertStepAsync(CharacterIdentityBuildStepRecord step, CancellationToken cancellationToken = default);

    Task<CharacterIdentityBuildStepRecord?> GetStepAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityBuildStepRecord>> ListStepsAsync(string buildId, CancellationToken cancellationToken = default);

    Task UpsertAngleAsync(CharacterIdentityAngleRecord angle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityAngleRecord>> ListAnglesAsync(string buildId, CancellationToken cancellationToken = default);

    Task UpsertAngleAttemptAsync(CharacterIdentityAngleAttempt attempt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityAngleAttempt>> ListAngleAttemptsAsync(string angleId, CancellationToken cancellationToken = default);

    Task DeleteAngleAttemptAsync(string attemptId, CancellationToken cancellationToken = default);

    Task RecordAngleAttemptOverrideAsync(string attemptId, string reason, string author, CancellationToken cancellationToken = default);
}
