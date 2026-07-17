# Debug 007 — No Character Data Injected During Opening Phase

**Created:** 2026-07-17
**Session:** `e9bf3cdb-079b-4db0-8482-7c9126038754` (Campground Intimacy)

## Report

After the opening couple character filter was added (Debug 006), the opening phase prompt contains **no character data at all**. The `CharacterDataSlot` produces no output because the character list is empty.

Root cause investigation via DB query confirmed:
- Session has 4 characters: Ken (Husband), Becky (Wife), Dean (OtherMan), Sam (OtherMan)
- `session.PersonaCharacterId` = `"55ed2a0a-e77e-4d5c-aed1-e5aea5d75345"` (Ken)
- Scenario character Becky has `RelationTargetId` = `"55ed2a0a-e77e-4d5c-aed1-e5aea5d75345"` (Ken's ID)

## Analysis

`ResolveOpeningCoupleIds` had two bugs:

### Bug 1: `IsPersona` flag not reliable
```csharp
var personaChar = scenario.Characters.FirstOrDefault(c => c.IsPersona);
```
The scenario character Ken does **not** have `IsPersona: true` set in the scenario JSON. The `IsPersona` property defaults to `false`. This lookup returns `null` → persona ID never added to `coupleIds`.

The authoritative persona identity is `session.PersonaCharacterId`, which is always populated.

### Bug 2: `RelationTargetId` stores character IDs, not names
```csharp
var spouseChar = scenario.Characters.FirstOrDefault(c =>
    string.Equals(c.RelationTargetId.Trim(), personaName, StringComparison.OrdinalIgnoreCase));
```
`personaName` = "Ken" (from `session.PersonaName`).
Becky's `RelationTargetId` = `"55ed2a0a-e77e-4d5c-aed1-e5aea5d75345"` (Ken's character ID GUID).
The string comparison "Ken" vs GUID always fails → spouse never found.

### Result
Both lookups failed → `coupleIds` empty → `openingCoupleIds` is an empty `HashSet` → `scenarioCharacters.Where(c => coupleIds.Contains(c.Id))` returns empty list → no characters in context → `CharacterDataSlot` produces nothing.

## Plan

Fix `ResolveOpeningCoupleIds` in `RolePlayContinuationService.cs`:

1. **Use `session.PersonaCharacterId` instead of `IsPersona`** to find the persona — this is the authoritative source
2. **Match `RelationTargetId` against the persona's character ID** (GUID), not the persona name

```csharp
private static HashSet<string> ResolveOpeningCoupleIds(
    RolePlaySession session,
    DreamGenClone.Web.Domain.Scenarios.Scenario scenario)
{
    var coupleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var personaId = session.PersonaCharacterId;
    if (!string.IsNullOrWhiteSpace(personaId))
    {
        coupleIds.Add(personaId);

        var spouseChar = scenario.Characters.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.RelationTargetId) &&
            string.Equals(c.RelationTargetId.Trim(), personaId, StringComparison.OrdinalIgnoreCase));
        if (spouseChar is not null)
            coupleIds.Add(spouseChar.Id);
    }

    return coupleIds;
}
```

**Blast radius:** Single method, no downstream changes. `RolePlayContinuationService.cs` only.

## Resolution

[Fix applied — see file diff above]

## Validated

[ ] Pending user confirmation with clean build + fresh session
