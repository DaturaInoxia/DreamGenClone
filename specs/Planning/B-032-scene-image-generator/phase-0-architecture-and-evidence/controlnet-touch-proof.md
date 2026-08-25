# ControlNet Touch Proof

**Status:** OpenPose-only and built-in Juggernaut inpainting proofs failed; do not integrate  
**Created:** 2026-08-24  
**Architecture:** `continuity-rendering-architecture.md`  
**Backlog:** B-097  

## 1. Proof Question

Can Juggernaut XL Ragnarok plus an SDXL-compatible pose control preserve one asymmetric, fully clothed touch relationship across four predetermined seeds without selecting a lucky result?

## 2. Frozen Requirement

- Exactly two fully clothed adults.
- Woman faces man.
- Woman's open right palm contacts the center of the man's shirt-covered chest.
- Woman's left arm remains down.
- Man's two hands remain down and do not touch the woman.
- Playful/flirtatious, non-explicit mood.
- No location-detail requirement in this proof.
- No character-identity consistency requirement in this proof.

## 3. Pass Gate

Use one unchanged prompt, negative prompt, pose control asset, workflow, and control configuration for four predetermined seeds.

Every render must pass all of these constraints:

| Constraint | Required |
|---|---|
| Visible cast | Exactly two adults |
| Clothing | Both fully clothed |
| Required contact | Woman's open right palm on center of man's shirt-covered chest |
| Forbidden contact | Man does not touch woman |
| Other limbs | Woman's left arm and both of man's arms remain down |
| Topology | No major limb/hand defect obscures the action |

Gate result is `PASS` only when all four renders pass every constraint. Do not generate additional seeds to replace failures.

## 4. Current Host Inventory

Populate from the current ignored environment files and live host. Never paste API keys, bearer tokens, private keys, or encrypted provider values.

| Item | Value |
|---|---|
| Inventory captured UTC | Initial: `2026-08-24T16:55:28Z`; post-install verification: `2026-08-24T17:09:42Z` |
| RunPod pod ID | `7sx63d6eu80uwr` |
| Pod name | `desperate_gold_weasel-migration` |
| GPU | NVIDIA A40, 46068 MiB; driver 580.159.04 |
| ComfyUI URL | `https://7sx63d6eu80uwr-3000.proxy.runpod.net` at capture time; re-read ignored env after migration |
| Direct SSH host/port | `root@194.68.245.147:22087` at capture time; migration-sensitive |
| OS/host | Ubuntu 22.04.5 LTS; container hostname `nba3e9ce8c740` |
| ComfyUI install/path mapping | Runtime `/ComfyUI`; persistent models `/workspace/comfyui/models` through extra model paths |
| ComfyUI revision | `ee9547ba31f5f2c1de0211a09c3fb829bd8e25e6` |
| Python version | 3.10.12 |
| Torch/CUDA versions | Torch 2.6.0; CUDA 12 runtime packages installed (exact Torch CUDA build still to record if needed) |
| Custom node directories/revisions | `ComfyUI-Manager` at `402e2c384f338d0ed0a7fb19caa93f29a0dc35fd`; persistent `comfyui_controlnet_aux` at `e8b689a513c3e6b63edc44066560ca5919c0576e`, linked into `/ComfyUI/custom_nodes` |
| Relevant nodes | Built-in `ControlNetLoader`, `ControlNetApply`, `ControlNetApplyAdvanced`, `DiffControlNetLoader`, `InpaintModelConditioning`, `VAEEncodeForInpaint`; installed `DWPreprocessor` exposes body/face/hand controls, TorchScript selectors, Xinsir stick scaling, and OpenPose JSON |
| Checkpoint inventory | `RealVisXL_V5.0_fp16.safetensors`, `flux1-schnell-fp8.safetensors`, `juggernautXL_ragnarok.safetensors`, Pony V6, SDXL base/refiner, SD 1.5, SD 2.1 |
| Juggernaut SHA-256 | `dd08fa32f98d05a2443ca1419e46df1575a0811f6e3b246d9dd47ff20f5eb66a` |
| ControlNet model inventory | `xinsir-controlnet-openpose-sdxl-1.0.safetensors`, 2,502,139,104 bytes, SHA-256 `b8524e557a7df60d081f5d4a0eb109967d107df217943bf88c2d99b9ebcc06c5`; persistent file linked into `/ComfyUI/models/controlnet` |
| Adapter model inventory | Empty (`clip_vision` and `loras`); no IP-Adapter/PuLID/InstantID nodes |
| Preprocessor model inventory | DWPose node installed; detector/pose assets not downloaded until first extraction and will cache under the persistent node's `ckpts` directory |
| Python dependency delta | `opencv-python-headless==4.10.0.84`, `matplotlib==3.10.9`, `scikit-image==0.25.2` plus their resolved dependencies; retained NumPy 1.26.4 and Torch 2.6.0+cu124; intentionally omitted ONNX Runtime and unrelated preprocessors |
| Free storage | Post-install: container overlay 4.0 GiB free; persistent `/workspace` reports 139 TiB available; node 246 MiB and ControlNet directory 2.4 GiB |
| Live verification | PID 12409, `/usr/bin/python3.10 main.py --listen --port 3000`; public `/system_stats` HTTP 200; production `/object_info` exposes `DWPreprocessor` and lists the Xinsir weight in `ControlNetLoader` |

