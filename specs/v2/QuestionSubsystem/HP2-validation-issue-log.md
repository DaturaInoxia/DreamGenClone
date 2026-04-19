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
- Status: Resolved (validated in runtime)
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

### Issue 2 Follow-up Implementation (2026-04-16)

- Added adaptive debug payload raw-vs-capped observability:
  - `AdaptiveStateUpdated` metadata now includes `rawStatDeltas` (pre-policy clamp values).
  - RolePlay debug page now renders a dedicated "Adaptive Stat Deltas" table showing `Raw`, `Applied`, and `Policy Adjustment` per stat.
- Externalized policy knobs to configuration:
  - Added `StoryAnalysis` option fields and appsettings entries for:
    - `AdaptiveThemeAffinityStackLimit`
    - `AdaptiveEarlyTurnInteractionThreshold`
    - `AdaptiveEarlyTurnPerStatDeltaCap`
    - `AdaptivePerInteractionTotalDeltaBudget`
    - `AdaptiveThemeAffinityCapBuildUp|Committed|Approaching|Climax|Reset`

### Issue 2 Closure Validation (2026-04-16)

- User confirmation:
  - Latest runtime behavior is stable and "stats are not as high" after policy/cap and observability changes.
- Forensic confirmation (session `9aba569b-d36f-4a3f-80dc-c193b78e0e97`):
  - Dean delta event (`f3f587a6-d70f-41c4-bb1d-70437018585c`) shows keyword-only reasons in `statDeltaReasons`:
    - `Loyalty`: `keyword:loyalty-positive(+2)`
    - `SelfRespect`: `keyword:selfrespect-positive(+1)`
  - No `theme-affinity:*` contributor appears for that event while runtime was in BuildUp.

### Issue 8 Implementation Start (2026-04-16)

- Status: Resolved (implemented and validated)

- Added DB-backed stat keyword category/rule subsystem:
  - New tables: `RolePlayStatKeywordCategories`, `RolePlayStatKeywordRules`.
  - New service: `IStatKeywordCategoryService` + SQLite implementation with CRUD and default seed.
- Runtime integration:
  - Direct stat keyword mutation in adaptive state updates now reads enabled categories/rules from DB service.
  - Hardcoded keyword mapping remains as in-service fallback when DB service is unavailable.
- Admin UI:
  - Added new CRUD page at `/stat-keyword-categories` (also `/profiles/stat-keyword-categories`).
  - Added navigation link in the app menu.

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
- Status: Resolved (implemented and validated)
- Category: RolePlay v2 runtime UX clarity
- Request:
  - Add a phase ladder/flow visualization in RolePlay v2 Runtime showing all phases in order.
  - Highlight current phase with strong visual treatment.
  - Show how close current phase is to completion and transition to next phase.
- Planning guidance:
  - Define explicit phase completion/proximity metrics before implementation.
  - Keep this enhancement in a dedicated slice after active issue queue work.

### Enhancement Request A Implementation Progress (2026-04-16)

- Added RolePlay v2 Runtime phase ladder rendering in workspace panel:
  - Ladder sequence is rendered as `BuildUp -> Committed -> Approaching -> Climax -> Reset`.
  - Current phase is visually highlighted with bracketed emphasis (for example `[Committed]`).
- Added explicit phase progress/proximity metric row:
  - Displays `% to next phase` and a details row that explains exactly which thresholds are being evaluated.
- Implemented per-phase completion/proximity policy:
  - `BuildUp -> Committed`: commitment-readiness metric (100% when active scenario is committed; otherwise candidate-confidence based when diagnostics are available).
  - `Committed -> Approaching`: averaged threshold progress across score, desire, restraint, and interactions-since-commitment.
  - `Approaching -> Climax`: averaged threshold progress across score, desire, restraint, and interactions-in-approaching; notes manual `/climax` override path.
  - `Climax -> Reset`: command-driven transition guidance (`/completeclimax`).
  - `Reset -> BuildUp`: auto-advance noted as complete-ready.

### Enhancement Request A Implementation Progress (2026-04-16, visual pass)

- Added stronger visual treatment in RolePlay v2 Runtime:
  - Color-coded phase badges for the ladder (completed/current/upcoming states).
  - Compact progress bar aligned with current phase status.
  - Phase-specific progress color mapping (BuildUp/Committed/Approaching/Climax/Reset).

### Runtime Loop Fix Note (2026-04-16)

