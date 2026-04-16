# HP2 Validation Issue Log

Session: HP2
SessionId: b8ea5fbc-323b-4d00-ba1e-fe29ded13e8d

## Issue 1

- Reported by: User
- Status: Resolved (validated in HP3 runtime)
- Category: Adaptive character state contamination / unexpected character entry
- Reported after: First default continue interaction
- User-observed unexpected character id: df08d5dd-9f8d-4340-ac74-a35c5da55089
- User-observed label/context: "Closest Character Stat Profile" / "Migrating Toward"
- User expansion: all scenario-defined characters are present and correct; this id appears as an additional character.
- User expansion: the extra character appears only after the first initial continue.
- User-observed stats:
  - Desire: 73
  - Restraint: 85
  - Tension: 23
  - Connection: 85
  - Dominance: 90
  - Loyalty: 15
  - SelfRespect: 90

## Runtime Evidence Collected

- Session created and started cleanly:
  - `Role-play session created` and `opened/start` in `DreamGenClone.Web/logs/dreamgenclone-20260415.log`.
- First interaction window telemetry present:
  - Compatibility check started/passed.
  - Diagnostics snapshots with `DecisionCount=0`.
  - Decision attempt telemetry present with `SkipReasons=TriggerCadenceNotReached`.
- Current log search result:
  - No direct match in log text for `df08d5dd-9f8d-4340-ac74-a35c5da55089` or `Closest Character Stat Profile` in `dreamgenclone-20260415.log`.
  - This suggests the unexpected entry may be in persisted session state/UI composition rather than explicit log line emission.
  - Expansion consistency check: the symptom pattern (appears after first continue, not at start) aligns with a post-continue state merge/materialization path rather than initial scenario hydration.

## Next Validation Step (tracking only)

- Capture persisted state payload for HP2 and inspect adaptive character map entries to confirm source path of the unexpected character object.

### Issue 1 Implementation Progress (2026-04-15)

- Root cause confirmed in `RolePlayEngineService.SyncSessionAdaptiveStateFromV2(...)`:
  - V2 snapshots were materialized into `AdaptiveState.CharacterStats` keyed by `snapshot.CharacterId` (GUID/id), which introduced an extra id-key entry even when the character already existed under a display-name key.
- Fix implemented:
  - Sync now resolves existing stat-block key by matching `CharacterId` first, preserving the existing display-name key when present.
  - Added fallback resolution via `CharacterPerspectives` for stable name key mapping.
  - Added cleanup path to remove stale duplicate id-key entries created by prior behavior.
- Regression test added and passing:
  - `SyncSessionAdaptiveStateFromV2_UsesExistingNameKey_ForMatchingCharacterId` in `DreamGenClone.Tests/RolePlay/RolePlaySessionBaseStatInitializationTests.cs`.
- Runtime validation confirmation:
  - User confirmed fix in new session HP3.
  - HP3 SessionId: `9ff2683fbfa34680b6dacf668dcc22a5`.
  - Observed outcome: extra adaptive character id entry did not reappear after continue.

## Issue 2

- Reported by: User
- Status: Open (tracking only, no fix attempted)
- Category: Adaptive stat mutation timing (first interaction)
- Reported after: First interaction/continue in HP2
- InteractionId: 02cfe72a-fb30-4c0a-aedb-28148a1ae559
- ActorKey: Becky
- User-observed stat deltas:
  - Desire: +2
  - Restraint: +1
  - Tension: +3
  - Loyalty: +2
  - SelfRespect: +1

### Why this occurs (code-path explanation)

- The first generated interaction is immediately processed by adaptive-state update logic; there is no warmup delay/guard that skips first-turn stat mutation for character interactions.
- In the interaction pipeline, continuation persistence calls adaptive update right away:
  - `RolePlayEngineService.ContinueAsync(...)` appends interaction, then calls `RolePlayAdaptiveStateService.UpdateFromInteractionAsync(...)`.