## 5. Dependency Decision

Do not select packages by assumption. Complete the inventory, then record:

| Decision | Selected value | Evidence |
|---|---|---|
| Pose preprocessor/node | `Fannovel16/comfyui_controlnet_aux` pinned at `e8b689a513c3e6b63edc44066560ca5919c0576e` (version 1.1.5), `DWPreprocessor`/DWPose full output | Project documents DWPose body/face/hand hint generation and API-visible OpenPose JSON; Apache-2.0 |
| SDXL pose-control model | Xinsir `controlnet-openpose-sdxl-1.0`, standard `diffusion_pytorch_model.safetensors`, repository SHA `23f966cd5cfdd3f7729c903e243d87152162d2b7` | Public, ungated, Apache-2.0, trained from SDXL base; model card reports higher HumanArt pose mAP than compared open models |
| Hand-keypoint support | Use DWPose full control image with body, face, and hand keypoints; proof determines whether the selected ControlNet honors hand-to-chest contact sufficiently | Aux project explicitly supports full DWPose/OpenPose hand output; Xinsir model card's sample disables hands, so hand adherence remains a measured risk, not an assumption |
| Control application node | ComfyUI core `ControlNetLoader` + `ControlNetApplyAdvanced` | Already exposed by live `/object_info`; official ComfyUI documentation supports this loader/apply path |
| Control asset authoring method | Extract DWPose from the existing successful clothed-touch image, persist the rendered hint image and OpenPose JSON, then keep both unchanged for all four seeds | Uses a pose already proven to represent the target relation and avoids manual skeleton guessing in the first proof |
| Required downloads | One pinned custom-node repository plus Python requirements/preprocessor assets; one 2,502,139,104-byte SDXL ControlNet weight | Primary project/model documentation and live HEAD metadata |
| Persistent paths | Custom node clone `/workspace/comfyui/custom_nodes/comfyui_controlnet_aux` symlinked into `/ComfyUI/custom_nodes`; ControlNet weight `/workspace/comfyui/models/controlnet/xinsir-controlnet-openpose-sdxl-1.0.safetensors` | Keeps repositories and large weights off the 5 GiB container overlay; only Python environment changes remain container-local and reproducible |
| Expected weight SHA-256 | `b8524e557a7df60d081f5d4a0eb109967d107df217943bf88c2d99b9ebcc06c5` | Hugging Face `x-linked-etag` for the standard safetensors at pinned repository revision; verify with `sha256sum` after download |

Host modifications require an explicit plan listing downloads, repositories, versions, storage paths, restart procedure, and rollback-by-forward-fix approach.

Installation conclusion: the host can now derive a hand-aware DWPose control image and load the selected SDXL pose-control weight. Keep identity adapters, LoRAs, depth controls, and detailers outside this first gate.

### Proposed Host Change Plan

Executed 2026-08-24 with the following evidence-backed refinement: an unconstrained install of the repository's full requirements would have replaced NumPy 1.26.4 with 2.2.6 and installed three OpenCV 5.0 distributions. Isolated ComfyUI startup probes identified the actual DWPose import chain, so only pinned OpenCV, Matplotlib, and scikit-image dependencies were added. `DWPreprocessor` then loaded successfully without ONNX Runtime; the proof workflow must select both TorchScript assets explicitly.

