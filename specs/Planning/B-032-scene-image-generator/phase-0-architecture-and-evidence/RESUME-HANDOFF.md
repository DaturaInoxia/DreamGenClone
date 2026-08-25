# RESUME HANDOFF — Scene Image Generator (B-032) Preprocessor Refinement

> **Read this first if you are picking up this work from a fresh machine / new chat session.**
> Everything needed to resume lives in this repo. Do NOT rely on prior chat memory.

**Feature**: `001-scene-image-generator` (backlog B-032, Scene Image Generator)  
**Last updated**: 2026-08-24
**Branch/work dir**: repo root `D:\src\DreamGenClone` (on other machines: the same repo, same paths relative to root)

---

## 1. TL;DR — where we are

> **Phase 2 continuity work:** read `continuity-rendering-architecture.md` first, then continue the current gate in `controlnet-touch-proof.md`. The original prompt-to-image pipeline below remains valid Phase 1 plumbing, but it is not sufficient for repeatable identity, exact pose/contact, detailed location geometry, or multiple POVs of one frozen moment. Do not resume by tuning prompts or generating random seeds.

- The Scene Image Generator feature is **fully implemented and tested** (a two-stage pipeline: text preprocessor → image model; Model Manager image config; Image Studio + Gallery pages; workspace triggers/indicators).
- **Build: 0 errors. Tests: 1195/1195 passing.**
- **CR-006 (Preprocessor Refinement) is now IMPLEMENTED** — beats + participants + full-turn + POV + transparency, in the order P1 → P2 → P3 → P4 → P5 → P6 (see §5). All six phases landed.
- Remaining manual items: `tasks.md` T068 (POC validation with a real image provider) and T069 (backlog state + Phase 2 likeness scope decision).

---

## 2. How to orient (read these first, in order)

1. `specs/Planning/B-032-scene-image-generator/phase-0-architecture-and-evidence/continuity-rendering-architecture.md` — controlling continuity architecture, terminology, delivery phases, and gates.
2. `specs/Planning/B-032-scene-image-generator/phase-0-architecture-and-evidence/controlnet-touch-proof.md` — host inventory and preserved proof results.
3. `specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/spec.md` — formal Phase 1 and Phase 2 user stories and requirements.
4. `specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/change-requests.md` — historical Phase 1 change log. CR-001..CR-006 are implemented.
5. `specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/design/preprocessor-refinement-design.md` — approved historical CR-006 design.
6. `specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/data-model.md` — current Phase 1 entities/tables; Phase 2 domain draft is in the continuity architecture.
7. `specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/contracts/scene-image-pipeline-contract.md` — current pipeline contract + debug events.
8. `specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/tasks.md` — original Phase 1 task list; it does not yet decompose Phase 2.

Also mandatory repo rules: `.github/copilot-instructions.md` (hard rules: no git restore, no fallback gate values, tests must pass, no RP-engine changes without plan+confirmation) and `helpers/start-webapp.ps1` for running the app.

---

## 3. Current implementation state (verified 2026-08-20)

### Implemented and working (do not re-do)
- **Two-stage pipeline**: `SceneImagePromptGenerationJobHandler` (text preprocessor) + `SceneImageRenderingJobHandler` (image model), on the generic background job queue.
- **Model Manager** image support: provider `ImageCapability`/`ImageGenerationPath`/`ContentPolicy`, model `ModelKind`/`ImageSizeSupported`, function defaults `RolePlaySceneImagePreprocessor` + `RolePlaySceneImage`.
- **Image Studio** page `/roleplay/studio/{sessionId}/{interactionId}` — style/size/aspect/explicit settings, editable prompt, refine, render, regenerate, delete, **Continue-from-image** (CR-003: restores prompt + settings).
- **Gallery** page `/roleplay/gallery/{sessionId}`.
- **Workspace** integration: per-interaction image button + count badge, header Gallery link.
- **CR-001**: Story Moment dark panel (`.scene-image-story-moment` in `SceneImageStudio.razor.css`).
- **CR-002**: defaults = Cartoon style + AllowExplicitImage checked.
- **CR-003**: `SceneImageRecord.SettingsJson` + Continue button.
- **CR-004**: Scene Context intensity resolved from profiles (studio + preprocessor job handler).
- **CR-005**: character likeness — `PhysicalAttributesFormatter.FormatVisualBlock` (visual-only) + `CHARACTER APPEARANCE` block in preprocessor + `CHARACTER LIKENESS` system rule.
- **Seedream/TogetherAI fixes** (outside CRs, from debugging): image request body uses `width`/`height` + `response_format:"base64"` (no `steps` — Seedream rejects it); image-kind models are tested via the images endpoint in `ProviderTestService`.

