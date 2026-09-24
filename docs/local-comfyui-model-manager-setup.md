# Register the Local ComfyUI Host in Model Manager (any host)

> **Goal:** add the **local WOOD-GAME-MAIN ComfyUI host** (RTX 5080 16 GB) to Model Manager as a
> first-class image provider alongside (or instead of) the RunPod Serverless endpoints, so the app
> can render through a **free local** image endpoint. These are **not RunPod pods** — they are a
> direct ComfyUI HTTP service on the dev machine itself, using `ImageProtocol.ComfyUi`
> (`/prompt` + `/history` + `/view`), exactly like the `RunPod ComfyUI` provider row but with a
> localhost/LAN Base URL.
>
> **This doc is the agent procedure.** It is written to run against the **live development database**
> (`DreamGenClone.Web/data/dreamgenclone.dev.db`) on **whatever host runs the webapp**. Every host
> has its own dev.db (created from the git-tracked snapshot), so the same idempotent command is run
> per host with that host's reachable Base URL.

## Endpoint facts (source of truth: `docs/flux-local-5080-comfyui-setup.md`)

| Fact | Value |
|---|---|
| Host | **WOOD-GAME-MAIN** (Windows 11, RTX 5080 16 GB, ComfyUI 0.34.0 at `D:\ComfyUI`) |
| Service | `main.py --listen 0.0.0.0 --port 8188` |
| Base URL (on the ComfyUI host itself) | `http://127.0.0.1:8188` |
| Base URL (from other LAN hosts) | `http://192.168.0.11:8188` (reassigned 2026-09-21; the old `192.168.0.16` no longer routes) |
| **Current provider `BaseUrl` on this system** | **`https://comfy.kenacwood.net`** — a public HTTPS front for the same ComfyUI (verified 2026-09-13: `/system_stats` → HTTP 200, `comfyui_version 0.34.0`). Use this from any host; the LAN URL is the fallback. Note the scheme: `http://…:8188` through the domain does **not** work (only HTTPS/443 is served). |
| Protocol in Model Manager | `ImageProtocol = ComfyUi` (`/prompt`), **not** `ComfyUiServerless`, **not** a pod |
| Generation checkpoints served | `juggernautXL_ragnarok.safetensors`, `bigLust_v16.safetensors`, `ponyDiffusionV6XL_v6.safetensors`, `ponyRealism_V23ULTRA.safetensors`, `flux1-dev-fp8.safetensors` |
| Editor checkpoints served | `Qwen-Rapid-AIO-NSFW-v23.safetensors`, `qwenImageEditRemix_aioV20.safetensors` (both merged/AIO) |
| Editor LoRA | `QwenEdit2511_AllIncludedGay_v2.safetensors` @ 0.8 |
| Identity stack present | IP-Adapter PLUS FACE / PuLID / FaceID / DWPose (same as the serverless volume, minus Qwen-Edit AIO) |
| Network exposure | LAN inbound TCP 8188 **plus a public HTTPS front** (`comfy.kenacwood.net`). That front is currently **unauthenticated** — treat the host as internet-reachable and put an access-control policy on it (e.g. Cloudflare Access); do not add a second unauthenticated exposure. |

## What gets created

A single provider row (one ComfyUI endpoint hosts every checkpoint) named
**`Local ComfyUI (WOOD-GAME-MAIN 5080)`** with:

- `ImageCapability = ImageOnly`, `ContentPolicy = AdultAllowed`, `ImageProtocol = ComfyUi`
- `ImageGenerationPath = /prompt`, `TimeoutSeconds = 600`, no credential reference (local, no auth)
- Registered models under it:

| ModelIdentifier | DisplayName | Family / Dialect | Enabled |
|---|---|---|---|
| `juggernautXL_ragnarok.safetensors` | Juggernaut XL Ragnarok (Local ComfyUI) | Sdxl / SdxlNaturalLanguage | ✅ |
| `bigLust_v16.safetensors` | BigLust v1.6 (Local ComfyUI) | Sdxl / SdxlNaturalLanguage | ✅ |
| `ponyDiffusionV6XL_v6.safetensors` | Pony V6 XL (Local ComfyUI) | Pony / PonyV6Tags | ✅ |
| `ponyRealism_V23ULTRA.safetensors` | Pony Realism v2.3 ULTRA (Local ComfyUI) | Pony / PonyV6Tags | ✅ |
| `flux1-dev-fp8.safetensors` | FLUX.1-dev fp8 (Local ComfyUI) | Flux / FluxNaturalLanguage | ✅ |

