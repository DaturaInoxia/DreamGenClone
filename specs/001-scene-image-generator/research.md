# Research: Scene Image Generator

**Feature**: 001-scene-image-generator | **Date**: 2026-08-19

This document resolves the open questions from the design draft (`specs/Planning/B-032-scene-image-generator.md` §14) and records the technical decisions grounding the plan. Each entry follows the spec-kit research format: Decision → Rationale → Alternatives considered.

---

## Decision Log

### R-001: Image API protocol — OpenAI-compatible `/v1/images/generations`

**Decision**: Use the OpenAI-compatible images-generations endpoint (`POST /v1/images/generations`) with a single unified client, mirroring the existing `CompletionClient` pattern.

**Rationale**: The existing Model Manager already supports three OpenAI-compatible providers (LM Studio, Together AI, OpenRouter) on the same `/v1/chat/completions` wire protocol. Together AI additionally exposes `/v1/images/generations` (used by FLUX.1 Schnell/Dev) with an OpenAI-compatible request body (`{ model, prompt, n, size, response_format, steps }`) and response (`{ data: [{ b64_json | url }] }`). A single `IImageGenerationClient` that reads the base URL, image path, auth header, and timeout from the resolved provider record eliminates per-provider code duplication and keeps the provider swappable — directly satisfying constitution principle IV (LLM-/model-agnostic boundary). The only provider-specific variation is `Authorization: Bearer {key}` for cloud vs. no-auth for local, which is already handled by `CompletionClient` and reuses the same DPAPI-encrypted key flow.

**Alternatives considered**:
- Per-provider image clients (TogetherFluxClient, OpenAiImageClient, …): Rejected — the wire protocol is identical; would create near-duplicate classes and a wider DI graph, contradicting the existing unified-client precedent.
- Raw `HttpClient` calls inline in the job handler: Rejected — violates the adapter-seam principle and makes the provider untestable in isolation.
- A generic "media generation" client covering both images and audio/video: Rejected as speculative scope creep; v1 is images-only.

---

### R-002: Two functions vs. one — preprocessor + renderer

**Decision**: Add two new `AppFunction` values: `RolePlaySceneImagePreprocessor` (text LLM) and `RolePlaySceneImage` (image model). They are independently configurable in the Model Manager.

**Rationale**: The preprocessor is a **text** completion call (chat completions, reusing `ResolvedModel` + `ICompletionClient`), while the renderer is an **image** generation call (images endpoint, new `ResolvedImageModel` + `IImageGenerationClient`). They have different wire protocols, different parameter sets (temperature/top-p vs. size/steps/response_format), and different content-policy implications. Splitting them into two functions lets the user pick a strong reasoning model for prompt drafting (e.g. DeepSeek) and a separate image model (e.g. FLUX.1) independently — and lets the no-fallback rule apply to each stage: if the preprocessor is unconfigured, the prompt stage fails fast with guidance; if the renderer is unconfigured, the render stage fails fast. This matches how the existing `RolePlaySemanticAnalysis` / `RolePlayAssistant` functions are separately assignable.

**Alternatives considered**:
- A single `RolePlaySceneImage` function covering both stages with one model: Rejected — a text model cannot serve the images endpoint, and an image model cannot draft a prompt. One function would force a single model to do two incompatible jobs.
- Hardcode the preprocessor to reuse `RolePlayGeneration` (the main RP model): Rejected — violates the no-hardcoded-default rule (the RP engine config must not be coupled to image-gen config) and gives the user no control over prompt-drafting quality.

---

### R-003: Content policy as a provider capability, not a per-request flag

**Decision**: `ImageContentPolicy` is a property of `Provider` (Unknown / SfwFiltered / AdultAllowed / AdultAllowedConfigurable), resolved at generation time. The studio's `AllowExplicitImage` toggle is honored **only** when the resolved provider's policy is adult-allowed; otherwise the preprocessor deterministically clamps to SFW and the attempt is logged.