1. Create `/workspace/comfyui/custom_nodes` if missing.
2. Clone `https://github.com/Fannovel16/comfyui_controlnet_aux` into the persistent custom-node directory and detach at commit `e8b689a513c3e6b63edc44066560ca5919c0576e`.
3. Symlink that persistent clone into `/ComfyUI/custom_nodes/comfyui_controlnet_aux`; do not copy it onto the container overlay.
4. Install only the validated DWPose runtime dependencies into the current ComfyUI Python environment, capturing package changes and errors. Do not install optional ONNX, identity, depth, detailer, or unrelated preprocessor packages.
5. Download the standard Xinsir safetensors from pinned repository revision `23f966cd5cfdd3f7729c903e243d87152162d2b7` directly to a temporary file under `/workspace/comfyui/models/controlnet`.
6. Verify size and SHA-256, then rename atomically to `xinsir-controlnet-openpose-sdxl-1.0.safetensors`.
7. Restart ComfyUI using the host's actual launch/supervision mechanism after inspecting it; do not assume a command.
8. Link the persistent weight into `/ComfyUI/models/controlnet` because this host's `extra_model_paths.yaml` maps only checkpoints; restart and verify `/object_info` exposes `DWPreprocessor` and the ControlNet loader lists the new weight.
9. If any step fails, preserve logs, remove only incomplete temporary artifacts created by that step, and repair forward from the documented state. Do not alter Juggernaut or other existing checkpoints.

Blast radius: the RunPod ComfyUI host only. No application code, database, prompt builder, or existing workflow changes. Persistent storage increases by approximately 2.5 GB plus the auxiliary repository/preprocessor assets; Python requirements modify the current container environment and must be captured for host recreation.

## 6. Proof Assets

Expected local paths:

| Artifact | Path / status |
|---|---|
| API workflow JSON | Revision 1: `helpers/runpod/workflows/controlnet-touch-proof.json`; revision 2: `helpers/runpod/workflows/controlnet-touch-proof-v2.json`; revision 3: `helpers/runpod/workflows/controlnet-touch-proof-v3.json` |
| Pose control image | Revision 1: `touch-pose-v1.png`, SHA-256 `b87c55dc50094d2090aa42a7bc6a293ff752db08ddf995f4c3edca95da68624a`; revision 2: `touch-pose-v2.png`, SHA-256 `eec2eab1911e41ec643a6e04ab7cdd4c34416c5006e92c2bd9673f83ff38ca27`; revision 3: `touch-pose-v3.png`, SHA-256 `c76f06ed32a9921bbf898e43d66d03336346862b1a3dbfb7b00a94c74cca5586` |
| Candidate source pose | `artifacts/tmp/images/juggernaut-suite/dg_juggernaut-flirt-clothed-24689_20260824-121954.png` - existing successful clothed touch; evaluate as DWPose extraction source |
| Positive prompt | Frozen in workflow and manifest; exactly two clothed middle-aged adults, woman's right palm on shirt-covered chest, all other arms down |
| Negative prompt | Frozen in workflow and manifest; anatomy, extra-person, nudity, reciprocal-touch exclusions |
| Parameter manifest | Revision 1: `manifest-v1.json`, frozen `2026-08-24T17:35:49Z`; revision 2: `manifest-v2.json`, frozen `2026-08-24T17:51:07Z`; revision 3: `manifest-v3.json`, frozen `2026-08-24T17:56:54Z`; `manifest.json` points to revision 3 |
| Output directory | `artifacts/tmp/images/controlnet-touch-proof/` |

The workflow must name the checkpoint `juggernautXL_ragnarok.safetensors` and use the SDXL/Juggernaut sampler baseline unless the proof records evidence for a deliberate change.

## 7. Predetermined Seeds

Freeze before the first controlled render:

1. `24690`
2. `24691`
3. `24692`
4. `24693`

These seeds must not be replaced after seeing results.

## 8. Results

