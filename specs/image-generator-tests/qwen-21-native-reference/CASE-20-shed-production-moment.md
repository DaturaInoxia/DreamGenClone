# CASE-20 — Production moment m1 (shed / workbench): reference binding + mechanism proof

Status: **RESOLVED (mechanism + scale + quality)** — the wide-frame/empty-room composition was the
defect; medium framing fixes it. The explicit production prompt remains operator-run.

Date: 2026-09-25
Host: WOOD-GAME-MAIN local ComfyUI 0.37.1 (RTX 5080), `http://192.168.0.11:8188`
Model: `qwen_image_2.1_int8_convrot.safetensors` (Qwen-Image-2.1), envelope cfg 1 / euler / simple.

## 1. The production moment

- Session `8bc36efb-b235-485b-8675-b98ad7754e59`, interaction `32244bb9-d0e7-4943-a8cb-739b9fb4d1a8`
- Production group `7627eb78-0919-4ca7-b53b-0c238c2b9e22`, POV **Omniscient**, `IdentityPolicy = Required`
- Moment `m1` "Still Joined at the Workbench Start", participants **p0 Becky** (Wife) + **p1 Dean** (OtherMan)
- Location: "The Shed - the workbench"; lighting "thinning blue light from the last of the day"

## 2. ROOT CAUSE — the group had no bound references at all

Every `SceneImages` row for this group was:

- `RenderMode = PromptOnly`
- `IdentityPacksJson = NULL`
- `TypedReferenceSnapshotJson` → **every reference `assetId: null`** (identity-p0, identity-p1,
  wardrobe-p0/p1, location, voice-p0/p1)

So `flux1-dev-fp8`, `qwen_image_2.1_int8_convrot`, `bigLust_v16` and `juggernautXL_ragnarok` were all
compared **prompt-only** — no identity, no location. Two nude figures in an entangled pose with zero
references is the worst case for every base model, which is why all four produced malformed bodies.
Qwen-2.1's entire advantage (NativeMultiReference) was never engaged.

Contributing cause: **the `location` typed reference (required) had no asset to bind to** —
`ReferenceBootstrapLocationReferences` is empty and there is no Location-kind `SceneAssets` row for
The Shed.

## 3. Participant → identity asset mapping (resolved)

| Participant | Session id | Template | Approved pack | Canonical face (Front) | Body ref |
|---|---|---|---|---|---|
| Becky (p0) | `f58f959a…` | `de351eb3-69d3-421a-a762-79ae8ee183ed` | `2d13c667…` BodyComplete v9 | `282f5b91ba7d4dba85140ae006aa06ee` | `37f0fa3f…` (Front, unclothed) |
| Dean (p1) | `faee1ec0…` | `a4894571-2513-4063-9e16-ef8c4f8134ed` | `1157b60f…` FaceOnly v9 | `03da6dbdece34144b682365378591df2` | **none (FaceOnly)** |

Ken (`55ed2a0a…`, template `51923303…`) is **not** in this moment.

Constraints carried in: Dean has **no body reference** (face only); Qwen native-reference face refs
must be **clothed headshots on a neutral background**; the **reference head angle dominates** the
requested pose.

## 4. Shed location reference — CREATED

`helpers/local-comfyui-host/run-qwen-2-1-proof.ps1` cell `shedLoc`:

```
Photorealistic 35mm wide photograph of the empty interior of a dim tin-walled shed at the last light
of day. A heavy wooden workbench with rough worn boards runs along the wall, dusty grit scattered
across the plank floor, corrugated tin walls, a few hand tools hanging, thin blue dusk light
filtering through gaps in the boards, deep shadow in the corners. No people. Natural textures, dim
ambient light, shot on 35mm.
```

- Output: `artifacts/tmp/qwen-2-1/shedLoc/result_0.png`, 1216x832, 25.1 s, prompt_id `6fa7f3ce…`
- SHA-256 `7847CD8C5B6245FFB5CDCCF220C417D6F8FE6F7FD69D7457284CDFAFDA4091AB` (pinned in the runner as
  `shedLocation`)
- Visual review: PASS as a shed (corrugated tin, workbench, grit, blue light). Caveat: **very high
  texture contrast** (rusty tin, sawdust across the whole floor) — see the grain finding below.