### Editor models (Qwen Image Edit) — four rows under the same provider

All four share `ModelKind = ImageEditor`, `ImageEditorGraphKind = MergedCheckpoint`,
`euler_ancestral` / `beta`, 8 steps, CFG 1, denoise 1, AuraFlow shift 3.1, CFGNorm 1,
text encoder `qwen_2.5_vl_7b_fp8_scaled.safetensors`, VAE `qwen_image_vae.safetensors`.

| DisplayName | `ImageEditorDiffusionModel` | `ImageEditorLoraName` @ strength | ModelIdentifier |
|---|---|---|---|
| Qwen Image Edit Rapid-AIO NSFW v23 (Local ComfyUI) | `Qwen-Rapid-AIO-NSFW-v23.safetensors` | — | `qwen_image_edit_2511_fp8mixed.safetensors` |
| Qwen Image Edit Rapid-AIO NSFW v23 + Gay/Trans LoRA (Local ComfyUI) | `Qwen-Rapid-AIO-NSFW-v23.safetensors` | `QwenEdit2511_AllIncludedGay_v2.safetensors` @ 0.8 | `qwen_edit_local_aio_v23_gaylora` |
| Qwen Image Edit Remix AIO v2.0 (Local ComfyUI) | `qwenImageEditRemix_aioV20.safetensors` | — | `qwen_edit_local_remix_aio_v20` |
| Qwen Image Edit Remix AIO v2.0 + Gay/Trans LoRA (Local ComfyUI) | `qwenImageEditRemix_aioV20.safetensors` | `QwenEdit2511_AllIncludedGay_v2.safetensors` @ 0.8 | `qwen_edit_local_remix_aio_v20_lora` |

> **The base local editor row keeps the historical `ModelIdentifier`
> `qwen_image_edit_2511_fp8mixed.safetensors`** even though its `ImageEditorDiffusionModel` was later
> repointed to the merged `Qwen-Rapid-AIO-NSFW-v23.safetensors` — the identifier is a stable key, not a
> description. Only the derived variants carry descriptive identifiers.

> **The Remix AIO v2.0 row and its +LoRA variant were added 2026-09-12/13.** Both are the same
> Civitai `Qwen Image Edit - Remix` AIO v2.0 merge (`qwenImageEditRemix_aioV20.safetensors`, Civitai
> model `2338517`; verified drop-in for the `MergedCheckpoint` graph — baked CLIP+VAE, single
> `CheckpointLoaderSimple`). Created through the idempotent clone path (see the apply sequence below),
> so the row is a straight clone of the base editor row plus the checkpoint/LoRA overrides.

> **These are single-image editors.** Both checkpoints are community *merged* ("AIO") Qwen-Image-Edit
> variants — one input image, one instruction. Verified 2026-09-13: they **ignore a second reference
> image** (`image2` wired into `TextEncodeQwenImageEditPlus` + `FluxKontextMultiReferenceLatentMethod`),
> so multi-image / location-reference editing does **not** work on them — the node accepts the input and
> the checkpoint drops it. Multi-image needs the official **`Qwen-Image-Edit-2511` Plus** model
> (split UNET graph), not these merges.

> **Pony Realism v2.3 ULTRA added 2026-09-08** (Civitai model `372465`, version `1920896`, file
> `ponyRealism_V23ULTRA.safetensors`, ~6.6 GB). A **photorealistic Pony V6 XL** merge — keeps Pony's
> tag dialect + uncensored rating system but renders photoreal people (the "photoreal Pony" the user
> asked to find). Registered through the idempotent `local-comfyui-configure` command (Pony /
> PonyV6Tags, enabled, additive — NOT the default). Download: `civitai.com/api/download/models/1920896`
> with the Civitai token. Smoke render PASS on the 5080 host 2026-09-08. **Identity-capable
> 2026-09-08:** `IdentityMechanism=IpAdapter`, `IdentityAdapterRef='PLUS FACE (portraits)'`,
> `IdentityStrength=0.8`; PLUS FACE proof on the host (Dean front ref) rendered likeness held (minor
> eye-color drift at 0.8). Proof workflow: `artifacts/tmp/dbquery/workflows/ponyrealism-ipadapter-proof.json`.