**Rationale**: NSFW filtering is a property of the **provider account/tier**, not something the user can override per request — Together AI's default tier filters adult content regardless of what the prompt says. Modeling it as a provider capability keeps the source of truth in one place (Model Manager), makes the no-fallback rule apply (Unknown policy → fail fast, never assume SFW), and gives the UI a clear signal for when to disable the explicitness toggle. The deterministic SFW clamp (rather than silently dropping the request) ensures the user always gets *something* or a clear explanation — satisfying FR-011 and SC-004. Logging the clamp (not silently skipping) preserves auditability.

**Alternatives considered**:
- Per-request content rating on the image record only: Rejected — would let the user request explicit content from a filtered provider, producing a confusing policy-rejection error every time; the clamp prevents wasted calls.
- A global app-level "allow NSFW" setting: Rejected — couples content policy to app config rather than the provider that actually enforces it; also a hidden default, which the repo forbids.
- Silent skip when explicit requested on a filtered provider: Rejected — violates FR-011 ("never silently bypass") and SC-004 (0% bypass). The clamp-or-explain contract is explicit.

---

### R-004: Disk storage for image bytes (exception to SQLite-default)

**Decision**: Store rendered image bytes on local disk under `data/scene-images/{sessionId}/{imageId}.png`; store only metadata in SQLite (`SceneImagePrompts`, `SceneImages`).

**Rationale**: Constitution principle VIII requires SQLite by default **unless** the spec documents an exception. The spec's FR-015 keeps metadata in SQLite; the exception is justified because image files are 0.5–4 MB each and the repo's DB-snapshot model explicitly keeps large blobs out of SQLite (see `.github/instructions/db-snapshot-workflow.instructions.md` — the dev DB already balloons from 600 KB prompt JSON; multi-MB image blobs would make it unworkable and would also end up in the git-tracked snapshot). Metadata rows stay small (<1 KB). The storage service mirrors the existing `TemplateImageStorageService` (root from `PersistenceOptions`, `SaveAsync`/`OpenReadAsync`), so the pattern is already proven. Files are served via a dedicated `UseStaticFiles` branch at `/scene-images` (kept out of `wwwroot` so build output never includes them) and are git-ignored alongside `dev.db`. Delete removes both the row and the file.

**Alternatives considered**:
- Store image blobs as BLOB columns in SQLite: Rejected per the DB-snapshot model — would explode DB size and git snapshot size.
- Store images in `wwwroot`: Rejected — `wwwroot` is committed to git; generated runtime images must never be committed.
- External object storage (S3, etc.): Rejected — violates local-first (principle I) and adds a cloud dependency for an inherently local feature.

---

### R-005: Background jobs for both stages (not synchronous UI calls)

**Decision**: Both the preprocessor and the renderer run as background jobs on the existing `GenericBackgroundJobQueue` (new `SceneImagePromptGeneration` and `SceneImageRendering` job types). The studio polls status.

**Rationale**: Image generation is slow (seconds to ~30s) and the preprocessor adds another LLM round-trip. Running them synchronously on the Blazor circuit would block the UI thread and risk circuit timeouts. The repo already has a proven background-job pattern (`GenericBackgroundJobQueue` + `IBackgroundJobHandler` + `GenericBackgroundJobWorker`, used by semantic analysis / encounter summary / location detection / steer generation). Reusing it gives: unbounded `Channel` queue, dedupe by key (prevents duplicate in-flight generation for the same record), worker-driven status transitions, and the existing `RolePlayDebugEventRecord` pipeline for prompt/response inspection. This satisfies FR-003's two-action contract and the "scheduling/queuing" requirement from the backlog without introducing new infrastructure.

**Alternatives considered**:
- Synchronous calls from the Blazor component with `await`: Rejected — blocks the circuit; a 30s image call risks timeout and freezes the UI.
- A dedicated image-generation queue/worker (like `SemanticBackgroundJobQueue`): Rejected as premature — the generic queue already dedupes and serializes; a dedicated queue is only warranted if image jobs starve other job types (not expected at single-user scale).
- `IHostedService` with a `Timer`: Rejected — no dedupe, no payload, harder to inspect.

---

### R-006: Per-interaction gallery grouping (not cross-session)

**Decision**: The gallery is a per-session page at `/roleplay/gallery/{sessionId}`, grouping images by interaction.