- Inside `UpdateFromInteractionAsync(...)`, when the actor is not `Narrative`, `System`, or `Instruction`, the service:
  - Resolves actor key (here: `Becky`).
  - Snapshots current actor stats (`statsBefore`).
  - Applies keyword-driven stat signals to the actor stats using `ScoreStatSignal(...)` with per-stat keyword lists and caps.
  - Optionally applies additional per-theme `StatAffinities` deltas when matching theme signals are present.
  - Emits `AdaptiveStateUpdated` debug metadata with `interactionId`, `actorKey`, and computed `statDeltas` = `(after - before)`.
- The payload shape reported by user (`interactionId`, `actorKey`, `statDeltas`) exactly matches the emitted adaptive debug event metadata contract.

### Relevant mechanics (current behavior)

- Keyword signal path (direct interaction text heuristics) runs on every eligible character interaction:
  - Desire keywords include terms such as `kiss`, `touch`, `desire`, `want`, `close`, `heat`.
  - Restraint keywords include terms such as `can't`, `wrong`, `shouldn't`, `hesitate`, `guilt`.
  - Tension keywords include terms such as `fear`, `caught`, `risk`, `panic`, `nervous`.
  - Loyalty keywords include positive/negative sets (for example `promise`, `vow`, `faithful` vs `affair`, `betray`, `cheat`).
  - SelfRespect keywords include positive/negative sets (for example `boundary`, `respect`, `dignity` vs `humiliate`, `ashamed`, `degraded`).
- Theme-affinity path can add extra increments on top of keyword deltas when active scored themes define `StatAffinities`.
- Net result: first interaction can legitimately produce immediate non-zero deltas, including multi-stat increments like the reported Becky payload.

### Evidence confidence and remaining gap

- High confidence on mechanism: confirmed directly in adaptive update code and event metadata construction.
- Remaining gap: exact lexical trigger terms for this specific interaction text were not recovered from flat logfile search; this likely requires direct persisted interaction payload inspection (DB/session payload), not only rolling app log lines.

### Issue 2 Evidence Addendum

- Addendum date: 2026-04-15
- Scope: HP2 first interaction runtime interpretation
- Evidence linkage:
  - Interaction payload shape (`interactionId`, `actorKey`, `statDeltas`) is aligned with the adaptive debug event emitted during `AdaptiveStateUpdated`.
  - The reported deltas are directionally consistent with first-turn keyword scoring and optional theme stat affinity contribution.
- Interpretation:
  - This looks like expected first-interaction adaptive mutation behavior under current design, not an out-of-band mutation path.
  - The event still remains tracked as an issue because the validation objective is to verify timing expectations explicitly in HP2.
- Outstanding forensic detail (still open):
  - Confirm exact matched lexical tokens in the persisted interaction content for this specific interaction id.

### Issue 2 Implementation Progress (2026-04-15)

- Explainability enhancement implemented in adaptive debug payload emission:
  - Added `statDeltaReasons` alongside `statDeltas` in `AdaptiveStateUpdated` metadata.
  - Each non-zero stat delta now includes source contributors such as keyword buckets (for example `keyword:tension(+2)`) and theme affinity contributions (for example `theme-affinity:infidelity(+1)`).
- Scope:
  - No change to core stat mutation outcomes.
  - Adds provenance only, so delta causes can be read directly from debug events.
- Validation:
  - File-level compile diagnostics show no errors in updated adaptive service code.
  - Web project build succeeded (with transient file-lock retry warning only).

### Issue 2 Additional Runtime Evidence (2026-04-15)

- Session investigated: `RP1` (internal session id `0663ffe3-ba9e-4823-b390-28a31e29c1f9`)
- User-provided reference id: `355ac0a7d8184c9884586ce59b47821e` (used as investigation handle)
- Interaction investigated: `3b0275f1-7383-40d8-8ae3-36a972c37662` (Actor: `Becky`)
- Recorded delta payload:
  - `Desire +3`, `Restraint +3`, `Tension +4`, `Connection -3`, `Dominance -3`, `Loyalty -4`, `SelfRespect -2`