| Seed | Output path | Cast | Clothing | Required contact | Forbidden contact absent | Limb topology | Result |
|---:|---|---|---|---|---|---|---|
| 24690 | `render-24690.png`; SHA-256 `795e9d9b653369b6473cbf4f761b771bb8d739eb924c3a513b5846885fda54b6` | pass: 2 | pass | **fail: collar/neck, not center chest** | pass | pass | **FAIL** |
| 24691 | `render-24691.png`; SHA-256 `0632955f47a2bcd340aa229bfbe327af2e3ad795d6bf31d32ee70849e4deda84` | pass: 2 | **fail: woman topless** | **fail: collar/neck, not center chest** | pass | pass | **FAIL** |
| 24692 | `render-24692.png`; SHA-256 `1ada7155c7fcdefe17499142ecc55a291750b4b56c6fa133f205dc73a3f1aa70` | pass: 2 | pass | **fail: collar/neck, not center chest** | pass | pass | **FAIL** |
| 24693 | `render-24693.png`; SHA-256 `39b4b39b1405f011c0271176fb8c49d8677c946b6af20e8f147bcfac931d743d` | pass: 2 | pass | **fail: collar/neck, not center chest** | pass | pass | **FAIL** |

## 9. Gate Decision

**Decision:** FAIL - 0/4 renders passed every constraint.

**Evidence summary:** The controlled workflow executed successfully for all four predetermined seeds. It consistently preserved exactly two people, prevented reciprocal touching, kept the non-contact arms down, and produced usable topology. It did not bind the required hand to the center chest: all four placed it at the collar/neck. Seed 24691 additionally ignored the clothing constraint. This proves that the current OpenPose control is effective for macro limb ownership/direction but is insufficiently positioned for the required contact point, and prompt negatives do not guarantee clothing.

**Single next discriminating action:** Create proof revision 2 by translating only the woman's required right wrist and right-hand keypoint cloud lower onto the man's sternum in the structured pose. Keep prompt, negative prompt, models, strength, sampler, all other keypoints, and seeds unchanged. Freeze new hashes before rerunning the same four seeds. Do not search additional seeds.

### Revision 2 Results

Revision 2 translated only the woman's right wrist and right-hand keypoint cloud to `(465, 700)`. The raw extraction, all other pose geometry, prompt, negative prompt, checkpoint, ControlNet model and schedule, sampler, resolution, and seeds remained unchanged.

| Seed | Prompt ID | Output path | Cast | Clothing | Required contact | Forbidden contact absent | Other limbs | Topology | Result |
|---:|---|---|---|---|---|---|---|---|---|
| 24690 | `5e96996b-d2fd-4af9-a6b8-08df8fa89afe` | `render-v2-24690.png`; SHA-256 `f84e567b78154f3b496596aaec6ffcb812faa94ae2c1796e959a99ce90d955a9` | pass: 2 | pass | **fail: partial hand at upper chest/collar, not open palm on center chest** | pass | pass | pass | **FAIL** |
| 24691 | `95e435ca-b402-4ecb-bc1f-c736258cb1c9` | `render-v2-24691.png`; SHA-256 `8654652471442d418a6401339e893f63dae7cc7b78771d5364c72bd00c70377f` | pass: 2 | **fail: woman topless** | **fail: hand hidden behind man, no visible center-chest palm contact** | pass | pass | pass | **FAIL** |
| 24692 | `0f5d0b9c-7669-414a-b926-8f94cd8bf0b5` | `render-v2-24692.png`; SHA-256 `da4f022d2a0f96ca894948ddafa7ec92635d93ba25c0efb8d63d9cfc916c1c55` | pass: 2 | pass | pass: open palm on center chest | pass | pass | pass | **PASS** |
| 24693 | `86885687-3015-4a8a-b1c9-6ad09bfa82cb` | `render-v2-24693.png`; SHA-256 `ed44751be8e414a9d372a6cc3309e0da78018d2bb2ea1b795a39c4129f3a96be` | pass: 2 | pass | **fail: fingertips pinch upper shirt/collar, not open palm on center chest** | pass | pass | pass | **FAIL** |

**Revision 2 decision:** FAIL - 1/4 renders passed every constraint.

**Evidence summary:** Lowering the controlled wrist/hand was directionally effective: one fixed seed changed from collar contact to the required open-palm center-chest contact. The other three did not satisfy exact contact, and seed 24691 again violated clothing despite unchanged explicit positive and negative clothing instructions. Macro limb ownership, non-contact arm positions, cast count, and usable topology remained stable.

**Single next discriminating action:** Create proof revision 3 by translating only the same right wrist and right-hand keypoint cloud farther down. Keep every other frozen input unchanged and rerun the same four seeds exactly once. This tests whether the improvement from 0/4 to 1/4 is a stable positional response rather than seed-specific variance.

