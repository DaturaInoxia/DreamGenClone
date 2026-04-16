# HP2 Execution Checklist

Date: 2026-04-15
Scope: RolePlay V2 HP2 validation follow-up
Mode: Single-issue slices, no mixed-scope changes per pass

## Goal

Preserve momentum from HP2 findings while preventing scope drift. Execute fixes in a stable sequence with explicit done criteria.

## Open Items (Tracked)

- Issue 2: First interaction stat deltas need clearer explainability/auditability.
- Issue 8: Move character stat keyword matching/categories to DB tables and add CRUD Profile UI (remove hardcoded hidden-only behavior).
- Issue 3: Diagnostics readability is low (IDs/reason strings are not human-meaningful).
- Issue 5: Writing style profile should potentially be reduced to Name/Description/Example/RuleOfThumb only.
- Issue 6: Narrative prompt should clearly reflect atmospheric intensity + selected writing style behavior.
- Issue 7: Repeated log-reader warnings when active log file is locked by another process.

## Completed Items

- Issue 1: Unexpected extra adaptive character entry appears after first continue.
	- Fixed in engine sync and regression-tested.
	- User runtime validation confirmed in HP3 (`9ff2683fbfa34680b6dacf668dcc22a5`).
- Issue 4: RP1 non-canonical adaptive stat keys in character stats.
	- Canonical-only guards implemented in update/seed/sync paths.
	- User runtime validation confirmed fixed in RP2.

## Enhancements Backlog (Planned)

- RolePlay v2 Runtime Phase Ladder visualization.
	- Goal: show all narrative phases in a ladder/flow view with clear progression context.
	- UX expectations:
		- Distinct visual indicator for current phase (color, weight, emphasis).
		- Progress-to-next-phase indicator for current phase completion proximity.
		- Readable phase ordering to make transition direction obvious.
	- Planning notes:
		- Define per-phase completion signal source (for example interaction counts, phase gates, confidence/fit thresholds).
		- Keep this enhancement separate from Issue 2 behavior work; deliver as its own scoped slice.

## Priority Order

1. Issue 3 (Diagnostics readability)
2. Issue 1 (Extra adaptive character)
3. Issue 4 (Canonical stat-key containment)
4. Issue 2 (Stat-delta explainability hardening)
5. Issue 8 (DB-backed stat keyword/category configuration + CRUD UI)
6. Issue 6 (Narrative prompt construction clarity/correctness)
7. Issue 7 (Log file read contention warnings)
8. Issue 5 (Steering profile model simplification decision)

Rationale:
- Issue 3 improves all subsequent debugging and runtime validation quality.
- Issue 1 likely affects adaptive correctness and should be addressed after visibility improves.
- Issue 2 is partly expected behavior; improve traceability after core visibility and contamination concerns.

## Slice A: Issue 3 Implementation Plan

Tasks:
- Add human-readable profile names alongside internal ids in adaptive transition displays/log metadata.
- Add plain-language translation for transition reason strings.
- Keep raw machine reason/id fields available for forensic use.
- Add tests for reason translation and profile name resolution fallbacks.

Done Criteria:
- Runtime panel shows readable transition summary and profile names.
- Logs/debug events include both machine and human-readable forms.
- No regression in existing transition computation.

Validation:
- Build web and tests project.
- Run targeted tests for transition reason parsing/rendering.
- Manual HP2 spot-check: one transition event readable without decoding ids.

## Slice B: Issue 1 Investigation/Fix Plan

Tasks:
- Trace first-continue path for adaptive character map insertions.
- Identify source of non-scenario character key/id insertion.
- Add guard/normalization to prevent unintended actor identity materialization.
- Add regression test for first-continue character map integrity.

Done Criteria:
- No extra adaptive character entry appears after first continue in HP2.
- Scenario-defined characters remain intact.
- Regression test fails before fix and passes after fix.

Validation:
- Re-run HP2 first-continue workflow.
- Confirm character stats panel and adaptive map are aligned.

## Slice C: Issue 2 Explainability Plan

Tasks:
- Add per-interaction stat-delta reason details (keyword/theme-affinity contributors).
- Emit concise actor-level explanation payload for deltas.
- Add tests for delta provenance formatting.

Done Criteria:
- For a given interaction, developer can answer why each non-zero delta occurred from logs/debug data alone.
- No change to core stat mutation logic unless separately approved.

Validation:
- Targeted tests for delta provenance output.
- HP2 replay spot-check for Becky sample pattern.

