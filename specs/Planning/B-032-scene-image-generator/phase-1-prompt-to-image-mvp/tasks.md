---
description: "Task list for Scene Image Generator (B-032) feature implementation"
---

# Tasks: Scene Image Generator

**Input**: Design documents from `/specs/Planning/B-032-scene-image-generator/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are included — the repo's hard rule requires every implementation change to leave the test suite green, and the design's state machines + parser + resolution paths are explicitly unit-test-covered per `data-model.md` and the contract. Tests are written alongside implementation (not strict TDD) since the no-fallback rule requires the fail-fast paths to be verified by tests before a task is marked complete.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. The 7 user stories map to phases in priority order. Because the spec marks US1, US2, US6, and US7 as P1 (the gating core) and US3, US4, US5 as P2, the phase ordering places the P1 stories first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g. US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

This is an additive feature in an existing 4-project .NET 9 layered solution (no new project). Paths follow the existing layout:

- Domain entities/enums/records: `DreamGenClone.Domain/<area>/<file>.cs`
- Application interfaces + DTOs: `DreamGenClone.Application/<area>/<file>.cs` (interfaces) and `DreamGenClone.Application/RolePlay/Models/<file>.cs` (DTOs)
- Infrastructure implementations: `DreamGenClone.Infrastructure/<area>/<file>.cs`
- Web (pages, service impls, job handlers, DI): `DreamGenClone.Web/<area>/<file>.cs` / `.razor`
- Tests: `DreamGenClone.Tests/RolePlay/<file>.cs`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No project scaffolding is needed (the solution exists). This phase captures the one configuration addition required before any code.

- [X] T001 Add `SceneImageRoot` to `PersistenceOptions` (default `data/scene-images`) in `DreamGenClone.Infrastructure/Configuration/PersistenceOptions.cs` and mirror the key in `DreamGenClone.Web/appsettings.json` + `appsettings.Development.json`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared infrastructure that EVERY user story depends on — Model Manager image capability, storage, persistence, and the image HTTP client. No user story can be implemented until this is complete.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. This phase delivers the plumbing that the POC (US1) needs to render a single image end-to-end.

- [X] T002 [P] Create `ImageProviderCapability` enum (None=0, TextAndImage=1, ImageOnly=2) in `DreamGenClone.Domain/ModelManager/ImageProviderCapability.cs`
- [X] T003 [P] Create `ImageContentPolicy` enum (Unknown=0, SfwFiltered=1, AdultAllowed=2, AdultAllowedConfigurable=3) in `DreamGenClone.Domain/ModelManager/ImageContentPolicy.cs`
- [X] T004 [P] Create `ModelKind` enum (Text=0, Image=1) in `DreamGenClone.Domain/ModelManager/ModelKind.cs`
- [X] T005 [P] Create `ResolvedImageModel` record (ProviderBaseUrl, ImageGenerationPath, ProviderTimeoutSeconds, ApiKeyEncrypted, ModelIdentifier, ContentPolicy, ProviderName, IsSessionOverride) in `DreamGenClone.Domain/ModelManager/ResolvedImageModel.cs`
- [X] T006 [P] Append `RolePlaySceneImagePreprocessor` and `RolePlaySceneImage` to `AppFunction` enum in `DreamGenClone.Domain/ModelManager/AppFunction.cs`
- [X] T007 [P] Add `ImageCapability`, `ImageGenerationPath` (default `/v1/images/generations`), `ContentPolicy` properties to `Provider` in `DreamGenClone.Domain/ModelManager/Provider.cs`
- [X] T008 [P] Add `ModelKind`, `ImageSizeSupported` properties to `RegisteredModel` in `DreamGenClone.Domain/ModelManager/RegisteredModel.cs`
- [X] T009 Add `SceneImageStatus` enum (Pending=0, Generating=1, Complete=2, Failed=3) and `SceneImagePromptStatus` enum (Pending=0, Complete=1, Failed=2) in `DreamGenClone.Domain/RolePlay/SceneImageRecord.cs` and `SceneImagePromptRecord.cs`
- [X] T010 [P] Create `SceneImagePromptRecord` entity (Id, SessionId, InteractionId, SettingsJson, InputExcerpt, OutputPrompt, Status, ModelIdentifier, ErrorMessage, CreatedUtc, UpdatedUtc) in `DreamGenClone.Domain/RolePlay/SceneImagePromptRecord.cs`
- [X] T011 [P] Create `SceneImageRecord` entity (Id, SessionId, InteractionId, PromptRecordId, PromptSnapshot, Status, FileRelativePath, ModelIdentifier, ProviderName, ContentPolicy, ImageSize, Style, ErrorMessage, RegenerateOfId, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc) in `DreamGenClone.Domain/RolePlay/SceneImageRecord.cs`
- [X] T012 [P] Create `IImageGenerationClient` interface (`Task<byte[]?> GenerateAsync(ResolvedImageModel, string prompt, string? size, CancellationToken)`) in `DreamGenClone.Application/Abstractions/IImageGenerationClient.cs`
- [X] T013 [P] Create `ISceneImageRepository` interface (prompt CRUD, image CRUD, `CountImagesByInteractionAsync`, `GetLatestPromptAsync`) in `DreamGenClone.Application/Abstractions/ISceneImageRepository.cs`
- [X] T014 [P] Create `ISceneImageStorageService` interface (SaveAsync, OpenReadAsync, DeleteAsync) in `DreamGenClone.Application/Abstractions/ISceneImageStorageService.cs`
- [X] T015 [P] Create `ISceneImageService` interface (EnqueuePromptAsync, EnqueueRenderAsync, GetPromptAsync, GetLatestPromptAsync, ListImagesByInteractionAsync, ListImagesBySessionAsync, CountImagesByInteractionAsync, DeleteImageAsync) in `DreamGenClone.Application/Abstractions/ISceneImageService.cs`
- [X] T016 [P] Create `ISceneImagePromptPreprocessor` interface (BuildMessages, ParseOutput) + `SceneImagePreprocessorResult` record in `DreamGenClone.Application/Abstractions/ISceneImagePromptPreprocessor.cs`
- [X] T017 [P] Create `SceneImageStudioSettings` DTO (Style, ImageSize, AspectRatio, AllowExplicitImage) in `DreamGenClone.Application/RolePlay/Models/SceneImageStudioSettings.cs`
- [X] T018 [P] Create `ScenePromptRequest` DTO (SessionId, InteractionId, Settings, ExcerptOverride) in `DreamGenClone.Application/RolePlay/Models/ScenePromptRequest.cs`
- [X] T019 [P] Create `SceneRenderRequest` DTO (SessionId, InteractionId, PromptRecordId, Prompt, ImageSize, RegenerateOfId) in `DreamGenClone.Application/RolePlay/Models/SceneRenderRequest.cs`
- [X] T020 Add SQLite migration for `Providers` (add ImageCapability, ImageGenerationPath, ContentPolicy columns) and `RegisteredModels` (add ModelKind, ImageSizeSupported columns) + create `SceneImagePrompts` and `SceneImages` tables with indexes in `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` (use existing guarded `pragma_table_info` column-existence pattern)
- [X] T021 Update `ProviderRepository` and `RegisteredModelRepository` read/write mapping to include the new columns in `DreamGenClone.Infrastructure/ModelManager/ProviderRepository.cs` and `RegisteredModelRepository.cs`
- [X] T022 [P] Implement `SceneImageStorageService` (SaveAsync returns `{sessionId}/{imageId}.png`, OpenReadAsync, DeleteAsync) rooted at `PersistenceOptions.SceneImageRoot` in `DreamGenClone.Infrastructure/Storage/SceneImageStorageService.cs` (mirror `TemplateImageStorageService`)
- [X] T023 [P] Implement `SceneImageRepository` (SQLite CRUD for both tables, `CountImagesByInteractionAsync`, `GetLatestPromptAsync`, status transition helpers) in `DreamGenClone.Infrastructure/RolePlay/SceneImageRepository.cs`
- [X] T024 [P] Implement `ImageGenerationClient` (POST `{BaseUrl}{ImageGenerationPath}`, Bearer auth for cloud, b64_json response decode, `ImageGenerationException` with 401/429/402/5xx/policy-reject mapping) in `DreamGenClone.Infrastructure/Models/ImageGenerationClient.cs` (mirror `CompletionClient`)
- [X] T025 Add `ResolveImagePromptModelAsync` (text model, reuses existing resolution path, fail-fast on missing `RolePlaySceneImagePreprocessor` default) and `ResolveImageModelAsync` (image model, fail-fast on missing default / `ModelKind != Image` / `ImageCapability == None` / `ContentPolicy == Unknown`) to `DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs`
- [X] T026 Add `SceneImagePromptGeneration` and `SceneImageRendering` constants to `DreamGenClone.Web/Application/BackgroundJobs/BackgroundJobTypes.cs`
- [X] T027 Register DI in `DreamGenClone.Web/Program.cs`: `ISceneImageStorageService`, `ISceneImageRepository`, `IImageGenerationClient`, `ISceneImageService`, `ISceneImagePromptPreprocessor` (singletons) + the two job handlers as `AddScoped<IBackgroundJobHandler, …>` + `UseStaticFiles` branch for `/scene-images` → scene-image root
- [X] T028 Add session image settings (`ImageGenerationEnabled`, `ImageStyleSuffix`, `ImageSize`, `AllowExplicitImage`) to `DreamGenClone.Web/Domain/RolePlay/WorkspaceSettingsState.cs`
- [X] T029 [P] Unit test `SceneImageStorageService` save/open/delete round-trip in `DreamGenClone.Tests/RolePlay/SceneImageStorageServiceTests.cs`
- [X] T030 [P] Unit test `SceneImageRepository` CRUD + status transitions + `CountImagesByInteractionAsync` in `DreamGenClone.Tests/RolePlay/SceneImageRepositoryTests.cs`
- [X] T031 [P] Unit test `ImageGenerationClient` request shape (path, auth header, body), b64 decode, and HTTP error → `ImageGenerationException` mapping in `DreamGenClone.Tests/RolePlay/SceneImageGenerationClientTests.cs`
- [X] T032 [P] Unit test `ModelResolutionService.ResolveImageModelAsync` fail-fast paths (missing default, text-only model, non-image provider, `ContentPolicy == Unknown`) + happy path in `DreamGenClone.Tests/RolePlay/SceneImageResolutionTests.cs`

**Checkpoint**: Foundation ready — Model Manager image support, storage, persistence, image client, and resolution all functional and tested. User story implementation can now begin.

---

## Phase 3: User Story 7 - Configure image capability (Priority: P1) 🎯 MVP gating

**Goal**: The user can configure an image-capable provider, register an image model, and assign models for both prompt-building and rendering — the gating requirement behind every other story.

**Independent Test**: Configure an image-capable provider and an image model in the Model Manager, then generate an image successfully (validated end-to-end in US1).

### Implementation for User Story 7

- [X] T033 [US7] Update `ModelManager.razor` provider editor with `ImageCapability`, `ImageGenerationPath`, `ContentPolicy` fields (with labels explaining the adult-content policy meaning) in `DreamGenClone.Web/Components/Pages/ModelManager.razor`
- [X] T034 [US7] Update `ModelManager.razor` model editor with `ModelKind` (Text/Image) and `ImageSizeSupported` fields in `DreamGenClone.Web/Components/Pages/ModelManager.razor`
- [X] T035 [US7] Add `RolePlaySceneImagePreprocessor` (text models) and `RolePlaySceneImage` (image-kind models only — dropdown filter) rows to the Function Defaults grid + a "no image model configured" callout in `DreamGenClone.Web/Components/Pages/ModelManager.razor`
- [X] T036 [US7] Add Information-level Serilog logs (structured: ProviderName, ModelIdentifier, FunctionName, ImageCapability, ContentPolicy) for provider/model CRUD and function-default changes in `DreamGenClone.Web/Components/Pages/ModelManager.razor` and the backing facade/service

**Checkpoint**: Image capability is configurable end-to-end. US1 can now be implemented and validated.

---

## Phase 4: User Story 1 - Generate an image for a story moment (Priority: P1) 🎯 MVP

**Goal**: The user picks an interaction, opens the Image Studio, and a saved image of that moment is produced via the two-stage pipeline. Reopening the studio shows the saved image.

**Independent Test**: Open a session, click "Generate image" on an interaction, run the two-stage pipeline, confirm an image appears on the studio and is still there after reopening.

### Implementation for User Story 1

- [X] T037 [P] [US1] Implement `SceneImagePromptPreprocessor` (`BuildMessages` composes system+user from session/interaction/scenario state/settings/policy/excerpt; `ParseOutput` tries JSON envelope `{prompt, excerpt}` then plain-text fallback, fails fast on empty/overlong) in `DreamGenClone.Web/Application/RolePlay/SceneImagePromptPreprocessor.cs`
- [X] T038 [US1] Implement `SceneImageService` (EnqueuePromptAsync + EnqueueRenderAsync validate session/config fail-fast, create Pending records, enqueue jobs with `dedupeKey = $"{jobType}:{recordId}"`; List/Get/Count/Delete methods) in `DreamGenClone.Web/Application/RolePlay/SceneImageService.cs`
- [X] T039 [P] [US1] Create `SceneImagePromptGenerationJobPayload` (SessionId, InteractionId, PromptRecordId) in `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobPayload.cs`
- [X] T040 [P] [US1] Create `SceneImageRenderingJobPayload` (SessionId, InteractionId, ImageRecordId) in `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobPayload.cs`
- [X] T041 [US1] Implement `SceneImagePromptGenerationJobHandler` (load record, resolve preprocessor model fail-fast, build messages, call `ICompletionClient`, parse output, write `OutputPrompt`/`InputExcerpt`, set Complete/Failed with ErrorMessage, emit `SceneImagePromptSent`/`SceneImageResponseReceived` debug events, structured logs) in `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs`
- [X] T042 [US1] Implement `SceneImageRenderingJobHandler` (set Generating, resolve image model fail-fast, call `IImageGenerationClient`, save bytes via storage, write FileRelativePath/model/policy/size/style, set Complete/Failed with ErrorMessage, emit `SceneImageResponseReceived` debug event, structured logs) in `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs`
- [X] T043 [US1] Create `SceneImageStudio.razor` page at route `/roleplay/studio/{sessionId}/{interactionId}` — header (back link, session title, gallery link), source panel (interaction content), context summary (intensity/phase/setting/characters read-only), settings (style/size/explicitness policy-aware), "Generate Prompt" button → preprocessor job → editable textarea, "Render Image" button → image job → results strip, status spinners, polling refresh, "no image model configured" guidance inline in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [X] T044 [US1] Add "Generate image" action to the interaction toolbar in `RolePlayWorkspace.razor` that navigates to `/roleplay/studio/{sessionId}/{interaction.Id}` (gated by `WorkspaceSettingsState.ImageGenerationEnabled`) in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T045 [US1] Add Information-level Serilog logs across the studio page, service enqueue paths, and both job handlers (structured: SessionId, InteractionId, ImageId/PromptRecordId, ModelIdentifier, ProviderName, DurationMs, ReasonCode) in the touched files
- [X] T046 [P] [US1] Unit test `SceneImagePromptPreprocessor` — `BuildMessages` per phase/intensity/style/policy, `ParseOutput` happy (JSON envelope) / fallback (plain text) / fail (empty, overlong) paths in `DreamGenClone.Tests/RolePlay/SceneImagePromptPreprocessorTests.cs`
- [X] T047 [P] [US1] Unit test `SceneImageService` + job handlers — EnqueuePromptAsync/EnqueueRenderAsync create Pending + dedupe; prompt handler Complete/Failed; render handler Generating→Complete/Failed; Failed persists ErrorMessage; delete removes file + row in `DreamGenClone.Tests/RolePlay/SceneImageServiceJobTests.cs`

**Checkpoint**: US1 fully functional — generate an image, reopen the studio, the image is still there. MVP delivered.

---

## Phase 5: User Story 2 - Build and edit the image prompt (Priority: P1)

**Goal**: The user can generate an editable prompt from the scene context, edit it, and render from the edited prompt. Changing settings regenerates a prompt that reflects them.

**Independent Test**: Generate a prompt, edit the text, render, confirm the image differs from an unedited-prompt render. Change settings, regenerate, confirm the prompt reflects them.

### Implementation for User Story 2

- [X] T048 [US2] Wire the editable prompt textarea in `SceneImageStudio.razor` so the "Render Image" button sends the current textarea text (not the stored `OutputPrompt`) as the render prompt; persist the edited text back to the prompt record's `OutputPrompt` on render in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [X] T049 [US2] Wire settings changes (style/size/aspect/explicitness) in `SceneImageStudio.razor` to re-run "Generate Prompt" when the user requests it, passing the updated `SceneImageStudioSettings` to `EnqueuePromptAsync` so the new prompt reflects the settings in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [X] T050 [US2] Seed the studio settings from `WorkspaceSettingsState` defaults (`ImageStyleSuffix`, `ImageSize`, `AllowExplicitImage`) on studio open in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`

