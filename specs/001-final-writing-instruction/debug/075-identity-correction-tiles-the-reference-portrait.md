# Editor face identity: the reference portrait is composited into the frame instead of swapping the face

## Report

Reported 2026-10-13 21:49 from the Scene Image Editor:

> the edit face identiy is broken ##### 73568daa-2aa6-437a-81c1-163c790b1f64

`73568daa-2aa6-437a-81c1-163c790b1f64` is the Identity pass's result row (`ProductionStage = Identity`,
`SourceImageId = e55355f9-594d-4148-a12b-1fcc63dd603b`, job `media-edit-image-editing:73568daa…`, **Complete**,
`Model=qwen_image_2.1_editor`, `References=1`, 234 s). The pipeline ran; the IMAGE is wrong: the approved face
reference — a head-and-shoulders portrait — is pasted into the composition at nearly full-frame scale, with the
original woman still standing in the yard. The second identity pass of the night (`2a19041b-04b0-48cd-86e5-e26c1d481028`,
source `6b15c869`) failed the same way.

`6b15c869` is itself an identity pass from 14:35 the same day, and that one was clean. All three used the same
editor model and one reference image; two of the three used the SAME reference asset.

## Analysis

**The instruction is the variable.** The identity pass sends `<source>` + the approved pack face + an instruction
assembled by `SceneImageService.BuildEditorIdentityInstruction`, which splices the vision compiler's `key` and
`visibleLocator` into `"…to the {key} at {locator}."` Measured from the dev store:

| Run (EDT) | Analysis version | Instruction target clause | Reference face | Result |
|---|---|---|---|---|
| 14:35 | `qwen-edit-2.1-rules-v1` | "…to the woman **at center-left**." | `c4d4b2b7…` | clean swap |
| 21:27 | `qwen-edit-2.1-rules-v3` | "…to the woman **on image left at left side of the image, near the center**." | `c4d4b2b7…` (**same asset as the clean run**) | reference portrait tiled into the frame |
| 21:28 | `qwen-edit-2.1-rules-v3` | "…to the woman **at on the image left**." | `206f367e…` | reference portrait tiled into the frame |

The v1 analysis wrote `key "woman"` / `visibleLocator "center-left"` (attempt `e5408ec9`, 18:34). The v3 analyses
wrote `key "woman on image left"` / `visibleLocator "on the image left"` (attempt `e9ad3ba3`, 21:28). So the field
that the template treats as a **bare place** became a **direction clause**, and `"at {locator}"` produced
"the woman at on the image left" — a target the editor cannot bind to a face. The documented failure mode of this
path is exactly "extra face tiled into the frame", and the long character-named variant
(`BuildFaceOnlyIdentityInstruction`, which never splices a locator) is the measured-clean one.

**Where the drift came from.** The v2 prompt rewrite (record 072) added frame-anchoring rules — *"name the side of
the frame ('toward the right edge of the image', 'on the image right') instead of a bare 'right'"* — written for the
**compiled prompt**, and the model applied them to `visibleLocator` as well. v1 happened to write a bare place there;
v2/v3 wrote the clause. Nothing in the code said which shape that field must have, and the template assumed the shape
the analysis only coincidentally produced.

Two smaller effects of the same analyses: `headView` came back `three_quarter_right` for a subject facing the camera
(so a 3/4 reference face was bound to a front-facing person), and `region` came back as a whole-body box
(`0.457 × 0.832`) rather than a face box.

Sequence: v2 (072) made identity analyses `invalid` → v3 (073) restored them → the correction step is where the
locator damage then showed, which is why this surfaced only tonight.

## Plan

1. State the targeting fields' shape in the 2.1 system message, so the field the template splices can only be a
   place. Verified against the configured compiler model before shipping, and the shipped literal checked
   byte-identical to the text that was measured.
2. Make the instruction tolerate any locator shape, so a clause can never be spliced into the sentence's grammar
   again.
3. Tests, build, focused + full suite, debug record.

## Resolution

- `QwenImage21EditPromptCompiler.cs` — one sentence appended to the observation paragraph:
  *"visibleLocator is a short place that reads naturally after the word \"at\" — \"center-left\", \"left of the
  frame\", \"image right, midground\" — never a direction or a sentence of its own; region is the box around that
  person's face, not their whole body."* `SystemPromptVersion` → `qwen-edit-2.1-rules-v4`.
