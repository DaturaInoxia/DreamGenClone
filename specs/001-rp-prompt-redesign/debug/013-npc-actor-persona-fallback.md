# Debug 013 — NPC Actor Resolved as Persona During Opening

**Created:** 2026-07-17

## Report

NPC prompt built for Ken instead of Becky during opening turn. Prompt shows `POV Character: Ken`, `Continue as: Ken (Husband)`, Becky as "comparison reference only". Becky's interaction generated with wrong POV and character data.

## Analysis

`ActorProfileResolver.ResolveNpcProfile` line 93:
```csharp
var actorName = session.CurrentTurnState == TurnState.Any
    ? session.PersonaName    // BUG: defaults to persona, never resolves spouse
    : ResolveNpcNameFromSession(session);
```

During opening, `CurrentTurnState` is `TurnState.Any` (no prior turns). NPC actor defaults to `session.PersonaName` ("Ken") — same "persona" legacy pattern. The spouse (Becky) is never resolved.

## Plan

When `CurrentTurnState == TurnState.Any`, resolve the NPC from the character roster — find the character whose `RelationTargetId` points to the persona (the spouse), rather than defaulting to `session.PersonaName`.

**Files:** `ActorProfileResolver.cs` only

## Resolution

Added `ResolveOpeningNpcName` method. Falls back to existing behavior only if no spouse found.

## Validated

[ ] Pending user confirmation
