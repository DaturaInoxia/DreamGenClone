# 008 — Stale session save re-activates a consumed one-shot staged direction (shower regression)

**Report**

- Session: `f1d424cc-eb01-47ca-8176-5c280b6fb696` (Campground Intimacy, theme Exhibitionism V3, BuildUp phase)
- Interaction: `c0ace590-aa77-462a-b498-bdfd2b147281` (idx 18, Becky, 01:52:32 UTC)
- Symptom: The narrative regressed back into the shower/"he saw me" peeping beat even though it had already moved past it. The same shower-exposure beat repeated across idx 9–12, 13–14, 17, 18, 21. The user only changed the Continuation Settings **Pacing to Fast** and the story jumped back into the shower.
- The `c0ace590` prompt contained `[Staged Scene Directions — Execute This Turn] Instruction Becky turns off the water and stands in the ringing quiet, letting Dean's gaze replay across her skin until a low, guilty heat coils in her belly.` — the same staged instruction that was already executed in Turn 6.

**Analysis**

- A user-staged Instruction (`f2e2755e`, idx 13, `ActorName="Instruction"`, `IsStagedDirection=true`) is a **one-shot** directive: injected via `StagedDirectionsSlot` on the next continuation, then `GraduateStagedDirections` (RolePlayEngineService.cs) flips `IsStagedDirection=false` to make it history.
- Log timeline (DreamGenClone.Web/logs/dreamgenclone-20260818.log):
  - `21:50:47` `Graduated 1 staged direction(s)` — Turn 6 end; flag flipped false in-memory; debounced save landed `21:50:48`.
  - `21:52:17` `Autosaved ... due to roleplay-session-updated` (interactions=18) — **the user's Fast pacing change**; this save serialized the component's STALE `_session` and re-wrote `IsStagedDirection=true` to the DB.
  - `21:52:22` Turn 7 prompt (Becky `c0ace590`) built **with the staged direction present** → the model re-wrote the shower scene.
  - `21:54:33` `Graduated 1 staged direction(s)` — Turn 7 end re-graduated it.
- Root cause: `RolePlayWorkspace.razor` handlers (`SaveContinuationOverrideAsync`, `ClearContinuationOverrideAsync`, `CommitTitleEditAsync`, `OnModelSettingsUpdated`, `OnAssistantModelChanged`, `PersistAssistantChatsAsync`, `SaveSessionSettingsAsync`) called `RolePlayEngine.SaveSessionAsync(_session)` with the component's in-memory `_session`. That object can be stale w.r.t. the DB: the engine graduates staged rows on its own session instance, and the debounced graduation save can land after the component's post-turn reload. Saving the stale blob re-persists stale per-interaction flags (`IsStagedDirection`), re-activating a consumed one-shot instruction.
- Secondary factors: fast pacing's `advance through multiple beats` conflicts with `Granularity: Meso — one step` + `One beat per turn`; the prompt's `Current Location:` field was empty; the Narrative closer (idx 17) re-opened the shower, so `c0ace590`'s Last Narrative was itself a shower scene.

**Plan (approved: "do the recomended fix")**

- Add `ReloadAndSaveSessionAsync(Action<RolePlaySession> mutate)` to `RolePlayWorkspace.razor`: loads the session fresh from the DB, applies the mutation, saves the fresh object, and refreshes `_session`.
- Route all user-initiated full-session saves through it so no stale blob is ever persisted.
- Blast radius: one Razor component; no engine/domain/prompt-slot changes; 7 handler call sites + 1 helper.

**Resolution**

- `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
  - Added `ReloadAndSaveSessionAsync(Action<RolePlaySession>)` helper (fresh DB load → mutate → save → `_session = fresh`).
  - `CommitTitleEditAsync` → `ReloadAndSaveSessionAsync(s => s.Title = _editingTitleText)`
  - `OnModelSettingsUpdated` → helper with model-settings lambda
  - `PersistAssistantChatsAsync` → helper with chats lambda
  - `OnAssistantModelChanged` → helper with model lambda
  - `SaveSessionSettingsAsync` → `ReloadAndSaveSessionAsync(CaptureResolvedIntensitySnapshot)`
  - `SaveContinuationOverrideAsync` → `ReloadAndSaveSessionAsync(s => s.ContinuationOverride = overrideValue)`
  - `ClearContinuationOverrideAsync` → `ReloadAndSaveSessionAsync(s => s.ContinuationOverride = null)`
- The two remaining direct `SaveSessionAsync(_session)` calls (inside `LoadSessionAsync`) operate on a freshly-loaded object and are intentionally left as-is.
- **Validated**: `dotnet build DreamGenClone.Web` 0 warnings/0 errors; full test suite 1048 passed / 0 failed (RolePlay filter 749 passed). Webapp restarted via `helpers/start-webapp.ps1`. `[ ] pending` until user confirms a fresh RP session no longer regresses after a Continuation Settings save.
