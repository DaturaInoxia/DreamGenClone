# B-073: Adaptive Panel — Character Points, LLM Prompt & Include/Exclude Decision Visibility

**State**: `designed`
**Priority**: medium
**Scope**: medium

---

## TL;DR

Surface the actor-selection ("who participates in the next turn") data in the adaptive panel so it shows: (1) per-character **points** — the participation `BaseScore` and its per-component breakdown; (2) the full **LLM prompt** (system + user) sent to the narrative-director model; and (3) the **decision** — raw model output, parsed characters, reasoning, source, and the excluded-by-filter set. Today the engine emits only a partial `OverflowActorSelection` debug event (candidates + total score + reasoning, no prompt, no raw output, no score math, no filter reasons), and the adaptive panel shows character stats but no participation/prompt view.

**Related items**: B-066 (adaptive panel — character selection data, `new`), B-049 (adaptive panel — comprehensive data visibility), B-050 (context-aware actor selection, `designed`), B-069 (story stall fix, `implemented`).

---

## Discovery Summary

### Current State (verified in code)

| Component | Detail |
|---|---|
| Scoring | `RolePlayEngineService.ScoreActorForAutoSelection` (line ~2386) computes `BaseScore` from: in-scene `+1000`, affinity Required `+500` / Preferred `+200` / Excluded `-1000`, time match `+100` / mismatch `-500`, recency (never `+200`, else `180/120/60/0` by turns-ago), turn-override `ResponsePriority` `+0..100` |
| LLM call | `ActorSelectionService.SelectActorsAsync` builds `PromptSystem` + `PromptUser`, calls the model, returns `PromptSystem`, `PromptUser`, `RawModelOutput`, `Reasoning`, `Source` (`LLM`/`Cache`/`Scoring`/`Fallback`) |
| Debug event | Engine emits `OverflowActorSelection` (RolePlayEngineService ~2880) with: `selectionSource`, `orderingSource`, `overrideChance`, `candidates[]` (name, role, score, affinityStatus, inScene, timeOfDayMatch, responsePriority, preferredPosition, participateInAutoContinue, finalPosition), `orderedFinal`, `reasoning`, `cacheKey` — **missing**: full prompt, raw output, score breakdown, batchSize, request context, filter reasons |
| Adaptive panel | `RolePlayWorkspace.razor` adaptive tab already has "Active Profiles And Controls", "RolePlay v2 Runtime Status", "Character Stats" (per-character collapsibles: base/curr/fit/gate, motivation, stat prompt texts, behavioral dimensions, memory). No participation/prompt section |
| Debug page | `RolePlayDebug.razor` shows `OverflowActorSelection` generically (raw JSON); "Prompts"/"Responses" tabs only cover `PromptBuilt`/`LlmRequestSent`/`LlmResponseReceived` — actor-selection prompt is a separate LLM call and is not shown anywhere today |
| Sink | `IRolePlayDebugEventSink` is write-only; `RolePlayDebugEventService` (concrete) already has `QuerySessionEventsAsync` but the interface does not expose it; workspace injects only the sink |

### Key Gaps

1. **Prompt not persisted.** `ActorSelectionResponse` carries `PromptSystem`/`PromptUser`/`RawModelOutput`, but they are discarded before the engine writes the debug event (only `reasoning` is kept).
2. **No score breakdown.** Only the total `BaseScore` is persisted; the per-component math (in-scene, affinity, time, recency, priority) is not.
3. **Excluded-by-filter set invisible.** Three upstream filters drop characters before they ever become candidates: location affinity `Excluded` (`ResolveAvailableCharacters`), `ParticipateInAutoContinue=false` override, and Opening-period OtherMan exclusion (`ObservedTurnCount <= OpeningPeriodTurnCount`, turn ≤ 3). These never appear in `candidates`, so exclusion is unexplained.
4. **Workspace cannot query events.** No read path on the sink interface.

### Two-stage exclusion model (must be reflected in the UI)

- **Stage A — upstream hard filters** (never reach the LLM): location `Excluded` affinity, `ParticipateInAutoContinue=false`, Opening-period OtherMan. The panel should show these with a filter reason.
- **Stage B — LLM decision** over the remaining candidates: the narrative-director returns `{"characters":[...],"reasoning":"..."}`.

---

## Design Decisions

