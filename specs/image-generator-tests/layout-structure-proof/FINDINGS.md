# B-119 route C1 — SDXL depth / canny structure conditioning: proof findings

**Date:** 2026-09-22 · **Host:** local ComfyUI `http://192.168.0.11:8188` (WOOD-GAME-MAIN, RTX 5080 16 GB) · **Status:** mechanism **PROVEN** for layout inheritance; the reclining discriminator is **NOT yet proven** (see §5).

This run answers one question:

> Can a **layout reference image** be contracted into an SDXL render with **Depth** or **Canny** ControlNet, so the two-body arrangement is inherited while the prompt supplies a new location and clothing?

It also runs the two comparison arms that make the answer meaningful: **no structure** (text only) and the existing app route (**OpenPose**).

## 1. What was done

| Step | Detail |
|---|---|
| T01 install | `helpers/local-comfyui-host/install-sdxl-controlnets.ps1` installed the SDXL depth + canny weights on the host (hash-pinned; re-run skips verified files). Doc: `docs/local-comfyui-model-manager-setup.md`. `ControlNetLoader` lists both with **no restart**. |
| T02 extract | `DepthAnythingV2` (vitl), `CannyEdgePreprocessor` (100/200), and `DWPreprocessor` structures extracted from the reference at the generation canvas. Maps: `runs/20260922-012314-c1-structure/00-maps/`. |
| Render matrix | 2 checkpoints × 4 conditioning variants = 8 renders, **identical seed and settings**, so the only variable is the conditioning. |
| Measurement | `measure-arrangement-match.py` — silhouette IoU + vertical/horizontal mass-profile correlation of each render against the reference (rembg `isnet-general-use`). |

## 2. Exact configuration (provenance — reproduce from `run-manifest.json`)

- Reference: `specs/image-generator-tests/sex-slideshow/runs/20260914-02-slideshow-hq/step14-woman-bends-over.png` (1248×832, scaled to canvas)
- Canvas **1216×832**, seed **20260922**, **30 steps**, CFG **5.0**, `dpmpp_2m_sde` / `karras`, denoise 1.0, **negative empty**
- Checkpoints: `bigLust_v16.safetensors`, `juggernautXL_ragnarok.safetensors`
- Strengths: openpose 0.8 · depth 0.75 · canny 0.6 (B-097 / prompt-compiler-standard range)
- Depth: `depth_anything_v2_vitl.pth`, resolution 1216 · Canny: low 100 / high 200, resolution 1216 · DWPose: hands disabled, body+face enabled, `yolox_l.torchscript.pt` + `dw-ll_ucoco_384_bs5.torchscript.pt`
- ControlNet weights: `controlnet-depth-sdxl-1.0.safetensors` (2,502,139,134 B, sha256 `66a6813e…`, byte-identical to the RunPod worker manifest), `controlnet-canny-sdxl-1.0.safetensors` (2,502,139,136 B, sha256 `b2e7d392…`), `thibaud-openpose-xl2\OpenPoseXL2.safetensors`

**Prompt (identical for all 8 renders, full text):**

> photorealistic photograph of a man and a woman in a sunlit modern living room, the man standing upright wearing a plain dark grey t-shirt and blue jeans, the woman bent forward at the waist wearing a light grey t-shirt and blue jeans, wooden floor, large window with warm afternoon light, light grey wall, full body head to toe, both fully clothed, natural skin texture, 35mm, sharp focus

The reference is a plain grey studio with both figures partly undressed; the prompt asks for a sunlit living room with both clothed. **A faithful render must therefore change the scene and the clothing while keeping the arrangement** — that is the discrimination.

## 3. Measured arrangement match (all variants, sorted by IoU vs the reference)

| Variant | IoU | row corr | col corr | centroid dx,dy (px) | area ratio |
|---|---|---|---|---|---|
| `bigLustv16-canny` | **0.966** | 0.998 | 0.999 | −0.6, −1.2 | 1.00 |
| `juggernaut-depth` | **0.962** | 0.997 | 0.999 | −0.5, −1.0 | 0.99 |
| `bigLustv16-depth` | **0.961** | 0.997 | 0.999 | −0.6, −1.9 | 1.00 |
| `juggernaut-canny` | **0.946** | 0.994 | 0.997 | +1.5, −3.1 | 1.00 |
| `bigLustv16-openpose` | 0.796 | 0.915 | 0.983 | 0.0, +13.0 | 0.98 |
| `juggernaut-openpose` | 0.793 | 0.961 | 0.986 | −2.0, −14.6 | 0.89 |
| `bigLustv16-text` | 0.259 | 0.731 | 0.593 | +80.6, −52.6 | 0.74 |
| `juggernaut-text` | 0.131 | 0.839 | 0.185 | +138.0, −24.3 | 0.48 |

