# Implementation Plan: Scene Image Generator

**Branch**: `001-scene-image-generator` | **Date**: 2026-08-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/spec.md`

## Summary

Add a scene image generation engine to DreamGenClone. The user selects a narrative interaction in the RP workspace, clicks **Generate image**, and a dedicated **Image Studio** page opens (`/roleplay/studio/{sessionId}/{interactionId}`). The studio runs a two-stage pipeline: (1) a **pre-processor LLM** (new `AppFunction.RolePlaySceneImagePreprocessor`) consumes the interaction, the scene's atmosphere (setting, time of day, phase, characters, resolved intensity), and the user's image settings (style, size, explicitness) to produce an **editable image prompt**; (2) an **image generation model** (new `AppFunction.RolePlaySceneImage`) renders the prompt into an image saved to disk and persisted in SQLite. Both stages are queued as background jobs (reusing the existing `GenericBackgroundJobQueue`) so the UI never blocks. The studio supports iterative refinement (edit prompt, change settings, "Refine prompt" with AI, regenerate — each render is a distinct saved version). Interactions with images show an **indicator** with a count badge in the workspace; a separate **per-session gallery** page (`/roleplay/gallery/{sessionId}`) lists all images grouped by interaction. Provider integration extends the existing Model Manager with image capability on providers/models and a first-class **content policy** (SFW-filtered vs adult-allowed) that gates explicit generation — never bypassing a provider's filter, never assuming a default policy. **Phase 1 = all plumbing + a POC** to validate NSFW behavior, image quality, and the basic flow with a real image-capable provider (expected: Together AI FLUX.1). Character likeness from reference images on character profiles is a later roadmap phase.

### Continuity Roadmap Amendment (2026-08-24)

The prompt-to-image POC is necessary plumbing but is not the final rendering architecture. Live Juggernaut testing proved that prose and fixed seeds do not reliably preserve asymmetric touch, exact blocking, object anchors, or one frozen moment across POVs. Phase 2 therefore adopts `continuity-rendering-architecture.md` as the controlling design:

- compile beat metadata into a persisted, camera-independent canonical visual plan;
- condition recurring identities and locations with persisted visual assets;
- use pose/depth/mask controls for geometry instead of relying on prompt wording;
- derive every POV shot from one frozen scene plan;
- persist complete workflow/control provenance;
- validate explicit constraints and apply bounded, targeted repair;
- fail fast when a controlled shot is missing required configuration, with no text-only fallback.

The earlier implementation gate was a standalone four-seed Juggernaut + SDXL ControlNet proof for one clothed, one-way hand-on-chest contact. That OpenPose/inpainting route was tested and rejected. Qwen Image Edit 2511 is now the selected semantic editing mechanism; its proof is recorded in `artifacts/tmp/images/qwen-simple-people-proof/`. No continuity app integration begins until the identity, location, control, and validation contracts are defined and tested.

## Technical Context

**Language/Version**: C# / .NET 9.0
**Primary Dependencies**: ASP.NET Core Blazor Server (interactive server rendering), Microsoft.Data.Sqlite, Serilog, `Microsoft.Extensions.FileProviders.Physical` (static-file serving for generated images), `System.Net.Http.Json` (image API client — same stack as `CompletionClient`)
**Storage**: SQLite for metadata (`SceneImagePrompts`, `SceneImages` tables; additive columns on `Providers`/`RegisteredModels`); local disk under `data/scene-images/` (git-ignored, alongside `data/dreamgenclone.dev.db`) for the rendered image files themselves — per the repo DB-snapshot model, files stay out of the DB and out of git
**Testing**: xUnit + Coverlet (existing `DreamGenClone.Tests` project)
**Target Platform**: Windows (local single-user desktop; DPAPI API-key encryption is Windows-only, inherited from existing Model Manager)
**Project Type**: Web application (Blazor Server) — additive feature across the existing 4-project layered architecture (Domain / Application / Infrastructure / Web)
**Performance Goals**: Image generation call latency bounded by the provider's per-provider timeout (default 120s local, 30s cloud — same as chat completions); background-queued so the UI never blocks; polling interval reuses the existing workspace polling cadence
**Constraints**: No hardcoded image model (Model Manager resolution, fail-fast); no fallback image model; explicit content policy required (no silent SFW assumption); image files must not be committed to git (only the sanitized snapshot DB is tracked); RP engine's core text-generation path (`RolePlayEngineService`, `RolePlayContinuationService`, `RolePlayPromptComposer`, prompt slots) must NOT be modified — image generation is additive and manual-only
**Scale/Scope**: Single user, 1–3 image-capable providers, ~5–10 image models, 2 new function defaults (preprocessor + renderer); per-session image counts expected in the low dozens

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow)
  - The existing RP core flow is untouched. Image generation is an additive, opt-in feature; LM Studio can be configured as an image provider if it exposes an image endpoint. Cloud image providers (Together AI, OpenRouter) are optional, same as today's chat providers. The feature fails fast with guidance when no image provider is configured — it does not block the core RP flow.
- [x] Module boundaries and adapter seams are explicit and swappable
  - New `IImageGenerationClient` (OpenAI-compatible `/v1/images/generations`) mirrors the existing `ICompletionClient` seam. `ISceneImageService`, `ISceneImagePromptPreprocessor`, `ISceneImageRepository`, `ISceneImageStorageService` are explicit interfaces. The image provider adapter is swappable via the Model Manager, consistent with constitution principles II and IV (LLM-/model-agnostic boundary).
- [x] .NET layered architecture uses separate projects with enforced dependency direction
  - Domain entities/enums/records in `DreamGenClone.Domain`; application interfaces and orchestration services in `DreamGenClone.Web/Application` (and `DreamGenClone.Application` where applicable); infrastructure implementations (`ImageGenerationClient`, `SceneImageRepository`, `SceneImageStorageService`) in `DreamGenClone.Infrastructure`; UI pages in `DreamGenClone.Web/Components/Pages`. Dependency direction: Web → Application → Domain; Infrastructure → Application/Domain. No new project needed.
- [x] Deterministic state transitions and JSON contract validation are test-covered
  - `SceneImageStatus` (Pending → Generating → Complete/Failed) and `SceneImagePromptStatus` (Pending → Complete/Failed) transitions are deterministic and unit-tested. The preprocessor's LLM output is parsed with a documented contract (plain-text prompt with optional JSON envelope fallback) and validated before persistence. Status transitions are driven by the background job handlers with explicit `UpdatedUtc` stamps.
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale
  - Metadata in SQLite (`SceneImagePrompts`, `SceneImages` tables — FR-015). **Exception**: rendered image bytes are stored on local disk under `data/scene-images/`, not in SQLite. Rationale: image files are 0.5–4 MB each and would balloon the dev DB (the repo DB-snapshot model explicitly keeps large blobs out of SQLite — see `.github/instructions/db-snapshot-workflow.instructions.md`); metadata rows stay small. Lifecycle: files persist until the user deletes the image record (delete removes both the row and the file); the directory is git-ignored so files are never committed.
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices
  - All new services/handlers/client use `ILogger<T>` with Serilog structured message templates (FR-016). Major paths (prompt generation, render start/complete/failed, provider call, file save, deletion, resolution failures) emit `LogInformation`/`LogWarning`/`LogError` with contextual properties (`SessionId`, `InteractionId`, `ImageId`, `ModelIdentifier`, `ProviderName`, `DurationMs`, `ReasonCode`).
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths
  - FR-017 covers the two job handlers, the service enqueue paths, the image client call, storage save/delete, and the two resolution methods. Debug events (`SceneImagePromptSent` / `SceneImageResponseReceived`) flow through the existing `RolePlayDebugEventRecord` pipeline for prompt inspection.
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes
  - FR-018 — no changes to the existing Serilog configuration mechanism; new loggers inherit the app's configured minimum level.

**Gate result: PASS** — No violations detected. The disk-storage exception for image bytes is documented above per constitution principle VIII.

## Project Structure

### Documentation (this feature)

```text
specs/Planning/B-032-scene-image-generator/phase-1-prompt-to-image-mvp/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── scene-image-pipeline-contract.md
└── tasks.md             # Phase 2 output (via /speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
├── ModelManager/
│   ├── AppFunction.cs                    # [MODIFIED] +2 enum members
│   ├── ImageProviderCapability.cs        # NEW enum: None/TextAndImage/ImageOnly
│   ├── ImageContentPolicy.cs             # NEW enum: Unknown/SfwFiltered/AdultAllowed/AdultAllowedConfigurable
│   ├── ModelKind.cs                      # NEW enum: Text/Image
│   ├── ResolvedImageModel.cs             # NEW record (mirrors ResolvedModel)
│   ├── Provider.cs                       # [MODIFIED] +ImageCapability/ImageGenerationPath/ContentPolicy
│   └── RegisteredModel.cs                # [MODIFIED] +ModelKind/ImageSizeSupported
└── RolePlay/
    ├── SceneImagePromptRecord.cs         # NEW entity + SceneImagePromptStatus enum
    └── SceneImageRecord.cs               # NEW entity + SceneImageStatus enum

