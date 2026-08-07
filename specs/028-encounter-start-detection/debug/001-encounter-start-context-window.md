# 001 — Encounter-Start Detection Fails: Model Context + Oversized Context Window

**Created:** 2026-08-05
**Status:** Code fix applied (EncounterStartContextTurns), awaiting runtime confirmation

## Report

- **Symptom:** `threesome-spontaneous-exclusion-v3` never detects encounter-start. `EncounterStartDetected` count = 0 in logs.
- **Sessions:** `1c0ae0e3`, `18fc8ec5` (threesome), `9a442a06` (exhibitionism).
- **Log evidence:** Every `TryDetectEncounterStartAsync` call failed:
  - Initially: LM Studio `400` — `n_keep: 7646 >= n_ctx: 4096` (model loaded with only 4096 context).
  - After user raised context: `400` at `n_ctx: 8448` with `n_keep` 8686–9282 (still too small), then 120s `TaskCanceledException` timeouts and `SemanticInference PARSE-FAILED: invalid JSON`.

## Analysis

- `RolePlaySemanticAnalysis` function default → `qwen2.5-14b-instruct-1m` (Local provider, `TimeoutSeconds=120`, loaded at n_ctx 4096→8448). The encounter-start prompt built with `Math.Max(12, session.ContextWindowSize)` prior interactions + a long event description is ~9k tokens → too big for the local model's context → 400; after the context bump, too slow (80–120s) → timeout; occasionally malformed JSON.
- Theme-scoring semantic analysis uses `session.ContextWindowSize` directly (no 12-floor) → smaller prompts → works.
- Encounter-**start** detection is universal (no theme mapping required). Encounter-**completed** requires an `encounter-completed` mapping (theme lacks one → returns early, never calls LLM).
- Re-entry guard: `if (IsEncounterActive || CurrentTimeSkipPhase != None) return;` — this legitimately skips detection for session `9a442a06` (CloseScene pending).

## Plan

Make the encounter detection context window configurable and small by default:
1. Add `EncounterStartContextTurns` (default 4) to `RolePlayMemoryOptions` (+ appsettings.json / appsettings.Development.json `RolePlayMemory`).
2. Replace `Math.Max(12, session.ContextWindowSize)` with `Math.Max(1, EncounterStartContextTurns)` in `TryDetectEncounterStartAsync` (~L5194) and `TryDetectEncounterBoundaryAsync` (~L5355).

## Resolution

- `DreamGenClone.Infrastructure/Configuration/RolePlayMemoryOptions.cs`: added `EncounterStartContextTurns { get; init; } = 4`.
- `DreamGenClone.Web/appsettings.json` + `appsettings.Development.json`: added `"RolePlayMemory"` config incl. `"EncounterStartContextTurns": 4`.
- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`: both encounter detectors now use `Math.Max(1, _memoryOptions?.Value.EncounterStartContextTurns ?? 4)`.
- Builds: Infrastructure ✅, Web ✅ (0 errors). Encounter/MultiEncounter tests 84/84 pass. RolePlay suite: 75 failures — all pre-existing (PromptBuilder/SlotContract/PhaseLifecycle/AdaptiveState/Lifecycle + FunctionDefaultRepository MaxConcurrentJobs DB-schema gap), unrelated to this change.
- Web app restarted on http://localhost:5177 with the new build.

## Validated

- [ ] pending — user to run a session to a non-sexual→sexual transition and confirm `EncounterStartDetected` in the log (and no `n_keep >= n_ctx` / `TaskCanceledException`).
- [ ] pending — tune `EncounterStartContextTurns` in `appsettings.Development.json` if needed.
- [ ] optional — add `encounter-started`/`encounter-completed` semantic mappings to `threesome-spontaneous-exclusion-v3` if encounter events should drive theme scoring.
