# B-121 Seed Prompts and Settings — the starting points

These are **starting points shipped as database rows**, not code constants. The user edits them in the
app; the seeded values below are what a fresh install begins with. `SeedBody` is written once at
migration and never edited, so **Reset to default** always restores exactly this text.

> These texts are the ones validated on Dean v8 / Becky v5 and recorded in repo memory
> `dean-front17-shirt-removal.md`. They encode four expensive lessons; do not "improve" them without
> re-reading that memory file.

## 1. Template keys and seed text

### `identity.front.generate` — front portrait (text-to-image)

Placeholders: `{CharacterName}`, `{Description}`.

```
Photorealistic frontal portrait of {CharacterName}: {Description}. Head and shoulders, facing the camera. Neutral background, even lighting, sharp focus, natural skin texture.
```

Faithful to the existing `SceneAssetProfilePackJobHandler.BuildFrontPortraitPrompt`. When
`{Description}` is empty the existing code degrades to "Photorealistic frontal portrait of {name},
head and shoulders, facing the camera." — preserve that behaviour as a *template variant*, not as a
code branch with an embedded string.

### `identity.garment.remove` — remove clothing (same-image edit)

Placeholders: `{SubjectPronounPossessive}` (e.g. "his"/"her").

```
Remove the shirt and replace it with the same plain background, so the subject is shown with bare neck and bare shoulders. Keep {SubjectPronounPossessive} neck, throat, collarbones and shoulders exactly as they are - do not remove, shorten, hide or cover the neck. Keep the identical crop, framing, zoom and head size. Do not add any clothing.
```

**Why each clause exists — do not delete any of them:**
- *"Keep … neck, throat, collarbones and shoulders exactly as they are"* — without it Qwen treats the
  neck as part of the collar and paints it out; every subsequent angle then invents an elongated neck.
- *"Keep the identical crop, framing, zoom and head size"* — without it the model zooms out and the
  head shrinks (measured: interocular 213.5 → 171).
- *"Do not add any clothing"* — the fix for the clothing-invention defect that started this work.
- The preserve-list deliberately does **not** contain the word "clothing".

### `identity.angle.three-quarter` — 3/4 view (same-image edit)

```
Turn the person's head and upper body to a three-quarter view so the nose points toward the LEFT side of the image and more of the left side of the face is visible. Keep the exact same face, hair, facial features, identity, bare neck and bare shoulders, and lighting unchanged. Do not add any clothing. Keep the identical crop, framing, zoom and head size.
```

### `identity.angle.profile` — full profile (same-image edit)

```
Turn the person's head to a full profile so the nose points toward the LEFT side of the image and the left side of the face is shown in full profile. Keep the exact same face, hair, facial features, identity, bare neck and bare shoulders, and lighting unchanged. Do not add any clothing. Keep the identical crop, framing, zoom and head size.
```

**Direction — read before editing these two templates.** Qwen's direction compliance is **not
reliable**. In this session:
- Anatomical wording ("turn to their left") produced **four renders facing the same way**.
- Image-space wording fixed it for some sources but not others.
- Both 3/4 renders came out image-left (−0.387 and −0.415 — correlation 0.954 direct vs 0.427 mirrored
  proved they were the *same* orientation, not mirror images).

Therefore the seed templates request the **image-LEFT** orientation (the one that renders reliably),
and the **right-hand views are produced by mirroring** — see the `DeriveByMirror` settings below. The
**yaw-sign gate**, not the prompt text, is what guarantees the convention. If you change the wording,
the gate still protects correctness — but do not weaken the gate on the assumption the wording works.

## 2. Settings seeds (`ReferenceWorkflowSettings`, global scope)

| Setting | Seed value | Notes |
|---|---|---|
| `EditorModelId` | *(unset — must be configured)* | The canonical editor is `Qwen Image Edit 2511 (Local ComfyUI)`, id `7bc5d932-4596-4b96-ac73-5162516a162f`. Seeding a model id is **not** done by migration — it must be explicitly configured (FR21-033: no guessed configuration). |
| `UpscalerModelName` | *(unset — must be configured)* | Local ComfyUI `UpscaleModelLoader` model name; the test runner uses `4x-UltraSharp.pth`. |
| `EnhanceTargetLongEdge` | `1024` | Lanczos resize after the 4× upscale. |
| `EyeGateMaxAbsIrisDyPercent` | `1.5` | Gate: `abs(irisDy%) <= this`. |
| `QualityGateMinSharpness` | `250` | Matches `ReferenceImageQualityAnalyzer`'s Good threshold. |
| `CropHeadroomPercent` | `8` | Headroom above the crown, as a percentage of image height. |
| `CropTargetAspect` | `1.0` | Square, matching the identity storage convention. |
| `DeriveByMirror.ThreeQuarterRight` | `true` | Right 3/4 derived by mirroring the left 3/4 render. |
| `DeriveByMirror.ProfileRight` | `true` | Right profile derived by mirroring the left profile render. |
| `DeriveByMirror.ThreeQuarterLeft` / `.ProfileLeft` | `false` | Left views are rendered directly. |
| `EyeToolPythonPath` | *(unset — must be configured)* | Interpreter for `tools/eye-validation/measure_iris.py`. Fail fast if unset; never hardcode a machine path. |

Sampling parameters (steps / CFG / sampler / scheduler / shift / CFGNorm) are **not** stored here —
they belong to the Model Manager's `RegisteredModels` row for the chosen model id (FR21-011).

## 3. Thresholds the implementing agent must not re-derive

- Eye gate applies to **front and 3/4 views only**. It is **invalid under yaw** and must not be
  evaluated on profiles; MediaPipe returns no face mesh on a full profile, so profiles are confirmed
  visually.
- `irisDy%` is meaningless with a turned head (3/4 ≈ −5…−8 %, profiles ≈ −36 %). Interocular distance
  **shrinks naturally with yaw** (front 183 → profiles 60–76 in the accepted v7 pack) — it is not a
  zoom indicator across views.
- Enhancement (4× upscale) **re-synthesises facial detail**. It must not be applied to the chosen
  likeness front before likeness is judged; this is why `front_16` was rejected.