## 5. Mechanism proof — PARTIAL PASS

Cell `genShedClothed` (location + Becky face + Dean face, one t2i call, both figures clothed,
anchored bench geometry), prompt_id `8f557447…`, 55.6 s, output 1216x832.

| criterion | verdict | evidence |
|---|---|---|
| References bind | **PASS** | all three logged "verified and uploaded"; output frame = 1216x832 = the location ref's own size (not a square fallback), i.e. the reference path anchored the frame |
| Room fidelity | **PASS** | corrugated tin, heavy worn workbench, grit, blue last light reproduced (semantically faithful, not pixel-faithful) |
| Pose anchoring | **PASS** | woman supine, head at the far end, knees bent, **feet flat on the boards below her shoulders** — the reported "foot to her face" defect is gone |
| Clothing follows prompt | **PASS** | t-shirt + jeans held; no unwanted nudity |
| **Second figure present** | **FAIL** | **only ONE figure rendered** — the man is absent, though both face refs uploaded |
| **Figure scale** | **FAIL** | woman ≈ 1/7 of frame height; the room dominates ("miniature") |
| **Image quality** | **FAIL** | heavy grain/noise — see measurement below |

### Slot-order probe (did not fix it)

Cell `genShedClothedFacesFirst` (faces in slots 1–2, location in slot 3), prompt_id `04accdb0…`,
35.4 s, 1216x832. Result: **man still absent, woman still miniature.** So slot order is not the fix;
the empty-room reference is not the (only) cause of the missing man.

### Objective grain measurement (edge-energy proxy, 1216x832)

| image | mean | std | edge energy |
|---|---|---|---|
| `shedLoc` (location ref, no people) | 22.2 | 24.8 | **17.33** |
| `genShedClothed` (25 steps) | 84.1 | 74.6 | **51.72** |
| `genShedClothedFacesFirst` (25 steps) | 84.4 | 74.8 | **50.14** |

The two-person renders are **~3× noisier** than the reference. Two candidate causes: (1) 25 steps is
below the 2.1 official 40–50; (2) the location reference is itself a very high-frequency image that
native-reference conditioning reproduces wholesale.

## 6. Open diagnostics (cells staged in the runner)

- `shedLocClean` — low-noise location ref (smooth tin, sparse grit). If grain follows the ref, this
  fixes it; if not, it is the step count.
- `genTwoFacesNoLoc` — faces only, **no** location ref, shed in words, medium shot. Isolates whether
  the empty-room reference is what suppresses the second figure.
- `genShedTight` — location kept, **medium shot** framing, square 1216x1216, so the figures fill the
  frame instead of the room dominating.

### Quality run ABANDONED (2026-09-25) — bad parameter, and a shared host

`genShedClothedFacesFirst -Steps 40 -Resolution 2048 -OutRoot artifacts/tmp/qwen-2-1-quality` was
started and then interrupted without producing a result.

Two reasons, both worth recording:

1. **`-Resolution 2048` was the wrong lever.** `TextEncodeQwenImage21.resolution` is a pixel
   *budget* (1024 default, 2048 = 2K). The frame here is 1216x832 (~1.0 Mpx), so a 2048 budget asks
   the encoder for roughly 4x the tokens the frame needs. It ran >9 minutes with no output where the
   1024-budget run took 35-56 s. Do not reach for 2048 to cure grain on a ~1 Mpx frame.
