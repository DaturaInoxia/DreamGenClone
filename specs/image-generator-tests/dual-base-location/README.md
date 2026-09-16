# Dual-Base + Location Reference Workflow

> **Read [`FINDINGS.md`](FINDINGS.md) first.** It records verified limitations, failed approaches, the Qwen AIO versus split-model distinction, and the next focused proof. Do not run a full slideshow until location and identity both pass a focused base-image proof.

This harness produces the core primitive the app needs for scene image generation with arbitrary locations:

- **Location reference** — a clean, character-free T2I render of a location (bedroom, outdoors, etc.)
- **Dual bases** — two T2I renders with identical location conditioning but different character orientations
  - Base A: Dean (right profile) + Becky (left profile) facing each other
  - Base B: Dean (right profile) + Becky (right profile) facing away

Both bases use regional IP-Adapter with angle-matched refs from the approved multiangle packs (Dean v8, Becky v5).

## Why This Matters

In the app, locations are arbitrary scene assets (not just "plain dark gray studio backdrop").
To produce a sex slideshow (or any multi-pose sequence) in an arbitrary location, we need:

1. A location reference image (IP-Adapter conditioning)
2. Two bases with the same location but different character orientations (so Becky can face away without identity drift)
3. The full edit chain runs from the appropriate base for each segment of the sequence

This is the production pattern. The studio sex-slideshow harness was a proof-of-concept; this is the real thing.

## Current status

The exploratory SDXL/img2img and Qwen AIO attempts are not sufficient proof of arbitrary-location preservation plus Dean/Becky identity. The next planned experiment is the split Qwen Image Edit 2511 graph with native references: source `image1`, Dean `image2`, Becky `image3`, and `FluxKontextMultiReferenceLatentMethod` using `index_timestep_zero`. See `FINDINGS.md` for the exact model files and graph evidence.

## Scripts

| File | Purpose |
|---|---|
| `make-location-reference.ps1` | Generate a clean location reference image (T2I, no characters) |
| `make-dual-base.ps1` | Generate two bases from a location ref + two character pose configs |

## Usage

### 1. Generate Location References

```powershell
# Bedroom
& specs/image-generator-tests/dual-base-location/make-location-reference.ps1 `
    -LocationName bedroom `
    -Prompt 'photorealistic bedroom interior, soft warm lighting, large bed with white sheets, wooden nightstands, neutral walls, window with sheer curtains, cozy and intimate atmosphere, no people, empty room' `
    -OutDir specs/image-generator-tests/dual-base-location/refs

# Secluded outdoors
& specs/image-generator-tests/dual-base-location/make-location-reference.ps1 `
    -LocationName outdoors `
    -Prompt 'secluded forest clearing, dappled sunlight through trees, soft grass, mossy logs, private and natural setting, no people, peaceful atmosphere' `
    -OutDir specs/image-generator-tests/dual-base-location/refs
```

### 2. Generate Dual Bases (Bedroom)

```powershell
$loc = 'specs/image-generator-tests/dual-base-location/refs/locref-bedroom-....png'
$deanProfr = 'specs/image-generator-tests/refs/dean/v8/profr.png'
$beckyProfl = 'specs/image-generator-tests/refs/becky/v5/profl.png'
$beckyProfr = 'specs/image-generator-tests/refs/becky/v5/profr.png'

& specs/image-generator-tests/dual-base-location/make-dual-base.ps1 `
    -LocationRef $loc `
    -DeanRefA $deanProfr -BeckyRefA $beckyProfl `
    -DeanRefB $deanProfr -BeckyRefB $beckyProfr `
    -PromptA 'photorealistic studio photograph of a man and a woman standing side by side facing each other in a bedroom, both fully clothed...' `
    -PromptB 'photorealistic studio photograph of a man and a woman standing side by side facing away from the camera in a bedroom, both fully clothed...' `
    -OutDir specs/image-generator-tests/dual-base-location/bases/bedroom `
    -Prefix bedroom
```

### 3. Run Full Sex Slideshow per Location

Adapt the sex-slideshow harness to:
- Use Base A for steps 1–11 (facing sequence)
- Switch to Base B for steps 12–20 (facing-away sequence)
- All edits use `-FromBase` from their respective base

## Identity Angle Mapping

| Character | Pose | Profile Ref |
|---|---|---|
| Dean (left) | facing right | `profr` |
| Becky (right) | facing left | `profl` |
| Becky (right) | facing away (right) | `profr` |

## Next

- Wire this into the app's job system (`SceneImageDualBaseGenerationJob`)
- Expose location reference assets in the UI
- Add character pose configuration UI for dual-base generation
- Character LoRAs (future) can replace or augment IP-Adapter for faces