### Revision 3 Results

Revision 3 translated only the same right wrist and right-hand keypoint cloud from `(465, 700)` to `(465, 815)`, a 115-pixel downward step comparable to the revision-1-to-revision-2 translation. Every other frozen input remained unchanged.

| Seed | Prompt ID | Output path | Cast | Clothing | Required contact | Forbidden contact absent | Other limbs | Topology | Result |
|---:|---|---|---|---|---|---|---|---|---|
| 24690 | `23e2c098-03a5-4545-b51b-2091ff2c1759` | `render-v3-24690.png`; SHA-256 `5b04c58055eecb44111abde1dfec5e4ff48fb875bd505e85ab0164eb9f796f2b` | pass: 2 | pass | **fail: required arm reaches behind man; no chest contact** | pass | **fail: required arm raised behind man instead of contacting chest** | pass | **FAIL** |
| 24691 | `b63d8634-f1e1-4781-aaca-d9bf098d419c` | `render-v3-24691.png`; SHA-256 `db697fdf94ccf4554c6ba6ba11c778fe46a853c8c418dbefb146c869cfa83ee9` | pass: 2 | **fail: woman topless** | **fail: required hand hidden behind man; no visible chest contact** | pass | **fail: required arm routes behind man** | pass | **FAIL** |
| 24692 | `37917dfd-a796-47e1-a386-6108bc183f22` | `render-v3-24692.png`; SHA-256 `f4135c6fdeee90708e847c4adad88b6ddf8b71994db687bc691b1f5564899798` | pass: 2 | pass | **fail: required hand hidden behind man; no visible chest contact** | pass | **fail: required arm routes behind man** | pass | pass | **FAIL** |
| 24693 | `8ce28892-1299-4d2d-afbf-c8aa70ca8a36` | `render-v3-24693.png`; SHA-256 `64b1184f5597ad63b19cc118248bf3547a0db1a033a8aeb8691435b712d6bdbb` | pass: 2 | pass | **fail: required arm/hand behind woman; no chest contact** | pass | **fail: required arm does not reach chest** | pass | **FAIL** |

**Revision 3 decision:** FAIL - 0/4 renders passed every constraint.

**Final OpenPose-only decision:** FAIL. The three controlled revisions scored `0/4`, `1/4`, and `0/4`. Vertical wrist/hand translation changed the output causally but did not produce stable front-surface contact. Moving farther down caused the model to route the controlled arm behind the bodies, demonstrating that the 2D skeleton does not encode the depth/occlusion relation needed to bind a palm to the front of the man's chest. The repeated topless result for seed 24691 independently demonstrates that prompt positives and negatives do not guarantee clothing.

**Architecture consequence:** Do not integrate this OpenPose-only workflow into the application. Preserve it as evidence that Xinsir OpenPose is suitable for macro pose ownership and direction, not exact contact geometry. The next proof must add one explicit front/back contact mechanism, such as depth-aware or masked local conditioning, and test clothing enforcement as a separate variable. Do not install or integrate another model/control family until that proof design, dependency delta, storage impact, and rollback-by-forward-fix plan are documented and approved.

## 10. Masked Inpainting Proof

Revision 1 used the successful OpenPose revision-2 seed `24692` as a frozen 1024x1024 source. A feathered ellipse bounded by `(285, 430)` and `(535, 700)` masked the hand/chest junction. Built-in `VAEEncodeForInpaint` regenerated only that region at full denoise, and `ImageCompositeMasked` restored every source pixel outside the mask. No model, node, package, or dependency was installed.