- Recorded reason payload (`statDeltaReasons`) confirms exact additive composition:
  - `Desire`: `keyword:desire(+1)` + `theme-affinity:infidelity-brief-disappearance(+1)` + `theme-affinity:infidelity-public-facade(+1)`
  - `Restraint`: `+1` + `+1` + `+1` from three infidelity theme-affinity contributors
  - `Tension`: `+1` + `+2` + `+1` from three infidelity theme-affinity contributors
  - `Connection`: `-1` + `-1` + `-1` from three infidelity theme-affinity contributors
  - `Dominance`: `-1` + `-1` + `-1` from three infidelity theme-affinity contributors
  - `Loyalty`: `-1` + `-2` + `-1` from three infidelity theme-affinity contributors
  - `SelfRespect`: `keyword:selfrespect-positive(+1)` + `-1` + `-1` + `-1` from three infidelity theme-affinity contributors

#### Interpretation

- The large first-character-turn delta is caused by stacking across multiple concurrently matched themes, not by a single keyword path.
- There is currently no first-turn or early-turn cap for stat-delta magnitude in adaptive stat mutation.
- Existing early-phase guard (`early-phase-no-escalation`) applies to adaptive intensity transition delta, not to character stat updates.

### Issue 2 Policy Hardening Implementation (2026-04-15)

- Implemented concrete stat-delta control policy in adaptive interaction updates:
  - Theme-affinity stacking limit: top `1` theme for stat-affinity application per interaction.
  - Theme-affinity phase caps:
    - `BuildUp=0`, `Committed=1`, `Approaching=1`, `Climax=2`, `Reset=0` (per-stat affinity contribution cap).
  - Early-turn per-stat cap:
    - First `3` actor turns, each stat delta capped at absolute `2`.
  - Global per-turn delta budget:
    - Sum of absolute deltas capped at `10` per interaction.
- Observability:
  - Policy-adjusted changes append explicit `policy:*` reasons into per-stat contributor metadata when caps alter raw deltas.

## Validation Note A: First Interaction Runtime Snapshot Looks Correct

- Status: Informational (non-issue confirmation)
- Context: RolePlay v2 runtime state immediately around first interaction in HP2
- User-provided snapshot:
  - Current Phase: BuildUp
  - Active Scenario: (none)
  - Active Variant: (none)
  - Willingness Profile: (none)
  - Husband Awareness Profile: (none)
  - Interactions Since Commitment: 0
  - Interactions In Approaching: 0
  - Completed Scenarios: 0
  - Latest Transition: (none)
  - Latest Decision: (none)
  - Top Candidate: infidelity
  - Top Candidate Confidence: 0.00
  - Top Candidate Fit: 0.0
- Assessment:
  - This snapshot is consistent with expected first-interaction baseline before scenario commitment.
  - BuildUp with no active scenario/variant and zero commitment counters is coherent at this stage.
  - A named top candidate with 0 confidence/fit is plausible as a ranked placeholder without commitment threshold crossing.

## Enhancement Request A

- Reported by: User
- Status: Planned (not started)
- Category: RolePlay v2 runtime UX clarity
- Request:
  - Add a phase ladder/flow visualization in RolePlay v2 Runtime showing all phases in order.
  - Highlight current phase with strong visual treatment.
  - Show how close current phase is to completion and transition to next phase.
- Planning guidance:
  - Define explicit phase completion/proximity metrics before implementation.
  - Keep this enhancement in a dedicated slice after active issue queue work.

## Issue 4

- Reported by: User
- Status: Resolved (validated in RP2 runtime)
- Category: Character stat contamination (non-canonical stat keys)
- Reported in: RP1 (new clean session using v2 Theme Template)
- Symptom:
  - Character stats panel included non-canonical keys (for example `Husband Connection`, `Wife Desire`, `Husband Tension`) alongside canonical adaptive stats.
- Expected:
  - Runtime adaptive character stats should contain canonical stat set only (`Desire`, `Restraint`, `Tension`, `Connection`, `Dominance`, `Loyalty`, `SelfRespect`) unless explicitly designed otherwise.

### Issue 4 Implementation Progress (2026-04-15)