**Checkpoint**: US2 functional — prompt is editable and settings-aware. US1+US2 together deliver the core two-stage flow.

---

## Phase 6: User Story 6 - Content policy handling (Priority: P1)

**Goal**: Explicit content is generated only when the provider allows it; otherwise the system produces SFW output or a clear explanation — never silently bypassing, never assuming a default policy.

**Independent Test**: Request an explicit image from a `SfwFiltered` provider — confirm SFW clamp or clear explanation (never bypass). Request from an `AdultAllowed` provider with explicitness on — confirm explicit content. Request with `ContentPolicy == Unknown` — confirm fail-fast guidance.

### Implementation for User Story 6

- [X] T051 [US6] Implement the content-policy clamp in `SceneImagePromptPreprocessor.BuildMessages` — when `resolvedPolicy == SfwFiltered`, instruct the model to produce SFW regardless of `AllowExplicitImage` and append `"keep fully clothed / non-explicit"`; log the clamp (ReasonCode `content_policy_clamped`) in `DreamGenClone.Web/Application/RolePlay/SceneImagePromptPreprocessor.cs`
- [X] T052 [US6] Disable/hide the explicitness toggle in `SceneImageStudio.razor` when the resolved provider policy is `SfwFiltered` (resolve the policy on studio open via `ResolveImageModelAsync` and surface it to the UI) in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [X] T053 [US6] Surface provider policy-rejection errors (from `ImageGenerationClient` → `SceneImageRenderingJobHandler` → `SceneImageRecord.ErrorMessage`) as a clear user-facing message in the studio results strip in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [X] T054 [P] [US6] Unit test the content-policy clamp — `BuildMessages` with `SfwFiltered` + `AllowExplicitImage=true` produces an SFW instruction; `AdultAllowed` + `AllowExplicitImage=true` allows explicit; `ParseOutput` never returns empty in `DreamGenClone.Tests/RolePlay/SceneImagePromptPreprocessorTests.cs` (extend T046)

