# Contract: IBehavioralFrameGenerator

**Layer**: Application (`DreamGenClone.Application/StoryAnalysis/Abstractions/`)  
**Implementation**: `DreamGenClone.Infrastructure/StoryAnalysis/CharacterBehavioralFrameGenerator.cs`  
**Replaces**: `BuildHusbandAwarenessInterpretationAsync()` in `ScenarioGuidanceGenerator.cs`

---

## Interface Definition

```csharp
namespace DreamGenClone.Application.StoryAnalysis.Abstractions;

/// <summary>
/// Generates LLM behavioral frame text for each character in a session that has an encounter profile bound.
/// Behavioral frames are injected as HARD CONSTRAINTs into the continuation prompt.
/// </summary>
public interface IBehavioralFrameGenerator
{
    /// <summary>
    /// Generates behavioral frame text for all characters with bound encounter profiles.
    /// Returns a dictionary keyed by character display label (e.g., "Wife — Sarah").
    /// Characters without a bound profile are omitted from the result (no empty entries).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GenerateFramesAsync(
        IReadOnlyDictionary<string, string> characterEncounterProfileIds,
        IReadOnlyList<ScenarioCharacter> characters,
        CancellationToken cancellationToken = default);
}
```

## Behavior Contract

| Condition | Result |
|---|---|
| Character has no entry in `characterEncounterProfileIds` | Character is omitted from result dictionary |
| Profile ID not found in DB | Character is omitted; Warning log emitted |
| Profile has `FullOverride=true` AND non-empty `AdditionalNotes` | Frame text = `AdditionalNotes` only |
| Profile has `FullOverride=true` AND empty `AdditionalNotes` | Frame text = generated dimension text (FullOverride ignored) |
| Profile has non-empty `AdditionalNotes` and `FullOverride=false` | Frame text = generated dimension text + "\n" + AdditionalNotes |
| Profile `TargetRole="Any"` | Frame text = AdditionalNotes if set, else empty → character omitted |
| Empty result dictionary | No HARD CONSTRAINT blocks injected for any character |

## Frame Text Generation

For a profile with `TargetRole != "Any"`:
1. Get dimensions for role from `BehavioralDimensionCatalog.GetDimensions(profile.TargetRole)`
2. For each dimension, look up value in `profile.EncounterStats` (default 50 if missing)
3. Call `BehavioralDimensionCatalog.ResolveTierText(role, dimensionName, value)` to get tier sentence
4. Concatenate all tier sentences into a single paragraph
5. If `AdditionalNotes` is set, append after generated text

## Character Label Format

```
"{character.Name} ({character.Role})"
```

Example: `"Sarah (Wife)"`, `"Michael (Husband)"`, `"James (OtherMan)"`

## Logging

- `Information` when generating frames: `"Generating behavioral frames for {Count} characters"`
- `Warning` when profile not found: `"Encounter profile {ProfileId} not found for character {CharacterId} — frame omitted"`
- `Debug` for each frame generated: `"Frame generated for {CharacterLabel} using profile {ProfileName}"`