### Key files (all in the Web project unless noted)
| File | Role |
|------|------|
| `DreamGenClone.Web/Application/RolePlay/SceneImagePromptPreprocessor.cs` | Builds preprocessor system+user prompt; **CR-006 primary target** |
| `DreamGenClone.Web/Application/RolePlay/ISceneImagePromptPreprocessor.cs` | Its interface (`BuildMessages(..., characters = null)`) |
| `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs` | Runs preprocessor in background; loads session + scenario + intensity |
| `DreamGenClone.Web/Application/RolePlay/SceneImageService.cs` | Enqueue prompt/render, queries, delete; snapshots `SettingsJson` |
| `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` | Calls image model, clamps SFW, writes record |
| `DreamGenClone.Web/Application/RolePlay/PhysicalAttributesFormatter.cs` | `FormatVisualBlock` (likeness) + `FormatBlock` (narrative) |
| `DreamGenClone.Web/Application/RolePlay/Models/SceneImageStudioSettings.cs` | Style/size/aspect/explicit DTO |
| `DreamGenClone.Web/Application/RolePlay/Models/SceneRenderRequest.cs` | Render request (carries `SettingsJson`) |
| `DreamGenClone.Domain/RolePlay/SceneImageRecord.cs` / `SceneImagePromptRecord.cs` | Entities |
| `DreamGenClone.Infrastructure/RolePlay/SceneImageRepository.cs` | SQLite CRUD + schema |
| `DreamGenClone.Infrastructure/Models/ImageGenerationClient.cs` | HTTP image call + `CheckImageModelHealthAsync` |
| `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (+`.css`) | Studio UI (**CR-006 P3/P4/P5 target**) |
| `DreamGenClone.Web/Components/Pages/SceneImageGallery.razor` | Gallery |
| `DreamGenClone.Web/Components/Pages/ModelManager.razor` | Provider/model image config |
| `DreamGenClone.Web/Program.cs` | DI registrations (see §4), static `/scene-images` |
| `DreamGenClone.Tests/RolePlay/SceneImage*Tests.cs` | Test suite for the feature |

### DI registrations (Program.cs)
- Scoped: `IScenarioService`, `StoryAnalysisFacade`, `IModelResolutionService`, `ISceneImageService`, `WorkspaceSettingsState`, both job handlers, `IRolePlayStateRepository`.
- Singleton: `IImageGenerationClient`, `ISceneImageRepository`, `ISceneImageStorageService`, `ISceneImagePromptPreprocessor`.

---

## 4. Core domain facts you MUST know for CR-006

### 4.1 Turns are first-class — "Full Turn" is real
- `RolePlayV2Turns` table + `RolePlayTurn` entity (`DreamGenClone.Domain/RolePlay/RolePlayTurn.cs`).
- Each turn has `OutputInteractionIds` (ALL interactions generated in one submission: e.g. Becky + Dean + Ken-observer + Narrative), `InputInteractionId`, `InitiatedByActorName`, `TurnIndex`, `TurnKind`.
- **To expand "interaction" → "full turn"**: given an interaction id, find its turn via `IRolePlayStateRepository.LoadTurnsAsync(sessionId, ...)` (match `InputInteractionId` or membership in `OutputInteractionIds`), then load all sibling interactions from `RolePlaySession.Interactions`.
- Fallback when no turn row exists (legacy): use the single interaction + nearby `Narrative` interactions in a small window.

### 4.2 Presence model already exists (use it — don't re-invent)
- `RolePlayScenePresenceHelper.IsActorInScene(RolePlaySession, actorName)` → `bool?` (true/false/null). Uses `CharacterLocations` truth-state + `CharacterLocationPerceptions` line-of-sight/proximity.
- `AdaptiveScenarioState.CharacterEncounterStates` → per-character `IsHavingSex` / `IsHavingSexConfirmed`.
- `AdaptiveScenarioState.CharacterLocations`, `CharacterSnapshots`, `CharacterRoles` (id→role), `CurrentSceneLocation`, `CurrentPhase`, `CurrentEncounterNumber`, `IsEncounterActive`.
- The current preprocessor **ignores all of this** and does naive name-substring matching + always-includes-persona. **That is the bug CR-006 fixes.**

### 4.3 Beat signals
- Interaction has `NarrativePhaseAtCreation`, `WasInSexScene`, `WasEncounterStart`, `WasEncounterBoundaryDetected`, `EncounterNumberAtCreation`, `InteractionIndexInEncounter`.
- Encounter summaries (`EncounterSummaryRecord`) have `StartInteractionIndex`/`EndInteractionIndex`.
- `ContinuationMarkerCatalog` + `SexualActivityKeywords` exist for encounter/beat concepts.

### 4.4 Image prompt engineering (the "expert" constraints)
- Image models respond to concrete descriptors, not abstract narrative POV.
- **Likeness** = fixed visual identity anchors (age/height/hair/eyes/skin/body type/marks) reproduced verbatim.
- **POV** = a *framing line* that varies while identity + beat stay constant (see design §4.2 table).
- Keep each single prompt simple; never send measurements/intimate fields to the image model.

---

## 5. THE PLAN TO IMPLEMENT (CR-006) — approved design

Design doc: `specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/design/preprocessor-refinement-design.md` (READ IT FULLY).

### Confirmed decisions (do not re-litigate)
1. **Beat selector always shown, user-selectable.**
2. **POV set derived from beat participants** (`Omniscient` + present participants); not a fixed 4-way set.
3. **One submission = one turn = one scene** (may contain 1..many beats).
4. **One POV at a time**, user-selectable per render.

### Phase order (implement in this order)

**P1 — Presence-grounded participant resolution** (small, low risk, fixes the Ken/Becky/Dean bug)
- In `SceneImagePromptPreprocessor.BuildCharacterAppearanceBlock` (or a new resolver), select participants via:
  1. Actor of the beat/interaction (hard include)
  2. `RolePlayScenePresenceHelper.IsActorInScene(...) == true`
  3. `CharacterEncounterStates` having-sex (confirmed || heuristic)
  4. Named in text (only after 1–3)
- **Persona only when actually present** (remove the always-include bug). Persona-as-observer → "observer" flag, not a subject entry.
- Fix `Characters present` line to use names (not `CharacterRoles.Values` roles).

**P2 — Full Turn context**
- New `SceneImageTurnResolver` (or method): interactionId → `RolePlayTurn` → sibling interactions.
- Pass the full-turn interactions into the preprocessor so the Narrative (omniscient) interaction contributes setting/environment detail.

**P3 — Transparency (Option 4)**
- Emit `SceneImageAnalysisCompleted` debug event: `{ turnId, beatId, pov, beats[], participants[{name,presence,reason}], excluded[{name,reason}], promptSources, settingsJson, rawAnalysis }`.
- Studio: collapsible **"Why this prompt?"** panel showing chosen beat, subjects, reason, POV framing, exact system+user prompt, excluded characters.
- Prompt preview before render (reuse the editable textarea context).

**P4 — Deterministic beat segmentation (Option 2B)**
- New `SceneImageBeatAnalyzer`: split turn interactions into beats using `WasEncounterStart`, `WasEncounterBoundaryDetected`, `InteractionIndexInEncounter==0`, phase transition, content signals; cap ~6 beats.
- Studio shows beat list; user selects.

**P5 — POV dimension (Option 3)**
- Add `BeatId` + `Pov` to `SceneImageRecord` (+ guarded migration in `SqlitePersistence.cs`, mirroring the `SettingsJson` migration pattern).
- POV framing lines per participant (design §4.2); POV selector derived from beat participants.
- One POV per render; re-render with another POV → new linked record.

**P6 — LLM beat labeling (Option 2A, optional enhancement)**
- Structured LLM pass over the full turn to label beats + suggest a beat; deterministic presence intersection still applies.

### DTOs / new files to create
- `SceneImageAnalysisResult`, `SceneImageBeat`, `SceneImageParticipant` (analysis DTOs under `DreamGenClone.Web/Application/RolePlay/Models/`).
- `SceneImageBeatAnalyzer.cs`, `SceneImageTurnResolver.cs` (Web project).
- Request/payload additions for `BeatId`/`Pov` (`SceneRenderRequest`, job payloads).

---

## 6. Build / test / run

```powershell
# Build (must be 0 errors). If the webapp is running it locks Web/bin — stop it first.
dotnet build DreamGenClone.sln