| Seed | Prompt ID | Output path | Contact | Clothing/identity | Exterior preservation | Topology | Result |
|---:|---|---|---|---|---|---|---|
| 24690 | `e2e0239e-e10e-4521-885c-38e2211f4ddf` | `inpaint-render-24690.png`; SHA-256 `a71878dd0ad3e7259ec307fd3b366b47d29dda8ccb2a16d3f7d6e51bcafa2f37` | **fail: wrong hand chirality contacts lower chest** | pass | pass | **fail: thumb/pinky orientation is reversed for her right arm; residual source fingertips at collar** | **FAIL** |
| 24691 | `4121be5b-2c5b-4d3d-9213-8ac51d0ac703` | `inpaint-render-24691.png`; SHA-256 `85a6e50d691b5a0615e4c28973ecc4b8f0583f161b9f626eef55511953943d0a` | **fail: only partial wrong-chirality fingers contact lower chest** | pass | pass | **fail: incomplete hand, reversed thumb/pinky orientation, and residual source fingertips** | **FAIL** |
| 24692 | `c41a9849-fc6d-4f62-8274-bd799a7ce977` | `inpaint-render-24692.png`; SHA-256 `311e9f8205f379a816e9a12c29dedd41b280b7750e67367228eaeb93f770c954` | **fail: wrong hand chirality on center chest** | pass | pass | **fail: thumb/pinky orientation is reversed for her right arm; residual source fingertips at collar** | **FAIL** |
| 24693 | `aa653324-de33-4593-8074-8f4487d48c6b` | `inpaint-render-24693.png`; SHA-256 `c6b9920b03234b8d1a338f51eb4cbdc8f46ae2d21c55215256a53c7fb3263b77` | **fail: wrong hand chirality on center chest** | pass | pass | **fail: thumb/pinky orientation is reversed for her right arm; residual source fingertip at collar** | **FAIL** |

**Inpaint revision 1 decision:** FAIL - 0/4 renders passed every constraint.

**Evidence summary:** Masked inpainting preserved both identities, clothing, composition, and all unmasked pixels. Several seeds reconstructed palm-to-chest contact, but all attached the wrong hand anatomy to the woman's right arm: with the back of the hand visible and fingers angled up-left, the thumb must be on the lower-left edge and the little finger on the upper-right edge; the renders reverse them. The ellipse also did not contain the complete original hand silhouette, leaving source fingertips outside the editable region in every result; seed 24691 failed to reconstruct a complete hand. Both complete source removal and explicit chirality control are required before this mechanism can pass.

**Single next discriminating action:** Preserve revision 1, then change only the mask shape so the complete original hand silhouette and contact region are editable. Keep source, prompt, negative prompt, checkpoint, sampler, denoise, compositing, and seeds unchanged. Freeze new hashes before rerunning the same seeds once. Score hand chirality explicitly; if revision 2 still reverses the hand, a later revision may change only the prompt to state the required image-space thumb and little-finger positions.

### Inpaint Revision 2 Results

Revision 2 changed only the mask bounds from `(285, 430, 535, 700)` to `(240, 420, 590, 720)`. The larger ellipse contains the complete original hand silhouette. Source, prompt, negative prompt, checkpoint, sampler, denoise, compositing, and seeds remained unchanged.

| Seed | Prompt ID | Output path | Contact/ownership | Chirality/surface | Exterior preservation | Result |
|---:|---|---|---|---|---|---|
| 24690 | `9eca535c-9b3b-49ba-b059-19bc96494dba` | `inpaint-render-v2-24690.png`; SHA-256 `93a1cf67cf393f7eafa3dc2627215d4f779996d1cad8ec4bf80a7871a28566c4` | **fail: malformed partial contact** | **fail: wrong-facing/malformed hand anatomy** | pass | **FAIL** |
| 24691 | `6a45af8e-b42f-46e3-bc69-078873d73ed2` | `inpaint-render-v2-24691.png`; SHA-256 `25e1069a1f883b35c269b23240f90fb9652b52a8c6eb881a6d7dfee43781562a` | **fail: detached partial hand does not contact chest** | **fail: no complete hand to establish chirality** | pass | **FAIL** |
| 24692 | `7ffe2143-e17c-4708-a379-34b4f9722db5` | `inpaint-render-v2-24692.png`; SHA-256 `c6147c00e3044c93b9f5e129a4d6b8531bd549b9ca2e89e37f0a876348541e32` | **fail: generated hand belongs to man and creates forbidden reciprocal contact** | **fail: wrong actor and orientation** | pass | **FAIL** |
| 24693 | `a74a4c9f-cc1d-4d7f-9f11-4e57654ca87f` | `inpaint-render-v2-24693.png`; SHA-256 `60b831d90d2b4d18c434cfdae97397dc44f36cb4adcbcb9519ed008e9fcda059` | **fail: hand edge approaches chest rather than palm lying flat** | **fail: palm faces camera instead of chest** | pass | **FAIL** |

**Inpaint revision 2 decision:** FAIL - 0/4 renders passed every constraint.

