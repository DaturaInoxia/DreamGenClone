# Debug 003: ActorProfileResolver — Narrative Treated as NPC

**Date:** 2026-07-17
**Session:** `0aad7fd1-5c7e-4e19-a79d-516af1658987`

## Report
Runtime error: `ActorProfileResolver: NPC actor 'Narrative' not found in character roster for session '0aad7fd1-5c7e-4e19-a79d-516af1658987'. Roster: Becky(...), Ken(...), Dean(...), Sam(...)`

## Analysis
`ActorProfileResolver.ResolveNpcNameFromSession` walks backwards through interaction history to find the last NPC actor. It picked up `ActorName = "Narrative"` from a Narrative interaction and tried to look it up in the character roster.

The resolver has a `PromptIntent.Narrative` check at the top that returns the Narrative profile directly, but the caller was passing `ContinueAsActor.Npc` (not Narrative intent) when looking for the active NPC. The interaction history included Narrative entries which the scan didn't filter out.

## Plan
Update `ResolveNpcNameFromSession` to skip:
- Actors named "Narrative" (system narrator, not a character)
- Interactions with `InteractionType == System`

## Resolution
- Updated `ActorProfileResolver.ResolveNpcNameFromSession`: added two new guard conditions:
  - `!string.Equals(interaction.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase)`
  - `interaction.InteractionType != Domain.RolePlay.InteractionType.System`

## Validated
- [x] 2026-07-17 — Build 0 errors, 104 tests pass
- [x] User confirmed fixed with new session

