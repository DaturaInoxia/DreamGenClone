# Phase 0 Research: Wife-Husband Aftermath Closure

**Branch**: `001-husband-aftermath` | **Date**: 2026-07-04 | **Spec**: [spec.md](spec.md)

This document resolves the open questions surfaced during plan Technical Context and Constitution Check, before any design artifact (Phase 1) is generated. Each task lists Decision / Rationale / Alternatives considered.

---

## Task 1: Detection call budget — confirm a single inference call per encounter boundary (Option C)

**Decision**: One LLM inference call per encounter-boundary detection, with branched consequence on which marker(s) are active post-detection (Option C from the B-056 design plan).

**Rationale**: The existing `TryDetectEncounterBoundaryAsync` already issues a single inference per detection — `detected.EvidenceSpan` is the AI's own evidence-text capture. The aftermath marker only changes *what we do after detection succeeds* (branch the state-machine consequence), not *whether we call the model*. Keeping this at one call preserves the existing performance profile, the existing keyword gate (`ContainsEncounterCompletionKeywords(detected.EvidenceSpan)`), and the existing `LastEncounterEvidenceSpan` capture pattern (just store the same `EvidenceSpan` text in the new persisted field for the injector to read).

**Alternatives considered**:
- *Option A (per-marker dedicated detector)* — two inference calls, one per marker class. Rejected: doubles LLM cost and adds a second state-mutation site. The existing detector already returns the structured `EvidenceSpan`; the markers differ only in *post-detection* consequence.
- *Option B (parallel calls)* — same cost profile; complicates the re-entry guard. Rejected for the same reason as A.

---

## Task 2: `[Aftermath:husband-contrast]` marker convention — best practices for theme-author markers

**Decision**: Use the existing bracketed-namespace-keyword convention (`[Namespace:action]`) already established for `[ClimaxMode:multi-encounter]`. The marker is plain text in theme phase-guidance fields; detection is a case-insensitive `GuidanceText.Contains("[Aftermath:husband-contrast]", StringComparison.OrdinalIgnoreCase)`.

**Rationale**: Theme authors already understand this convention (ClimaxMode is in production). Reusing it means zero new UI surface, zero new persistence, zero learning curve. Strict-config compliance: the marker lives in editable persisted theme data, not in hidden code-only defaults — consistent with the repo's roleplay-engine no-fallback rule.

**Alternatives considered**:
- *Dedicated theme-author UI control* — adds a checkbox/toggle for each phase guidance entry. Rejected: inflates the theme-editor UI without delivering different behavior; the bracket convention already gets us freeform multi-marker composition (aftermath + multi-encounter + pacing combinations compose naturally).
- *Structured theme attribute (`RequiresHusbandAftermath: true`)* — would require a schema migration on `RPTheme.PhaseGuidance` and editor changes. Rejected for v1 scope; the marker carries the same intent at zero infrastructural cost.

---

## Task 3: Spouse resolution — reuse `RelationTargetId == personaName` lookup

**Decision**: Extract a `ResolveSpouseForAftermathAsync(RolePlaySession session, CancellationToken ct)` helper from the existing spouse-resolution logic in `BuildOpeningNarrativePromptAsync` (line 2730). Both code paths share one source of truth. Returns `(personaName, spouseName)` where the spouse is the first non-empty-name NPC whose `RelationTargetId` equals `personaName` (case-insensitive).

**Rationale**: The codebase already solves this problem once. The opening narrative's `coupleClause` construction uses exactly this lookup, and creating a second resolver would duplicate the contract — a forbidden pattern in the repo's no-fallback rule ("duplicated configuration-source resolution logic across services"). Extraction (not copying) keeps future changes single-site.

**Alternatives considered**:
- *New relation type enum (`SpouseOf` / `MarriedTo`)* and lookup by relation-type. Rejected: data migration risk and no current code paths use relation-type for spouse identification — `RelationTargetId == personaName` is the established convention.
- *Role-based lookup (CharacterRole.Partner / Spouse)*. Rejected for the same reason.
- *Free-form user override in the theme* (e.g. `[Aftermath:husband-contrast:character="Husband Name"]`). Rejected: extends theme-author surface unnecessarily; the existing spouse-resolution lookup is sufficient for the supported scenario patterns (M/F wife-cheating-on-husband). Edge cases (MFF, open-marriage) hit the explicit abort path with a diagnostic log, per the no-fallback rule.

---

