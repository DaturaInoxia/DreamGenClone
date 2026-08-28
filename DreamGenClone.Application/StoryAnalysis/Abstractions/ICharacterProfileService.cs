using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis.Abstractions;

public interface ICharacterProfileService
{
    Task<CharacterProfile?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterProfile>> GetByRoleAsync(string targetRole, CancellationToken cancellationToken = default);
    Task SaveAsync(CharacterProfile profile, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}