- `SceneImageEditPromptCompilerResolver.cs` — v3 added to `Qwen21HistoricalVersions`, so an attempt queued under
  v1, v2 or v3 still resolves and then fails on the handler's explicit "contract changed" check.
- `SceneImageService.BuildEditorIdentityInstruction` — the locator rides in a parenthetical:
  `"Apply the face of the person shown in Picture N to the {key} (position: {locator})."`. The sentence — and the
  proven face-primary recipe around it — is unchanged; only the locator's slot is, and it now reads identically for a
  bare place and for a clause.
- `helpers/local-comfyui-host/run-local-aio-edit-proof.ps1` — the offline harness mirrors this builder, so its copy
  moved in step (it is the mirror the pinned test warns about).
- Tests — `QwenImage21EditPromptCompilerTests.The21Instruction_FixesTheTargetingFieldsSurfacesSpliceIntoSentences`
  (new) and the 2511 text asserted free of it; `SceneImageServiceJobTests` pins the new instruction verbatim plus a
  three-case theory over locator shapes (`"on the image left"`, `"left side of the frame"`, `"center-left"`) asserting
  the sentence holds and the broken form is absent; `SceneAssetImageEditRunCutoverTests` and the resolver theory
  updated for the new text and the v4 history.

### Why the field sentence is one appended sentence and not a paragraph

Measured against the configured compiler model (same image, same intent, every sample):

| Candidate text | Pose intent expands? | `visibleLocator` | `headView` (front-facing subject) |
|---|---|---|---|
| v3 (shipped tonight) | 3/3 expanded | "on the image left" | `three_quarter_right` (wrong) |
| v4 as a **section** | **0/3 — verbatim echo** | "left side of the frame" | `front` |
| v4 as **one sentence** (shipped) | **3/3 expanded** | "center-left" / "left of the frame" | `front` |

The paragraph form cost the pose intent its expansion 3/3 — the defect record 072 exists to prevent — so the shipped
wording is the one-sentence form, which is byte-identically what was probed
(`artifacts/tmp/probe/verify_v4_matches_probed.py` → `IDENTICAL`).

## Validated

- [x] Probe, same harness as record 073 (`artifacts/tmp/probe/probe_field_sentence.py`, `probe_v4_fields.py`):
  identity intent returns `ready` with ONE target on both source images (6/6), `visibleLocator` a spliceable place in
  every sample, `headView = front` for the front-facing subject, `region` face-sized (84×76, 140×140, 160×160, 240×240)
  where v3 reported `0.457 × 0.832` of the frame.
- [x] Echo attribution (`probe_echo_attribution.py`): v3 3/3 expanded vs the first v4 draft 3/3 echoed — the regression
  was mine, found by the probe, and fixed by the reworded-by-measurement form above.
- [x] Shipped compiler literal equals the probed text byte for byte.
- [x] `dotnet build DreamGenClone.Web/DreamGenClone.csproj` — 0 errors, no warning from either changed file.
- [x] Focused set (compiler, resolver, scene-image service, carryability guard, location prefill, prompt-plan guard,
  studio UI contract, reference clauses): **193 passed, 0 failed**.
- [x] Full suite — **4278 passed / 27 failed / 4305 total**; all 27 are the pre-existing unrelated `CharacterLora*`
  set (record 072), nothing outside it.
- [ ] Pending operator acceptance: re-run one identity correction (Analyze faces → bind → apply). The face should
  swap in place. This cannot be probed off-line — the editor is a ComfyUI image model and every sample is a real
  render on the operator's host.

## Verified but NOT fixed (reported, not silently left)

- **`region` is improved but still unreliable**: the one-sentence form returned a face-sized box on four samples and a
  whole-body box (678×900) on one. Nothing consumes it today (the identity run passes `region: null`), so it is
  display-only — but it must not be used to MASK an identity edit until it is measured to be reliable.
- **`headView` still misclassified once** on the bending-over source (`three_quarter_right`), so the reference face
  view can still be picked wrong for an off-axis subject.
- **The template's parenthetical is not measured against the editor model** (each sample is a real render). Only the
  compiler sentence was measured; the template change is reasoned from the three observed instructions and pinned by
  tests. If the operator's re-run still tiles, this wording is the next thing to change, and the region-confined
  identity pass (masking the correction to the detected face box) is the structural fix.