- Added canonical-only enforcement in adaptive state service:
  - Non-canonical stat keys are pruned during interaction updates and scenario seeding.
  - Unsupported stat names from style-profile stat bias and theme stat affinities are ignored.
- Added canonical-only rewrite during V2 snapshot sync in engine service:
  - Snapshot apply now rewrites stat blocks with canonical key set only, preventing stale/legacy keys from persisting.
- Added regression test:
  - `UpdateFromInteractionAsync_RemovesNonCanonicalStatKeys` in `DreamGenClone.Tests/RolePlay/RolePlayAdaptiveStateServiceTests.cs`.
- Validation note:
  - Compile diagnostics are clean for changed files.
  - Full test/build execution was blocked by a running `.NET Host` file lock on `DreamGenClone.Web\bin\Debug\net9.0\DreamGenClone.dll`.
- Runtime validation confirmation:
  - User confirmed fixed in new session RP2.
  - Observed outcome: non-canonical Husband/Wife-prefixed stat keys no longer appear in Character Stats.

## Issue 5

- Reported by: User
- Status: Open (needs design decision + impact analysis)
- Category: Steering profile model simplification
- Request:
  - Profile Theme Writing style should only require `Name`, `Description`, `Example`, and `Rule of Thumb`.
  - User concern: additional attributes may be unnecessary.

### Issue 5 Initial Findings (2026-04-15)

- The additional steering profile attributes are currently used by runtime logic:
  - `ThemeAffinities`: used to bias theme scoring multipliers during interaction updates.
  - `EscalatingThemeIds`: present in model and persistence, usage impact needs deeper trace before removal.
  - `StatBias`: used to bias adaptive character stats during scenario seeding.
- Implication:
  - Removing extra attributes is a behavior change, not only schema cleanup.
  - Requires explicit decision whether to retire those mechanics or keep them but hide from UI.

## Issue 6

- Reported by: User
- Status: Open (prompt construction audit)
- Category: Narrative prompt assembly correctness
- User concern:
  - Narrative prompts should use Intensity Theme Atmospheric.
  - Writing style should reflect selected writing style profile.
  - Prompt preview appears to show style profile id/details but no explicit intensity theme line.
  - Example snippet: `Writing Style Profile: ba99d5b9f5be4584ab72e93bb311dae4`.

### Issue 6 Initial Findings (2026-04-15)

- Narrative intent path explicitly forces atmospheric intensity at prompt-build time:
  - For `PromptIntent.Narrative`, resolved intensity is forced to `Intro` (atmospheric) and reason is tagged `narrative-forced-atmospheric`.
- Prompt output includes resolved intensity lines (`Resolved Intensity`, `Resolved Intensity Description`) rather than a literal label `Intensity Theme Atmospheric`.
- Writing style profile handling currently prints selected profile id plus description/example/rule-of-thumb blocks.
- Potential UX/clarity gap:
  - Prompt text may look like id-centric style metadata and may not make atmospheric forcing explicit enough in user-facing preview.

## Issue 7

- Reported by: User
- Status: Open (bug)
- Category: Logging/diagnostics log-file read contention
- Symptom:
  - Console warning repeats: `Failed to read log file ... because it is being used by another process`.
  - Affects `DreamGenClone.Web/logs/dreamgenclone-YYYYMMDD.log`.

### Issue 7 Initial Findings (2026-04-15)

- Warning source identified in debug event log reader:
  - `RolePlayDebugEventService.GetRecentLogLinesAsync(...)` calls `File.ReadLines(...)` on active rolling log files.
  - Exceptions are logged as warning: `Failed to read log file {LogFile}`.
- Likely cause:
  - Reader and logger sink compete on same active file with non-shared lock semantics from another process/sink instance.
- Candidate remediation paths (to decide in fix pass):
  - Open file with explicit shared read/write access and resilient retry/backoff.
  - Skip currently-active locked file and read previous rolled file(s) only.
  - Reduce warning noise by downgrading expected lock-contention cases to debug level with throttle.

## Issue 8