**Checkpoint**: US6 functional — content policy is enforced correctly across all three paths. P1 stories (US1, US2, US6, US7) all complete.

---

## Phase 7: User Story 3 - Iterate and refine (Priority: P2)

**Goal**: The user can regenerate (keeping previous versions), refine the prompt with an AI instruction, and the system prevents duplicate in-flight generation.

**Independent Test**: Render an image, edit the prompt, render again — confirm both versions are retained. Use "Refine prompt" — confirm the prompt updates. Attempt a duplicate generation — confirm it's prevented.

### Implementation for User Story 3

- [X] T055 [US3] Add a "Refine prompt" input + button to `SceneImageStudio.razor` that calls `EnqueuePromptAsync` with a `refineInstruction` threaded through to `SceneImagePromptPreprocessor.BuildMessages` (same prompt record, updated `OutputPrompt`) in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` and `DreamGenClone.Web/Application/RolePlay/SceneImagePromptPreprocessor.cs`
- [X] T056 [US3] Wire the "Regenerate" action on each image in the results strip to `EnqueueRenderAsync` with `RegenerateOfId` set to the parent image id (creates a new `SceneImageRecord`, never overwrites) in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [X] T057 [US3] Confirm dedupe prevents duplicate in-flight generation — the studio UI shows "Generating…" for an in-flight record and disables the render button until it completes (dedupe is already enforced by `dedupeKey` in T038; this task is the UI guard) in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`