**Rationale**: The spec's assumptions explicitly scope the gallery to per-session in v1. A single session's images are the unit a user browses while reviewing a story; cross-session browsing adds pagination/search complexity that doesn't serve the POC. Grouping by interaction (with the interaction excerpt as the group header) mirrors how the user thinks about the images ("the moment where…") and matches the studio's per-interaction organization. The `SceneImages` table indexes on `(SessionId, InteractionId)` to make the grouped query cheap.

**Alternatives considered**:
- Global cross-session gallery with a session filter: Rejected — out of v1 scope (spec assumption); defer to the roadmap phase.
- No gallery, only the studio: Rejected — the spec's User Story 5 requires a dedicated gallery surface (FR-009, SC-007).

---

### R-007: Editable prompt stored as a first-class record (with render snapshots)

**Decision**: Persist the editable prompt as a `SceneImagePromptRecord` (one per generation/refine attempt per interaction). Each render creates a `SceneImageRecord` that references the prompt record **and** stores a `PromptSnapshot` of the exact text sent to the image model (so edits between render and audit are preserved).

**Rationale**: The two-stage pipeline has an editable middle: the user can edit the prompt after the preprocessor produces it and before rendering. Persisting the prompt as its own record makes "Refine prompt" cheap (another preprocessor call on the same record), makes the prompt inspectable in the studio on reopen, and lets the debug pipeline show the preprocessor's output. The `PromptSnapshot` on the image record guarantees that regenerating or auditing an image reproduces exactly what was sent — even if the user later edits the prompt record. This satisfies FR-014 (record exact prompt + settings + provider/model + status per image) and FR-006 (each render is a distinct saved version).

**Alternatives considered**:
- Store only the final prompt on the image record (no prompt record): Rejected — loses the editable-draft state and makes "Refine prompt" need to regenerate from scratch; also loses the preprocessor-output audit trail.
- Versioned prompt history (a list of prompt versions per interaction): Rejected for v1 — adds complexity; a single editable record per attempt is enough for the POC. Versioning is a roadmap candidate.
- Overwrite-in-place on regenerate: Rejected — violates FR-006 ("regenerating MUST NOT overwrite previous versions") and SC-008.

---

### R-008: Static-file serving via a dedicated `/scene-images` branch

**Decision**: Serve generated images via `app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(sceneImageRoot), RequestPath = "/scene-images" })`, separate from `wwwroot`.

**Rationale**: `wwwroot` is committed to git and built into the publish output — generated runtime images must never land there. A dedicated `PhysicalFileProvider` rooted at `data/scene-images/` (git-ignored, alongside `dev.db`) keeps the images out of git and out of build artifacts while still letting the browser fetch them by URL (`/scene-images/{sessionId}/{imageId}.png`). This is the standard ASP.NET Core pattern for serving generated content and avoids writing a custom controller/endpoint just for image bytes.

**Alternatives considered**:
- A minimal API endpoint (`app.MapGet("/scene-images/{*path}", …)`) that streams from disk: Works but reinvents `UseStaticFiles` (caching headers, range requests, MIME mapping) for no benefit.
- Embed images as base64 data URIs in the rendered HTML: Rejected — bloats the Blazor render tree and the DB-snapshot-unfriendly payload; also breaks the gallery's lazy loading.
- Serve from `wwwroot`: Rejected — commits images to git (FR violation of the DB-snapshot model).

---

### R-009: Debug visibility via the existing `RolePlayDebugEventRecord` pipeline

**Decision**: Emit two debug event kinds — `SceneImagePromptSent` (preprocessor system+user prompt + resolved policy + settings) and `SceneImageResponseReceived` (raw preprocessor output / image model response summary + status) — through the existing `IRolePlayDebugEventSink` / `RolePlayDebugEventRecord` pipeline.

**Rationale**: The repo already has a mature debug-event pipeline used by the semantic engine, encounter detection, and steer generation (`RolePlayDebugEventService`, `QuerySessionEventsAsync`, the workspace Debug View). Reusing it means image-gen prompts are inspectable in the same place as RP prompts, with no new infrastructure. This satisfies FR-014's audit requirement and gives the POC a built-in way to validate prompt shape and NSFW behavior (SC-004, SC-005). Events are gated behind the same `RolePlayFeatureFlags`-style flag if needed.

