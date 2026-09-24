# Dual-base-location on the LOCAL ComfyUI host

RunPod Serverless was the proof-of-concept for this pipeline. These are the same graphs and prompts
executed against the local ComfyUI host (`http://192.168.0.11:8188`, WOOD-GAME-MAIN RTX 5080 16 GB).

## One command

```powershell
powershell -ExecutionPolicy RemoteSigned -File specs/image-generator-tests/dual-base-location/run-local-dual-location.ps1 `
  -ComfyUiUrl http://192.168.0.11:8188
```

Stages, each writing its own `workflow.json` + `images.json` beside the render, plus `run-manifest.json`:

| Stage | Output |
|---|---|
| `03-studio-a` / `04-studio-b` | FLUX + XLabs OpenPose figure renders on white (28 steps, `XlabsSampler`) |
| `05-composites` | rembg cutout + placement onto each location, plus the harmonization mask (`composite-two-poses.py`) |
| `06-harmonized-<loc>-<a\|b>` | the 4 base images: bedroom-A/B, outdoors-A/B |

Pose skeletons and location references are copied from a source run (`-SourceRun`), never regenerated —
regenerating a location would change the room.

### Prerequisite

The figure stage needs the XLabs FLUX runtime. On a stock ComfyUI it is absent, and the runner says so
before queueing anything:

```
powershell -NoProfile -ExecutionPolicy Bypass -File helpers/local-comfyui-host/provision-xlabs-flux.ps1 -RestartComfyUi
```

## Graph portability

Samplers, seeds, denoise, masks, prompts and canvas are identical to the serverless proofs. The only
change is the FLUX loader, because the pod served `flux1-dev-fp8.safetensors` as a **checkpoint** while
the local host has it as a **UNET** under `models/diffusion_models`:

- `CheckpointLoaderSimple` → `UNETLoader`
- `CLIPTextEncode.clip` from `["4",1]` → `["2",0]` (`DualCLIPLoader`, already present in the pod graphs)

Ported graphs live in `proofs-local/`. Pixel-identical output is not achievable (a newer ComfyUI and
different launch flags change accumulation), so a local run is numerically close to the serverless run,
not bit-exact. Where it matters it agrees: the local `bedroom-b` reproduces the serverless run's framing,
figure placement, room preservation, and its quirk of the man turning his head toward camera.

## Performance — the "the host is slow" trap

The first local attempt measured **117 s/step** (~55 min for one render) and never finished a job.
The GPU was at 100% utilisation but drawing only **61 W** — the signature of SMs stalled on weight
transfers, not a slow card.

Cause: LM Studio's `llama-server` (`qwen2.5-vl-7b-instruct-abliterated`, `--n-gpu-layers 999999`) held
~6 GB of the 16 GB card, leaving ComfyUI to stage an 11,350 MB UNet + 4,777 MB T5 through ~9.5 GB —
so every step evicted and re-staged weights. The serverless proof was not a 16 GB comparison either:
`endpoints.json` lists `gpuTypeId: "RTX 4000 Ada"` (**20 GB**) and that card was otherwise idle.

| | Before | After |
|---|---|---|
| Per step | 117.0 s/it | **1.06 s/it** |
| 28-step render | ~55 min, never completed | **29 s** |
| Contention | `lms ps` = 1 model loaded | unloaded (`lms unload --all`, JIT-reloads on demand) |

**Check `lms ps` before blaming the host.** Free VRAM was the fix; `--fast` is *not* — it broke the Qwen
edit graph (below) and must never be added to the ComfyUI launcher.

## Identity stage — `07-identity/`

The face pass mirrors the app rather than approximating it. There are **two** identity paths, and the runner
reproduces either — it picks by looking at the bindings:

| App path | Instruction | Reference |
| --- | --- | --- |
| `SceneImageService.EnqueueIdentityAsync` (Studio) | `BuildFaceOnlyIdentityInstruction` — long, feature-explicit | each character's approved pack **canonical Front** asset |
| `SceneImageService.EnqueueEditorIdentityAsync` (face picker in `ImageEditWorkspace.razor`) | `BuildEditorIdentityInstruction` — short, face-primary, names the area | caller-chosen asset **per face**, plus `VisibleLocator` |

All bindings carrying `visibleLocator` → editor path; none carrying it → face-only path; mixed → throws.
The editor path is the one that can say *where* a face is and *which view* to apply, so this run uses it.
`SceneImageStudio.razor` → `EnqueueIdentityAsync` **discards** the freeform instruction it passes and persists
the built string instead — which is why the Studio currently cannot pick a view.

### One character per pass (what the proof harness required)

**In the harness configuration, two references in a single edit did not transfer two faces — the second
reference was rendered as an extra figure.** Verified on all four bases: one edit carrying Becky + Dean
produced a third face in both A poses and a merged man in `bedroom-a`. Those results are retired to
`artifacts/tmp/superseded-two-ref-third-face/`, not deleted. Same failure class as
`specs/001-rp-prompt-redesign/debug/043` §6 ("a second identity reference does NOT transfer identity — it
adds a person", 92.6% scene change).

**Trigger isolated 2026-09-21 — it is the INSTRUCTION, not the reference count or the reference view.** Two
references in ONE edit, on `outdoors-a`:

| Instruction builder | References | Result | Artifact |
|---|---|---|---|
| `BuildFaceOnlyIdentityInstruction` (**Studio** action) | canonical Front ×2 | clean | `00592` / `00593` |
| `BuildFaceOnlyIdentityInstruction` | Profile ×2 | clean | `00595` |
| `BuildEditorIdentityInstruction` (**editor** path) | canonical Front ×2 | **extra face tiled into the frame** | `00594` |
| `BuildEditorIdentityInstruction` | Profile ×2 | **extra face** | `00578`–`00581` |

Artifacts: `artifacts/tmp/b126-repro-app-identity/`. So the **Studio** identity action is not affected
(matching the app's observed behaviour), while the **editor** identity path — which runs this same builder
verbatim in the app — is. The reference **view** is irrelevant. See
`specs/Planning/B-126-multi-character-scene-composition/plan.md` §2.

Splitting the work into one single-reference edit per character fixes it, chaining each result into the next
pass's source. The man's applied identity survived the woman's later pass, so nothing had to be re-applied.

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-sequential-identity.ps1 `
  -ComfyUiUrl http://192.168.0.11:8188 `
  -SourceImage "<run>/06-harmonized-bedroom-a/img-local-bedroom-a_0.png" `
  -IdentityBindingsPath "<run>/07-identity/bindings-A.json" `
  -OutDir "<run>/07-identity/bedroom-a"