**Evidence summary:** Expanding the mask eliminated the residual source fingertips, confirming revision 1's mask-coverage diagnosis. It did not produce reliable hand ownership, chirality, contact, or surface orientation. The model variously generated a malformed hand, a detached hand, the man's hand, or a camera-facing palm. A broad local mask preserves exterior identity and composition but leaves the base checkpoint underconstrained inside the editable region.

**Single next discriminating action:** Preserve revision 2 and change only textual conditioning. State image-space anatomy explicitly: the back of the woman's right hand faces the camera, fingers point diagonally up-left, her right thumb is on the lower-left edge, her little finger is on the upper-right edge, and her palm faces away from the camera and lies flat on the man's shirt. Exclude the man's hand, a floating hand, and a camera-facing palm. Keep the revision-2 source, mask, checkpoint, sampler, denoise, compositing, and seeds unchanged.

### Inpaint Revision 3 Results

Revision 3 changed only positive and negative textual conditioning. It explicitly described the right-hand thumb/little-finger layout, back-of-hand camera orientation, palm-to-shirt surface orientation, lower-right ownership path, and forbidden man's/floating/camera-facing hands. Source, revision-2 mask, checkpoint, sampler, denoise, compositing, and seeds remained unchanged.

| Seed | Prompt ID | Output path | Contact/ownership | Chirality/surface | Exterior preservation | Result |
|---:|---|---|---|---|---|---|
| 24690 | `82784e64-b297-4173-8c45-811d8312354b` | `inpaint-render-v3-24690.png`; SHA-256 `edfa3434bf0e8242bcc8ed506d1ac0211d7dc2873859655a687de866ae407c49` | **fail: required hand is absent** | **fail: no hand to establish chirality or contact surface** | pass | **FAIL** |
| 24691 | `9bbabf9a-ac89-4b0c-baac-116291e1bc12` | `inpaint-render-v3-24691.png`; SHA-256 `ff6d426f11aca071aa879f01606e86cdd2fb4363a0e9bc775107e6c6ef925ebd` | **fail: only a partial fingertip enters the region** | **fail: no complete hand** | pass | **FAIL** |
| 24692 | `75201605-a93e-437a-bf5d-0ab1ee311e94` | `inpaint-render-v3-24692.png`; SHA-256 `0345f37c027967f717c9f229a05034cf6ef881930b8fcf113b13be218d3a4d6e` | **fail: pointing gesture instead of palm contact** | **fail: requested hand surface and finger arrangement absent** | pass | **FAIL** |
| 24693 | `be935608-269c-4253-9206-22e422463ceb` | `inpaint-render-v3-24693.png`; SHA-256 `47c4f6f867d6f6c50f2d2605c17dea0f13c8273a90d4a3a8ae150e6cbbd49eb6` | **fail: generated sleeved arm and fist belong to man** | **fail: wrong actor, hand, and surface** | pass | **FAIL** |

**Inpaint revision 3 decision:** FAIL - 0/4 renders passed every constraint.

**Final built-in inpainting decision:** FAIL. The three controlled inpaint revisions scored `0/4`, `0/4`, and `0/4`. Revision 1 demonstrated that local regeneration can preserve identities, clothing, and composition, but its mask left source fingertips. Revision 2 removed those pixels and exposed unstable ownership, chirality, and hand-surface generation. Revision 3 showed that explicit image-space anatomy language does not reliably control those properties in Juggernaut XL.

**Required-contact scoring rule:** A requested right-hand contact passes only when the hand is connected to the intended actor's right arm, thumb and little-finger positions are anatomically consistent with that arm and viewing angle, the intended palm/back surface orientation is correct, and the intended surface contacts the target. Contact location alone is insufficient.

**Architecture consequence:** Do not integrate built-in Juggernaut masked inpainting for exact interaction correction. Stop mask-coordinate and natural-language anatomy tuning. The next proof requires a mechanism that directly conditions local geometry and ownership, such as a dedicated instruction-based image editor with demonstrated hand editing, or local pose/depth/reference conditioning that encodes hand keypoints and front/back surface relationships. Any new model or node installation requires a separate inventory, dependency/storage plan, approval, and frozen-seed proof.

## 11. Handoff Log

