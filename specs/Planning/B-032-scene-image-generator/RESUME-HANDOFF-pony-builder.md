# RESUME HANDOFF — Scene Image Prompting: Pony/ComfyUI + Per-Model Builder Split

> **Read this first if you are picking up this work from a fresh machine / new chat session.**
> Everything needed to resume lives in this repo. Do NOT rely on prior chat memory.

**Feature area**: Scene Image Generator (backlog B-032) — PonyV6/ComfyUI integration + per-model prompt-builder refactor
**Last updated**: 2026-08-24
**Branch**: `001-scene-image-generator`
**Repo root**: `C:\zPersonal\Source\DreamGenClone` (on other machines: same repo, same relative paths)

---

## 1. TL;DR — where we are

> **Phase 2 continuity work now starts at `continuity-rendering-architecture.md`.** The prompt-only POC cannot reliably preserve pose, asymmetric contact, identity, location geometry, or a frozen moment across POVs. B-097 is no longer an undecided ControlNet-vs-accept item: the approved first gate is the standalone four-seed ControlNet touch proof recorded in `controlnet-touch-proof.md`. Do not change app prompt code or integrate ControlNet before that proof passes.

- **PonyV6 + ComfyUI is fully wired** into the app (client + protocol + storage). The app records `ponyDiffusionV6XL_v6.safetensors` on every rendered image via the Comfy provider (`ImageProtocol = ComfyUi`).
- **Seed control added** — `SceneImageStudioSettings.Seed` (nullable `long?`), threaded through `IImageGenerationClient.GenerateAsync` → dispatcher → `ComfyUIImageClient` (KSampler seed). Blank = random, fixed value = reproducible. **This is the fix for "app renders differently every time vs the script."**
- **Baseline negative aligned** to the script's guard set (anti-duplication + `airbrushed, plastic skin`).
- **Permanent `SceneImageRequestSubmitted` debug event** added — logs the exact positive/negative/seed/checkpoint the app submits, for audit.
- **Per-model prompt builder split (JUST DONE, uncommitted)** — `SceneImagePromptPreprocessor` renamed to `PonySceneImagePromptBuilder`; new `IPonySceneImagePromptBuilder` + `ISceneImageLLMPromptBuilder` interfaces. **All 1222 tests pass.**
- **Backlog items added**: B-096 (facial-hair negative), B-097 (ControlNet pose lock).

---

## 2. How to orient (read these first)

1. `.github/copilot-instructions.md` — **mandatory repo rules** (no git restore, no fallback gate values, tests must pass, no RP-engine change without plan+confirmation).
2. `specs/Planning/B-032-scene-image-generator/continuity-rendering-architecture.md` — controlling Phase 2 goal, architecture, terminology, persisted domain draft, studio workflow, and gates.
3. `specs/Planning/B-032-scene-image-generator/controlnet-touch-proof.md` — host inventory and preserved proof results.
4. `specs/Planning/B-032-scene-image-generator/RESUME-HANDOFF.md` — the earlier B-032 preprocessor handoff. Its file table contains historical names; use current code for exact symbols.
5. `specs/Planning/backlog.md` — B-032, B-096, B-097, B-098, and B-099.
6. `artifacts/tmp/image-prompts.md` — historical prompt catalog; useful evidence, not the Phase 2 control architecture.

---

## 3. Current implementation state (verified 2026-08-23)

### Implemented and working (do not re-do)
- **PonyV6 + ComfyUI** end-to-end: `ComfyUIImageClient` (POST `/prompt`, poll `/history/{id}`, GET `/view`), routed by `ImageProtocol` on the provider. Checkpoint `ponyDiffusionV6XL_v6.safetensors` on the RunPod pod.
- **Seed control** (new): `SceneImageStudioSettings.Seed` → `SceneImageRenderingJobHandler.ResolveSeed` → `GenerateAsync(..., seed)` → `ComfyUIImageClient.BuildDefaultWorkflow(..., seed)` → `KSampler.seed`. **Blank = random; set = reproducible.**
- **Baseline negative** now matches the script's guard set: `extra penis, multiple penises, two penises, duplicate anatomy, blurry, low quality, ugly, deformed, extra limbs, bad anatomy, watermark, text, censored, mosaic, airbrushed, plastic skin`.
- **`SceneImageRequestSubmitted` debug event** — permanent observability of the exact submitted payload.
- **Per-model builder split** (see §4).

