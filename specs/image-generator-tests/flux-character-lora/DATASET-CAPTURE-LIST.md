# Flux Character LoRA — Training Dataset Capture List

Status: spec (not yet implemented)
Goal: build per-character LoRA training sets for **Dean** and **Becky** so identity holds
in the initial Flux render and the Qwen face-replace edit is no longer needed per render.

## Seeding: what it helps with (and what it does NOT)

- **Helps:** reproducibility and variety. A fixed seed per capture cell makes the build
  deterministic (same prompt + seed = same render on the same endpoint) and guarantees
  each cell is a *different* noise layout — so you get genuine pose/context diversity
  instead of near-duplicates.
- **Does NOT help:** identity. A seed controls the noise → composition/pose, not the face.
  Identity consistency comes from the **face-normalization step** (Qwen identity edit below),
  not from seeds.
- Policy: assign one seed per cell, recorded in the manifest. Convention — Dean cells use
  seeds `40000..40099`, Becky cells use `41000..41099`. Never reuse a seed across cells.

## Characters

| Trigger token | Character | Profile pack |
|---|---|---|
| `ohwx-dean` | Dean | faee1ec0 (assets 63f3f809) |
| `ohwx-becky` | Becky | f58f959a (assets 5f8ea303) |

## Capture matrix (per character — run twice, once per character)

**~30 core cells + ~6 variation cells = ~36 images per character.**

Angles: F = front, 34L/34R = three-quarter, PL/PR = full profile.
Distances: CU = close-up (face/torso), HB = half-body (waist up), FB = full-body.

| Angle \ Distance | CU | HB | FB | cells |
|---|---|---|---|---|
| Front (F) | 2 | 2 | 2 | 6 |
| 34 Left (34L) | 1 | 2 | 2 | 5 |
| 34 Right (34R) | 1 | 2 | 2 | 5 |
| Profile Left (PL) | 2 | 2 | 2 | 6 |
| Profile Right (PR) | 2 | 2 | 2 | 6 |
| Over-shoulder / behind | — | 1 | 1 | 2 |
| **Total** | 8 | 11 | 11 | **30** |

**Variation cells (+6, per character):** 2 alternate outfits, 2 lighting extremes
(hard rim light, very dim), 2 expressions (laughing, surprised) at close-up/half-body.

Profiles are weighted highest because profile/angled identity is the current failure mode
(BigLust matrix: IP-Adapter PLUS FACE is frontal-optimized, profiles hold weakly).

## Context / lighting / clothing variety (spread across cells)

- Lighting: indoor dim, indoor bright, outdoor day, outdoor golden hour, outdoor night —
  rotate through so the LoRA does not bake in one lighting.
- Clothing: ≥4 outfits per character, plus 1–2 minimal/undressed for body-type. Single
  subject only; explicit anatomy is the base model's job, not the training data's.
- Background: vary (plain wall, room, outdoors). Keep backgrounds simple enough to caption.

## Resolution