## Slice D: Issue 4 Canonical Stat-Key Containment Plan

Tasks:
- Enforce canonical-only character stat keys during interaction updates.
- Enforce canonical-only character stat keys during scenario seeding.
- Ignore unsupported stat names coming from profile/theme stat bias or affinity payloads.
- Ensure V2 snapshot sync rewrites stat blocks to canonical key set only.
- Add regression test proving non-canonical keys are removed.

Done Criteria:
- Character stats contain only canonical adaptive keys after update/seed/sync paths.
- Fresh RP1 runtime no longer displays Husband/Wife-prefixed adaptive stat keys.
- Regression test covers non-canonical key cleanup.

Validation:
- Build web/tests projects (or validate no compile diagnostics in changed files when build is lock-blocked).
- Run targeted adaptive-state regression tests.
- Manual RP1 replay spot-check in a new clean session.

## Guardrails

- One issue per implementation pass.
- Do not combine behavior changes with broad refactors.
- Keep HP2 issue log updated after each pass.
- If runtime evidence conflicts with assumptions, pause and re-baseline before coding.

## Next Action

Start Slice A (Issue 3) with a focused diagnostics readability patch and tests.

## Progress Update

- 2026-04-15: Slice A started.
- Implemented in UI: human-readable formatting for `Resolution Reason`, `Adaptive Transition Reason`, `Adaptive Transition Path`, `Recent Transition`, and latest transition reason row.
- Machine trace retained: raw reason strings and ids are still included in display for forensic debugging.
- 2026-04-15: Slice B started and code fix applied for Issue 1.
- Implemented in engine sync: preserve existing character-name keys when applying V2 snapshots by matching `CharacterId` and avoid creating duplicate GUID/id keys.
- Added regression test to lock expected behavior (`SyncSessionAdaptiveStateFromV2_UsesExistingNameKey_ForMatchingCharacterId`).
- 2026-04-15: Enhancement A added to planned backlog.
  - RolePlay v2 Runtime phase ladder/flow visualization with current-phase highlighting and progress-to-next-phase indicator.
- 2026-04-15: Slice C started for Issue 2.
  - Added per-stat provenance payload (`statDeltaReasons`) to adaptive debug events so each non-zero delta includes source contributors.
	- Added policy hardening in adaptive interaction updates:
		- Top-1 theme-affinity stacking for stat mutation.
		- Phase-based theme-affinity caps (`BuildUp=0`, `Committed=1`, `Approaching=1`, `Climax=2`, `Reset=0`).
		- Early-turn per-stat cap (first 3 actor turns, abs delta <= 2).
		- Global per-interaction total abs-delta budget (<= 10).
- 2026-04-15: Slice D started for Issue 4.
	- Added canonical-only cleanup guards in adaptive update/seed paths and ignored unsupported bias/affinity stat names.
	- Updated V2 snapshot sync to rewrite character stat dictionaries with canonical keys only.
	- Added regression test `UpdateFromInteractionAsync_RemovesNonCanonicalStatKeys`.
	- Build/test execution currently blocked by active `.NET Host` lock on `DreamGenClone.Web\bin\Debug\net9.0\DreamGenClone.dll`.
- 2026-04-15: Slice D runtime validation completed.
	- User confirmed fix in RP2; non-canonical Husband/Wife-prefixed keys no longer reproduced.
- 2026-04-15: New issue intake added.
	- Issue 5: evaluate whether `ThemeAffinities`, `EscalatingThemeIds`, and `StatBias` should remain runtime features or be removed/hidden.
	- Issue 6: validate narrative prompt intensity/style composition clarity against expected atmospheric + selected-writing-style behavior.
	- Issue 7: fix recurring `Failed to read log file` warnings caused by log reader contention.
	- Issue 8: externalize character stat keyword/category mappings to DB + add Profile UI CRUD so mappings are not hardcoded/hidden.
- Remaining in Slice A:
	- Add/adjust tests for reason translation and profile-name fallbacks.
	- Validate runtime output in HP2 manual pass.

## Immediate Next Validation

- Validate Issue 2 output in runtime/debug event viewer by checking `statDeltaReasons` appears and is human-actionable.
- Start Issue 8 design pass: define schema + migration for keyword categories/stat rules and scaffold CRUD page.
- Begin Issue 6 prompt-construction validation with concrete before/after prompt captures in narrative mode.
- Complete remaining Slice A tests for diagnostics readability formatting.