DreamGenClone.Application/  (interfaces live here per layered architecture)
├── Abstractions/
│   ├── IImageGenerationClient.cs         # NEW seam (OpenAI-compatible images endpoint)
│   ├── ISceneImageRepository.cs          # NEW repository interface
│   ├── ISceneImageStorageService.cs      # NEW storage interface
│   ├── ISceneImageService.cs             # NEW public service interface
│   └── ISceneImagePromptPreprocessor.cs  # NEW preprocessor interface
└── RolePlay/
    └── Models/
        ├── SceneImageStudioSettings.cs   # NEW DTO (style/size/aspect/explicitness)
        ├── ScenePromptRequest.cs         # NEW DTO
        └── SceneRenderRequest.cs         # NEW DTO

DreamGenClone.Infrastructure/
├── Models/
│   └── ImageGenerationClient.cs          # NEW: POST /v1/images/generations, b64 decode, error mapping
├── Storage/
│   └── SceneImageStorageService.cs       # NEW: mirrors TemplateImageStorageService
├── RolePlay/  (or Persistence/)
│   └── SceneImageRepository.cs           # NEW: SQLite CRUD for both tables + counts
├── Configuration/
│   └── PersistenceOptions.cs            # [MODIFIED] +SceneImageRoot
└── Persistence/
    └── SqlitePersistence.cs             # [MODIFIED] migration + repo mapping helpers

