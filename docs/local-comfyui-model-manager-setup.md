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
| `flux1-dev-fp8.safetensors` | FLUX.1-dev fp8 (Local ComfyUI) | **Unknown / Unknown** | ⛔ **disabled** |

> **Why FLUX is disabled (not a fallback — a hard constraint):** the app's `SceneImageModelFamily`
> enum only has `Pony / Sdxl / Api`, and the ComfyUI client only builds Pony/SDXL workflows. An
> **enabled** FLUX row would make the app route an SDXL workflow at a FLUX checkpoint and fail. The
> FLUX row is registered **present-but-disabled** so Model Manager records the local checkpoint
> exists; it becomes routable only after the B-112 Flux-family code slice lands
> (`specs/Planning/B-112-flux-local-5080-host/plan.md` §7). Do not enable it before then.

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
