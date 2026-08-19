# B-087: Continue — show first returned interaction, don't wait for the full batch

**State**: `planned` (awaiting confirmation before implementation)
**Priority**: high
**Scope**: small→medium (revised up from `small` after research — touches engine callback contract + tracker + Blazor render path)
**Backlog**: `specs/Planning/backlog.md` → B-087

---

## 1. Problem statement

On **Continue** (overflow / multi-actor batch), the workspace shows nothing committed until **every** actor in the batch **and** the narrative have finished and the whole turn has been flushed to the DB. Only then does `OnTrackerStatusChanged` fire → `LoadSessionAsync()` → all new interactions appear at once.

For a 3-actor batch + narrative this means the user stares at a single "Generating…" bubble (which silently concatenates all actors' streamed text into one buffer) for the full duration, then gets a wall of 4 interactions.

## 2. Root cause (verified in code)

| Layer | Current behavior | Location |
|---|---|---|
| Engine entry | `ContinueAsAsync` returns `ContinueAsResult`; workspace **discards** the return (tracker takes non-generic `Task`) | `RolePlayEngineService.cs:1411`, `RolePlayWorkspace.razor:7313-7314` |
| Batch loop | Sequential `for (i < batchSize)`. Per actor: align state → `ContinueAsync(onChunk)` → `result.ParticipantOutputs.Add` → `session.Interactions.Add` (in-memory) → `UpdateStateAndDetectEncounterAsync(skipBoundaryDetection:true)`. **No per-actor DB save.** | `RolePlayEngineService.cs:1683-1703` |
| Persistence | **One** end-of-turn flush after loop + narrative + V2 pipelines | `RolePlayEngineService.cs:1813-1814` (`QueueRolePlaySessionSave` + `FlushAsync`) |
| Streaming | **One** shared `onChunk` across all actors (no delimiter/reset). Workspace concatenates every actor's chunks into one `_pendingResponseBuilder` | `RolePlayEngineService.cs:1691`, `RolePlayWorkspace.razor:7698-7723` |
| Completion hook | **None** per-actor / per-interaction. Only whole-task `OnJobStatusChanged` | `IRolePlaySubmissionTracker.cs`, `RolePlaySubmissionTracker.cs:110-126` |
| Render | Bound to `_session.Interactions` via `GetDisplayInteractions()`; new rows appear only after `LoadSessionAsync()` reload | `RolePlayWorkspace.razor:7830, 8431-8432` |
| Pending bubble | Isolated child `RolePlayPendingResponse` (self-renders, 300 ms throttle, parent NOT re-rendered per chunk) renders **after** the interaction loop | `RolePlayWorkspace.razor:9201-9203`, `RolePlayPendingResponse.razor` |
| Narrative | Non-streaming, generated **after** the participant loop (no `onChunk`) | `RolePlayEngineService.cs:1777` (`ContinueNarrativeAsync`) |

**Additional finding (B-010 gap):** the B-010 backlog note claims *"On Continue: if NOT at bottom, cursor placed at pre-generate boundary and scroll jumps to it."* This is **not implemented**. `_readCursorInteractionIndex` is written only by `AdvanceReadCursor` (IntersectionObserver) and `SetReadCursor` (click) — there is no proactive pin at Continue-start, and `scrollToCursor` (`roleplay-workspace.js:154`) is never invoked from the razor. B-087 should implement this stated behavior.

## 3. Goal / target behavior

When the user presses Continue (or Send), each completed interaction is committed and displayed **as soon as that actor finishes**, while later actors are still generating. Specifically:

1. Actor 1 streams → its streaming bubble is shown → on completion the bubble is replaced by Actor 1's committed interaction row.
2. Actor 2 streams → its bubble shown → replaced by committed row. (Actors are generated sequentially, so arrival order == generation order; no reordering needed.)
3. Narrative (non-streaming) → a "waiting for narrative…" bubble → replaced by the committed narrative row when it finishes.
4. A status indicator shows progress: *"Generating Becky (1 of 3)…"*.
5. **Read cursor (B-010):** on the **first** new interaction of the turn, if the user was NOT at the bottom, place the `→ new →` divider at that index and scroll to it; if the user WAS at the bottom, keep follow-to-bottom as each row appends.
6. **Navigation resilience (B-027):** if the user navigates away mid-batch and returns, the already-completed (persisted) interactions are visible immediately on reload, and subsequent actors' completions stream into the re-mounted component.

## 4. Design decisions

### D1 — New per-interaction completion callback on the engine (recommended)

Add a dedicated callback to `ContinueAsAsync` (mirrors the existing `onChunk` pattern; keeps the continuation service untouched):

```csharp
public async Task<ContinueAsResult> ContinueAsAsync(
    ContinueAsRequest request,
    Func<string, Task>? onChunk = null,
    Func<RolePlayInteraction, int, int, Task>? onInteractionCompleted = null, // (interaction, positionInTurn, turnActorCount)
    CancellationToken cancellationToken = default)
```

- Invoke `onInteractionCompleted?.Invoke(interaction, positionInTurn, batchSize)` **after** each participant interaction is added to `session.Interactions` + `result.ParticipantOutputs` (i.e. right after the existing `UpdateStateAndDetectEncounterAsync(... skipBoundaryDetection:true)` call in the loop body).
- Invoke it for the **narrative** interaction too (after `session.Interactions.Add(narrative)`), so narrative appears immediately when done rather than at end-of-turn.
- `IRolePlayContinuationService` is **unchanged** — the callback is engine-level (the engine owns batch semantics). This keeps blast radius smaller.

Rejected alternatives: (B) structured sentinel chunk via `onChunk` — fragile, mixes text and control; (C) tracker-only event without an engine callback — can't deliver the in-memory interaction object to the live component.

### D2 — Per-actor persistence (hybrid: live display + flush per actor)

- **Live display** uses the callback-delivered `RolePlayInteraction` object directly (append to the rendered list + `StateHasChanged`). No DB reload needed for the on-page case.
- **Per-actor flush**: after invoking `onInteractionCompleted`, call `_autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-continueas-actor-completed")` + `await _autoSaveCoordinator.FlushAsync(cancellationToken)` inside the loop, so a navigation-return mid-batch sees the completed interactions via `LoadSessionAsync()`.

Safety analysis (why per-actor flush is safe here):
- The session blob's `AdaptiveState` is `[JsonIgnore]` (verified — B-071 memory: "AdaptiveState is [JsonIgnore] on the session blob — it lives only in V2 tables"). So a mid-turn blob flush persists **only interactions + session metadata**, not adaptive state.
- V2 adaptive state is persisted by `RunRolePlayV2PipelinesAsync` at end-of-turn (unchanged). A mid-turn reload therefore shows last-completed-turn V2 state until the current turn finishes — an **acceptable transient** for the adaptive panel only.
- The end-of-turn flush at `:1813-1814` is **additive** (still runs); multiple `FlushAsync` calls per turn are coalesced by `AutoSaveCoordinator`.

**To verify during implementation:** `AutoSaveCoordinator.FlushAsync` contention under rapid sequential flushes (up to `SceneContinueBatchSize` ≤ 6 per turn) and that `TryDetectEncounterStartAsync` (encounter-start, runs per-actor — boundary is the only one skipped) doesn't double-persist in a conflicting way.

### D3 — Tracker gains a swappable interaction-completed callback (B-027 resilience)

Mirror the existing chunk-callback wrapper so a returning component can re-attach:

- `RolePlayRunningSubmission`: add `RolePlayInteractionCallbackWrapper InteractionCallbackWrapper { get; } = new();` (a second wrapper instance of the same swallow-on-dispose shape as `RolePlayChunkCallbackWrapper`).
- `IRolePlaySubmissionTracker`: add `AttachInteractionCompletedCallback(string sessionId, Func<RolePlayInteraction,int,int,Task>? cb)` + `DetachInteractionCompletedCallback(string sessionId)`.
- `RolePlaySubmissionTracker`: implement by swapping the wrapper inner (identical pattern to `AttachChunkCallback`/`DetachChunkCallback`).

> **Note on a latent issue in the current chunk path:** the workspace passes its **raw** `OnRolePlayResponseChunkAsync` delegate directly into `ContinueAsAsync`/`SubmitPromptAsync` (`razor:7313, 4757`), so the tracker's `ChunkCallbackWrapper` is **not** what the engine invokes — `AttachChunkCallback` on a returning component does not route live chunks to the new instance (the `_isDisposed` guard just silences the old one). For B-087 we will follow the **same raw-delegate + `_isDisposed` guard** pattern for parity and to keep scope contained, relying on **per-actor flush + `LoadSessionAsync`** for navigation resilience (the completed interactions are what matter; missing a few live chunks on return is the existing accepted behavior). A follow-up could refactor both chunk and interaction callbacks to route through the wrapper — **out of scope for B-087**.

### D4 — Pending bubble becomes per-actor

Replace the single accumulating `_pendingResponseBuilder` model with a **current-actor** streaming bubble:

- Reset `_pendingResponseBuilder` at the start of each actor (engine signals actor start — see D5), and clear it when `onInteractionCompleted` fires for that actor (the committed row replaces the bubble).
- `RolePlayPendingResponse` keeps its isolated self-render contract (no parent re-render per chunk). Only its label/text swaps per actor.
- Narrative: non-streaming, so its bubble shows only the label ("Waiting for narrative continuation…") with no text, then is replaced by the committed narrative row.

### D5 — Actor-start signal

To reset the streaming buffer per actor, the engine should signal "actor N starting" before each `ContinueAsync` call. Cheapest option: reuse `onChunk` with a **null/empty reset convention** is fragile — prefer a tiny additional callback `Action<int,string>? onActorStart` (position, actorName) **or** fold it into the existing chunk path by having the workspace reset its builder when it receives the first chunk of a new actor. Recommended: a small `onActorStart(position, actorName)` callback (cheap, explicit, no string-protocol hacks). This is the one additive surface beyond D1.

### D6 — Status indication

Update `BuildContinuePendingResponseLabel` to a progress form: `"Generating {ActorName} ({position} of {turnActorCount})…"` for the current actor; `"Waiting for narrative continuation…"` for the narrative leg. The label is already the pending bubble's `Label` parameter.

### D7 — Read cursor (B-010) pin — implement the missing behavior

On the **first** `onInteractionCompleted` of a turn:
- Capture whether the user was at the scroll bottom **before** appending (reuse existing JS helper `rolePlayWorkspace.isStoryNearBottom` at `roleplay-workspace.js:9`).
- If **not** at bottom: set `_readCursorInteractionIndex` to the index of the first new interaction and invoke `rolePlayWorkspace.scrollToCursor` (the JS helper already exists at `roleplay-workspace.js:154`).
- If **at** bottom: append + `rolePlayWorkspace.scrollStoryToBottom` (existing helper at `:3`) so each new row is followed.
- Subsequent interactions in the same turn: keep the same rule (follow if at bottom, otherwise leave cursor pinned at the first new index — the user scrolls themselves).

This realizes B-010's stated intent that is currently missing from code.

### D8 — Scope: Continue + Send (single-actor)

Apply the same incremental display to `SubmitPromptAsync` (Send with text, single actor + optional narrative). The participant interaction should appear as soon as generated, narrative when it finishes. Low extra cost (same callback), consistent UX. Recommended **include**.

## 5. Affected files (blast radius)

| File | Change | Risk |
|---|---|---|
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | `ContinueAsAsync` + `SubmitPromptAsync`: new `onInteractionCompleted` + `onActorStart` params; invoke per actor & narrative; per-actor `QueueRolePlaySessionSave`+`FlushAsync` inside loops | Medium — engine hot path; sequential ordering must be preserved |
| `DreamGenClone.Web/Domain/RolePlay/RolePlayRunningSubmission.cs` | Add `RolePlayInteractionCallbackWrapper` (new wrapper class in same file or shared generic) | Low — additive |
| `DreamGenClone.Web/Application/RolePlay/IRolePlaySubmissionTracker.cs` | Add `AttachInteractionCompletedCallback` / `DetachInteractionCompletedCallback` | Low — additive |
| `DreamGenClone.Web/Application/RolePlay/RolePlaySubmissionTracker.cs` | Implement the two new members (mirror chunk pattern) | Low |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | New `OnRolePlayInteractionCompletedAsync` + `OnRolePlayActorStartAsync`; append to `_session.Interactions` + `StateHasChanged`; reset pending buffer per actor; wire B-010 cursor pin; attach/detach interaction callback in mount/dispose + `QueryTrackerOnMountAsync` | Medium — large file; Razor-editing rules apply (full-context reads, micro-steps) |
| `DreamGenClone.Web/Components/Pages/RolePlayPendingResponse.razor` | Label/text swap per actor (already supports `SetText`; may add `SetLabel`) | Low |
| `DreamGenClone.Web/wwwroot/js/roleplay-workspace.js` | Wire `scrollToCursor` for not-at-bottom (helper already exists at `:154`); reuse existing `isStoryNearBottom` (`:9`) for the C# at-bottom check | Low |
| `DreamGenClone.Tests/RolePlay/**` | New tests: callback invocation order (participants then narrative), per-actor flush count, tracker attach/detach, workspace append-on-completion | Medium — new test coverage |

**Unchanged (important):** `IRolePlayContinuationService` / `RolePlayContinuationService` — the callback is engine-level only.

## 6. Risks & constraints

- **Hard Rule — no RP engine code changes without plan + confirmation:** this document is the plan; **no code will be touched until explicit "go ahead"**.
- **Hard Rule — all tests must pass:** new tests added; full RP test suite run before declaring done.
- **Hard Rule — no fallbacks for gate values:** N/A (display/persistence cadence, not gate thresholds).
- **Blazor render cost:** parent re-renders **once per completed interaction** (≤ ~6/turn), NOT per chunk. Chunk streaming still isolated in `RolePlayPendingResponse`. Acceptable.
- **Adaptive panel transient:** mid-turn reload shows last-turn V2 state until the turn completes — acceptable, documented.
- **Sequential generation ⇒ no real reordering** — "reorder/insertion in place" from the backlog reduces to ordered append.
- **Narrative non-streaming** — appears as a committed row only (no streaming text for it).
- **Per-actor flush cost** — up to `SceneContinueBatchSize` (≤ 6) extra session-blob writes per turn; verify `AutoSaveCoordinator` handles rapid sequential `FlushAsync` without contention.

## 7. Test plan

1. **Engine callback order** — `ContinueAsAsync` with a 3-actor batch + narrative: assert `onActorStart` fires 3× (then narrative-start), `onInteractionCompleted` fires 4× in order (participant 1, 2, 3, narrative), each carrying the correct `positionInTurn`/`turnActorCount`.
2. **Per-actor flush** — assert `QueueRolePlaySessionSave`+`FlushAsync` called once per participant inside the loop (and the end-of-turn flush still fires once).
3. **Tracker attach/detach** — `AttachInteractionCompletedCallback` swaps inner; `Detach…` nulls it; no-op on absent session; `ObjectDisposedException` swallowed + detaches (mirror existing `RolePlaySubmissionTrackerTests`).
4. **Workspace append-on-completion** — `OnRolePlayInteractionCompletedAsync` appends to `_session.Interactions` and triggers render; pending bubble cleared for that actor.
5. **B-010 cursor pin** — not-at-bottom ⇒ `_readCursorInteractionIndex` set to first new index + `scrollToCursor` invoked; at-bottom ⇒ `scrollStoryToBottom` invoked; cursor never moves backward.
6. **Navigation resilience** — simulate dispose mid-batch: per-actor flush has persisted completed interactions; re-mount ⇒ `QueryTrackerOnMountAsync` ⇒ `LoadSessionAsync` shows them; later actors' completions reach the new instance.
7. **Full RP suite green** (`dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~RolePlay"`).

## 8. Open questions (need your call before implementation)

1. **Per-actor DB flush** — accept up to ~6 extra session-blob writes/turn for navigation resilience? (Recommended: **yes**.) Alternative: display-only (in-memory) and accept that navigation-away mid-batch shows nothing new until the turn completes.
2. **Pending bubble UX** — replace the single concatenated bubble with a per-actor streaming bubble that converts to a committed row? (Recommended: **yes**.)
3. **B-010 cursor pin** — implement the missing "pre-generate boundary" pin here (cursor at first new interaction + scroll-to-cursor when not at bottom; follow-to-bottom when at bottom)? (Recommended: **yes** — fulfills B-010's stated intent.)
4. **Scope** — include the **Send** path (`SubmitPromptAsync`, single actor + narrative) for the same incremental display, or Continue-only? (Recommended: **include Send** — low extra cost, consistent UX.)
5. **`onActorStart` callback** — accept a second tiny engine callback for actor-start (clean per-actor buffer reset), or try to infer actor boundaries from chunk stream (fragile)? (Recommended: **accept the small callback**.)

---

## 9. Confirmation

Per the repo Hard Rule, **no code changes will be made until you reply "go ahead" / "yes"** (and ideally confirm the open questions above). On approval, implementation proceeds in this order: tracker wrapper → engine callbacks + per-actor flush → workspace callback + pending bubble + B-010 pin → tests → full suite green → update backlog state to `implemented`.