**Checkpoint**: US3 functional — iteration works and preserves history.

---

## Phase 8: User Story 4 - See which moments have images (Priority: P2)

**Goal**: Interactions with images show an indicator with a count badge; interactions without images show no indicator.

**Independent Test**: Generate an image for an interaction — confirm the indicator with count appears. Confirm an interaction with no images shows no indicator.

### Implementation for User Story 4

- [X] T058 [US4] Load `CountImagesByInteractionAsync(sessionId)` on session load in `RolePlayWorkspace.razor` and render an image icon + count badge in the interaction header for interactions with count ≥ 1 in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T059 [US4] Refresh the count map via the existing polling loop (re-query when any image generation for the session is in flight) in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`

**Checkpoint**: US4 functional — indicator visible and accurate.

---

## Phase 9: User Story 5 - Browse all session images in a gallery (Priority: P2)

**Goal**: A separate per-session gallery page lists all images grouped by interaction, with full-size viewing and a link to the studio.

**Independent Test**: Generate images for multiple interactions — confirm the gallery lists them grouped by interaction, with full-size view and "Open in studio".

### Implementation for User Story 5

- [X] T060 [US5] Create `SceneImageGallery.razor` page at route `/roleplay/gallery/{sessionId}` — grid of all session images grouped by interaction (group header = interaction excerpt), click → lightbox full-size, "Open in studio" → `/roleplay/studio/{sessionId}/{interactionId}`, empty-state message when no images in `DreamGenClone.Web/Components/Pages/SceneImageGallery.razor`
- [X] T061 [US5] Add gallery links from the workspace header and the studio page header to `/roleplay/gallery/{sessionId}` in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` and `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [X] T062 [US5] Add a "Delete image" action in the gallery (calls `SceneImageService.DeleteImageAsync`, removes the row + file, refreshes the grid) in `DreamGenClone.Web/Components/Pages/SceneImageGallery.razor`

**Checkpoint**: US5 functional — gallery complete. All 7 user stories implemented.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Validation, edge cases, and the POC gates.

- [X] T063 [P] Handle edge cases in `SceneImagePromptPreprocessor` — empty/very short interaction (use scene context or explain), very long interaction (truncate to ~1200 chars input / ~800 chars guidance via `PromptTextTruncation`) in `DreamGenClone.Web/Application/RolePlay/SceneImagePromptPreprocessor.cs`
- [X] T064 [P] Handle edge cases in `SceneImageStudio.razor` — missing interaction (graceful message), missing session (fail fast before enqueue), delete confirmation in `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`
- [X] T065 Verify debug events (`SceneImagePromptSent` / `SceneImageResponseReceived`) are inspectable in the workspace Debug View and via `QuerySessionEventsAsync` in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` (Debug View integration)
- [X] T066 Run `dotnet build DreamGenClone.sln` and confirm 0 errors / 0 warnings
- [X] T067 Run the full test suite `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj` and confirm all tests pass (no pre-existing failures hidden, no new failures)
- [ ] T068 Run the POC validation checklist from `specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/quickstart.md` §6 — NSFW (filtered clamp / adult-allowed / unset policy), image quality across styles/sizes, basics (generate/edit/regenerate/indicator/gallery/delete), unconfigured guidance
- [ ] T069 Update backlog: B-032 state `new` → `designed` (design approved) then `planned` (tasks generated) in `specs/Planning/backlog.md`; record POC findings and decide Phase 2 (likeness) scope