### Key files (Web project unless noted)
| File | Role |
|------|------|
| `DreamGenClone.Web/Application/RolePlay/PonySceneImagePromptBuilder.cs` | **RENAMED from `SceneImagePromptPreprocessor.cs`**. Pony deterministic beat builder + legacy LLM path. |
| `DreamGenClone.Web/Application/RolePlay/IPonySceneImagePromptBuilder.cs` | **RENAMED from `ISceneImagePromptPreprocessor.cs`**. Pony deterministic interface. |
| `DreamGenClone.Web/Application/RolePlay/ISceneImageLLMPromptBuilder.cs` | **NEW**. LLM-driven (Seedream-era) interface (`BuildMessages`/`ParseOutput`). |
| `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs` | Uses `IPonySceneImagePromptBuilder` (calls `BuildDeterministicBeatPrompt`). |
| `DreamGenClone.Web/Application/RolePlay/SceneImageRenderingJobHandler.cs` | Uses `IPonySceneImagePromptBuilder` (negative + SFW clamp). |
| `DreamGenClone.Web/Program.cs` | DI: `PonySceneImagePromptBuilder` registered as `IPonySceneImagePromptBuilder` + aliased for `ISceneImageLLMPromptBuilder`. |
| `DreamGenClone.Infrastructure/Models/ComfyUIImageClient.cs` | ComfyUI client; `BuildDefaultWorkflow(..., seed)`; baseline negative. |
| `DreamGenClone.Infrastructure/Models/ImageGenerationClientDispatcher.cs` | Routes by `ImageProtocol` (ComfyUi vs OpenAI). |
| `DreamGenClone.Web/Application/RolePlay/Models/SceneImageStudioSettings.cs` | Added `Seed` field. |
| `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` | Added Seed input in Image Settings. |
| `DreamGenClone.Tests/RolePlay/SceneImagePromptPreprocessorTests.cs` | Updated to `PonySceneImagePromptBuilder`. |

---

## 4. The per-model builder split (JUST DONE, UNCOMMITTED)

**Why:** the single `SceneImagePromptPreprocessor` bundled two model dialects. Git history proved there was **never a separate Seedream deterministic builder** — the Seedream-era path was the **LLM-driven** `BuildMessages`/`ParseOutput` (a different architecture). Pony's deterministic `BuildDeterministicBeatPrompt` was added later and drives production.

**What changed (forward-only, all 1222 tests green):**
1. `SceneImagePromptPreprocessor.cs` → `PonySceneImagePromptBuilder.cs` (git mv). Class now `PonySceneImagePromptBuilder : IPonySceneImagePromptBuilder, ISceneImageLLMPromptBuilder` — implements BOTH the Pony deterministic path and the legacy LLM path.
2. `ISceneImagePromptPreprocessor.cs` → `IPonySceneImagePromptBuilder.cs` (git mv). Declares only the Pony deterministic members.
3. New `ISceneImageLLMPromptBuilder.cs` — declares `BuildMessages` ×2 + `ParseOutput`.
4. Both handlers + `Program.cs` re-pointed to `IPonySceneImagePromptBuilder`.
5. Tests updated to `PonySceneImagePromptBuilder`.

**Why both interfaces on one class:** the LLM path (`BuildMessages`/`ParseOutput`) is still exercised by tests and is the recoverable Seedream-era architecture. Keeping it on the same concrete class avoids dead code while making the two dialects separately-registered interfaces — so a future Seedream/FLUX-specific builder can be added and swapped without touching Pony's path.

**Next step for the next agent (if continuing this):** decide whether to fully split `PonySceneImagePromptBuilder` into two concrete classes (one per interface) and add a **dispatch seam** that picks the builder by `resolved.ImageProtocol` (ComfyUi → Pony deterministic; OpenAI → LLM/Seedream). Currently the dispatch is NOT wired — both handlers hardcode the Pony builder.

---

## 5. Prompt-catalog findings (validated against PonyV6, seed 24680)