## Task 4: Wire-level enum value for `AftermathCoupleInteraction`

**Decision**: `AftermathCoupleInteraction = 3` in the `TimeSkipPhase` enum. No DB migration for the `CurrentTimeSkipPhase` column itself — the existing `INTEGER NOT NULL DEFAULT 0` accepts the new value with zero schema work.

**Rationale**: The existing enum values are `None = 0`, `CloseScene = 1`, `AdvanceTime = 2`. The next free integer is `3`. The persisted column type is `INTEGER` (not a CHECK-constrained enum), so a new value needs no migration. The legacy back-compat read fallback at `RolePlayStateRepository.cs:595` (`(TimeSkipPhase)reader.GetInt32(35)`) handles the new value transparently — the cast is value-preserving. The dirty-flag contract docstring (already at `AdaptiveScenarioState.cs:219`) gets a one-line extension: "`LastEncounterEvidenceSpan` is also part of the dirty set — flushed at turn completion alongside the time-skip phase fields."

**Alternatives considered**:
- *Add a separate `AftermathPending` BOOLEAN column* (shadows the legacy `TimeSkipPending` approach). Rejected: doubles the persistence surface and creates a third state column to coordinate; one enum is cleaner and self-consistent.
- *Reuse `CloseScene = 1` for aftermath and add a differentiator flag*. Rejected: forces a phase-conditional branch into the existing leg emission block, breaking the existing test suite's `CloseScene → AdvanceTime` assertions.

---

## Task 5: DB migration pattern for `LastEncounterEvidenceSpan TEXT`

**Decision**: Add the `LastEncounterEvidenceSpan TEXT` column via the established `HasColumnAsync` + `ALTER TABLE` pattern in `RolePlayStateRepository.EnsureSchemaAsync` (mirrors the existing `CurrentTimeSkipPhase` migration at lines 1030–1041, except no backfill UPDATE is needed — the new column is nullable and defaults to NULL).

**Rationale**: The migration is idempotent (`HasColumnAsync` guard ensures first-run only), thread-safe via the existing startup lock, and consistent with the constitution's SQLite-default rule. Read-path mapping uses the explicit ordinal-aware pattern at line 595: `LastEncounterEvidenceSpan = reader.IsDBNull(36) ? null : reader.GetString(36)`. Write-path parameter binding uses `state.LastEncounterEvidenceSpan ?? (object)DBNull.Value` to handle the nullable reference.

**Alternatives considered**:
- *Re-derive `LastEncounterEvidenceSpan` from `detected.EvidenceSpan` at injector build-time rather than persisting it*. Rejected: requires keeping the detection result alive across turns until the aftermath injector fires; the persisted-field approach is robust to session reloads (the FR-006 pattern in `HydrateV2State`), and avoids the re-derivation cost.
- *Store the evidence span in a side table `AftermathEvidenceRecords`*. Rejected: over-engineered for a single optional string captured per encounter boundary; a denormalized nullable column suffices and matches the existing multi-encounter state-field pattern.

---

## Task 6: Injector pipeline priority slot for `HusbandAftermathInjector`

**Decision**: Priority 85 — fires after `PositionListInjector` (80) and before `BeatStageInjector` (90). Other injectors (TurnContext 5, TimeLocation 10, BehavioralFrame 20, ThemeContract 30, ThemeAIGuidance 40, IntensityContract 50, Escalation 60, SceneTimeDirection 70, ScenePresence 75) all fire unchanged around it. `FinalDirectiveInjector` (100) still fires — but its Fast Pacing HC is suppressed during the aftermath phase only via a targeted `BuildText` guard (not a `ShouldFire` filter).

**Rationale**: The aftermath contrast directive needs (a) the wife-husband framing data assembled by `BehavioralFrameInjector` (20) — character identity and role, (b) the scene-presence and position context from the position-list pipeline (75/80), and (c) it must appear *before* the final "Continue from your character's perspective." closer (100). Slotting at 85 places the directive after the scene context blocks and before the beat-stage and final directive — natural reading order in the assembled prompt.

**Alternatives considered**:
- Priority 95 (just before FinalDirectiveInjector). Rejected: would place the contrast directive after `BeatStageInjector` (90), which emits episodic beat-stage framing — pushing the contrast directive behind beat-stage context loses its prominence.
- Priority 70 (alongside SceneTimeDirection). Rejected: too early — the injector would emit before position/presence context is built, leaving the contrast directive floating without the scene scaffold around it.

