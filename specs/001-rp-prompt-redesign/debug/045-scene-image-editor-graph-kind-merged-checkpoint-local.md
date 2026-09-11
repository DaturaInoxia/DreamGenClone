# 045 — Scene Image Editor: Configurable ComfyUI Graph + Local Rapid-AIO Checkpoint

**Status:** In progress (code + tests done; artifact download running on the local host; DB flip + proof render pending)
**Date:** 2026-09-11

## Report

The user wants the **local** ComfyUI host (`WOOD-GAME-MAIN`, `http://192.168.0.16:8188`) to run the **same
renderer as the RunPod serverless editor** — `Qwen-Rapid-AIO-NSFW-v23.safetensors` (the NSFW-merged
checkpoint chosen in `specs/Planning/B-101-serverless-migration/plan.md` MODEL DECISION 2026-08-28) —
instead of the stock, safety-aligned `qwen_image_edit_2511_fp8mixed.safetensors` (whose genital region
blanks to "mannequin").

Two blockers were found:

1. **Artifact absent:** the local host has no AIO checkpoint (only SDXL/Pony checkpoints).
2. **The app could not have used it anyway.** The merged-checkpoint graph is bound to the *protocol*:
   `RunPodServerlessEditingClient` always builds `BuildAioMergedCheckpointWorkflow`
   (`CheckpointLoaderSimple`), while `ComfyUIImageEditingClient` — the client for the `ComfyUi`
   protocol the local provider uses — always built the split graph
   (`UNETLoader` + `CLIPLoader` + `VAELoader`). Pointing the local row at a merged checkpoint would
   have submitted a graph referencing a non-existent `diffusion_model` and no standalone TE/VAE.

## Analysis

The graph shape was previously implied by transport, with no persisted configuration. Inferring it from
artifact names would be a hidden/guessed default, which this repo forbids
(`.github/copilot-instructions.md`: no hardcoded runtime defaults, no guessed values, RP behavior
controls must be UI-backed persisted data).

Evidence gathered:
- `ImageProtocol` enum: `ComfyUi = 1` (custom ComfyUI at `http://192.168.0.16:8188`), `ComfyUiServerless = 2`.
- `ImageEditingClientDispatcher` switches on `model.ImageProtocol`; the serverless client hardwires the
  AIO graph (comment: the AIO worker "accepts only the merged-checkpoint graph").
- Live host `/object_info/UNETLoader` showed only `qwen_image_edit_2511_fp8mixed.safetensors`;
  `/object_info/CheckpointLoaderSimple` had no Qwen checkpoint.
- AIO artifact: `https://huggingface.co/Phr00t/Qwen-Image-Edit-Rapid-AIO/resolve/main/v23/Qwen-Rapid-AIO-NSFW-v23.safetensors`,
  HTTP 200, **28,431,840,023 bytes** (repo UI is now sensitivity-gated; direct resolve still works).
- Host: SSH `wood-game-main\kenac@192.168.0.16` (key `~/.ssh/dgcomfy_ed25519`), `D:` 1.0 TB free,
  **Windows PowerShell 5.1**.

## Plan (approved)

