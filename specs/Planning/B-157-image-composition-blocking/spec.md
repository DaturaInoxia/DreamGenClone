# B-157 — Spec: Image composition blocking & reference-role prompt assembly

## 1. Problem

A Composition / Asset render can bind several reference images (per-character face, body, wardrobe,
plus a frame-wide location and pose). The images are sent correctly and tagged `<image1>…<imageN>`,
but the prompt never tells the model **which reference is which person** or **where each person sits in
the frame**, so multi-reference renders degrade to "the first face wins" and the operator has to type
the scene's spatial arrangement by hand.

Verified root causes (2026-10-09):

- **R1 — no frame-space blocking.** The Still brief `SemanticInputSnapshotJson` describes the world
  ("Becky at the clothesline, Dean at the slider, twenty feet apart") but never the frame ("Becky left,
  Dean right, wide side view"). See [evidence in README.md](README.md).
- **R2 — the role clause labels collide.** `ReferenceRoleClauses` names each reference by its pack
  *view* label ("3/4 Left"), so two characters' faces both read "3/4 Left" and no line ties a person's
  face to their body and wardrobe. See `ReferenceRoleClauses.LineFor` and
  `SceneImageRenderingJobHandler.AddIdentityPackReferencesAsync`.
- **R3 — prose and role clause are disconnected.** The scene prose (pre-processor over the brief) and
  the role clause are assembled separately, with no shared placement vocabulary.
- **R4 — no prompt build / validate / preview area.** The final prompt (draft + `<imageN>` block) is
  assembled server-side in several handlers and never shown to the operator.

## 2. Goals / non-goals

**Goals**

1. Derive frame-space blocking (subject positions, camera, shot) deterministically from existing
   beat/moment data; the operator stops typing the scene.
2. Bind each reference to a person and a position in the prompt, following the vendor's own format.
3. One place that assembles and validates the final prompt + reference map.
4. An operator-facing preview so the mapping is visible before render.
5. A minimal, no-prose binding→prompt assembler for the Asset Manager.

**Non-goals**

- No new image model, provider, or graph. No new UI *surface* — extend the shared `ImageStepComposer`.
- No character-name identity in the prompt (names are refuted; see research).
- No hardcoded/default guessing: blocking derivation is driven by a configurable staging profile and
  fails fast when required input is missing (same posture as the RP engine's no-fallback rule).

## 3. Design

### 3.1 `SceneBlocking` — the frame-space record

```text
SceneBlocking {
  profileVersion: string,             // which staging profile produced this
  subjects: [{
    subjectKey: string,               // p0 / p1 — the typedReferences subject key
    framePosition: { horizontal: Left|Center|Right,
                     depth: Foreground|Midground|Background },
    roleText: string                  // "the person on the LEFT"
  }],
  camera: { angle: string, shotSize: string, side: string? },  // from camera/lens intent
  layering: [ string ],               // foreground→background order, for prose
  rationale: string                   // the moment's compositionRationale, carried verbatim
}
```

`SceneBlocking` is produced by a **`SceneBlockingResolver`** (pure, deterministic, testable) from:

| Input | Field |
|---|---|
| per-character world position | `frozenState.characters[].physicalLocation` / `position` / `sightline` |
| framing hints | `moment.compositionRationale` |
| subject involvement | `participantSummary[].involvement` (active/observer) — default placement rule |
| staging rules | a persisted, configurable `StagingProfile` (defaults live in the profile, never in code) |

The default placement rule is **involvement-based** and declared in the `StagingProfile`: the active
subject is foreground/center, observers are side/background. Camera is keyword-extracted from
`compositionRationale` (carried verbatim into the prose), falling back to the profile's default camera.
When the required inputs are missing and the profile cannot place a subject, the resolver fails fast
with a diagnostic — it never invents a placement.

### 3.2 Emit `blocking` into the Still brief

`DeterministicMultimodalMediaCompiler.BuildStill` currently assembles the brief from `moment`,
`frozenState`, `continuity`, `typedReferences`, `videoKeyState`. It gains a `blocking` member produced
by the resolver, so the brief is the single snapshot of *what* the scene is **and** *how it is framed*.
The blocking is snapshotted (not resolved later) for reproducibility — a later profile edit cannot
rewrite a past render's framing.

### 3.3 Role clause re-keyed on `typedReferences` + `SceneBlocking`

The role clause stops deriving from bindings alone. It is built from the brief's `typedReferences`
(which already names `identity-p0`, `wardrobe-p0`, `identity-p1`, `wardrobe-p1`, `location` with
`subjectKey`) joined to `SceneBlocking.subjects`. Emitted form (matches vendor format):

```text
REFERENCE IMAGES — AUTHORITATIVE:
<image1> is the person on the LEFT — their identity comes from this image.
<image2> supplies that same person's build/body.
<image4> supplies that same person's clothing.
<image3> is the person on the RIGHT — their identity comes from this image.
<image5> supplies the background scene.
Keep both persons' faces, builds and clothing unchanged; do not re-describe them in words.
```

The `<imageN>` numbering still follows send order (unchanged owner: `ReferenceBindingShape.InSendOrder`);
only the *labelling* changes, from view-label to subject + position.

### 3.4 `FinalImagePromptPlan` — one assembly + validation point

A single builder composes the final prompt and validates it:

- inputs: scene prose (draft), `typedReferences`, bindings in send order, `SceneBlocking`, leading image
  count (0 for generate, 1 for edit).
- output: final prompt text + a **reference map** (`<imageN>` → subject → slot → position).
- validation (fail-fast): tag count == image count; every subject referenced exactly once per slot;
  no two subjects share a position; reference count ≤ 10 (≤ 6 when a pose is carried).

This replaces the ad-hoc `AppendToImagePrompt` / `AppendToEditInstruction` call sites in
`SceneImagePromptGenerationJobHandler`, `SceneImageRenderingJobHandler`,
`SceneAssetGenerationJobHandler`, and the edit compiler path — one owner, four consumers.

### 3.5 Asset Manager: minimal binding→prompt assembler

The Asset Manager has no beat/moment scene to describe, so it skips the prose half entirely: the same
`FinalImagePromptPlan` runs with an empty prose input and a trivial default blocking
("subject centered, facing camera") declared in the Asset Manager's staging profile. Slots render
directly to the role block — no operator typing.

### 3.6 Preview in the shared composer

`StepPromptPanel` (inside `ImageStepComposer`) gains a read-only "Final prompt" view that shows the
assembled prompt, the reference map, and the blocking — plus an optional placement override (a small
left/right/center × foreground/background selector) that rewrites only the `SceneBlocking` and re-runs
the assembly. Because it lives in the one shared composer, every surface gets it.

## 4. Config / UI surface

- `StagingProfile` persisted in a new `ImageStagingProfiles` table, edited as a new tab on the `/profiles`
  page ([`ThemeProfiles.razor`](../../../DreamGenClone.Web/Components/Pages/ThemeProfiles.razor)) — not Model Manager.
- Placement override lives in the composer preview (data → `SceneBlocking`, never hidden code paths).
- Missing required configuration fails fast with a named diagnostic.

## 5. Acceptance criteria

1. A two-character Composition with face+body+wardrobe+location references emits a final prompt whose
   role clause names each subject by position and groups that subject's references; no view-label
   collisions.
2. The generated scene prose names frame positions that agree with the role clause (single vocabulary).
3. Blocking is snapshotted in the Still brief and reproducible.
4. The composer shows the final prompt + reference map before render.
5. Asset Manager produces a role-clause prompt from bindings alone.
6. Missing blocking inputs fail fast with a diagnostic; no default placements are invented.
7. `MaxReferences` honored (10, 6 with pose); tests cover the validator.