---

## Phase 11: Manual Qwen Source-Image Editing (Approved Vertical Slice)

**Purpose**: Add a dedicated, manual source-image editing path without changing the existing Pony or SDXL text-to-image routes.

- [X] T070 Add the dedicated `IImageEditingClient` ComfyUI Qwen workflow client with source upload, prompt submission, history polling, and output download. Read all Qwen artifacts and sampler settings solely from `ResolvedImageEditorModel`.
- [X] T071 Add `SceneImageEditRequest`, edit payload/type/handler, DI registration, and `ISceneImageService.EnqueueEditAsync`; validate source existence, completion, session/interaction ownership, file path, and non-empty instruction before record creation.
- [X] T072 Update Model Manager add/details forms with all persisted Qwen artifact and sampler settings; filter `RolePlaySceneImageEditor` to image-kind models.
- [X] T073 Add a per-completed-image instruction/action in `SceneImageStudio.razor` that queues a manual edit only when explicitly invoked.
- [X] T074 Add focused workflow/service tests and validate with the full test suite.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1 (`PersistenceOptions.SceneImageRoot` for the storage service). **BLOCKS all user stories.**
- **User Story 7 (Phase 3, P1)**: Depends on Phase 2 (Model Manager image support). Must complete before US1 can be validated end-to-end (you need a configured image model to render).
- **User Story 1 (Phase 4, P1)**: Depends on Phase 2 (storage, persistence, image client, resolution) + Phase 3 (configured image model). **MVP.**
- **User Story 2 (Phase 5, P1)**: Depends on Phase 4 (the studio + pipeline exist). Extends the studio with editable-prompt + settings-aware regeneration.
- **User Story 6 (Phase 6, P1)**: Depends on Phase 4 (preprocessor + studio exist). Adds the policy clamp + UI guards.
- **User Story 3 (Phase 7, P2)**: Depends on Phase 4 (pipeline) + Phase 5 (editable prompt). Adds refine + regenerate + dedupe UI.
- **User Story 4 (Phase 8, P2)**: Depends on Phase 2 (repository count query) + Phase 4 (images exist). Adds the workspace indicator.
- **User Story 5 (Phase 9, P2)**: Depends on Phase 2 (repository list query) + Phase 4 (images exist). Adds the gallery page.
- **Polish (Phase 10)**: Depends on all desired user stories being complete.