- User-observed issue:
  - Session reached `Climax` and remained there without returning to `Reset -> BuildUp` unless an explicit command was entered.
- Fix implemented:
  - Added alias support for climax completion command markers (`/endclimax`, `/end-climax`, `end climax`) in addition to existing markers.
  - Added auto-complete fallback from `Climax -> Reset` when climax interaction progression reaches threshold (prevents dead-loop when no explicit command is issued).
  - Reset/repurpose phase counter behavior on entering `Climax` to support deterministic auto-exit counting.
- Validation:
  - Build and diagnostics passed after changes.

### Runtime Loop Fix Follow-up (2026-04-16)

- User-observed follow-up:
  - Phase reached `Climax`, stayed for one interaction, then jumped to `Committed` instead of clearly spending time in `BuildUp`.
- Root cause adjustments:
  - Tightened climax completion detection to explicit command tokens only (slash commands), preventing accidental phrase matches from normal narrative text.
  - Added short post-reset BuildUp cooldown (`1` interaction) before recommit logic can run, preventing immediate `Reset -> BuildUp -> Committed` bounce.
- Net effect:
  - `Climax -> Reset -> BuildUp` remains automatic.
  - Recommit now waits at least one BuildUp interaction rather than snapping back instantly.

### Runtime Loop Fix Follow-up 2 (2026-04-16, session forensic)

- User-observed follow-up:
  - For session `d7d9a0c2-8dca-4e2d-937f-806352f1b622`, phase reached `Approaching`/`Climax` and then appeared to fall back to `Committed`.
- Forensic findings (logs + DB):
  - `RolePlayV2PhaseTransitions` persisted repeated `Committed -> Approaching` events, with no explicit `Approaching -> Committed` transition event.
  - Log timeline showed `phase="Approaching"` or `phase="Climax"` entering scenario commitment evaluation, followed by `RolePlayV2 scenario committed ... Phase="Committed"` in the same cycle.
- Root cause:
  - A second phase writer existed in the v2 pipeline (`RolePlayEngineService.RunRolePlayV2PipelinesAsync`) that always forced `CurrentPhase=Committed` when a commit result was returned, even when recommitting the same active scenario mid-arc.
- Fix implemented:
  - Preserve current phase for same-scenario recommit when already in active arc phases (`Committed`/`Approaching`/`Climax`).
  - Only force-enter `Committed` (and reset phase interaction counter) when scenario changes or when coming from `BuildUp`/`Reset`.
- Expected behavior after fix:
  - Mid-arc same-scenario recommit no longer fabricates `Committed -> Approaching` loops.
  - `Approaching -> Climax -> Reset` progression remains governed by lifecycle thresholds/commands instead of commit-side phase overwrite.

## Investigation Item E

- Reported by: User
- Status: Planned (investigate further)
- Category: Narrative phase pacing / progression quality
- Hypothesis:
  - Gate phase promotion on narrative "beat credits", not only aggregate stat thresholds.
  - Require key narrative beat types to accumulate credits before promotion is eligible.
  - Initial beat families to evaluate: conflict, reveal, escalation, consequence.
  - Character stats may accelerate credit gain rate, but may not bypass beat-credit minimums.
- Rationale:
  - Preserve visible story progression between climax cycles.
  - Allow later cycles to feel easier while still requiring narrative movement.
- Investigation guidance:
  - Define per-phase beat-credit requirements and optional per-cycle easing policy.
  - Define deterministic beat detection contract (structured metadata vs heuristic classification).
  - Add runtime/debug observability showing credits earned, required, and blocking reason when promotion is denied.

## Enhancement Request B

- Reported by: User
- Status: Planned (not started)
- Category: Question subsystem runtime UX / manual invocation
- Request:
  - Add a UI trigger/button that allows the user to invoke adaptive question+answer generation on demand for each character at any time during a roleplay session.
- Planning guidance:
  - Define whether invocation is per-character, current-speaker, or both.
  - Ensure invocation emits explicit decision-attempt telemetry and preserves normal cadence path semantics.
  - Gate rollout to avoid destabilizing default continuation flow.

## Enhancement Request C

- Reported by: User
- Status: Resolved (implemented and validated)
- Category: Question subsystem phase-aware steering
- Request:
  - Adaptive question+answer behavior should be phase-aware.
  - In `BuildUp`, question direction should prioritize theme steering/context progression (subtle scenario guidance) more strongly than raw stat-driven pressure.