---

## Task 7: Scope of Fast Pacing HC suppression in `FinalDirectiveInjector`

**Decision**: Suppress the Fast Pacing HC *only* during the `AftermathCoupleInteraction` phase. The `FinalDirectiveInjector` retains its base content (the "Continue from your character's perspective." line at the end of every prompt — that line is pacing-agnostic) and emits normally for `CloseScene` and `AdvanceTime`. Other HC variants (Slow burn, Slow burn-explicit, etc.) remain unchanged for all phases.

**Rationale**: Wide rejection of the entire `FinalDirectiveInjector` during aftermath would also drop the baseline "continue from your character's perspective" closer, which is pacing-neutral and necessary. The closure narrative is incompatible *only* with the Fast Pacing HC — Fast Pacing would race the model through to a time advance and starve the closure beat of its narrative room. Scope-limited suppression (one HC inside the `if (Pacing == Fast)` block in `BuildText`) delivers the contract with minimal blast radius. B-055's broader I7 wording for non-marker Fast remains a separate follow-up (out of scope per the plan's deferred items).

**Alternatives considered**:
- *Drop `FinalDirectiveInjector.ShouldFire` to false entirely during aftermath*. Rejected: removes the base closer, violating the contract that every prompt ends with a perspective anchor.
- *Introduce a new pacing enum value (`ScenePacing.Closure`)* to neutralize Fast. Rejected: extends the `SceneDirection.Pacing` enum and forces downstream consumers to handle a new value — over-engineered for a one-phase suppression.

---

## Task 8: Best practices — encounter-boundary detection in non-Climax phases

**Decision**: Reuse the existing `encounter-completed` semantic event mapping for the non-Climax aftermath cases. The mapping's existing keyword collage (orgasm / interruption / separation / afterglow / etc., per production themes) already covers exposure/interruption cases that occur in BuildUp, Approaching, or Committed phases.

**Rationale**: The mapping is phase-agnostic by design — it's a semantic-event detector that responds to sexual-content keywords in the model output text. Climax-locking the detection was an artifact of the multi-encounter-only era; the marker-driven path unlocks it without changing the keyword collage. The plan's out-of-scope list explicitly defers a dedicated "visual-only boundary" detector for non-sex scenarios to B-055 — the existing keyword collage is sufficient for the v1 aftermath cases (wife having sex or being visually exposed, then going back to her husband).

**Alternatives considered**:
- *Add per-phase keyword collages (BuildUp-specific, Committed-specific)*. Rejected: would force theme authors to maintain duplicate keyword sets and inflate the semantic-mapping surface without clear payoff for v1.
- *A new "exposure-completed" semantic event for non-sex exposure (separate from `encounter-completed`)*. Rejected for the same reason — deferred to B-055 I5 in the planning doc.

---

## Task 9: Best practices — actor selection contract during overflow legs

**Decision**: The aftermath leg returns *only* the wife and husband as `OverflowActorCandidate(ContinueAsActor.Npc, name, reason)`. Persona is excluded from the candidate batch (per clarification Q1 — persona observes; the actor filter is wife + husband by *relation*, not by persona-match). If either spouse is unresolvable, return an empty list and let the existing caller fall through to the no-overflow cleanup path; simultaneously abort the aftermath phase (clear `CurrentTimeSkipPhase` to `AdvanceTime` if multi-encounter, else `None`) and emit `HusbandAftermathAbortedMissingSpouse` debug + Serilog `LogWarning`.

**Rationale**: The existing caller at `RolePlayEngineService.cs:1475` and `:1261` already treats an empty `candidates` list as "no overflow," so the abort path is clean (no new control-flow). This honors the repo's no-fallback rule: missing-config aborts explicitly with a diagnostic log; no silent default actor selection ("best effort" / guessed RP values are forbidden).

**Alternatives considered**:
- *Auto-pick any non-wife non-persona character as a "stand-in" husband*. Rejected: directly violates the no-fallback rule ("hidden recovery paths that alter RP behavior without explicit configured data").
- *Emit a UI-facing toast / dialog when the spouse is missing*. Rejected for v1; the planning document's "Further Considerations 1" recommends debug-log-only, deferring comprehensive diagnostic-panel visibility to B-049. The roleplay diagnostic panel already surfaces `RolePlayDebugEventRecord` entries, so the `HusbandAftermathAbortedMissingSpouse` debug event is *visible* to the user debugging the session but doesn't interrupt gameplay.

