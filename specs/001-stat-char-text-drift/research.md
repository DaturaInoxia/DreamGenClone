# Research: Stat-Driven Character Instruction Text & Encounter Dimension Drift

**Branch**: `001-stat-char-text-drift` | **Date**: 2026-05-30

All NEEDS CLARIFICATION items resolved. No unknowns remain.

---

## 1. Stat Delta Mutation Pipeline

**Decision**: Apply drift in `ApplyTrackedDelta()` — the single internal method that all three scoring paths (keyword, theme affinity, semantic events) converge on.

**Rationale**: Centralising drift in `ApplyTrackedDelta` means keyword scoring, theme affinity, semantic event scoring, and UI manual assignments all trigger drift through the same path without duplicating logic. `DecisionPointService.ApplyDeltas()` calls `CharacterStatProfileV2Accessor.ApplyDelta()` directly (bypassing `ApplyTrackedDelta`) — this path must also call `StatToDimensionMappings.ApplyDelta()` to satisfy FR-011 (any stat value change triggers drift). The third path — direct `SetStat()` from `RolePlayWorkspace.razor` for manual stat input — must also wire drift to satisfy FR-011.

**Alternatives considered**:
- Hook drift in `CharacterStatProfileV2Accessor.ApplyDelta()` — rejected because `ApplyDelta` is a low-level accessor with no knowledge of role context (required for rule lookup); injecting service dependencies there would violate the accessor's stateless pattern.
- Hook drift only in semantic scoring — rejected because FR-011 requires all stat changes to trigger drift.

---

## 2. Stat Mutation Paths for Tension & Connection (removal targets)

**Decision**: Remove from all four mutation paths.

**Findings**:
- **Keyword rules**: `RolePlayAdaptiveStateService` line ~260 — tension/connection keyword categories drive `ApplyTrackedDelta(..., "Tension"/"Connection", ...)`. Remove the keyword category entries from seeded data.
- **Theme affinity**: `StatAffinities["Tension"]` / `StatAffinities["Connection"]` entries on RPTheme records. Remove from DB seed data; any live theme affinity entries for these stats silently produce no mutation once `ResolveSupportedStatName` no longer recognises them.
- **Semantic event mappings**: `RPSemanticStatMapping` rows with `TargetStat = "Tension"` or `"Connection"` in DB. Must be removed from seed.
- **Average calculations in `RolePlayContinuationService`**: `AverageTension` and `AverageConnection` lines ~795–800 must be removed along with the record fields.

---

## 3. Persistence Timing

**Decision**: No change needed — existing save pattern is already per-interaction.

**Findings**: `RolePlayStateRepository.SaveAdaptiveStateAsync()` is an upsert triggered after every `UpdateFromInteractionAsync()` call. `CharacterSnapshotsJson` is persisted on every scoring cycle. `RuntimeEncounterStats` will be serialised inside the existing JSON column at the same frequency — no new save hooks needed.

---

## 4. Session-Start Frame Coherence (Q3 clarification)

**Decision**: `RuntimeEncounterStats` restored from `CharacterSnapshotsJson` on session load must be passed into `ScenarioGuidanceInput.CharacterRuntimeStats` immediately — no delta required first.

**Finding**: `RolePlayContinuationService.BuildPromptAsync()` at lines ~781–822 constructs `ScenarioGuidanceInput` from `session.AdaptiveState`. After implementing this feature, it will read `session.AdaptiveState.CharacterStats` (the runtime snapshot dict) and include it in `CharacterRuntimeStats`. Since `CharacterStats` is populated by `RebuildCharacterStatsCache()` at load time, the drifted `RuntimeEncounterStats` is already available on the first continuation of a resumed session. No special handling required.

---

## 5. Profile Rebind Reset (Q2 clarification)

**Decision**: When `CharacterEncounterProfileIds[charId]` changes to a new profile ID, `RuntimeEncounterStats` for that character must be reset to the new profile's `EncounterStats` values.

**Finding**: Profile rebind occurs in two places:
1. `RolePlayEngineService` line 302 (session creation)
2. `RolePlayWorkspace.razor` line 1526 (mid-session UI change)

