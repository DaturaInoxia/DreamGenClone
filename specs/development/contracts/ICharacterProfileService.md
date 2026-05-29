# Contract: ICharacterProfileService

**Layer**: Application (`DreamGenClone.Application/StoryAnalysis/Abstractions/`)  
**Replaces**: `IBaseStatProfileService` + `IHusbandAwarenessProfileService`

---

## Interface Definition

```csharp
namespace DreamGenClone.Application.StoryAnalysis.Abstractions;

public interface ICharacterProfileService
{
    // CRUD
    Task<CharacterProfile?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterProfile>> GetByRoleAsync(string targetRole, CancellationToken cancellationToken = default);
    Task SaveAsync(CharacterProfile profile, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    // Seeding
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}
```

## Behavior Contract

| Method | Behavior |
|---|---|
| `GetAsync(id)` | Returns null if not found; no exception |
| `GetAllAsync()` | Returns all profiles sorted by TargetRole then Name |
| `GetByRoleAsync(role)` | Returns profiles where TargetRole == role OR TargetRole == "Any"; sorted by Name |
| `SaveAsync(profile)` | Validates CharacterStats keys and EncounterStats keys before write; updates UpdatedUtc; throws ArgumentException on invalid stat keys |
| `DeleteAsync(id)` | Returns false if not found; no exception; refuses to delete IsSeeded=true profiles (returns false) |
| `EnsureDefaultsAsync()` | Seeds the 25 unified archetypes defined in the spec; idempotent (INSERT OR IGNORE semantics) |

## Error Handling

- `SaveAsync` throws `ArgumentException` if CharacterStats contains non-canonical key names (as returned by `AdaptiveStatCatalog.StatNames`)
- `SaveAsync` throws `ArgumentException` if EncounterStats contains keys not in `BehavioralDimensionCatalog.GetDimensions(profile.TargetRole)`
- All other failures propagate as `InvalidOperationException` wrapping the underlying storage exception, with Serilog Error logging

## Logging

- `Information` on successful save: `"CharacterProfile {Id} ({Name}) saved"`
- `Information` on delete: `"CharacterProfile {Id} deleted"`
- `Warning` on GetAsync not found: `"CharacterProfile {Id} not found"`
- `Warning` when delete refused for seeded profile: `"CharacterProfile {Id} is a seeded default and cannot be deleted"`