| UTC date | Agent/host | Action | Outcome | Next action |
|---|---|---|---|---|
| 2026-08-24 | VS Code agent / local Windows host | Created persistent proof contract and froze seeds | Inventory not yet captured | Inventory live RunPod ComfyUI nodes, models, versions, and storage |
| 2026-08-24 | VS Code agent / RunPod `7sx63d6eu80uwr` | Captured live read-only host/API inventory, revisions, empty control/adapter directories, and Juggernaut checksum | Host has built-in ControlNet application nodes but no preprocessor custom node or control weights | Research and select one pinned hand-aware pose preprocessor plus one SDXL pose-control model; prepare host-change plan |
| 2026-08-24 | VS Code agent / local + RunPod metadata | Verified primary docs and pinned minimal dependencies; excluded undocumented twins weight and all identity/depth/detailer scope | Host-change plan ready; no host modifications made | Obtain explicit approval, then execute the nine-step host plan and verify node/model visibility |
| 2026-08-24 | VS Code agent / RunPod `7sx63d6eu80uwr` | Installed pinned `comfyui_controlnet_aux`, targeted DWPose dependencies, and checksum-verified Xinsir SDXL OpenPose weight; linked persistent assets into runtime paths and restarted ComfyUI | Production API exposes hand-aware `DWPreprocessor`; `ControlNetLoader` lists exactly the selected model; public endpoint HTTP 200 | Extract and inspect the frozen pose control image and OpenPose JSON from the successful touch source using explicit TorchScript detector and estimator settings |
| 2026-08-24 | VS Code agent / local + RunPod `7sx63d6eu80uwr` | Extracted DWPose with persistent TorchScript assets, corrected misattributed non-contact limbs in structured JSON, froze manifest, and rendered seeds 24690-24693 exactly once | Revision 1 failed 0/4: macro pose and one-way ownership held, but every hand landed at collar/neck; seed 24691 was topless | Revision 2: translate only the required wrist/hand control lower onto the sternum, freeze new provenance, rerun the same four seeds |
| 2026-08-24 | VS Code agent / local + RunPod `7sx63d6eu80uwr` | Preserved revision 1, translated only the required wrist/hand control to `(465, 700)`, froze revision-2 provenance, and rendered the same four seeds exactly once | Revision 2 failed 1/4: seed 24692 passed exact contact; two remained too high, one hid the hand and was topless; all macro non-contact geometry held | Revision 3: move only the required wrist/hand cloud farther down, freeze provenance, and rerun the same seeds once |
| 2026-08-24 | VS Code agent / local + RunPod `7sx63d6eu80uwr` | Translated only the required wrist/hand control from `(465, 700)` to `(465, 815)`, froze revision-3 provenance, and rendered the same four seeds exactly once | Revision 3 failed 0/4: all visible chest contact was lost and the required arm routed behind the bodies; seed 24691 was again topless | Stop OpenPose-only tuning; design a separately approved proof for explicit contact depth/occlusion and independent clothing enforcement |
| 2026-08-24 | VS Code agent / local + RunPod `7sx63d6eu80uwr` | Restarted existing ComfyUI without dependency changes; froze a bounded masked-inpaint workflow and rendered seeds 24690-24693 once | Inpaint revision 1 failed 0/4 because the ellipse excluded original fingertip pixels; identity, clothing, composition, and all unmasked pixels held; two seeds otherwise achieved exact contact | Preserve revision 1 and enlarge only the mask shape to include the full original hand silhouette before rerunning the same seeds |
| 2026-08-24 | VS Code agent / local + RunPod `7sx63d6eu80uwr` | Expanded only the inpaint mask to include the complete original hand silhouette and reran the same four seeds once | Inpaint revision 2 failed 0/4: residual pixels were removed, but ownership, chirality, contact, and palm orientation varied or failed in every seed | Preserve revision 2; test one prompt-only revision with explicit image-space right-hand anatomy and surface orientation |
| 2026-08-24 | VS Code agent / local + RunPod `7sx63d6eu80uwr` | Changed only textual conditioning to specify image-space right-hand anatomy, surface orientation, ownership, and forbidden alternatives; reran the same seeds once | Inpaint revision 3 failed 0/4: hand absent, partial, pointing, or assigned to man; explicit thumb/pinky language did not control geometry | Stop Juggernaut mask/prompt tuning; evaluate a geometry-conditioned or dedicated instruction-based image-edit model under a separately approved proof |