Both set `session.AdaptiveState.CharacterEncounterProfileIds[characterId] = profileId`. The reset of `RuntimeEncounterStats` must be wired at both sites, or handled centrally by introducing a `RebindEncounterProfile(characterId, newProfileId)` method on `RolePlayAdaptiveStateService` that is called from both sites and handles both the `CharacterEncounterProfileIds` assignment and the `RuntimeEncounterStats` reset.

**Preferred pattern**: Centralise in a new `RolePlayAdaptiveStateService.RebindEncounterProfileAsync(characterId, profileId)` method to avoid duplicate reset logic.

---

## 6. Reset Character Action

**Decision**: FR-023 (reset clears `RuntimeEncounterStats`) is self-contained — no separate "reset character" UI component needs to be created as part of this feature. The clearing of `RuntimeEncounterStats` is handled by the same profile rebind path, since "reset character" is currently implemented as "reapply the original profile".

**Finding**: No standalone "reset character" action exists in the UI. The baseline reset is triggered by profile rebind (new profile sets `BaselineStats`). FR-023 is satisfied by ensuring the rebind path (decision 5 above) always clears `RuntimeEncounterStats`.

---

## 7. UI Components Showing Tension & Connection

**Decision**: Remove Tension and Connection from all UI stat display/edit panels as part of the stat reduction scope (Q4 clarification: UI cleanup is in scope).

**Findings**:
- `RolePlayWorkspace.razor` — adaptive stats panel iterates `AdaptiveStatCatalog.CanonicalStatNames`. Removing Tension and Connection from that catalog is the single change required; the panel is data-driven.
- `RolePlayWorkspace.razor` — average stat calculations for `tension` and `connection` (lines ~2087–2104) must be removed.
- `ThemeProfiles.razor` — theme stat affinity UI shows Tension and Connection delta fields. The affinity UI is data-driven from `CanonicalStatNames`; removing those names from the catalog removes them from the UI automatically. Any hardcoded references must be identified and removed.

---

## 8. Prompt Injection Format Decision

**Decision**: Use format consistent with the second injection site (per-turn constraints block):
`HARD CONSTRAINT — enforce in this response: {label} current state: {statStateText}`

**Rationale**: The second injection site (line 1205 in `RolePlayContinuationService`) uses `"HARD CONSTRAINT — enforce in this response:"` — the stronger, per-response directive phrasing. Stat state text belongs at this stronger level, not in the scenario guidance block. Placing it immediately after the behavioral frame for the same character in this block maintains co-location of character directives.

**Finding**: Two injection sites exist for character behavioral frames:
1. `AppendScenarioGuidance()` in `RolePlayAssistantPrompts` (structural guidance block) — uses format `HARD CONSTRAINT — {label} behavioral frame (authoritative, ...):`
2. Per-turn constraints block in `RolePlayContinuationService` ~line 1205 — uses format `HARD CONSTRAINT — enforce in this response: {label} behavioral frame: {frameText}`

Stat state text will be injected at site 2 only, immediately after the matching behavioral frame line for the character.

---

## 9. Synthesized Sentence Approach

**Decision**: Concatenate active band texts with `"; "` separator. No template or NLG required.

**Rationale**: Band texts are already written as complete LLM-directive clauses ("she craves physical intensity with urgency"). Joining with `"; "` produces grammatically coherent multi-clause directives the LLM handles well. NLG synthesis (combining into a single grammatical sentence) adds implementation complexity with minimal gain — LLMs parse semicolon-separated constraint clauses reliably.

**Example**: Desire=82 + Restraint=12 + Loyalty=15 for Wife →
`"she craves physical intensity with urgency; she will initiate, escalate, and pursue without hesitation; she has almost no capacity to hold back; inhibition is functionally absent; her commitment to her marriage is effectively absent; she feels no guilt and faces no internal resistance to transgression"`

---

## 10. AdaptiveStatCatalog — Single Source of Truth for Stat Names

**Finding**: `AdaptiveStatCatalog.CanonicalStatNames` (likely in `DreamGenClone.Domain` or `DreamGenClone.Application`) is the enumerable of canonical stat name strings used by both the UI stats panel and by stat resolution logic. Removing Tension and Connection from this catalog drives the UI cleanup automatically. This is the correct place to make the stat reduction change rather than patching individual files.

**Action**: Confirm `AdaptiveStatCatalog` location and modify it to drop Tension and Connection. All data-driven stat references cascade from there.