Raw data: `runs/20260922-012314-c1-structure/arrangement-match.json`.

## 4. Honest per-image review (every render inspected)

| Variant | Verdict | Notes |
|---|---|---|
| `bigLustv16-canny` | **PASS (arrangement) / CAVEAT** | Arrangement reproduced almost exactly, new sunlit room. **But it reproduces the source's garment state** — jeans pulled down around the thighs, as in the reference. Canny edges encode the reference's clothing folds, so it cannot be told "same arrangement, new clothing". The plausibly explicit output is a direct consequence. |
| `juggernaut-depth` | **PASS** | Closest render to the reference arrangement (man upright left, woman bent forward right, heads adjacent), relocated into the prompted living room. Same garment-mass coupling visible (unusual belt/cuff shapes) but far weaker than canny. |
| `bigLustv16-depth` | **PASS** | Arrangement held; new room; clothing reads as the prompt except the woman's jeans also sit low — the depth mass of the pulled-down jeans is being re-interpreted. |
| `juggernaut-canny` | **PASS (arrangement) / CAVEAT** | Same over-coupling: garment folds from the reference reappear (jacket-like wrap at the man's thighs). |
| `bigLustv16-openpose` | **PARTIAL** | Two people, roughly right arrangement, but the bent-over torso is approximated (row corr 0.915, centroid shifted 13 px down) — the skeleton carries *a* pose, not this one. |
| `juggernaut-openpose` | **PARTIAL** | Same (row corr 0.961, centroid offset −14.6 px, area ratio 0.89 = slimmer figures). |
| `bigLustv16-text` | **FAIL (as intended)** | Two people in the right kind of room, but the **wrong arrangement** — the man is the one bent over and the layout is mirrored/reshuffled (IoU 0.259). This is the baseline proving the prompt alone does not place them. |
| `juggernaut-text` | **FAIL (as intended)** | Both figures upright, facing each other (IoU 0.131). |

No render added a person, dropped a figure, or produced fused/extra limbs; all 8 are complete head-to-toe two-person frames.

## 5. What is proven, and what is NOT

**Proven:**
1. The host can run SDXL depth and canny ControlNet (weights installed, verified, no restart needed).
2. **Depth and Canny inherit a two-body arrangement essentially exactly** (IoU ≥ 0.946, centroid within ~3 px) while the prompted location and clothing change — the C1 mechanism works locally on BigLust and Juggernaut, in 16 GB, with no cloud call.
3. Text alone does not place two people (IoU 0.13–0.26) — this is now measured, not asserted.

**NOT proven:**
- **That depth/canny beat OpenPose for the reclining/multi-body case.** That is the headline claim of route C1, and **this reference does not test it**: it is a bent-forward-at-the-waist layout with straight legs, i.e. inside OpenPose's working class (standing/squatting/kneeling hold; lying/all-fours re-pose). OpenPose scored 0.79 here, so the A/B was *not* discriminating.
- Canny's "new clothing" behaviour: at strength 0.6 it reproduces the source's garment state. Depth is the better structure for "same arrangement, different scene and clothing".

**Confounds to state plainly:**
- The OpenPose arm here is *DWPose-extract-from-photo → OpenPose ControlNet*. The extract is visibly approximate (two skeletons detected, but with the known dot-cluster face rendering and distorted limb topology). The app's real OpenPose path uses an authored pose preset (B-118), not a photo extract, so this arm understates the app route's fidelity while overstating the C1 advantage. Do not quote 0.79 as "OpenPose's ceiling".

## 6. Next step (the actual reclining test)

Re-run the same harness against a **genuine reclining / lying two-body layout reference** — the class OpenPose provably re-poses (`specs/Planning/B-117-pose-controlnet-render`, `openpose-pose-library` notes: lying silhouette ≈ standing silhouette in 2D). Success criterion is pre-declared and measurable with the existing script: **`depth` IoU ≥ 0.90 while `openpose` IoU stays < 0.80 and the rendered figures stay lying.** Only then is route C1's discriminator proven.

The harness is already parameterised — no new code is needed beyond a different `-Reference`:

```powershell
powershell -ExecutionPolicy RemoteSigned -File specs/image-generator-tests/layout-structure-proof/run-c1-structure-proof.ps1 `
  -Reference <reclining-two-body-reference.png> `
  -OutDir specs/image-generator-tests/layout-structure-proof/runs/<yyyyMMdd>-c1-reclining
```

Then, as a separate step and never in the same run (one variable at a time): regional IP-Adapter identity per subject (B-119 **T08**) over the winning depth variant.
