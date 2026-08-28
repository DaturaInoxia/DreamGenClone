# 036 — Scene Image Edit Compiler: Model-Level Refusal (Swap to Abliterated Qwen2.5-VL)

**Status:** Resolved (compiler model swapped to uncensored abliterated Qwen2.5-VL)
**Date:** 2026-08-27

## Report

After the v2 prompt fix (035) resolved preservation strictness, explicit-sexual edit requests were
STILL refused by the compiler. The user's request "lower the woman's head bringing her mouth closer to
the object" returned `invalid` with the prompt-echoed reason *"The request is genuinely impossible or
self-contradictory."* The user believed the model was an unrestricted NSFW model.

- RP session: `423a270c-49a6-4cad-9ccf-94d68bdc6e7c`, source image `074956e3-69ca-4e57-a442-ddeec3723992`
- Attempts `e97c4752` (04:48) / `cdc43712` (04:44) → `invalid`, reason echoed from the v2 prompt.

## Analysis

Two models are involved and the user's "unrestricted" assumption applied to the wrong one:
- **Renderer** `qwen_image_edit_2511_fp8mixed.safetensors` — the image-edit model; handles adult content (proven).
- **Compiler** `Qwen/Qwen2.5-VL-7B-Instruct-AWQ` — the stock, **safety-aligned** instruct VL model; it
  refused explicit-sexual edits regardless of prompt wording. `GET /v1/models` on pod
  `yx7zudzunz95b3` (migration successor of `image-vision-qwen-vl-prod`) confirmed only the stock model
  was served.

This is a model-capability/alignment limit, not a prompt bug. The fix is a model swap, not prompt work.

## Plan (approved)

Swap the compiler model on the running migration pod from stock AWQ → **`huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated`** (BF16, uncensored, revision `fa935a7958b3669b194c7ba4d1cfcebbe222641d`, ~16.6 GB / 4 shards). Update repo provisioner/launcher for reproducibility; download+verify+swap on the pod; restart vLLM; sync Model Manager.

## Resolution

- **Repo:** `provision-runtime.sh` pinned to the abliterated repo/revision with exact blob checksums
  (4 safetensors + tokenizer.json `c0382117…`); `start-vllm.sh` served model name → abliterated id and
  `--host 0.0.0.0` (matches deployed reality); README updated; added `download-abliterated.sh` and
  `start-abliterated-vllm.sh`.
- **Pod (`yx7zudzunz95b3`, SSH root@64.247.206.201:12326):** downloaded 16.6 GB to staging, verified
  SHA-256 of all shards, stopped vLLM, **removed the stock model**, swapped, restarted vLLM on port 3004
  → `GET /v1/models` now serves `huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated`.
- **Model Manager (DB, backed up first):** `RegisteredModels` row `db602892` ModelIdentifier →
  `huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated`, DisplayName → "Qwen2.5-VL 7B abliterated image
  compiler", ArtifactRevision → `fa935a79…`, Notes updated.
- **Docs:** deployment manifest identity fields + `POD-CONNECTIONS-AND-MODEL-MANAGER.md` row updated.

## Validated

- [x] Pod `/v1/models` serves the abliterated model (GET + POST via proxy, 200).
- [x] Model Manager row updated (verified by query).
- [x] Behavioral: the previously-failing intent ("lower the woman's head bringing her mouth closer to the
      object") now compiles to **`ready`** via the abliterated compiler (direct API test, v2 prompt + schema).
- [ ] User runtime check: re-trigger the previously-failing edits in the image editor — they should now
      compile to `Ready`/`clarification` instead of `invalid`.
- [ ] Compiler proof corpus re-validation under the new model (pending; v2 prompt + abliterated model).

## Notes / follow-ups

- The user's "analyze on open" feature idea (show what the model sees when the editor opens) is queued
  for implementation after this swap — new lightweight describe call + UI + additive DB column.
- One attempt `b91fb782` ("the women stick out her tongue") was left stuck in `Compiling` by a mid-job
  process kill; re-triggering creates a fresh attempt.
