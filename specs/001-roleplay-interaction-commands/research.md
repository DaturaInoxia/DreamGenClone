# Research: RolePlay Interaction Commands

**Feature**: `001-roleplay-interaction-commands`  
**Date**: 2026-04-04

## Design Decisions

### D1: Alternatives Storage Strategy — Flat List with Parent Reference

**Decision**: Store sibling alternatives as additional `RolePlayInteraction` entries in the session's `Interactions` list, linked by a `ParentInteractionId` field. The original interaction has `ParentInteractionId = null`. Each alternative points to the original's `Id`.

**Rationale**: The session is already serialized as a single JSON blob via `System.Text.Json` into SQLite's `PayloadJson` column. Adding alternatives as entries in the same list avoids schema changes, new tables, or nested collection serialization. Navigation uses `AlternativeIndex` (0-based ordinal among siblings) and `ActiveAlternativeIndex` (persisted on the parent interaction to track which sibling is displayed).

**Alternatives considered**:
- Nested `List<RolePlayInteraction> Alternatives` property on each interaction: cleaner conceptually but changes JSON shape significantly and complicates the flat iteration used in `BuildPromptAsync` and `TakeLast(12)`.
- Separate SQLite table for alternatives: violates the existing session-as-JSON-blob pattern and requires migration logic.

### D2: Context Building — Flag-Aware Filtering Before Prompt Assembly

**Decision**: Insert a filtering step before the existing `BuildPromptAsync` method that: (a) removes Excluded interactions, (b) keeps Hidden interactions, (c) for each interaction position selects only the active alternative, and (d) applies Pinned-priority trimming when context exceeds token budget.

**Rationale**: The existing `BuildPromptAsync` iterates `session.Interactions.TakeLast(12)`. The filter produces a "context view" list that replaces direct iteration. This isolates flag logic from prompt formatting. Pinned trimming removes oldest non-pinned first; if still over budget, it trims oldest pinned (ensuring pinned are last to go, not immovable).

**Alternatives considered**:
- Filtering inside `BuildPromptAsync` directly: mixes concerns and makes the method harder to test independently.
- LINQ predicates inline: works for Excluded/Hidden but doesn't handle Pinned trimming elegantly.

### D3: Retry/Rewrite Prompt Construction — Reuse Existing Prompt Pipeline

**Decision**: Retry and rewrite operations reuse the existing `BuildPromptAsync` + `ICompletionClient.GenerateAsync` pipeline. The retry service builds a prompt that includes the original interaction content plus a modifier instruction (e.g., "Rewrite this to be longer", "Rewrite as [character]", or the user's custom instruction). Model selection uses `IModelResolutionService` with an optional override `modelId` parameter for "Retry with model".

**Rationale**: Reusing the existing pipeline avoids duplicating prompt assembly, model resolution, and completion client logic. The retry service only adds the instruction framing — everything else (scenario context, persona, recent history) comes from the established path.

**Alternatives considered**:
- Dedicated retry prompt builder: more explicit but duplicates significant prompt assembly logic.
- Stateless "send content + instruction only" (no session context): faster but produces output disconnected from story context.

### D4: Fork Implementation — Extend Existing BranchService

**Decision**: Add `ForkAboveAsync(sessionId, interactionId)` and `ForkBelowAsync(sessionId, interactionId)` to the existing `IRolePlayBranchService`. Fork Above copies interactions from start to the target (inclusive). Fork Below copies from the target to the end. Both copy only the currently active alternative for each position. The existing `ForkSessionAsync(fromInteractionIndexInclusive)` remains unchanged.

**Rationale**: The existing `RolePlayBranchService` already handles session cloning, interaction copying, and parent-session references. Fork Above/Below are variants of the same pattern with different slicing logic. Keeping them in the same service maintains cohesion.

**Alternatives considered**:
- New `ISessionForkService`: introduces a new service for little benefit; fork is a branch operation.
- Reusing `ForkSessionAsync` with direction parameter: possible but the "Fork Below" semantic (start from N) differs from the current "start from 0 to N" assumption.

### D5: Delete Cascade — Position-Based Removal

**Decision**: "Delete + below" removes the targeted interaction plus all interactions at higher positions in the session list. Position is determined by the interaction's index in the `Interactions` list (after filtering out alternatives that belong to other parents). All alternatives of any removed interaction are also removed.

**Rationale**: The session's interaction list is ordered by insertion time. "Below" means "later in the conversation". This matches the UI's vertical timeline where newer interactions appear lower.

**Alternatives considered**:
- Timestamp-based removal (delete all after CreatedAt): fragile if clocks are adjusted or alternatives have varying timestamps.
- Soft delete (mark as deleted, filter in UI): increases complexity for no clear user benefit when the spec says deletion is permanent.

### D6: Edit Mode — UI-Only State with Content Snapshot

**Decision**: Edit mode is tracked entirely in the Blazor component (`RolePlayWorkspace.razor`) as a `Dictionary<string, string>` mapping interaction IDs to their pre-edit content snapshots. No domain flag is persisted. Save calls `InteractionCommandService.UpdateContentAsync` which replaces the interaction's `Content` property and triggers auto-save. Cancel restores from the snapshot dictionary.

**Rationale**: Edit mode is inherently transient — it only exists while the user is actively editing. Persisting it would add unnecessary complexity and could leave interactions "stuck" in edit mode if the browser closes.

**Alternatives considered**:
- `IsEditing` boolean on the domain model: needlessly persisted, creates stale state if not cleaned up.
- Separate edit component with its own state: over-engineering for a simple text replacement.

### D7: Generating State — Component-Level Tracking with UI Lockout

**Decision**: Track which interactions have in-flight generation using a `HashSet<string>` of interaction IDs in the workspace component. While an interaction ID is in the set, retry/rewrite toolbar commands are disabled for that interaction and a "generating..." indicator replaces the content area. On completion (success or failure), the ID is removed and commands re-enable.

**Rationale**: This is purely a UI concern. The service layer doesn't need to know about concurrent generation prevention — it's enforced at the component level by disabling the buttons. If a generation fails, the set is cleared and an inline error is shown per the spec clarification.

**Alternatives considered**:
- Server-side generation queue with status polling: over-engineering for a single-user local app.
- Optimistic locking on the interaction: unnecessary when the UI already prevents concurrent access.
