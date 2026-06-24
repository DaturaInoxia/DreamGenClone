# Contract: Resistance Profile Service API

**Feature**: `001-wife-resistance-motivation` | **Date**: 2026-06-07

## Interface: IStatResistanceProfileService

**Namespace**: `DreamGenClone.Application.StoryAnalysis`
**Registration**: `builder.Services.AddScoped<IStatResistanceProfileService, StatResistanceProfileService>();`

```csharp
public interface IStatResistanceProfileService
{
    /// <summary>
    /// Validates and persists a resistance profile. On first save, seeds the default
    /// if no profiles exist. Enforces: unique name, contiguous 0-100 threshold coverage,
    /// single default profile, canonical target stat name.
    /// </summary>
    /// <exception cref="ArgumentException">Name empty or missing.</exception>
    /// <exception cref="InvalidOperationException">Name already exists or threshold validation fails.</exception>
    Task<StatResistanceProfile> SaveAsync(
        StatResistanceProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all resistance profiles ordered by name. Ensures defaults are seeded
    /// before returning.
    /// </summary>
    Task<List<StatResistanceProfile>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a single profile by ID. Returns null if not found.
    /// </summary>
    Task<StatResistanceProfile?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the default profile (IsDefault=1), falling back to most recently updated.
    /// Ensures defaults are seeded before querying. Returns null if no profiles exist.
    /// </summary>
    Task<StatResistanceProfile?> GetDefaultAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a profile by ID. Refuses to delete the seeded default (IsSeeded check).
    /// Returns true if a row was deleted.
    /// </summary>
    Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default);
}
```

## Contract: Resistance Directive Resolution

**Service**: `ScenarioGuidanceGenerator` (existing, modified)
**Method**: `BuildResistanceInterpretationAsync(ScenarioGuidanceRequest, CancellationToken) → string`

### Input Contract

The method receives the full `ScenarioGuidanceRequest` which already contains:
- `SelectedResistanceProfileId` (string? — from session adaptive state)
- `CharacterRuntimeStats` (IReadOnlyDictionary<string, CharacterStatProfileV2>? — per-character stats including Wife's Loyalty/selfRespect and OtherMan's PersistencePastLimits dimension via RuntimeEncounterStats)
- `CharacterEncounterProfileIds` (for resolving Husband dimensions from CharacterProfile)

### Output Contract

Returns a string for injection into the prompt, or `string.Empty` if:
- No Wife-role character is present in the session
- No ResistanceProfile is selected (should not happen with seeded default)
- The resolved effective stat does not match any threshold band (should not happen with validated contiguous coverage)

**Format when non-empty**:
```
HARD CONSTRAINT — {WifeLabel} resistance directive (authoritative, overrides escalation guidance): {band.PromptDirective}
```

### Resolution Algorithm

```
1. If SelectedResistanceProfileId is null/empty → return ""
2. Load ResistanceProfile by ID; if null or no thresholds → return ""
3. Find Wife character in CharacterRuntimeStats by CharacterRole="Wife"; if none → return ""
4. Read targetStatValue = Wife's value for profile.TargetStatName (via CharacterStatProfileV2Accessor)
5. Resolve Husband Attentiveness/IntimacyAvailability from Husband character (default 50)
6. Resolve Wife SelfRespect from Wife's stats (default 50)
7. Resolve OtherMan PersistencePastLimits from OtherMan character (default 50)
8. Compute motivationScore = ((100−Attentiveness) + (100−IntimacyAvailability) + (100−SelfRespect) + PersistencePastLimits) / 4
9. Compute effectiveStat = min(targetStatValue + motivationScore, 100)
10. Find threshold band where effectiveStat ∈ [MinValue, MaxValue]
11. If band found → return "HARD CONSTRAINT — {WifeLabel} resistance directive (authoritative, overrides escalation guidance): {band.PromptDirective}"
12. If no band → return ""
```

## Persistence Contract: ISqlitePersistence (Additions)

Five new method signatures added alongside the existing `StatWillingnessProfiles` methods:

```csharp
// In ISqlitePersistence — added after the willingness methods
Task SaveStatResistanceProfileAsync(StatResistanceProfile profile, CancellationToken cancellationToken = default);
Task<StatResistanceProfile?> LoadStatResistanceProfileAsync(string id, CancellationToken cancellationToken = default);
Task<StatResistanceProfile?> LoadDefaultStatResistanceProfileAsync(CancellationToken cancellationToken = default);
Task<List<StatResistanceProfile>> LoadAllStatResistanceProfilesAsync(CancellationToken cancellationToken = default);
Task<bool> DeleteStatResistanceProfileAsync(string id, CancellationToken cancellationToken = default);
```

## Adaptive State Contract

### AdaptiveScenarioState (Addition)

```csharp
// In DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs
public string? SelectedResistanceProfileId { get; set; }
```

### RolePlayStateRepository (Modifications)

- `SaveAdaptiveStateAsync`: Add column `SelectedResistanceProfileId` to UPSERT, bind parameter `$selectedResistanceProfileId`
- `LoadAdaptiveStateAsync`: Read new ordinal for `SelectedResistanceProfileId` (next available after existing columns)

## Facade Contract: StoryAnalysisFacade (Additions)

Passthrough methods matching the service interface, following the existing pattern:

```csharp
public Task<StatResistanceProfile> SaveStatResistanceProfileAsync(StatResistanceProfile profile)
    => _resistanceProfileService.SaveAsync(profile);

public Task<List<StatResistanceProfile>> ListStatResistanceProfilesAsync()
    => _resistanceProfileService.ListAsync();

public Task<bool> DeleteStatResistanceProfileAsync(string id)
    => _resistanceProfileService.DeleteAsync(id);
```