1. Download the AIO checkpoint to `D:\ComfyUI\models\checkpoints\` on the local host (resumable).
2. Add a persisted, UI-backed editor graph setting `ImageEditorGraphKind` (`SplitUnet` | `MergedCheckpoint`)
   and select the generated graph from it; **fail fast** when a ComfyUI-protocol editor has none.
3. Idempotent, guarded DB command `qwen-edit-local-aio-configure` to point the local editor row at the
   AIO checkpoint with that checkpoint's own settings (8 steps / CFG 1 / euler_ancestral / beta).
4. Verify with a real local edit render; update docs + memory.

## Resolution (so far)

Code:
- **NEW** `DreamGenClone.Domain/ModelManager/ImageEditorGraphKind.cs` — enum + `ImageEditorGraphKinds`
  text contract (`ParseOrNull` returns null for blank, throws on unknown values).
- `RegisteredModel.ImageEditorGraphKind` (string?, null = not configured).
- `ResolvedImageEditorModel.GraphKind` (appended, defaulted null so existing positional callers compile).
- `SqlitePersistence` — `RegisteredModels.ImageEditorGraphKind TEXT NULL` in the create-table DDL and the
  additive `ALTER TABLE` migration list.
- `RegisteredModelRepository` — insert/upsert column, `$imageEditorGraphKind` parameter, both SELECTs
  (appended after `MaximumOutputTokens` to avoid renumbering existing ordinals) and `ReadModel` ordinal 44.
- `ImageEditorModelResolver` — parses the persisted value; **requires** it for `ComfyUi`;
  rejects a non-merged value on `ComfyUiServerless` (which always runs the merged graph).
- `ComfyUIImageEditingClient.BuildResolvedWorkflow` — selects split vs merged graph from
  `GraphKind`; throws when unconfigured (no guessing). Both edit entry points now use it. Serverless
  client unchanged.
- `ModelDetailsEditor.razor` + `ModelManager.razor` — "Editor Graph" dropdown (inline editor) and both
  model-copy paths carry the new field.
- `DreamGenClone.DbQuery/ModelManagerTransfer.cs` — column registry entry.
- `DreamGenClone.DbQuery/Program.cs` — `qwen-edit-local-aio-configure` (idempotent; validates the local
  provider protocol and that `RolePlaySceneImageEditor` already points at the local editor row; second run
  reports "already configured" and writes nothing).
- Tests: `ComfyUIImageEditingClientTests` (split graph, merged graph + its own settings, fail-fast,
  persistence round-trip); `ImageEditingClientDispatcherTests` updated to declare the graph for the
  ComfyUI-protocol case.

Infra:
- **NEW** `helpers/local-comfyui-host/download-qwen-aio-checkpoint.ps1` + `diagnose-download-prereqs.ps1`.
  Lessons recorded: the host runs **PowerShell 5.1** (no `?.`), and **`ssh` kills detached child
  processes when the session closes** — the download must run inside a persistent SSH session.

## Not changed

- `RunPodServerlessEditingClient` and the serverless endpoint (`img-qwen-edit-serverless`,
  `79wkn5jz5d5txx`) — unchanged; the local switch is additive.
- `qwen-edit-serverless-configure` — untouched. (It still guards on the legacy `127.0.0.1:3002`/serverless
  base URL and would refuse on the current local-provider base URL; separate pre-existing issue.)

## Validated

- [x] Build green: tests project `0 Error(s)`, DbQuery project `0 Error(s)`, web project `0 Error(s)`.
- [x] Affected tests green (11/11 editing/dispatcher/repository tests); new graph tests included.
- [x] Artifact downloaded + byte-verified: `Qwen-Rapid-AIO-NSFW-v23.safetensors`,
      28,431,840,023 bytes, SHA-256 `FDB919FC81BEA63F13759967FC92C9118142E5C70D4E6795199233A35EEFA233`
      (8 parallel range chunks, assembled + size-verified).
- [x] ComfyUI registers the checkpoint in `/object_info/CheckpointLoaderSimple`.
- [x] Webapp rebuilt and restarted (Development, cwd `DreamGenClone.Web`, HTTP 200 on :5177; dev DB
      confirmed by 52 fresh `HealthCheckResults` rows).
- [x] `qwen-edit-local-aio-configure` executed: editor row `7bc5d932` → AIO checkpoint,
      `MergedCheckpoint`, 8 steps / CFG 1 / euler_ancestral / beta; second run reports
      "already configured … No changes made" (idempotent).
- [x] Non-explicit proof render through the app's exact merged graph on the local host: shirt changed
      to solid red, second person/pose/background/identity held; garment style drifted (button-up →
      polo) — expected at 8 steps / CFG 1. Output:
      `artifacts/tmp/proofs/qwen-edit-local/local-aio-qwen-edit-local_00001_.png`.
- [ ] End-to-end edit driven from the Scene Image Editor UI (needs a user-initiated edit).
- [x] Docs (`docs/local-comfyui-model-manager-setup.md`), helper README, and memory updated.

## Pre-existing failures (NOT caused by this change)

The full suite currently reports **3 failures** in `SdxlSceneImagePromptBuilderTests`
(`BuildCanonicalMessages_SystemPrompt_EnforcesResearchedCompilerRules`,
`..._WithUserRemovals_EmitsAuthoritativeRemovalNotice`, and one more). Both
`DreamGenClone.Web/Application/RolePlay/SdxlSceneImagePromptBuilder.cs` and its test file were modified at
**12:59–13:00 today by a concurrent workstream** (not by this task — no SDXL file is in this change set),
and the tests now assert "USER REMOVALS — AUTHORITATIVE" / the absence of
"CANONICAL PROVIDER REQUEST SNAPSHOT" that the working-tree builder does not yet implement. Reported, not
touched, per the repo's no-unrequested-changes rule.