Full catalog: `artifacts/tmp/image-prompts.md`. Key rules learned (all verified this session):

1. **Pony ignores "no X" in the positive** — put negation in the **negative** prompt (e.g. `beard, mustache, stubble, facial hair`). B-096.
2. **Camera/POV words re-compose the scene** — `pov from Dean's eyes` breaks missionary/bun. **Workaround: describe what the POV watches** (`man looking down watching her ass, close up view of ass`) instead of naming the camera. B-097.
3. **Concrete body tokens beat vague ones** — `chubby` works; `fat`/`full figure` don't; `curvy` is too vague. Use specific single tokens.
4. **Attribute position matters a little** — early tokens shape composition; repeat/weight key attributes (age, bun, boobs) or the model uses its default attractive-adult prior.
5. **Ethnicity**: explicit `caucasian` repeated + avoid conflicting skin descriptors; naming ethnicities in the negative can backfire (over-attend).
6. **Obstruction/occlusion (blinds) is NOT honored via prompt** — Pony gives a clear in-room view. Needs ControlNet/IMG2IMG (B-097).

---

## 6. RunPod + ComfyUI setup and agent access (IMPORTANT)

### What is set up on the pod
- **Pod**: `desperate_gold_weasel`, host id `qguv5e029`.
- **ComfyUI proxy URL**: `https://qguv5e029u58lb-3000.proxy.runpod.net` (the `-3000` port is the ComfyUI HTTP proxy).
- **Checkpoint**: `ponyDiffusionV6XL_v6.safetensors` — stored on **`/workspace`** (NOT the default container disk, which is only 5 GB and fills up). The ComfyUI `extra_model_paths.yaml` points at `/workspace` so the checkpoint is found. **After a pod recycle, `extra_model_paths.yaml` must be re-created and ComfyUI restarted** (this was a recurring issue).
- **ComfyUI** exposes `/prompt`, `/queue`, `/history`, `/view` (the app's `ComfyUIImageClient` uses these).

### How an agent connects and runs commands
There are **two access paths** — HTTP (for image generation) and SSH (for pod maintenance). Both need the git-ignored env files.

**A. HTTP / ComfyUI (for generating images) — the primary path:**
1. Create (do NOT commit) `helpers/runpod/.runpod-env.ps1`:
   ```powershell
   $env:RUNPOD_API_KEY = "rp-xxxxxxxx"          # narrow RunPod API key (User Settings → API Keys)
   $env:COMFYUI_URL    = "https://qguv5e029u58lb-3000.proxy.runpod.net"
   $env:RUNPOD_POD_ID  = "desperate_gold_weasel"
   ```
   (`.runpod-env.ps1` is git-ignored — never commit it.)
2. Generate images with the helper scripts (from repo root):
   ```powershell
   .\helpers\runpod\generate-one.ps1 -WorkflowPath helpers\runpod\workflows\pony-nsfw-single.json -OutputDir artifacts\tmp -Prefix img -Seed 24680 -Prompt "<prompt>"
   ```
   - `generate-one.ps1` = exactly ONE image per run (deterministic with `-Seed`).
   - `pod.ps1 -Action status|start|stop` = pod lifecycle via the RunPod GraphQL API.
   - `model.ps1 -List` = list checkpoints on the pod.

**B. SSH (for pod maintenance — install models, fix `extra_paths`, restart ComfyUI):**
1. Place the private key at `artifacts/runpod/ssh_ed25519` (git-ignored via `artifacts/`).
2. Create `artifacts/runpod/.ssh-env.ps1` with the **SSH over exposed TCP** host/port from the RunPod console:
   ```powershell
   $env:RUNPOD_SSH_USER = "root"
   $env:RUNPOD_SSH_HOST = "<PUBLIC_IP>"
   $env:RUNPOD_SSH_PORT = "<PUBLIC_SSH_PORT>"
   ```
3. Connect:
   ```powershell
   .\helpers\runpod\ssh.ps1 -Command 'whoami'    # run one remote command
   .\helpers\runpod\ssh.ps1                        # interactive shell
   ```
   **Note**: the exposed TCP route is required for automation. The basic gateway route requires a PTY and echoes piped commands, so it is for interactive use only. The public IP and port can change after pod migration/recreation; always copy them from the current Connect tab.

### Critical operational warnings
- **Never commit** `.runpod-env.ps1`, `.ssh-env.ps1`, or the SSH key — all git-ignored.
- **Never run `git clean -fd`** — it deletes ignored files including the live `dreamgenclone.dev.db`.
- **Pod disk is small** — keep checkpoints on `/workspace`, not the container disk.
- **After pod recycle**: re-create `extra_model_paths.yaml` + restart ComfyUI before generating.

---

## 7. Backlog state

- **B-096** (`new`): auto-place facial-hair suppression in the negative when prompt requests clean-shaven. Awaiting go-ahead.
- **B-097** (`designed`): ControlNet/controlled-render architecture. First gate is the four-seed clothed touch proof; inventory exact host dependencies before selecting node/model packages.
- **B-032** (`planned`): scene image engine — the parent feature.

---

## 8. Operational notes

- **The dev server must be restarted** to load the renamed types + seed control. The running server (PID 552/565) holds the old DLLs — a normal `dotnet build` fails with `MSB3021` file-lock errors until it's stopped. Build to a temp output (`-p:BaseOutputPath=obj/_tmp_/`) to verify compile while it runs.
- **DB**: `dreamgenclone.dev.db` is the live DB (git-ignored); `dreamgenclone.snapshot.db` is the git-tracked sanitized copy. Never commit `.db`/`.bak` except the snapshot. Use `dotnet run --project DreamGenClone.DbQuery -- sql <file>` for queries.
- **RunPod/ComfyUI**: pod `desperate_gold_weasel`, host `qguv5e029`, ComfyUI URL `https://qguv5e029u58lb-3000.proxy.runpod.net`. Checkpoint `ponyDiffusionV6XL_v6.safetensors` on `/workspace`. Scripts in `helpers/runpod/` (`generate-one.ps1` = one image per run, `-Seed` param).
- **PNG metadata**: ComfyUI embeds the workflow in the PNG's `tEXt` chunk — `artifacts/tmp/inspect-png-chunks.ps1` extracts it (useful to recover what prompt produced an image).

---

## 9. Immediate next steps (for the next agent)

1. Read `continuity-rendering-architecture.md` and continue only the current gate.
2. Inventory the current RunPod ComfyUI host's installed nodes and model directories without installing anything or exposing secrets.
3. Record that inventory in `controlnet-touch-proof.md`.
4. Select and document one SDXL-compatible pose-control dependency from evidence, then obtain approval before modifying the host.
5. Author the one-way clothed touch control asset and API workflow; run exactly the four predetermined-seed gate.
6. Do not change app prompt code or add app integration until the proof report records a pass.

---

## 10. Pony prompting research + Beat Prose → Pony plan (added 2026-08-23, IMPORTANT)

**⚠️ Read these before touching ANY Pony/ComfyUI prompt code:**

- **`.github/instructions/pony-v6-prompting.instructions.md`** — authoritative, pod-validated Pony V6 XL prompting rules. The current builder output was proven broken (cartoon/overhead/deformed, 1-person collapse, dropped background people, forced-naked vs sundress contradiction). Key facts: Pony is an anime/cartoon model; must use the FULL quality string `score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up`; always a `rating_*` tag by policy; short tag-like prompts (never narrative prose); count tags (`1boy, 1girl`); explicit camera view tag; `euler_ancestral` 25 steps; minimal negative.
- **`specs/Planning/B-032-scene-image-generator/plan-pony-beat-prose.md`** — the plan to convert Beat Prose → Pony tag prompt in `PonySceneImagePromptBuilder.BuildDeterministicBeatPrompt`, with the exact code changes, tests, blast radius, and validation steps.
- The app constant `PonyQualityTags` is **wrong** (short form + hardcoded `rating_explicit`) — flagged in the plan §5.1.
- For photorealistic output, Pony is the wrong model — use `sd_xl_base_1.0.safetensors` or `flux1-schnell-fp8.safetensors` (both on the pod) with natural-language prompts. Reference workflow: `helpers/runpod/workflows/sdxl-beach.json`.