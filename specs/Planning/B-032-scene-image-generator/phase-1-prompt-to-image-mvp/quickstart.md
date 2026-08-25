# Quickstart: Scene Image Generator

**Feature**: 001-scene-image-generator  
**Date**: 2026-08-19

This is a developer quickstart for running and validating the feature locally. It is **not** an end-user guide.

---

## Prerequisites

- Windows (DPAPI API-key encryption is Windows-only — inherited from the Model Manager).
- .NET 9 SDK.
- The dev database at `DreamGenClone.Web/data/dreamgenclone.dev.db` (git-ignored; created on first run). Do **not** run `git clean -fd` — it deletes the live dev DB.
- An image-capable provider account. Expected v1 provider: **Together AI** (FLUX.1 via `/v1/images/generations`). A text LLM for the preprocessor stage (e.g. the existing DeepSeek or Llama model already configured for RP).
- The webapp started from `DreamGenClone.Web` with `ASPNETCORE_ENVIRONMENT=Development` (use `helpers/start-webapp-dev-clean.ps1`). Starting from the repo root or without the env var reads the wrong DB. **Do not start/stop the webapp yourself** — the user runs it.

---

## 1. Configure the image provider and models (Model Manager)

1. Open the app and navigate to **Model Manager** (`/model-manager`).
2. Add (or edit) an image-capable provider:
   - **Provider type**: Together AI (already a supported `ProviderType`).
   - **Base URL**: `https://api.together.ai/v1`
   - **Image capability**: `TextAndImage` (new field).
   - **Image generation path**: `/v1/images/generations` (default).
   - **Content policy**: choose **`SfwFiltered`** *or* **`AdultAllowed`** explicitly. **`Unknown` is not allowed** — image generation will fail fast with guidance until this is set. (For the POC, test both a filtered and an adult-allowed provider to validate the clamp and the explicit path.)
   - **API key**: your Together AI key (DPAPI-encrypted on save).
3. Register an image model under that provider:
   - **Model identifier**: e.g. `black-forest-labs/FLUX.1-schnell` (exact Together AI identifier).
   - **Display name**: `FLUX.1 Schnell (Together)`.
   - **Model kind**: `Image` (new field — required for the renderer dropdown).
   - **Image size supported**: e.g. `1024x1024` (informational).
4. In **Function Defaults**, assign:
   - `RolePlaySceneImagePreprocessor` → your preferred text LLM (e.g. DeepSeek) with temperature/top-p/max-tokens.
   - `RolePlaySceneImage` → the FLUX image model (the dropdown filters to image-kind models only).
5. A "no image model configured" callout on the Model Manager page confirms the gap when the function default is unset.

---

## 2. Generate an image from a story moment

1. Open a roleplay session (`/roleplay/workspace/{sessionId}`) that has at least one narrative interaction.
2. On an interaction, click the new **Generate image** action in the interaction toolbar.
3. You are navigated to the **Image Studio** at `/roleplay/studio/{sessionId}/{interactionId}`.
4. **Settings**: choose a style (realistic / cinematic / anime / cartoon / painterly / sketch), an image size, and (if the provider policy is adult-allowed) an explicitness toggle. The toggle is disabled/hidden when the provider is SFW-filtered.
5. **Generate Prompt**: click the button. The preprocessor LLM runs (background job) and the editable prompt appears in a textarea, along with the pulled passage highlighted in the source panel. Status shows a spinner while Pending.
6. **Edit** the prompt text if desired.
7. **Render Image**: click the button. The image model runs (background job) and the rendered image appears in the results strip. Status shows a spinner while Pending/Generating.
8. **Iterate**: edit the prompt, change settings, or use **Refine prompt** (enter a short instruction like "more atmospheric" and click) to regenerate the prompt, then render again. Each render is a distinct saved version in the results strip.
9. Leave the studio and reopen it (same URL) — the saved images are still there.

---

## 3. Check the workspace indicator

- Back in the workspace, the interaction you generated an image for now shows an **image icon + count badge** in its header. Interactions with no images show no indicator. The indicator refreshes via the existing polling loop.

---

## 4. Browse the gallery

- Navigate to `/roleplay/gallery/{sessionId}` (linked from the workspace header and the studio page).
- All session images are listed, **grouped by interaction** (each group shows the interaction excerpt).
- Click an image for a full-size lightbox view; "Open in studio" jumps to that interaction's studio.

---

## 5. Inspect prompts (debug)

- Open the workspace **Debug View** and filter for `SceneImagePromptSent` / `SceneImageResponseReceived` events to inspect the exact preprocessor prompts, the resolved content policy, and the image-model response status — useful for validating NSFW behavior and prompt shape during the POC.

---

## 6. POC validation checklist (Phase 1 success criteria)

- [ ] **NSFW — filtered provider**: request an explicit image against a `SfwFiltered` provider. Confirm the system either produces a safe-for-work version (clamp) or a clear policy-rejection message — **never** silently bypasses. (SC-004: 0% bypass.)
- [ ] **NSFW — adult-allowed provider**: with `AllowExplicitImage` on and an `AdultAllowed` provider, confirm explicit content can be generated.
- [ ] **NSFW — unset policy**: with `ContentPolicy == Unknown`, confirm generation fails fast with Model Manager guidance (no silent SFW assumption).
- [ ] **Image quality**: render a small sample across styles/sizes; record quality findings and any prompt-shape improvements to feed back into the preprocessor instructions. (SC-005: ≥90% acceptable.)
- [ ] **Basics**: generate → edit prompt → render → regenerate (previous version kept) → indicator appears → gallery lists it → delete removes it. (SC-001, SC-002, SC-003, SC-007, SC-008.)
- [ ] **Unconfigured**: with no image model assigned, confirm generation surfaces clear configuration guidance (SC-006: 100% guidance, 0% silent failure).
- [ ] **Build + tests**: `dotnet build DreamGenClone.sln` is green; the full RP test suite passes.

---

## 7. Build & test commands

```powershell
# Build the solution
dotnet build DreamGenClone.sln

# Run the RP-area tests (minimum after a change; ideally the full suite)
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~SceneImage"

# Full test suite
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj
```

---

## 8. Where files live

- Image files: `DreamGenClone.Web/data/scene-images/{sessionId}/{imageId}.png` (git-ignored; **never committed**).
- Metadata: `SceneImagePrompts` + `SceneImages` tables in `data/dreamgenclone.dev.db` (dev) / `data/dreamgenclone.db` (prod). The snapshot DB (`dreamgenclone.snapshot.db`) does **not** carry image bytes (they're on disk).
- Debug events: existing `RolePlayDebugEvents` table, kinds `SceneImagePromptSent` / `SceneImageResponseReceived`.

---

## 9. Out of scope for Phase 1

- Automatic image generation after turns (manual only).
- Character likeness from reference photos on character profiles (roadmap — D9).
- Cross-session gallery (v1 gallery is per-session).
- Image editing / inpainting / controlnet.