### User Story Independence

- **US7 (P1)**: Independently testable — configure a provider/model in the Model Manager UI.
- **US1 (P1)**: Independently testable — generate one image, reopen the studio, confirm it persists. (Requires US7-configured model to validate end-to-end, but the code is independent.)
- **US2 (P1)**: Independently testable — generate a prompt, edit it, render, confirm the image differs.
- **US6 (P1)**: Independently testable — request explicit from a filtered provider, confirm clamp/explain.
- **US3 (P2)**: Independently testable — render, regenerate, confirm both kept.
- **US4 (P2)**: Independently testable — generate an image, confirm the indicator appears.
- **US5 (P2)**: Independently testable — generate images, open the gallery, confirm grouping.

### Within Each User Story

- Interfaces/DTOs before implementations (foundational phase handles this).
- Domain entities before repositories (foundational phase handles this).
- Services before job handlers before UI pages.
- Core implementation before edge-case handling.
- Story complete (build + tests green) before moving to the next priority.

### Parallel Opportunities

- **Phase 2**: T002–T019 (enums, records, entities, interfaces, DTOs) are all [P] — different files, no dependencies. T020–T027 (migration, repos, client, resolution, DI, settings) depend on the domain types but can be parallelized across files once T002–T011 exist. T029–T032 (tests) are [P] and can run alongside their implementation targets.
- **Phase 4**: T037 (preprocessor), T039/T040 (payloads), T046/T047 (tests) are [P].
- **Phase 6**: T054 (policy-clamp test) is [P].
- **Phase 10**: T063/T064 (edge cases) are [P] (different files).

