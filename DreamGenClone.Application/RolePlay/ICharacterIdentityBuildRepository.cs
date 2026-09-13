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
}
