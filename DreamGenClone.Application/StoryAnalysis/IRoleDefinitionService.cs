using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis;

public interface IRoleDefinitionService
{
    Task<RoleDefinition> SaveAsync(RoleDefinition roleDefinition, CancellationToken cancellationToken = default);

    Task<List<RoleDefinition>> ListAsync(CancellationToken cancellationToken = default);

    Task<RoleDefinition?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}