---

## Parallel Example: Foundational Phase (Phase 2)

```bash
# Launch all domain types in parallel (T002–T011):
Task: "Create ImageProviderCapability enum in DreamGenClone.Domain/ModelManager/ImageProviderCapability.cs"
Task: "Create ImageContentPolicy enum in DreamGenClone.Domain/ModelManager/ImageContentPolicy.cs"
Task: "Create ModelKind enum in DreamGenClone.Domain/ModelManager/ModelKind.cs"
Task: "Create ResolvedImageModel record in DreamGenClone.Domain/ModelManager/ResolvedImageModel.cs"
Task: "Append AppFunction enum members in DreamGenClone.Domain/ModelManager/AppFunction.cs"
Task: "Add Provider image properties in DreamGenClone.Domain/ModelManager/Provider.cs"
Task: "Add RegisteredModel image properties in DreamGenClone.Domain/ModelManager/RegisteredModel.cs"
Task: "Create SceneImagePromptRecord entity in DreamGenClone.Domain/RolePlay/SceneImagePromptRecord.cs"
Task: "Create SceneImageRecord entity in DreamGenClone.Domain/RolePlay/SceneImageRecord.cs"

# Then the interfaces/DTOs in parallel (T012–T019):
Task: "Create IImageGenerationClient in DreamGenClone.Application/Abstractions/IImageGenerationClient.cs"
Task: "Create ISceneImageRepository in DreamGenClone.Application/Abstractions/ISceneImageRepository.cs"
Task: "Create ISceneImageStorageService in DreamGenClone.Application/Abstractions/ISceneImageStorageService.cs"
Task: "Create ISceneImageService in DreamGenClone.Application/Abstractions/ISceneImageService.cs"
Task: "Create ISceneImagePromptPreprocessor in DreamGenClone.Application/Abstractions/ISceneImagePromptPreprocessor.cs"
Task: "Create SceneImageStudioSettings DTO in DreamGenClone.Application/RolePlay/Models/SceneImageStudioSettings.cs"
Task: "Create ScenePromptRequest DTO in DreamGenClone.Application/RolePlay/Models/ScenePromptRequest.cs"
Task: "Create SceneRenderRequest DTO in DreamGenClone.Application/RolePlay/Models/SceneRenderRequest.cs"

# Then the infra impls + tests in parallel:
Task: "Implement SceneImageStorageService in DreamGenClone.Infrastructure/Storage/SceneImageStorageService.cs"
Task: "Implement SceneImageRepository in DreamGenClone.Infrastructure/RolePlay/SceneImageRepository.cs"
Task: "Implement ImageGenerationClient in DreamGenClone.Infrastructure/Models/ImageGenerationClient.cs"
```