- Planning guidance:
  - Add per-phase weighting policy between theme/context evidence and stat-pressure signals.
  - Emit reasoning metadata that shows which phase policy was applied per generated/ skipped question.

## Enhancement Request D

- Reported by: User
- Status: Resolved (done)
- Category: Question subsystem direct-question trigger
- Request:
  - When any actor (including persona as a character) asks a direct question to a target actor, trigger an immediate adaptive question+answer decision for the target actor instead of waiting for normal cadence.
  - When scene location changes inside story context (for example bar -> garden), trigger adaptive decision generation for all actors (including persona) via a queued fanout.
  - Use the context-aware option pattern described in `specs/v2/Context-Aware Stat-Altering Question System - Analysis.md` (natural response options with mapped hidden stat profiles).
- Design intent:
  - Event-driven detection from interaction metadata and speaker/target context.
  - Avoid post-hoc dependence on LLM response parsing to decide whether to trigger.
- Planning guidance:
  - Define deterministic trigger contract for direct-question detection and role-pair targeting.
  - Define scene-location change detection contract and all-actor fanout queue semantics.
  - Include true-location vs perceived-location model and line-of-sight/proximity rules in context policy.
  - Define cadence-bypass safeguards (single active decision, dedupe, cooldown).
  - Define initial option families and hidden stat profiles aligned with the analysis example.
  - Add explicit decision-attempt diagnostics for trigger, skip, and generation outcomes.

### Enhancement Request D Implementation Progress (2026-04-16)

- Slice 1 implemented (engine trigger plumbing):
  - Added decision triggers: `CharacterDirectQuestion` and `SceneLocationChanged`.
  - Added direct-question detection + target resolution in engine orchestration.
  - Added scene-location change detection from scenario locations in recent interaction stream.
  - Added context shaping behavior:
    - direct-question => single target-focused decision context,
    - scene-location-change => all-actor queued decision contexts.
  - Added diagnostic log events for trigger detection.
- Validation:
  - Focused Release roleplay tests passed after this slice (`DecisionPointMutationTests`, `RolePlaySessionLifecycleTests`).
- Slice 2 implemented (location truth/perception foundation):
  - Added persisted adaptive state fields for scene-location truth and perception:
    - `CurrentSceneLocation`
    - `CharacterLocations` (true location per actor)
    - `CharacterLocationPerceptions` (observer perception with confidence/LOS/proximity)
  - Added DB schema/migration support for:
    - `CurrentSceneLocation`
    - `CharacterLocationsJson`
    - `CharacterLocationPerceptionsJson`
  - Added engine policy to update perceived locations from true locations using v1 LOS/proximity heuristics:
    - same location + not hidden => LOS/proximity true, confidence 100
    - otherwise perception degrades toward last-known/assumed state
  - Added repository round-trip test coverage for new location fields in `RolePlaySessionLifecycleTests`.
- Slice 3 implemented (runtime visibility + diagnostics surfacing):
  - Workspace runtime panel now shows:
    - current scene location,
    - per-actor true location rows,
    - observer->target perceived location rows with confidence, LOS, proximity, and source.
  - Engine now emits `LocationStateUpdated` debug events with structured location truth/perception payloads.
  - Debug page now includes a `Locations` tab and structured renderers for location truth/perception metadata.
- Enhancement D completion update (2026-04-16):
  - Added queue UX controls for pending/fanout management:
    - skip current,
    - persisted defer/restore/skip for deferred queue,
    - deferred decision review controls in workspace.
  - Added regression coverage for queue/deferred semantics in `RolePlaySessionLifecycleTests`:
    - pending prompt excludes deferred/applied decisions,
    - defer/restore/skip lifecycle updates state and audit interaction as expected.
  - Validation: roleplay-focused regression run (`FullyQualifiedName~RolePlay`) passed with 135/135 tests.

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

## Issue 9

- Reported by: User
- Status: Open (needs investigation)
- Category: Character location tracking quality
- Problem statement:
  - Current location tracking does not consistently maintain believable per-character location state.
  - User asked whether model-assisted tracking should be used to improve location continuity.
- Investigation scope:
  - Evaluate hybrid approach: deterministic state model as source-of-truth + model-assisted inference for ambiguous moves/perceptions.
  - Compare against current rule-only location updates for continuity, correctness, and drift over long sessions.
  - Define guardrails so model suggestions cannot violate hard world constraints (for example impossible teleports, line-of-sight contradictions).

## Issue 10

