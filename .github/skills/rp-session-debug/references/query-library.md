# RP Session Debug — Pre-Baked Query Library

All queries in `artifacts/tmp/dbquery/queries/`. Run via:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/<file>.sql <sessionId>
```

## Session Overview

| Query | Description |
|-------|-------------|
| `session-overview.sql` | Name, type, schema version, last updated |
| `session_payload_start.sql` | Session payload JSON (interactions, settings, scenario) |
| `session_payload_all.sql` | Full payload |
| `session_payload_raw.sql` | Raw payload text |
| `session_payload_check.sql` | Payload integrity check |
| `session_profile_check.sql` | Profile bindings on session |

## Adaptive State

| Query | Description |
|-------|-------------|
| `adaptive-state.sql` | Full adaptive state row |
| `adaptive_state.sql` | Alternative adaptive state view |
| `check_session_adaptive.sql` | Adaptive state existence check |
| `recent_adaptive.sql` | Recent adaptive state changes |

## Turns

| Query | Description |
|-------|-------------|
| `turns.sql` | All turns ordered by index |
| `session_turns_v2.sql` | V2 turns with details |
| `schema_v2turns.sql` | V2 turns schema |

## Character Snapshots & Stat Deltas

| Query | Description |
|-------|-------------|
| `char-snapshots.sql` | Full CharacterSnapshotsJson |
| `stat-deltas.sql` | SemanticStatDeltaBreakdownsJson |

## Theme Scores

| Query | Description |
|-------|-------------|
| `theme-scores.sql` | All themes ordered by score DESC |
| `theme-tracker.sql` | PrimaryTheme, SecondaryTheme, selection rule, turn counts |
| `theme_affinities.sql` | Theme stat affinities |
| `theme_assignments.sql` | Theme profile assignments |
| `theme_fit_rules.sql` | Theme fit rules |
| `theme_phase_guidance.sql` | Theme phase guidance text with markers |
| `debug_theme_guidance.sql` | Debug theme guidance |

## Candidate Evaluations

| Query | Description |
|-------|-------------|
| `evals.sql` | All candidate evaluations with full score breakdown |
| `session_candidate_evals.sql` | Candidate evaluations summary |

## Gate Evaluations

| Query | Description |
|-------|-------------|
| `gates.sql` | Gate evaluation debug events |
| `gates_6.sql` | Gates for session 6 |
| `fast_gates.sql` | Fast gate check |
| `phase_blockers.sql` | What's blocking phase transitions |

## Phase Transitions

| Query | Description |
|-------|-------------|
| `phase-transitions.sql` | Phase transition history |
| `phase-detail.sql` | Detailed phase info |
| `phase-summary.sql` | Phase summary |

## Semantic Analysis

| Query | Description |
|-------|-------------|
| `semantic-analysis.sql` | Per-interaction per-character semantic analysis results |
| `semantic-applied.sql` | SemanticInferredEvidenceApplied — signals, theme deltas, stat deltas |
| `semantic-check.sql` | Semantic state check |

## Debug Events

| Query | Description |
|-------|-------------|
| `debug-events.sql` | All debug events timeline |
| `debug_event_counts.sql` | Event counts by kind |
| `debug_find_log_tables.sql` | Find log tables |
| `debug_prompts.sql` | Prompt debug events |
| `debug_prompt_extract.sql` | Extract prompt text |
| `prompt_sizes.sql` | Prompt sizes overview |
| `debug_session_state_snapshot.sql` | Session state snapshot |

## Prompt HARD Constraints

| Query | Description |
|-------|-------------|
| `prompt-hard-constraints.sql` | Which prompts contain HARD CONSTRAINT stat text |

## Intensity

| Query | Description |
|-------|-------------|
| `find_session_intensity.sql` | Session intensity state |
| `session_intensity_detail.sql` | Intensity detail |
| `session_intensity_detail_cc.sql` | Intensity detail by character |
| `intensity-profile.sql` | Intensity profile config |
| `intensity-state.sql` | Intensity runtime state |

## Profiles

| Query | Description |
|-------|-------------|
| `list_tone_profiles.sql` | All tone profiles |
| `list_tone_scene_directives.sql` | Tone scene directives |
| `list_profiles.sql` | All profiles |
| `all_profiles.sql` | All profiles (alt) |
| `all_scenarios.sql` | All scenarios |
| `all_scenario_defs.sql` | All scenario definitions |
| `scenario_catalog_all.sql` | Full scenario catalog |
| `scenario_default_profile.sql` | Default scenario profile |

## Concept Injections

| Query | Description |
|-------|-------------|
| `concept_injections.sql` | Concept injection state |

## Theme Machine Diagnostics

| Query | Description |
|-------|-------------|
| (via `RolePlayV2ThemeMachineDiagnostics` table) | Theme machine state transitions |

## Completion History

| Query | Description |
|-------|-------------|
| (via `dbq completions <id>`) | Scenario completion history |

## Formula Versions

| Query | Description |
|-------|-------------|
| (via `dbq formula <id>`) | Formula version refs |

## Ad-hoc Session Queries (Existing)

These were created for specific debugging sessions — may still be useful:

| Query | Original Session |
|-------|-----------------|
| `session_36b3_*.sql` | Session 36b3 |
| `session_448_detail.sql` | Session 448a49e1 |
| `session_8419_*.sql` | Session 8419 |
| `session_b368_detail.sql` | Session b368 |
| `session_b368_profiles.sql` | Session b368 profiles |
| `session_b368_scenario_default.sql` | Session b368 scenario |
| `session_d594_*.sql` | Session d594 |
| `profile_516919.sql` | Profile 516919 |
| `q41.sql` | Query 41 |