# Tests (must stay green; currently 1157 passing)
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj

# Run the app (start from DreamGenClone.Web, Development env, port 5177)
.\helpers\start-webapp.ps1        # or start-webapp-dev-clean.ps1
```

- **Never** `git restore` / `git checkout --` / `git reset --hard` — fixes are forward-only code edits.
- **Never** commit `dreamgenclone.dev.db`; only `dreamgenclone.snapshot.db` is tracked.
- Stop/restart the webapp only when a build needs the locked `Web/bin`.

---

## 7. What is NOT done / manual items

- `tasks.md` **T068** — POC validation checklist (needs a running app + a configured image provider): NSFW (filtered clamp / adult-allowed / unset policy), image quality across styles/sizes, basics (generate/edit/regenerate/indicator/gallery/delete), unconfigured guidance.
- `tasks.md` **T069** — backlog B-032 state already moved to `planned` (verified in `specs/Planning/backlog.md`); record POC findings + decide Phase 2 (likeness) scope when the POC is done.
- **CR-006 is IMPLEMENTED (P1–P6 all landed).** Remaining: manual POC validation of the new beat/POV/transparency flow in the running app.

---

## 8. Environment / other-machine setup

- Repo is a normal .NET 9 solution (`DreamGenClone.sln`, 4 projects + `artifacts/tmp/dbquery`). `dotnet restore` + `dotnet build` on any machine.
- SQLite DB lives at `DreamGenClone.Web/data/`. Dev DB `dreamgenclone.dev.db` is git-ignored (has encrypted API keys); the git-tracked `dreamgenclone.snapshot.db` is sanitized. **Clone/pull gets the snapshot, NOT the dev DB** — after cloning, run the app once (Development) to seed a fresh dev DB, or copy your dev DB from the original machine if you need your real providers/sessions.
- Providers/models (including the TogetherAI image setup) are data in the DB, not code. On a fresh machine you'll re-add the image provider + model in Model Manager (ImageCapability, ContentPolicy, ModelKind=Image, function defaults).
- DB query tool: `dotnet run --project artifacts/tmp/dbquery -- <command>` (see `.github/instructions/dbquery-reference.instructions.md`). Debug events (incl. `SceneImagePromptSent`) are in the `RolePlayDebugEvents` table and the workspace Debug View.

---

## 9. Quick verification checklist after resuming

1. `dotnet build DreamGenClone.sln` → 0 errors.
2. `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj` → all pass (≥1195).
3. Start app → Model Manager shows the image fields; Studio/Gallery routes render.
4. Confirm the preprocessor emits `SceneImagePromptSent` with the `CHARACTER APPEARANCE` block (Debug View) — CR-005 baseline.
5. Confirm the studio shows the **Beat selector** + **POV selector** + **"Why this prompt?"** panel (CR-006 P3/P4/P5), and the `SceneImageAnalysisCompleted` debug event appears (Debug View).