- Face close-ups: `1024×1024`.
- Half/full-body: `832×1216` portrait (app's native size).
- ≥1024 px on the short side; no upscales that introduce artifacts.

## Face-normalization step (the identity-consistency fix)

After each cell renders:

1. Run the Qwen identity edit with the **face-only** instruction (the proven
   `qwen_apply_faces.py` recipe: "keep pose/bodies/position/lighting exactly unchanged
   except the face").
2. Match the reference view to the head angle of the shot:
   F→front, 34L→34l, 34R→34r, PL→profl, PR→profr.
3. Visually verify each normalized image before it enters the training set.

This makes the training set identity-consistent even though the raw T2I identity is weak.

## Body consistency (nude + clothed + tattoos / body shape / body hair)

One LoRA per character captures the WHOLE person, not just the face. The rule that keeps it
consistent:

- **Invariant features** — face, body shape/proportions, skin tone, tattoos, body hair, scars,
  grooming — must be identical in every training image and left **uncaptioned** (they bind to
  the trigger token).
- **Variable features** — clothed vs nude, pose, lighting, background, expression — are
  **captioned** and controlled by the prompt at generation time.

Nude and clothed are two states of the same body: the LoRA learns the body from the
under/undressed shots and learns "clothes on top" from the clothed shots. Balance the set
~50/50 so the model doesn't default to one state (too much nude -> it strips clothes; too much
clothed -> nudity gets weak).

### Enforcing body consistency in the pipeline

1. **Canonical body card per character** (single source of truth) — write it once and paste it
   verbatim into every training-generation prompt:
   `{body shape}, {skin}, {body hair}, {tattoo: design + exact placement}, {scars/marks}, {grooming}`.
2. **Condition on a full-body reference** during generation (IP-Adapter full-body or the
   identity pack's full-body asset) so shape is conditioned, not left to text.
3. **Normalize the body after generation**, not just the face: run the Qwen edit with a
   full-body reference to stamp body shape + marks, or inpaint the tattoo/marks when they drift.
4. **Verify every image against the card** — tattoo present and in the right spot, body shape
   matches — and reject drift before it enters the set.

### Why this matters for tattoos/body hair specifically

Text-to-image cannot reliably render a *specific* tattoo or grooming from a prompt, but a LoRA
CAN learn a distinctive tattoo/body-hair pattern **if it appears consistently in the training
data**. That's circular — you need images with the tattoo to train the model to render it — so
bootstrap: stamp the tattoo/marks into each training image in the normalize step first, then the
LoRA locks them as identity. If a mark appears in only some images, the model treats it as
optional and it will flicker in and out.

## Canonical body cards (Dean, Becky)

Single source of truth for invariant features. Paste the `BodyCard` line verbatim into every
training-generation prompt. Items marked **[DECIDE]** need an exact design/placement chosen
before capture starts — vague "a few tattoos" will not train consistently.

### Dean

- `BodyCard`: `45-year-old man, 6'1", toned muscular build, light olive skin slightly rough,
  short brown hair, green eyes, rugged casual style.`
- Tattoo: one on the upper thigh — **[DECIDE]** design + exact leg/position.
- Body hair: **[DECIDE]** (e.g. moderate chest hair, trimmed).
- Grooming: **[DECIDE]**.

### Becky

- `BodyCard`: `50-year-old woman, 5'8", curvy, full bust, soft waist, wide hips, fair smooth
  skin, brown hair in a bun, blue eyes, tongue ring and nose ring, casual style.`
- Tattoos: a few on arms and legs — **[DECIDE]** exact designs + placements (e.g. small flower
  on left forearm, butterfly on right ankle).
- Body hair: **[DECIDE]** (e.g. trimmed dark pubic hair).
- Piercings: tongue ring + nose ring (fixed — from canonical data).

## Location consistency (sets / backgrounds)

Locations are handled differently from characters — they tolerate more variation, and there are
four escalating tools. Use the cheapest one that holds.

| Tool | What it locks | When to use | Cost |
|---|---|---|---|
| Canonical text description | overall look | every scene — always do this | free |
| ControlNet (Depth/MLSD) | exact geometry (wall/furniture placement) | recurring sets, same room | cheap — needs a map, not a render |
| IP-Adapter | look/style (lighting, palette, decor vibe) | geometry holds but style drifts | cheap (one ref render) |
| Location LoRA | layout + look pixel-stable | a set used constantly that keeps drifting | training job (20-40 imgs) |

The split that matters: **ControlNet pins geometry; IP-Adapter transfers look.** IP-Adapter does
NOT keep the couch in the same corner. For a recurring fictional set, the typical stack is
`text description + Depth/MLSD ControlNet map`, with IP-Adapter added on top only if the
decor/lighting drifts.

### ControlNet for fictional locations — what to generate

Your locations are made-up, so you define them once, extract the geometry, and reuse it:

1. **Generate one establishing shot** of the location with the app — the canonical render of the
   room from the standard camera angle you'll reuse. This is your fictional location, defined.
2. **Extract the conditioning map** from that shot (once, offline):
   - **Depth map** — grayscale, brightness = distance from camera; holds 3D layout (furniture
     shapes, depth). Best for rooms/outdoor spaces.
   - **MLSD map** — straight-line/edge drawing; holds architecture (walls, corners, doorframes).
     Best for rectilinear interiors (trailer, bathroom).
3. **Store the map** as the location's canonical geometry asset, next to the identity refs.
4. **Every future render** of that room feeds the map into ControlNet + the text description:
   the model rebuilds the SAME geometry while the people/action change.

How many per location: a map only locks geometry for ONE camera angle. Generate an establishing
shot per standard angle you reuse (typically 2–6), each with day/night variants if lighting
matters → 2–12 maps per location. The preprocessor runs only ONCE per shot to make the map;
inference needs the ControlNet model + the map, not the preprocessor.

Base-model note: SDXL base (BigLust/Juggernaut) → use an SDXL Depth or MLSD ControlNet. If the
base later moves to Flux, swap to Flux.1 Depth/MLSD ControlNets.

### Worker state (what's installed vs needed)

- **IP-Adapter**: already on the identity-worker image (`ComfyUI_IPAdapter_plus`).
- **ControlNet**: Loader/Apply are built into ComfyUI core, but the serverless identity-worker
  has NO ControlNet model and NO preprocessor (`comfyui_controlnet_aux` lives only on the
  DWPose worker). Adding location ControlNet = add a Depth/MLSD model to the volume
  `models/controlnet/` (record in `pod-registry.json` / `endpoints.json`), and either add the
  preprocessor node or ship pre-extracted maps.

## Captioning

- One `.txt` sidecar per image (Kohya) or a single `dataset.toml` (ai-toolkit).
- Caption = `{trigger_token}, {pose/angle}, {distance}, {clothing}, {lighting}, {background}, {expression}`.
- Do **not** describe invariant identity in the caption (face, body shape, tattoos, body hair,
  scars) — the trigger token *is* the identity; caption only the variable attributes
  (clothed/nude, pose, lighting, background, expression). See Body consistency above.

## Quality gates (every image)

- [ ] Exactly one subject, no second person, no cropped limbs of others.
- [ ] Face matches the approved ref after normalization (visual check).
- [ ] Sharp, in focus, no watermark/text artifacts.
- [ ] Angle and distance recorded and match the cell.

## Manifest (per character)

`manifest.json` with one entry per cell: `cellId`, `angle`, `distance`, `seed`, `prompt`,
`sourceRenderHash`, `normalizedHash`, `approved` (bool).

## Build order

1. Generate all raw cells (solo shots, weak identity is fine).
2. Face-normalize each via Qwen identity edit.
3. Verify each (gate list above) and fill the manifest.
4. Hand the approved set + captions to LoRA training (Kohya / ai-toolkit).