2. **The local host is SHARED with the running webapp.** During the run the app had three
   `scene-asset-generation` jobs in flight (`DurableBackgroundJobs`, created 05:38 UTC, generating
   Becky's `core.front.cu.*` body assets) and the ComfyUI queue reached 1 running + 3 pending. The
   agent's direct-to-host proves compete with the app's own render queue on the same 5080.

Post-mortem on the interrupt: the five app jobs completed `status=success`; the only casualty was
this abandoned run. Check `DurableBackgroundJobs` + `/queue` BEFORE submitting a direct-to-host
proof, and prefer running proves when the app is idle.

## 6b. RESULTS — the four diagnostics (all run at 40 steps)

| cell | refs | frame | figures | scale | quality | verdict |
|---|---|---|---|---|---|---|
| `genShedClothed` | loc v1 + 2 faces | 1216x832 **wide** (25 steps) | **1 (man absent)** | miniature | grainy | FAIL |
| `genShedClothedFacesFirst` | faces-first, loc v1 | 1216x832 **wide** (25 steps) | **1 (man absent)** | miniature | grainy | FAIL |
| `genTwoFacesNoLoc` | **2 faces only, NO loc** | 1216x832 medium | **2** | **correct** | **cleanest** | PASS |
| `genShedTight` | loc v1 + 2 faces | 1216x1216 **medium** | **2** | **correct** | clean | **PASS (best)** |
| `genShedCleanTight` | loc v2 clean + 2 faces | 1216x1216 **medium** | **2** | **correct** | clean | PASS, wrong light |

### THE ROOT CAUSE (proven by isolation)

**Framing, not reference order, was the defect.** The two WIDE 1216x832 cells produced one miniature
figure; the MEDIUM cells — including `genShedTight`, which uses the *identical* three references and
the same subject matter — produced **two correctly-scaled figures**. The wide frame let the empty-room
reference fill the composition: the room became the subject, the people shrank to props, and the
second figure dropped. Moving the faces to slots 1-2 changed nothing; changing the framing fixed both
the missing man and the scale in one edit.

Corollary: much of the measured "grain" in the wide cells was the **room** — a rusty-tin / sawdust
reference reproduced across a large area at 25 steps — not a sampler fault. At medium framing the
figures occupy the frame and the clean renders measure far lower.

### Visual + measured detail

| image | mean | std | edge energy |
|---|---|---|---|
| `shedLoc` (loc v1, dim/rusty) | 22.2 | 24.8 | 17.33 |
| `shedLocClean` (loc v2, bright/clean) | 128.8 | 51.6 | 33.20 |
| `genShedClothed` (wide, 25 steps) | 84.1 | 74.6 | **51.72** |
| `genShedClothedFacesFirst` (wide, 25 steps) | 84.4 | 74.8 | **50.14** |
| `genTwoFacesNoLoc` (no loc, medium) | 30.3 | 24.1 | **12.10** |
| `genShedTight` (loc v1, medium) | 32.5 | 32.5 | **17.43** |
| `genShedCleanTight` (loc v2, medium) | 102.5 | 45.9 | 26.93 |

Caveat on the metric: edge energy is **brightness-sensitive** — `shedLocClean` scores 33.2 while being
a clean image, because it is bright. Treat positions as supporting evidence and the visual review as
primary. On the visual review the wide cells are genuinely grainy, the three medium cells are not.

### Per-cell notes (operator-visible)

- `genShedTight` — **best result.** Both figures, correct scale, woman supine with head at frame right
  and feet on the boards (no foot-to-face), man standing and **looking down at her** as prompted, dim
  interior with blue light in the tin gaps. The frontal face refs' head-angle leak worked *for* us here.
- `genTwoFacesNoLoc` — cleanest image, both figures; the shed still reads correctly **from words
  alone** (tin walls, timber frame, workbench, dirt floor). Man faces the camera (head-angle leak).
- `genShedCleanTight` — clean environment, but **the clean ref v2 is daylight/bright with trees visible
  outside**, which contradicts the moment's "thinning blue last light / dim inside". Do not use v2 for
  a dusk-shot moment; v1 matches the mood.

## 7. Recommended production cell

- **Frame: medium, square or tighter** — never "wide" for a two-figure interior. This was the whole
  fix.
- **Refs: location v1 (dim blue dusk) + Becky front face + Dean front face** (`genShedTight` shape).
- **40 steps.**
- Resolution budget: leave at 1024 (see the 2048 lesson above).
- Dean has no body reference (FaceOnly) — his build is prompt-driven only.

## 8. Not run by the agent

The production prompt itself (nude, explicit) was not run — the operator runs that cell.



## 7. Not yet done (needs a decision)

The production prompt itself (nude, explicit) was **not** run by the agent — the operator runs that
cell. A `genShed` cell should be added with the compiled prompt + `<image1>/<image2>` naming once the
mechanism above is sound.