---

## Implementation Strategy

### MVP First (US7 → US1)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002–T032) — **CRITICAL, blocks all stories**
3. Complete Phase 3: US7 — Configure image capability (T033–T036)
4. Complete Phase 4: US1 — Generate an image (T037–T047)
5. **STOP and VALIDATE**: Test US1 end-to-end with a real image provider (the POC). This is the gating MVP.

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US7 (configure) → Test independently (Model Manager UI)
3. US1 (generate) → Test independently (POC end-to-end) → **MVP delivered**
4. US2 (edit prompt) → Test independently
5. US6 (content policy) → Test independently (NSFW validation — a POC gate)
6. US3 (iterate) → Test independently
7. US4 (indicator) → Test independently
8. US5 (gallery) → Test independently
9. Polish (edge cases, build, full test suite, POC checklist, backlog update)

### Parallel Team Strategy

With multiple developers:
1. Team completes Setup + Foundational together (heavy parallelism in Phase 2).
2. Once Foundational is done:
   - Developer A: US7 (Model Manager UI) + US1 (studio + pipeline)
   - Developer B: US6 (content policy — preprocessor clamp + UI guards)
   - Developer C: US4 (indicator) + US5 (gallery)
3. US2 and US3 depend on US1's studio, so they follow US1.
4. Stories integrate independently — each is a self-contained slice.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- [Story] label maps each task to its user story for traceability.
- Each user story is independently completable and testable.
- Verify build + tests are green before marking a task complete (repo hard rule: no failing tests left).
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently.
- The RP engine's core text-generation path (`RolePlayEngineService`, `RolePlayContinuationService`, `RolePlayPromptComposer`, prompt slots) is **NOT modified** by any task — image generation is additive.
- The no-fallback rule applies to every resolution path: missing image model / non-image model / non-image provider / `ContentPolicy == Unknown` all fail fast with explicit diagnostics (T025, T041, T042).
- Image files live on disk under `data/scene-images/` (git-ignored) — never commit image bytes; only metadata goes in SQLite.