DreamGenClone.Web/
├── Application/
│   ├── ModelManager/
│   │   └── ModelResolutionService.cs    # [MODIFIED] +ResolveImageModelAsync / ResolveImagePromptModelAsync
│   ├── BackgroundJobs/
│   │   └── BackgroundJobTypes.cs        # [MODIFIED] +2 consts
│   └── RolePlay/
│       ├── SceneImageService.cs          # NEW: enqueue/list/get/delete/counts
│       ├── SceneImagePromptPreprocessor.cs  # NEW: LLM prompt builder + output parse + SFW clamp
│       ├── SceneImagePromptGenerationJobHandler.cs    # NEW IBackgroundJobHandler
│       ├── SceneImageRenderingJobHandler.cs           # NEW IBackgroundJobHandler
│       ├── SceneImagePromptGenerationJobPayload.cs    # NEW
│       └── SceneImageRenderingJobPayload.cs           # NEW
├── Components/Pages/
│   ├── SceneImageStudio.razor            # NEW: /roleplay/studio/{sessionId}/{interactionId}
│   ├── SceneImageGallery.razor           # NEW: /roleplay/gallery/{sessionId}
│   ├── ModelManager.razor                # [MODIFIED] image capability fields + 2 function rows
│   └── RolePlayWorkspace.razor           # [MODIFIED] "Generate image" trigger + indicator
├── Domain/RolePlay/
│   └── WorkspaceSettingsState.cs         # [MODIFIED] +image settings
└── Program.cs                            # [MODIFIED] DI registrations + static-file serving