---

## Task 10: Marker opt-in boundary — what is the valid phase guidance surface for the marker?

**Decision**: The marker MUST appear in a `PhaseGuidance` entry's `GuidanceText` field. Detection uses the same `Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase)).Any(x => x.GuidanceText.Contains("[Aftermath:husband-contrast]"))` pattern as `IsMultiEncounterClimax`. The marker is *valid* in any phase except `Reset` (where `IsAftermathHusbandContrast` explicitly returns `false`).

**Rationale**: This mirrors the existing `[ClimaxMode:multi-encounter]` surface exactly. Theme authors have one place to put the marker — the phase guidance text on the theme editor's phase-guidance panel. Multi-phase consistency: a theme author who wants aftermath to fire in any non-Reset phase that has an encounter simply adds the marker to each phase guidance entry they want it for; or just to the Climax phase guidance for the classic wife-cheating-in-climax setup.

**Alternatives considered**:
- *Theme-level marker* (one entry in `RPTheme.PhaseGuidance` for "all phases that have encounters"). Rejected: would couple the marker to a non-existent theme-level guidance surface and require schema/UI changes; phase-level placement matches the established `[ClimaxMode:multi-encounter]` pattern.
- *Allow marker on the `RPTheme` root (across all phases)*. Rejected: would force `Reset` phase to继承 the marker, violating FR-002's explicit Reset exclusion. Phase-level placement is precise.

---

## Task 11: Re-entry guard scope

**Decision**: The existing `if (state.CurrentTimeSkipPhase != None) return;` guard at `RolePlayEngineService.cs:4552–4561` naturally extends to the new three-leg flow. While the state machine is mid-`CloseScene`, mid-`AftermathCoupleInteraction`, or mid-`AdvanceTime`, no new encounter-boundary detection fires. The state machine must complete its full trajectory back to `None` before the next boundary can be detected. FR-012 codifies this.

**Rationale**: The existing guard already preserves the invariant "one state-machine trajectory at a time." Adding `AftermathCoupleInteraction` to the enum does not weaken this — it's still a single active phase at a time. New detection re-engages on the next interaction after `None` is restored.

**Alternatives considered**:
- *Reset the state machine on detection failure rather than awaiting None*. Rejected: would bypass the explicit leg ordering — the user's D7 decision ("Aftermath is BEFORE advance-time, not after") requires the leg sequence to complete as a unit.
- *Allow the user to abort the trajectory mid-stream via a slash command*. Rejected for v1; the `steer` command already provides user-override capability (out-of-scope per the plan; `/skipaftermath` is not introduced).

---

## Task 12: Concurrency / parallel test execution guard

**Decision**: Use the existing pure-unit test pattern from `MultiEncounterTimeSkipTests.cs` — inline `AdaptiveScenarioState` construction, no DI, no shared static state. The new `AftermathHusbandContrastTests.cs` follows the same pattern. No `ConcurrentDictionary`-style fixes are needed (the existing static cache is unaffected).

**Rationale**: Per repo memory (`/memories/repo/speckit-notes.md`), xUnit runs RP tests in parallel and any shared mutable state in `RolePlayEngineService` caches can corrupt results. The 28-test baseline tests survive this because they don't touch the cache; the new 18-test matrix inherits the same discipline.

**Alternatives considered**:
- Integration-style tests invoking `TryDetectEncounterBoundaryAsync` end-to-end. Rejected: requires Serilog + DI scaffolding, slows the suite, and doesn't add behavioral signal beyond what the pure-unit pattern delivers.

---

## Summary of resolved NEEDS CLARIFICATION items

None — the spec's Clarifications section already records the four user-driven answers (persona exclusion, non-Climax dual-marker chain scope, FR-010 prose rewrite scope, wife-character identification rule). All Technical Context and Constitution Check items above are technical-design decisions rather than spec ambiguities, and each has been settled above with a Decision / Rationale / Alternatives record.

## References

- Existing enum: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs:293–304`
- Existing detector: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:4544`
- Existing leg emission: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:1514–1600`
- Existing actor filter: `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs:2185`
- Marker helper to mirror: `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs:57`
- Repository migration pattern: `DreamGenClone.Infrastructure/RolePlay/RolePlayStateRepository.cs:1030`
- Backlog: `specs/Planning/backlog.md`
- Design source plan (already shipped by user): `specs/Planning/B-056-husband-aftermath.md`