```

Each base folder ends up with `final.png`, `identity-manifest.json` (pass order, view and locator per step) and
`step<N>-<character>/` holding that pass's own output. The single-binding JSON the script writes per pass is
the same shape the app persists on `SceneImageRecord.IdentityReferenceBindingsJson` (ordinal, characterName,
targetKey, visibleLocator, fileRelativePath, sha256) and resolves under the identity storage root. Ordinals
must be 1..N contiguous and every sha256 is verified before the job is queued — the app's own fail-fast.

**The reference view must match the head in the frame.** Pose A: the woman faces left, the man faces right.
Pose B: both face right. So `bindings-A.json` pairs the woman with `ProfileLeft` (`image right facing left`)
and `bindings-B.json` pairs her with `ProfileRight` (`image right facing right`); the man uses `ProfileRight`
(`image left facing right`) in both.

Proof of alignment is executable, not asserted: `SceneImageServiceJobTests.BuildFaceOnlyIdentityInstructionIsPinnedForTwoCharacterIdentityPass`
asserts the app's exact string, and the runner builds that same text. Change either and the other fails.

All four bases were pushed through the stage with Qwen-Rapid-AIO-NSFW-v23 at 8 steps / CFG 1 /
`euler_ancestral` / `beta` / denoise 1.0.

## Observed behaviour (QC)

- **Scene preserved, no drift, no added person** on all four — geometry, lighting and framing held. The
  no-added-person result comes from the sequential single-reference passes above, not from the stage itself.
- **The man's identity lands in all four** — an unambiguous match to `ProfileRight` (nose, brow, jaw,
  hairline, stubble all carried).
- **The woman's match is plausible but not provable at this size** — her head is only ~80-125 px in frame,
  so upscaling adds no detail. A tighter framing or a face-region crop would be needed to call it.
- **Heads rotate toward camera** in the B poses (the B base already did this for the man; the pass does it
  for both). Strict right-profile is not preserved by this stage.
- **Reference bleed** — hair becomes the reference's updo; the woman's top shifts grey → white in
  `bedroom-b`. This is the documented garment/expression import class of failure.
- **Expression is taken from the reference**, not the source (repo-specific finding, `specs/001-rp-prompt-redesign/debug/043`).
- Becky reads as pregnant in the bases (prompt artefact: "curvy woman's body" + loose tee), and the identity
  pass inherits it from the source.

## Open items

- Angle matching does not exist in the app (`SceneImageHeadAngleResolver` is specs-only). This run side-steps
  it by choosing the reference view by hand for each pose; automating that choice is still open.
- The Studio path cannot choose a reference view or name a face area (canonical Front + face-only instruction
  only). `EnqueueEditorIdentityAsync` can do both. Giving the Studio that capability is the UI follow-up.
- **The editor identity path needs sequencing; the Studio path does not.** `EnqueueEditorIdentityAsync`
  dispatches one edit for all selections and its instruction is the unsafe one (see the matrix above). Fixing
  it is B-126 Phase 1 and does not require changing the Studio action.
- **A face-detection recap check must ignore forearm false positives.** MediaPipe flagged the woman's tattooed
  forearm as a third face in `bedroom-b`; keeping only boxes whose bottom edge sits above the head line
  removes it. Do not read a raw detector count as "a third person was added".
- The 3 `SdxlSceneImagePromptBuilderTests` failures in the RolePlay suite are pre-existing and belong to a
  different workstream (see repo notes, 2026-09-11).