- Reported by: User
- Status: Planned (change list defined)
- Category: Adaptive scenario/theme comeback behavior
- Problem statement:
  - When an active scenario is set, non-active themes are effectively suppressed from score growth.
  - This makes it hard for a previously relevant theme to recover and compete again when narrative signals return.

### Issue 10 Proposed System Changes

- Change 1: Convert hard suppression into soft suppression scoring
  - Current behavior increments `SuppressedHitCount` for non-active themes and skips score updates.
  - Proposed behavior applies reduced evidence gain for non-active themes on match (for example `20%` of normal signal) with a small per-turn cap.
  - Expected effect: latent momentum can build for comeback themes without causing rapid oscillation.

- Change 2: Add active-scenario staleness decay
  - If the active scenario has no new interaction-evidence hits across consecutive turns, apply incremental score decay.
  - Expected effect: stale active scenarios gradually release ranking pressure and allow challengers to re-enter.

- Change 3: Include suppression momentum in pivot selection
  - Extend momentum candidate logic to consider recent suppressed-hit streaks, not only raw score/evidence totals.
  - Expected effect: recurring narrative signals for non-active themes contribute to pivot readiness.

- Change 4: Make pivot threshold stale-aware
  - Keep default pivot margin for stability.
  - Reduce required overtake margin when active scenario is stale (no-evidence window reached).
  - Expected effect: controlled pivots become possible when current scenario clearly loses contextual support.

- Change 5: Reduce hard active-scenario lock in prompt framing
  - Keep active scenario as primary guidance.
  - Permit one secondary-beat lane from top alternate theme when evidence supports it.
  - Expected effect: model can emit comeback-aligned text cues that scoring can detect.

- Change 6: Soften candidate gate zeroing to heavy penalty
  - Replace hard `FitScore=0` on gate fail with a strong penalty multiplier in candidate ranking path.
  - Expected effect: weak-but-emerging candidates remain visible for hysteresis and recovery logic.

### Issue 10 Suggested Initial Tuning

- `SuppressedEvidenceMultiplier = 0.20`
- `SuppressedEvidencePerTurnCap = 1.5`
- `ActiveScenarioNoHitStaleTurns = 2`
- `ActiveScenarioStaleDecayPerTurn = 1.25`
- `PivotOvertakeMarginDefault = 2.0`
- `PivotOvertakeMarginWhenStale = 1.0`
- `GateFailScorePenaltyMultiplier = 0.35`

### Issue 10 Validation Plan

- Add unit tests for:
  - suppressed-theme soft gain behavior,
  - active-scenario stale decay behavior,
  - stale-aware pivot margin behavior,
  - gate-penalty ranking behavior.
- Validate with forensic replay on session traces where expected comeback previously failed.

### Issue 10 Implementation Progress (2026-04-18)

- Status: MVP implemented (code/config/tests updated)
- Included in this pass:
  - Added StoryAnalysis configuration knobs:
    - `SuppressedEvidenceMultiplier` (default `0.20`)
    - `SuppressedEvidencePerTurnCap` (default `1.5`)
    - `GateFailScorePenaltyMultiplier` (default `0.35`)
  - Adaptive scoring update:
    - Non-active themes now retain `SuppressedHitCount` behavior.
    - Non-active themes now also gain reduced/capped interaction evidence score instead of full skip when active scenario is set.
    - Added `suppressed-interaction-evidence` entries in recent evidence for observability.
  - Candidate selection update:
    - Gate-failed candidates no longer hard-zero fit score.
    - Gate-failed candidates now receive heavy score penalty via `GateFailScorePenaltyMultiplier`.
    - Rationale now includes weighted-to-penalized score explanation for failed gates.
  - Test updates:
    - Added suppression comeback test for non-active theme capped gain.
    - Updated dominant-role gate expectation from hard-zero to penalized non-zero score.

- Validation status:
  - File-level diagnostics on all changed files report no errors.
  - Test execution currently blocked by local file lock contention on `DreamGenClone.Web/bin/Debug/net9.0/*.dll` from active `.NET Host` process; rerun required after lock is cleared.

### Issue 10 Implementation Progress (2026-04-18, Phase 2)

