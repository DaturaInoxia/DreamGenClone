# Plan: B-060 — HusbandAftermathInjector: Per-Character Memory & Dynamic Relation Labels

**TL;DR**: `HusbandAftermathInjector` (from B-056) fires for **every** prompted actor during `AftermathCoupleInteraction` — including the husband. It also selects the most recent `EncounterCompletion` record regardless of `CharacterId`, so Ken can receive Becky's (or Dean's) encounter memory. Fix: `ShouldFire` gates on whether **this actor** has an `EncounterCompletion` record in the current cycle. `BuildText` reads **that actor's own record** (matched by `CharacterId`), and dynamically generates the "return to your [relation]" text from the character's actual role label instead of hardcoding "husband".

## Background

The injector was designed for the wife's aftermath contrast (Becky had an encounter → returns to Ken/the husband). But:
- `ShouldFire` only checks `TimeSkipPhase == AftermathCoupleInteraction` — no character check.
- `BuildText` grabs `EncounterSummaries.OrderByDescending(OccurredUtc).First()` across all characters.
- The instruction text hardcodes "husband", "his face", "husband-wife scene".

During `AftermathCoupleInteraction`, `ResolveOverflowContinueActorsAsync` prompts both wife and husband. The injector fires for both, giving Ken Dean's encounter memory.

## Problem

1. **Wrong memory injected** — `BuildText` selects the most recent EncounterCompletion regardless of who it's for. Ken gets Becky/Dean's memory.
2. **Fires for wrong character** — `ShouldFire` doesn't check whether the current actor actually participated in an encounter.
3. **Hardcoded relation labels** — "husband", "his face", "husband-wife scene" assume wife→husband direction. Doesn't work if the husband has the encounter and returns to wife, or for other relation types (boyfriend, fiancé, etc.).

## Design

### Data Available in `PromptInjectionContext`

| Property | Type | What It Holds |
|---|---|---|
| `ActorName` | `string` | Current character being prompted (e.g. "Becky", "Ken") |
| `Session.PersonaName` | `string` | Persona name (e.g. "Ken") |
| `Session.PersonaRole` | `string` | Persona role from `CharacterRoleCatalog` (e.g. "Husband") |
| `Session.AdaptiveState.CharacterRoles` | `Dictionary<string, string>` | Maps character-name → role (e.g. "Becky"→"Wife", "Ken"→"Husband") |
| `Session.AdaptiveState.EncounterSummaries` | `List<EncounterSummaryRecord>` | Has `CharacterId` (character name), `SummaryType`, `CycleIndex`, `ActiveSummary` |

No new DI dependency needed — all data flows through `PromptInjectionContext.Session`.

### `ShouldFire` — Character-Gated

```
ShouldFire(context):
    if CurrentTimeSkipPhase != AftermathCoupleInteraction → false
    if EncounterSummaries has no EncounterCompletion with
         CharacterId == context.ActorName AND CycleIndex == currentCycle → false
    return true
```

Only fires for actors who **actually have an encounter record** in this cycle.

### `BuildText` — Actor's Own Record + Dynamic Relation

1. **Select record**: Filter `EncounterSummaries` by `CharacterId == context.ActorName` (not any character). Take most recent by `OccurredUtc`.
2. **Resolve partner label**:
   - If `ActorName == PersonaName` (persona is the actor → return to spouse):
     - Find the spouse in `CharacterRoles`: the character whose role is the complementary relationship role.
     - Partner label = spouse's role lowercased (e.g. "Wife" → "wife").
   - If `ActorName != PersonaName` (spouse is the actor → return to persona):
     - Partner label = `PersonaRole.ToLowerInvariant()` (e.g. "Husband" → "husband").
3. **Generate text**: Replace hardcoded "husband" / "his face" / "husband-wife scene" with the dynamic partner label and gender-neutral "their face".
4. **Return empty if no record**: Defensive guard if `ShouldFire` somehow gates incorrectly.

### Partner Label Resolution — Examples

| Actor | Actor Role | Persona | Persona Role | Partner Label | Text |
|---|---|---|---|---|---|
| Becky | Wife | Ken | Husband | "husband" | "...return to your husband..." |
| Ken | Husband | Ken | Husband | "wife" | "...return to your wife..." |
| Alex | Boyfriend | Sam | Girlfriend | "girlfriend" | "...return to your girlfriend..." |
| Sam | Girlfriend | Sam | Girlfriend | "boyfriend" | "...return to your boyfriend..." |