DreamGenClone.Tests/RolePlay/  (new test files)
├── SceneImageResolutionTests.cs
├── SceneImagePromptPreprocessorTests.cs
├── SceneImageGenerationClientTests.cs
├── SceneImageServiceJobTests.cs
├── SceneImageRepositoryTests.cs
└── SceneImageStorageServiceTests.cs
```

**Structure Decision**: Reuse the existing 4-project layered architecture (Domain / Application / Infrastructure / Web) — no new project. Domain owns entities/enums/records; Application owns interfaces and orchestration; Infrastructure owns the HTTP client, SQLite repository, and disk storage; Web owns the pages, service implementations, job handlers, and DI wiring. This matches the constitution's module-boundary principle (II) and the existing `TemplateImageStorageService` / `CompletionClient` precedents.

## Complexity Tracking

No constitution violations to justify. The disk-storage exception for image bytes is documented inline in the Constitution Check (principle VIII) and is consistent with the repo's existing DB-snapshot model — not a complexity escalation.

---

## Post-Design Constitution Re-evaluation (Phase 1 complete)

Re-checked after the design artifacts (`research.md`, `data-model.md`, `contracts/scene-image-pipeline-contract.md`, `quickstart.md`) were produced.

- [x] **Local-first runtime preserved** — Image generation is additive and opt-in; the RP core flow is untouched (explicitly listed in Constraints and in the "Explicitly NOT modified" section of the design draft). LM Studio can serve as an image provider if it exposes an image endpoint; cloud image providers are optional. The feature fails fast with guidance when unconfigured — it never blocks the core RP flow.
- [x] **Module boundaries explicit and swappable** — The contract document defines clean seams: `IImageGenerationClient` (mirrors `ICompletionClient`), `ISceneImageService`, `ISceneImagePromptPreprocessor`, `ISceneImageRepository`, `ISceneImageStorageService`. Provider-specific concerns (base URL, auth, image path, content policy) live in the resolved `ResolvedImageModel` value object and the provider record — not in core logic. Swapping the image provider is a Model Manager config change, not a code change.
- [x] **Layered architecture enforced** — The Project Structure maps each new file to exactly one project by responsibility: Domain (entities/enums/records), Application (interfaces + DTOs), Infrastructure (HTTP client + SQLite repo + disk storage), Web (pages + service impls + job handlers + DI). Dependency direction is unchanged and enforced by existing project references.
- [x] **Deterministic state transitions + JSON contract validation** — `data-model.md` documents the monotonic `SceneImageStatus` and `SceneImagePromptStatus` state machines with Mermaid diagrams; transitions are worker-driven with explicit timestamps. The preprocessor output contract (`contracts/scene-image-pipeline-contract.md` §4) enforces JSON-in/JSON-out with a tolerant envelope-and-plain-text-fallback parser that **fails fast on empty/overlong output** (constitution principle V — never silently degrade). Unit tests cover all transitions and the parser's happy/fallback/fail paths.
- [x] **SQLite-default persistence (with documented exception)** — Metadata in SQLite (`SceneImagePrompts`, `SceneImages`); image bytes on disk under `data/scene-images/` (git-ignored). The exception is documented inline in the Constitution Check above and in `data-model.md` §SQLite Schema Changes, with rationale (DB-snapshot model keeps multi-MB blobs out of SQLite) and lifecycle (delete removes row + file). Consistent with the existing `TemplateImageStorageService` precedent.
- [x] **Serilog structured logging** — `contracts/scene-image-pipeline-contract.md` §6 defines the debug-event kinds and the structured `MetadataJson` fields; the Technical Context enumerates the `ILogger<T>` properties (`SessionId`, `InteractionId`, `ImageId`, `ModelIdentifier`, `ProviderName`, `DurationMs`, `ReasonCode`). All new services/handlers/client use `ILogger<T>`.
- [x] **Logging coverage across layers** — FR-017 covers both job handlers, both enqueue paths, the image client call, storage save/delete, and both resolution methods. The two debug events give per-session UI inspection on top of Serilog logs.
- [x] **Configurable log levels** — No changes to the Serilog configuration mechanism; new loggers inherit the app's configured minimum level (FR-018).

**Post-design gate result: PASS** — All eight constitution principles are satisfied by the design. No violations; no complexity-tracking entries needed. The feature is ready for `/speckit.tasks` to produce the task breakdown.
