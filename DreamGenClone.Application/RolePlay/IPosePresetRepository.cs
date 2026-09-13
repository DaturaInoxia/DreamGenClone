using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public interface IPosePresetRepository
{
    Task<IReadOnlyList<PosePreset>> ListAsync(CancellationToken cancellationToken = default);

    Task<PosePreset?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PosePreset>> SearchAsync(
        string? name, string? category, CancellationToken cancellationToken = default);

    Task UpsertAsync(PosePreset preset, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
}
