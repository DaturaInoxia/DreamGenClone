using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class RoleDefinitionService : IRoleDefinitionService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<RoleDefinitionService> _logger;

    public RoleDefinitionService(ISqlitePersistence persistence, ILogger<RoleDefinitionService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<RoleDefinition> SaveAsync(RoleDefinition roleDefinition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleDefinition);
        await EnsureDefaultsAsync(cancellationToken);

        roleDefinition.Name = CharacterRoleCatalog.Normalize(roleDefinition.Name);
        roleDefinition.Description = (roleDefinition.Description ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(roleDefinition.Name)
            || string.Equals(roleDefinition.Name, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Role name is required.", nameof(roleDefinition));
        }

        var existing = await _persistence.LoadAllRoleDefinitionsAsync(cancellationToken);
        if (existing.Any(x => !string.Equals(x.Id, roleDefinition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, roleDefinition.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Role name already exists.");
        }

        roleDefinition.UpdatedUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(roleDefinition.Id))
        {
            roleDefinition.Id = Guid.NewGuid().ToString();
            roleDefinition.CreatedUtc = DateTime.UtcNow;
        }

        await _persistence.SaveRoleDefinitionAsync(roleDefinition, cancellationToken);
        _logger.LogInformation("Role definition saved: {RoleId}, Name={Name}", roleDefinition.Id, roleDefinition.Name);
        return roleDefinition;
    }

    public async Task<List<RoleDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _persistence.LoadAllRoleDefinitionsAsync(cancellationToken);
    }

    public Task<RoleDefinition?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.LoadRoleDefinitionAsync(id, cancellationToken);

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.DeleteRoleDefinitionAsync(id, cancellationToken);

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        var existing = await _persistence.LoadAllRoleDefinitionsAsync(cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var defaults = new[]
        {
            new RoleDefinition
            {
                Name = CharacterRoleCatalog.Wife,
                Description = "Primary female lead role used by character, persona, and template editors.",
                UseForAdaptiveProfiles = true,
                CreatedUtc = now,
                UpdatedUtc = now
            },
            new RoleDefinition
            {
                Name = CharacterRoleCatalog.Husband,
                Description = "Primary husband or male partner role used by character, persona, and template editors.",
                UseForAdaptiveProfiles = true,
                CreatedUtc = now,
                UpdatedUtc = now
            },
            new RoleDefinition
            {
                Name = CharacterRoleCatalog.TheOtherMan,
                Description = "Third-party male role used by character, persona, and template editors.",
                UseForAdaptiveProfiles = true,
                CreatedUtc = now,
                UpdatedUtc = now
            },
            new RoleDefinition
            {
                Name = CharacterRoleCatalog.BackgroundCharacters,
                Description = "Background or extra role for supporting characters. Not used for adaptive role targeting.",
                UseForAdaptiveProfiles = false,
                CreatedUtc = now,
                UpdatedUtc = now
            }
        };

        foreach (var role in defaults)
        {
            await _persistence.SaveRoleDefinitionAsync(role, cancellationToken);
        }

        _logger.LogInformation("Seeded default role definitions.");
    }
}