- Reported by: User
- Status: Open (queued as next implementation item)
- Category: Adaptive stat-rule configurability / admin UX
- Request:
  - Move character stat keyword matching out of hardcoded code paths.
  - Add database-backed configuration for Character Stats and keyword matching categories.
  - Add a Profile UI CRUD page so these mappings are visible and editable.
  - Hardcoded/hidden keyword-to-stat mapping should no longer be the only source of truth.

### Issue 8 Scope (planned)

- Data model:
  - Add table(s) for keyword categories and stat mapping rules (for example category metadata + keyword entries + target stat + polarity/weight + caps).
  - Add migration and seed path for current default mappings.
- Runtime:
  - Replace direct hardcoded keyword arrays with repository/service lookups.
  - Preserve canonical stat-name validation and normalization.
- UI:
  - Add an admin/profile CRUD page for managing categories, keywords, and stat mapping behavior.
  - Include validation and preview/impact hints where possible.
- Observability:
  - Include mapping source id/category in debug metadata so statDeltaReasons remain explainable.

## Issue 3

- Reported by: User
- Status: Open (tracking only, no fix attempted)
- Category: Observability clarity / diagnostics UX
- Problem statement:
  - Adaptive runtime diagnostics are confusing and not meaningful enough for user or developer interpretation.
  - IDs and compact reason strings do not clearly convey decision intent or actionable meaning.
- User-provided example values:
  - Resolution Reason: `selected=SuggestivePg12, adaptive=Emotional, progression=early(-1)`
  - Adaptive Transition Reason: `desire-low-or-restraint-high-deescalate|phase=BuildUp|phase-delta=0|stat-delta=-1`
  - Adaptive Transition Time: `2026-04-15 17:09:00`
  - Adaptive Transition Path: `96b9e19cd16048a49e6460d0c115e658 -> a441720bf98d49d5b599aa460114a8f6`
  - Recent Transition: `96b9e19cd16048a49e6460d0c115e658 -> a441720bf98d49d5b599aa460114a8f6 (desire-low-or-restraint-high-deescalate|phase=BuildUp|phase-delta=0|stat-delta=-1)`

### Intent of these fields (current design intent)

- `Resolution Reason`:
  - Encodes why the final profile choice was resolved from multiple influences.
  - `selected=SuggestivePg12` means the explicit selected profile baseline.
  - `adaptive=Emotional` means adaptive engine target profile outcome.
  - `progression=early(-1)` means early-phase progression rule applied a downshift by one level.
- `Adaptive Transition Reason`:
  - Encodes the stat/phase rule that triggered de-escalation or escalation.
  - In this sample, low desire or high restraint triggered de-escalation, with phase and delta components appended.
- `Adaptive Transition Path` and `Recent Transition`:
  - Show source profile id -> target profile id for the transition that occurred.
  - Current rendering uses internal profile ids, which are stable for machines but low-meaning for humans.

### Why this is still a valid issue

- Even if the fields are technically correct, they are not currently self-describing.
- Human-readable profile names and translated reason text are missing from primary display.
- This creates high cognitive load during HP2 validation and weakens debugging usefulness.

### Tracking outcome

- Logged as a usability/observability issue for diagnostics readability.
- No fix applied in this validation pass.

### Implementation Progress (2026-04-15)

- Slice A started: diagnostics readability patch implemented in RolePlay workspace UI.
- Updated runtime display to include human-readable translations for resolution/transition reasons while retaining raw machine strings.
- Updated transition path display to show profile names with ids (instead of ids only).
- Updated style-resolution interpretation to prioritize current Intensity Profile names in readable text (reducing legacy enum-token confusion such as `SuggestivePg12`).
- Added a collapsible `Adaptive Resolution Diagnostics` block so detailed diagnostic rows are hidden by default and expanded on demand.
- Added a new `Resolution Explanation` row that states, in plain English, what the system was trying to do, what it changed, and why for the current session state.
- Pending:
  - Add focused tests for translation/fallback formatting behavior.
  - Complete HP2 manual runtime verification pass for updated readability output.