The label is resolved from the *other* character's role label (the one they're returning to), lowercased. This works for any role in `CharacterRoleCatalog` (Wife, Husband, OtherMan) and any future additions (Boyfriend, Girlfriend, Fiancé, Partner, etc.) with zero code changes.

## Steps

### Phase 1 — `ShouldFire` gating

**File**: `DreamGenClone.Web/Application/RolePlay/Injectors/HusbandAftermathInjector.cs`

1. Add character-filter condition: current actor must have an `EncounterCompletion` record in the current cycle, matched by `CharacterId == context.ActorName`.

```csharp
public bool ShouldFire(PromptInjectionContext context)
    => context.Session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.AftermathCoupleInteraction
    && context.Session.AdaptiveState.EncounterSummaries
        .Any(s => s.SummaryType == EncounterSummaryType.EncounterCompletion
               && s.CycleIndex == context.Session.AdaptiveState.CycleIndex
               && string.Equals(s.CharacterId, context.ActorName, StringComparison.OrdinalIgnoreCase));
```

### Phase 2 — `BuildText` per-character record selection

2. Filter `EncounterSummaries` by `CharacterId == context.ActorName` instead of "any character".
3. Remove the comment saying "any character is fine."

```csharp
var record = state.EncounterSummaries
    .Where(s => s.SummaryType == EncounterSummaryType.EncounterCompletion
             && s.CycleIndex == currentCycle
             && string.Equals(s.CharacterId, context.ActorName, StringComparison.OrdinalIgnoreCase))
    .OrderByDescending(s => s.OccurredUtc)
    .FirstOrDefault();
```

### Phase 3 — Dynamic partner label

4. Add a helper method to resolve the partner label:

```csharp
private static string ResolvePartnerLabel(PromptInjectionContext context)
{
    var actorName = context.ActorName;
    var personaName = context.Session.PersonaName;
    var characterRoles = context.Session.AdaptiveState.CharacterRoles;

    if (string.Equals(actorName, personaName, StringComparison.OrdinalIgnoreCase))
    {
        // Actor IS the persona — find the spouse's role
        var personaRole = context.Session.PersonaRole;
        var spouseRole = characterRoles.Values.FirstOrDefault(r =>
            !string.Equals(r, personaRole, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(r, CharacterRoleCatalog.OtherMan, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(r, CharacterRoleCatalog.BackgroundCharacters, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(r, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase));
        return spouseRole?.ToLowerInvariant() ?? "partner";
    }
    else
    {
        // Actor is NOT the persona — partner is the persona
        return context.Session.PersonaRole?.ToLowerInvariant() ?? "partner";
    }
}
```

5. Replace hardcoded text in `BuildText`:

| Current | New |
|---|---|
| `"your husband"` | `"your {partnerLabel}"` |
| `"to his face"` | `"to their face"` |
| `"this husband-wife scene"` | `"this {partnerLabel}-{actorRole} scene"` |
| Actor role label | Read from `CharacterRoles[ActorName]` lowercased |

### Phase 4 — Return empty on missing record

6. Add guard: if `record is null` (after per-character filter), return `string.Empty`. This shouldn't fire if `ShouldFire` gates correctly, but defensive.

### Phase 5 — Validation

7. `dotnet build DreamGenClone.Web --no-restore`
8. `dotnet build DreamGenClone.Tests --no-restore`
9. Check for existing `HusbandAftermathInjector` tests — run and verify they still pass (or update them to match the new character-aware behavior).
10. If no tests exist for the injector, consider adding: one for wife actor (fires), one for husband actor (doesn't fire unless he has a record), one for no-record actor (doesn't fire).

## Files Changed

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/Injectors/HusbandAftermathInjector.cs` | `ShouldFire` + `BuildText` + new helper |
| `DreamGenClone.Tests/.../HusbandAftermathInjectorTests.cs` | Update/add tests if they exist |

## Verification

1. **Build**: 0 errors.
2. **Tests**: existing injector tests pass with updated character-aware expectations.
3. **Manual**: Re-run the same session scenario — Becky's prompt should include her encounter memory; Ken's prompt should NOT include the aftermath memory block (since Ken has no encounter record).
4. **Edge cases**:
   - Actor with encounter record but different from persona's record → uses actor's own record.
   - Actor without any encounter record → injector doesn't fire.
   - Future role labels (Boyfriend, Girlfriend, Fiancé) → resolved dynamically, no code change needed.
