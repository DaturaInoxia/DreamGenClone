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
| Base URL (from other LAN hosts / the webapp box) | `http://192.168.0.16:8188` |
| Protocol in Model Manager | `ImageProtocol = ComfyUi` (`/prompt`), **not** `ComfyUiServerless`, **not** a pod |
| Checkpoints served | `juggernautXL_ragnarok.safetensors`, `bigLust_v16.safetensors`, `ponyDiffusionV6XL_v6.safetensors`, `flux1-dev-fp8.safetensors` |
| Identity stack present | IP-Adapter PLUS FACE / PuLID / FaceID / DWPose (same as the serverless volume, minus Qwen-Edit AIO) |
| Firewall | inbound TCP 8188 allowed on LAN; never expose unauthenticated ComfyUI publicly |

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
> **CAVEAT — stock is SFW-aligned:** this local model is the stock `qwen_image_edit_2511_fp8mixed`,
> NOT the `Qwen-Rapid-AIO-NSFW-v23` serverless variant, so explicit/adult edits are safety-blanked
> (genital region → mannequin). Use local for implied/SFW edits; switch the default back to the
> RunPod NSFW-AIO editor for explicit edits. Registration was done via direct DB
> (model id `7bc5d932-4596-4b96-ac73-5162516a162f`); `local-comfyui-configure` does NOT create
> editor rows yet.

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

Run from the repo root on that host, pointing at the ComfyUI host:

```powershell
# On the ComfyUI host itself:
dotnet run --project DreamGenClone.DbQuery -- local-comfyui-configure http://127.0.0.1:8188

# From another LAN host (the webapp box reaching WOOD-GAME-MAIN):
dotnet run --project DreamGenClone.DbQuery -- local-comfyui-configure http://192.168.0.16:8188
```

The command is **idempotent** (upserts provider + model rows in one transaction). Re-running with a
changed Base URL updates it in place. It is the supported path; do not hand-INSERT these rows.

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