**Alternatives considered**:
- A separate image-gen debug log file: Rejected — fragments debugging across two surfaces; the existing pipeline already handles per-session event queries.
- Only Serilog logs (no debug events): Rejected — logs are aggregated, not per-session-queryable in the UI; the Debug View is the established way to inspect prompts in this repo.

---

### R-010: Preprocessor output contract — plain-text prompt with JSON-envelope fallback

**Decision**: The preprocessor is instructed to return the image prompt as plain text. The parser first tries to detect a JSON envelope (`{ "prompt": "...", "excerpt": "..." }`); if absent, it treats the entire output as the prompt string. Either way the result is validated (non-empty, length-capped) before persistence.

**Rationale**: Constitution principle V (JSON-in/JSON-out contract enforcement) favors structured output, but image prompts are free-form text and forcing strict JSON risks model refusals or escaping noise on long prompts. A tolerant parser (try-JSON-else-plain-text) keeps the contract for the structured fields we care about (`excerpt`) while degrading gracefully to plain text — but **never** to untyped/empty output: an empty or over-long result fails fast with an explicit error (FR-014, principle V's "fail fast, never silently degrade"). The debug event records whichever form was produced so the POC can evaluate which the chosen preprocessor model produces reliably.

**Alternatives considered**:
- Strict JSON-only contract: Rejected — image prompts frequently contain quotes/commas that trip fragile JSON; a strict requirement would raise parse-failure rates on the POC.
- Plain-text-only (no envelope): Rejected — loses the "pulled excerpt" field that the studio highlights; the envelope is cheap to support and useful for the source-passage display.

---

### R-011: Iteration UX — separate "Generate Prompt" / "Render Image" / "Refine prompt"

**Decision**: The studio exposes three explicit actions: **Generate Prompt** (preprocessor → editable textarea), **Refine prompt** (preprocessor with a user instruction → updates the textarea), **Render Image** (image model from the current textarea text). Each render is a new `SceneImageRecord`.

**Rationale**: The spec's User Story 2 and 3 require an editable prompt and iteration. Separate buttons give the user explicit control over each stage (per the planning decision D5) — they can generate a prompt, edit it, render, then either edit again or ask the AI to refine, without an accidental full re-run. "Refine prompt" is the "help from another model" path described in the original request: it reuses the preprocessor function with a different instruction framing (same model, cheaper than re-rendering). Keeping each render as a distinct record (R-007) satisfies SC-008 (regenerate never loses the previous version). Dedupe on the queue (R-005) prevents duplicate in-flight renders of the same record.

**Alternatives considered**:
- A single "Generate" button that runs both stages: Rejected per D5 — removes the editable-prompt middle stage that the spec's User Story 2 requires.
- "Refine" that re-renders automatically: Rejected — the user should see and approve the refined prompt before spending an image-generation call.

---

### R-012: Workspace indicator via a per-interaction count query

**Decision**: The workspace loads a `Dictionary<string,int>` of interaction→image-count via `ISceneImageRepository.CountImagesByInteractionAsync(sessionId)` on session load and refreshes it via the existing polling loop. Interactions with count ≥ 1 render an image icon + count badge.

**Rationale**: The spec's User Story 4 (FR-008, SC-002) requires a visible indicator with a count. A single grouped-count query (indexed on `SessionId, InteractionId`) is cheaper than loading all image rows just to show a badge, and it avoids loading image bytes into the workspace. Reusing the existing polling loop for refresh keeps the indicator eventually-consistent without a new signal channel. The indicator is the **only** image surface in the story stream (per D6 — no inline thumbnails in v1), so the workspace change is minimal and low-risk to the RP engine UI.

**Alternatives considered**:
- Loading all image rows in the session payload: Rejected — pulls image metadata the workspace doesn't need (prompt text, file paths) and grows the session load.
- Push updates via the background job (SignalR-style): Rejected — the polling loop already exists and is sufficient for a manual-trigger feature with low update frequency.