1. **Two new event kinds** (mirror existing `LlmRequestSent` / `LlmResponseReceived` pattern):
   - `ActorSelectionPromptSent` — system prompt, user prompt, resolved model, batchSize, cacheKey, request context (phase, location, time-of-day, narrativeSummary, activeThemes, recentSemanticEvents), timestamp.
   - `ActorSelectionResponseReceived` — raw model output, parsed characters, reasoning, source, durationMs.
   - Keeps the existing `OverflowActorSelection` event for the post-mapping ordering result. **Open decision**: also delete `OverflowActorSelection` once the pair covers it, or keep both.
2. **Persist full prompt + raw output, untruncated.** Add the three fields from `ActorSelectionResponse` to the new events.
3. **Score breakdown per candidate.** Change `ScoreActorForAutoSelection` to return `(double Total, IReadOnlyList<(string Component, double Delta)> Parts)` (or an out param) and persist `scoreBreakdown[]` per candidate in the response/event. Post-hoc recomputation in the UI is rejected as brittle (recency bands, override clamp).
4. **Filter-reason capture.** Emit `excludedByFilter[]` with `{ name, reason }` for the three Stage-A filters so the exclusion story is complete.
5. **Sink read path.** Add `QuerySessionEventsAsync(sessionId, eventKind, search, take, ct)` to `IRolePlayDebugEventSink` (implementation already exists in `RolePlayDebugEventService`). Workspace already injects the sink — no new DI wiring.
6. **Adaptive panel section.** New collapsible section "Turn Actor Selection" (above "Character Stats"):
   - Latest decision card: candidates table (Name · Role · In-Scene · Affinity · Time Match · Score · Score Breakdown tooltip · Final Position), Included/Excluded badge, source badge (`LLM`/`Scoring`/`Fallback`), reasoning.
   - Filtered-out list (Stage-A excludes with reasons).
   - Collapsible "Prompt" block (system + user, monospace) and raw decision JSON.
   - Optional history list of recent `ActorSelectionResponseReceived` events (timestamp + chosen set).
7. **Severity normalization.** Use `Severity = "Info"` (lowercase) for the new events to match `RolePlayDebugEventRecord` defaults, unlike the existing `OverflowActorSelection` which uses `"Information"`.

---

## Files Touched & Blast Radius

| File | Change | Risk |
|---|---|---|
| `DreamGenClone.Web/Application/RolePlay/ActorSelectionService.cs` | None (already returns `PromptSystem`/`PromptUser`/`RawModelOutput`) — verify only | None |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | `ScoreActorForAutoSelection` signature change (score breakdown out param); emit two new debug events; capture `excludedByFilter[]`; optionally drop `OverflowActorSelection` | Low — additive instrumentation; no behavior change; scoring math identical |
| `DreamGenClone.Application/Abstractions/IRolePlayDebugEventSink.cs` | Add `QuerySessionEventsAsync` | Low — interface addition |
| `DreamGenClone.Web/Application/RolePlay/RolePlayDebugEventService.cs` | Expose existing query (no impl change needed) | None |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | New "Turn Actor Selection" collapsible section; query latest events; render candidates/prompt/decision/filters | Medium — Razor; follow `.github/instructions/razor-editing.instructions.md` |
| `DreamGenClone.Web/Components/Pages/RolePlayDebug.razor` | Optional: render the two new event kinds with dedicated blocks (mirror `AdaptiveStateUpdated` block pattern) | Low |

**Not changed**: prompt slot pipeline, continuation service, phase/gate logic, model resolution. No fallback/default branches introduced. Missing required config still fails fast per repo rules.

---

## Open Decisions (before implementation)

- **A.** Keep `OverflowActorSelection` alongside the new pair, or remove it?
- **H.** `ActorSelectionSource.Cache` is currently unreachable — `LastActorOrdering`/`LastContextFingerprint` are written but never read to short-circuit a selection. Recommend handling separately (remove dead cache path per repo no-fallback rules, or implement a real cache read). Treat as follow-up, not part of this item.

---

## Verification

1. Build solution (web + tests): 0 errors.
2. Unit tests for `ScoreActorForAutoSelection` breakdown (component sums == total).
3. Manual: run an overflow-continue in a session; confirm the adaptive panel shows the latest selection with prompt, raw output, reasoning, source badge, and filtered-out set.
4. Confirm `RolePlayDebug` page's Prompts/Responses tabs surface `ActorSelectionPromptSent` / `ActorSelectionResponseReceived` (if Debug page rendering included).