> **Qwen Image Edit 2511 (Local ComfyUI) — editor model added 2026-09-09.** Files (public HF,
> ~30 GB): `qwen_image_edit_2511_fp8mixed.safetensors` → `models/diffusion_models`,
> `qwen_2.5_vl_7b_fp8_scaled.safetensors` → `models/text_encoders`, `qwen_image_vae.safetensors` →
> `models/vae` (Comfy-Org repos; the split UNET graph, matching `ComfyUIImageEditingClient`). The
> ComfyUI 0.34.0 host already ships the full Qwen-Image-Edit node set
> (`TextEncodeQwenImageEditPlus`, `FluxKontext*`, `ModelSamplingAuraFlow`, `CFGNorm`). Local smoke
> **PASS 2026-09-09**: 40-step single-edit ~2 min 54 s on the 5080 (offload), edit applied + identity
> held. Registered under the `Local ComfyUI` provider with editor settings 40 steps / CFG 4 /
> euler / simple / denoise 1 / AuraFlow 3.1 / CFGNorm 1, and `RolePlaySceneImageEditor` function
> default **repointed to local** (RunPod editor row kept intact as fallback).
> **UPDATED 2026-09-11 — the local editor now runs the same merged checkpoint as the serverless
> endpoint.** `Qwen-Rapid-AIO-NSFW-v23.safetensors` is installed at
> `D:\ComfyUI\models\checkpoints\` (28,431,840,023 bytes, SHA-256
> `FDB919FC81BEA63F13759967FC92C9118142E5C70D4E6795199233A35EEFA233`), fetched with
> `helpers/local-comfyui-host/fetch-qwen-aio-parallel.ps1` + `assemble-qwen-aio-chunks.ps1`
> (8 parallel range requests — a single connection is throttled to ~2 MB/s for this artifact).
> The local editor row `7bc5d932-4596-4b96-ac73-5162516a162f` was repointed with
> `qwen-edit-local-aio-configure` (idempotent): `ImageEditorDiffusionModel` =
> `Qwen-Rapid-AIO-NSFW-v23.safetensors`, **8 steps / CFG 1 / euler_ancestral / beta** (Lightning
> merge — never 40/4), denoise 1, AuraFlow shift 3.1, CFGNorm 1.
>
> **This is why the local model is no longer the stock 2511.** The previous caveat applied to
> `qwen_image_edit_2511_fp8mixed`, the safety-aligned Comfy-Org repack (genital region → mannequin).
> It remains installed and can be restored by setting the row back to `SplitUnet` +
> `qwen_image_edit_2511_fp8mixed.safetensors` + 40/4/euler/simple.
>
> **New required setting — Editor Graph.** The ComfyUI workflow graph is configured data, never
> inferred from artifact names. `RegisteredModels.ImageEditorGraphKind` (`SplitUnet` |
> `MergedCheckpoint`) is exposed in Model Manager as **Editor Graph** and is **required** for a
> ComfyUI-protocol editor (the resolver fails fast when it is blank). `SplitUnet` = separate
> diffusion model + text encoder + VAE; `MergedCheckpoint` = one checkpoint bundling model+clip+vae
> (`CheckpointLoaderSimple`). A merged checkpoint with `SplitUnet` will not render.
>
> **Proof (non-explicit, 2026-09-11):** `helpers/local-comfyui-host/run-local-aio-edit-proof.ps1`
> submitted the app's exact merged graph plus `"Change only the man's shirt from blue to solid red."`
> against `specs/image-generator-tests/qwen/images/base.png`; the shirt turned red, the second
> person/pose/background/identity held, and the garment *style* drifted (button-up → polo) — expected
> at 8 steps / CFG 1. Output: `artifacts/tmp/proofs/qwen-edit-local/`.
>
> **Six-cell controlled re-run on the local AIO checkpoint (2026-09-11):**
> `run-qwen-six-edit-proof-local-aio.ps1` replayed the six frozen edits from
> `specs/image-generator-tests/qwen/manifest.json` with their original prompts/seeds.
> **5/6 target edits present, 5/6 preservation-clean.** The failure is
> `woman-left-facing-profile`: the woman rotates correctly but the **man also rotates into profile**,
> whereas the committed stock-model output keeps him front-facing. Local timing: 25 s (first cell, incl.
> model load) then ~20 s per cell, vs ~199 s per cell on the pod at 40 steps / CFG 4. Expect the trade
> of a Lightning merge: much faster, less fine-detail fidelity and less surgical preservation.
>
> Host gotchas recorded in `helpers/local-comfyui-host/README.md`: PowerShell is **5.1** (no `?.`),
> and `Start-Process`-detached transfers launched over SSH **survive the disconnect** and can silently
> compete with later downloads.

> **FLUX enabled 2026-09-08:** the B-112 §7 Flux-family code slice landed (`SceneImageModelFamily.Flux`
> + `FluxNaturalLanguage`, ComfyUI split-UNET `BuildFluxWorkflow`, `FluxSceneImagePromptCompiler`,
> validation arms + dropdowns). The FLUX row is now **enabled** and routable — additive, and it is
> NOT the `RolePlaySceneImage` default. Do not repoint the default unless you intend FLUX to replace
> the active model. See `specs/Planning/B-112-flux-local-5080-host/plan.md` §7.

> **ControlNet + DWPose present 2026-09-08 (proof-verified on this host):** the full pose-conditioning
> stack works locally. Nodes: `comfyui_controlnet_aux` (incl. `DWPreprocessor`), Impact Pack,
> IP-Adapter/PuLID — all present. ControlNet weight:
> `D:\ComfyUI\models\controlnet\thibaud-openpose-xl2\OpenPoseXL2.safetensors` (thibaud
> `controlnet-openpose-sdxl-1.0`, 5,004,167,829 B — the exact file the RunPod pose workflows use;
> on Windows ComfyUI lists it as `thibaud-openpose-xl2\OpenPoseXL2.safetensors`, backslash path).
> DWPose TorchScript ckpts seeded in
> `custom_nodes\comfyui_controlnet_aux\ckpts\dwpose\` (`yolox_l.torchscript.pt`,
> `dw-ll_ucoco_384_bs5.torchscript.pt`). Proof 2026-09-09 (Dean v7 front ref, BigLust 1024²):
> DWPose extract PASS + OpenPoseXL2 ControlNet render PASS. Proof workflows (git-ignored):
> `artifacts/tmp/controlnet-local-proof/*.json`. **No Model Manager rows** — ControlNet/DWPose are
> workflow-side conditioning, not served checkpoints.
>
> **SDXL depth + canny ControlNet weights installed 2026-09-22 (B-119 T01, route C1):** route C1
> contracts a layout from a reference image, and until now the host had **only** the OpenPose weight,
> so every depth/canny graph failed at load. Installed into `D:\ComfyUI\models\controlnet\` by the
> idempotent helper `helpers/local-comfyui-host/install-sdxl-controlnets.ps1` (run over SSH; it
> re-runs safely and skips hash-verified files):
>
> | File | Bytes | SHA-256 | Source |
> |---|---|---|---|
> | `controlnet-depth-sdxl-1.0.safetensors` | 2,502,139,134 | `66a6813e6bd7270ecfe68206a59ddd605a011ae85321188376605c66e0a4f303` | `diffusers/controlnet-depth-sdxl-1.0` → `diffusion_pytorch_model.fp16.safetensors` |
> | `controlnet-canny-sdxl-1.0.safetensors` | 2,502,139,136 | `b2e7d3921058a442cc80430d1ec8847f42599c705e2451c95e77cf4dcf8d6c25` | `diffusers/controlnet-canny-sdxl-1.0` → `diffusion_pytorch_model.fp16.safetensors` |
>
> The depth hash is a **byte-exact match** for the `controlnet-depth-sdxl-1.0.safetensors` recorded in
> the RunPod identity-worker manifest (`specs/Planning/B-032-scene-image-generator/phase-2-character-identity/proofs/identity-conditioning/model-manifest-2026-08-26.md`),
> so the same workflow JSON runs on the host and the worker unchanged. Verified: `ControlNetLoader`
> lists both weights with **no ComfyUI restart** (folder mtime cache invalidation). Host-only change —
> **not** a RunPod pod, so it is not in `helpers/runpod/pod-registry.json`.

> **Identity / ReferenceConditioning declarations 2026-09-09:** `local-comfyui-configure` now
> declares **and** qualifies `ReferenceConditioning` (IP-Adapter PLUS FACE, strength 0.8) for the
> three local models with a passing local identity proof — **Juggernaut XL Ragnarok**, **BigLust
> v1.6 (Local)**, and **Pony Realism v2.3 ULTRA**. Proof ids: `20260908-local-sdxl-ipadapter-juggernaut`,
> `20260908-local-sdxl-ipadapter-biglust`, `ponyrealism-ipadapter-proof-2026-09-08` (endpoint =
> this provider). Existing Pose/ControlNet strategy + qualification entries are preserved (merged).
> **Pony V6 XL and FLUX.1-dev fp8 intentionally stay unqualified**: Pony V6's IP-Adapter mechanism
> loads but the identity matrix is off-model (FAIL), and FLUX has no IP-Adapter PLUS FACE path.

This is **additive**: it never repoints function defaults and never disables an existing RunPod
provider/model, so the local endpoint can replace or run alongside the RunPod Serverless endpoints
per whatever you assign in Model Manager / Studio.

## How an agent applies it (per host)

**Prereq:** the target host's `dreamgenclone.dev.db` must exist (copy the snapshot if not:
`copy DreamGenClone.Web\data\dreamgenclone.snapshot.db DreamGenClone.Web\data\dreamgenclone.dev.db`).

Run from the repo root on that host, pointing at the ComfyUI host. **Run the whole sequence** — the
provider command alone does not create the editor rows:

```powershell
# 1. Provider + the generation (checkpoint) models.
dotnet run --project DreamGenClone.DbQuery -- local-comfyui-configure https://comfy.kenacwood.net

# 2. Editor models: the base local editor row, then each variant (each clones the base row).
dotnet run --project DreamGenClone.DbQuery -- qwen-edit-local-aio-configure
dotnet run --project DreamGenClone.DbQuery -- qwen-edit-local-aio-lora-configure QwenEdit2511_AllIncludedGay_v2.safetensors 0.8
dotnet run --project DreamGenClone.DbQuery -- qwen-edit-remix-aio-configure
dotnet run --project DreamGenClone.DbQuery -- qwen-edit-remix-aio-lora-configure QwenEdit2511_AllIncludedGay_v2.safetensors 0.8
```

Substitute the URL your host can reach (`http://127.0.0.1:8188` on the ComfyUI host itself,
`http://192.168.0.11:8188` on the LAN, `https://comfy.kenacwood.net` from anywhere).

Every command is **idempotent** (upserts in one transaction) and matches on a stable key, so
re-running with a different value updates in place instead of duplicating: the editor variants match
on `ModelIdentifier`, so re-running `qwen-edit-remix-aio-lora-configure` reports
`Updated editor variant model <id>`. These commands are the supported path — **do not hand-INSERT or
hand-UPDATE these rows.**

Two further commands help when syncing hosts:

| Command | Use |
|---|---|
| `provider-endpoint-update <providerId> <expectedCurrentBaseUrl> <newBaseUrl>` | Change **only** a provider's `BaseUrl` with a compare-and-swap guard (fails if someone else changed it first). This is the supported way to repoint a host at a new endpoint. |
| `modelmanager-export [outFile]` / `modelmanager-import <jsonFile>` | Mirror the whole Model Manager config (Providers + RegisteredModels + FunctionModelDefaults). Default export path is the **git-tracked** `DreamGenClone.Web/data/model-manager.export.json`; API keys are never exported, so the target host re-enters them. Useful for cloning this system's full configuration to another host in one step. |

## Verify

1. Provider + models are present and correctly flagged:

   ```powershell
   dotnet run --project DreamGenClone.DbQuery -- sql DreamGenClone.DbQuery/queries/local-comfyui-provider.sql
   ```

   The query filters `Providers.Name = 'Local ComfyUI (WOOD-GAME-MAIN 5080)'` and left-joins its
   `RegisteredModels`.

2. Web app → **Model Manager** (`/model-manager`): the `Local ComfyUI` provider shows
   `ImageCapability=ImageOnly` and its three enabled SDXL models (plus disabled FLUX).
3. **Test Connection** on the provider (local ComfyUI must be running).
4. Render a test image via Studio with one of the local models and confirm it resolves to
   `http://<host>:8188` (see `RolePlaySceneImage` diagnostics / the recorded `Provider.BaseUrl`).

## Rollback

Delete the provider (cascades to its models) only if you created it:

```sql
DELETE FROM Providers WHERE Name = 'Local ComfyUI (WOOD-GAME-MAIN 5080)';
```

Any function default that pointed at a local model must be re-pointed in Model Manager first. This
never touches RunPod providers.

## Notes / guardrails

- These are **not** RunPod pods — do not add them to the pod inventory, `pod-registry.json`, the
  deployment manifests, or the `runpod-pod-migration`/`runpod-pod-creation` skills. They belong to
  the local-host setup docs (`docs/flux-local-5080-comfyui-setup.md`) and this Model Manager runbook.
- One provider row (one endpoint) hosts all checkpoints — mirrors how `RunPod ComfyUI` is modeled,
  not one-provider-per-model.
- Local renders are free but occupy the 5080; FLUX fp8 is slow (~1–3 min/img) when it is eventually
  enabled. Use the local provider for development/iteration and keep RunPod Serverless for any
  production load that needs scale.
