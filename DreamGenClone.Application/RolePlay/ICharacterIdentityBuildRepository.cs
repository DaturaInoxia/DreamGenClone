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

    /// <summary>The persisted step plan of one target kind, in pipeline order (seeded at schema ensure).</summary>
    Task<IReadOnlyList<CharacterIdentityStepDefinition>> ListStepPlanAsync(
        CharacterIdentityTargetKind kind, CancellationToken cancellationToken = default);

    Task UpsertAngleAsync(CharacterIdentityAngleRecord angle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityAngleRecord>> ListAnglesAsync(string buildId, CancellationToken cancellationToken = default);

    Task UpsertAngleAttemptAsync(CharacterIdentityAngleAttempt attempt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityAngleAttempt>> ListAngleAttemptsAsync(string angleId, CancellationToken cancellationToken = default);

    Task DeleteAngleAttemptAsync(string attemptId, CancellationToken cancellationToken = default);

    Task RecordAngleAttemptOverrideAsync(string attemptId, string reason, string author, CancellationToken cancellationToken = default);

    /// <summary>Persists one body view's state (B-122 Phase 0). Keyed by the view's own id.</summary>
    Task UpsertBodyViewAsync(CharacterIdentityBodyView view, CancellationToken cancellationToken = default);

    /// <summary>Every body view of a build, in a stable order (state, then canonical views before extended ones).</summary>
    Task<IReadOnlyList<CharacterIdentityBodyView>> ListBodyViewsAsync(string buildId, CancellationToken cancellationToken = default);
}