- Status: Implemented and validated (focused test pass)
- Scope implemented:
  - Added stale-aware pivot policy knobs:
    - `ActiveScenarioNoHitStaleTurns`
    - `PivotOvertakeMarginDefault`
    - `PivotOvertakeMarginWhenStale`
    - `PivotCommittedInteractionWindow`
    - `PivotCommittedInteractionWindowWhenStale`
  - Updated context-momentum pivot logic:
    - Detects active-scenario staleness from recent interaction-evidence windows.
    - Keeps strict committed-phase pivot window when scenario is not stale.
    - Expands committed-phase pivot window when scenario is stale.
    - Uses stale-aware overtake margin (`default` vs `when stale`).
- Defaults shipped in appsettings:
  - `ActiveScenarioNoHitStaleTurns=2`
  - `PivotOvertakeMarginDefault=2.0`
  - `PivotOvertakeMarginWhenStale=1.0`
  - `PivotCommittedInteractionWindow=3`
  - `PivotCommittedInteractionWindowWhenStale=8`
- Regression coverage added:
  - `UpdateFromInteractionAsync_StaleCommittedScenario_AllowsLatePivot`
  - `UpdateFromInteractionAsync_NonStaleCommittedScenario_DoesNotLatePivot`
- Validation:
  - Focused tests passed: total `15`, failed `0`, succeeded `15`, skipped `0`.
- Success criteria:
  - Fewer incorrect location/perception jumps in runtime diagnostics.
  - Clear reconciliation policy when model-inferred location conflicts with deterministic state.
  - Auditable event trail showing why a location change was accepted or rejected.

## Issue 10

- Reported by: User
- Status: In Progress (implementation planned)
- Category: BuildUp theme candidate accuracy / RP theme metadata completeness
- Problem statement:
  - BuildUp candidate gating currently depends on global session averages, which can dilute 2-character themes when a session contains 3+ characters.
  - RP theme model/UI needs explicit participant roles and weights maintained for each theme so scenario candidate weighting can be character-scoped.
- Scope constraints:
  - Dev mode only for rollout.
  - New sessions only.
  - No migration/backfill for existing RP sessions required.
- Implementation objectives:
  - Update RP theme model/service/UI to store and edit participant roles and role weights.
  - Populate role+weight metadata for all RP themes in dev data.
  - Use this metadata in BuildUp candidate weighting/gating so relevant character subsets drive selection.
  - Surface scoped stats and gate-gap diagnostics in workspace adaptive panel.
- Tracking checkpoints:
  - Model/service contract updates.
  - RP Themes admin UI role/weight editor.
  - Dev startup/theme population coverage check.
  - BuildUp scoped weighting integration.
  - Regression tests for mixed 2-character vs 3-character scenarios.

### Issue 10 Implementation Start (2026-04-17)

- Plan approved to proceed in slices:
  - Slice 1: RP theme model/service/UI role+weight editing and persistence verification.
  - Slice 1B: RP Themes UX split into metadata list page + dedicated full CRUD detail page.
  - Slice 2: Dev-only role+weight population pass for all RP themes.
  - Slice 3: BuildUp candidate scoring updates to use role-scoped weighted stats for new sessions.
  - Slice 4: Workspace diagnostics alignment and regression test updates.

### Issue 10 Implementation Progress (2026-04-17, Slice 1)

- RP Themes admin UI updated to support participant role/weight editing in Theme Editor:
  - Added editable `Participant Roles and Weights` table with add/remove controls.
  - Added inline role name and weight inputs with clamping/validation (`0.1` to `10.0`).
- Theme save/edit wiring updated:
  - Role/weight rows now serialize into `RPTheme.FitRules` (`RoleName`, `RoleWeight`) on save.
  - Edit flow now hydrates role/weight rows from existing theme fit rules.
  - Form reset clears participant role/weight state.
- Validation status:
  - Web project build succeeded after these updates.

### Issue 10 Implementation Progress (2026-04-17, Slice 1B)

- RP Themes list page reworked to metadata-only listing:
  - Columns limited to `Id`, `Label`, `Category`, `Weight`, `Enabled` and action button.
  - Removed inline editor from list page.
  - Added `View Details` navigation to a dedicated detail route.
- New full CRUD detail page added for each theme:
  - Route: `/rp-themes/{themeId}` with `/rp-themes/new` for create flow.
  - Sticky top command bar with always-visible `Save Theme` and `Delete Theme` actions and inline status/error messages.
  - List-based sections are collapsible and collapsed by default.
  - Single-value metadata editors remain always visible.
  - Each list section supports add/edit/remove CRUD operations.
  - Added informational popup support per section indicating RP Engine/model usage; sections not used still display explicit `not used` messaging.

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
