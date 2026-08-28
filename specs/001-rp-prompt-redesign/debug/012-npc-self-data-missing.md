# Debug 012 — Slot 5 NPC Self-Data Missing

**Created:** 2026-07-17

## Report

NPC prompt shows "Becky [Role: Wife] (comparison reference only)" with no Ken self-description. The NPC actor (Ken) has no character data rendered — only other characters appear, and non-present ones show as "comparison reference only".

## Analysis

`CharacterDataSlot` has a Player self-description block that renders persona description + physical attributes, but no equivalent for NPC actors. When profile.Kind is Npc, the actor is skipped in the character loop (matched as self via `actorName`) but no self-description is rendered.

## Plan

Add NPC self-description block before the "Other characters" loop. When `profile.Kind != ActorProfileKind.Player`, find the actor's character in `characters` list, look up `context.CharacterDetails[character.Id]`, and render description + appearance.

**Files:** `CharacterDataSlot.cs` only

## Resolution

Added NPC actor self-description block.

## Validated

[ ] Pending user confirmation
