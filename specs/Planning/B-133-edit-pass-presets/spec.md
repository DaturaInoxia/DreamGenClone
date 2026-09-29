# B-133 — Image edit passes from presets (spec)

**Created:** 2026-09-27
**State:** design agreed; Phase 1 (preset data) implemented; Phases 2–5 open.

## 1. Problem

Two defects, one mechanism:

1. **Light cannot go low enough.** The identity references are bright, high-key studio images and the LoRA cell
   render is *conditioned on them* (Qwen-Image-2.1 native multi-reference). Measured on the 19 live attempts: subject
   luminance spans 64.8 → 182.4 of 255 with shadow p10 running 1.2 → 93.9 — so different levels ARE produced, but the
   subject is floored around 65 because the conditioning keeps the reference's exposure on the face. A text-to-image
   pass cannot be told to overrule its own reference.
2. **Expressions do not come through.** The cell prompts carry an expression *axis* ("a serious closed-mouth
   expression"), and the reference images carry a face, so the render returns a near-neutral face. A label is not an
   instruction.

## 2. Decisions (operator, 2026-09-27)

| # | Decision |
|---|---|
| D1 | **Deterministic assembly.** A preset assembles the final instruction itself; it is never handed to the vision compiler as an intent. The compiler would paraphrase away the exact detail the operator picked, and it costs a model call per edit. Free text keeps the compiler; "refine with the compiler" is opt-in. |
| D2 | **One preset row, two assembly rules.** The detail is written as a noun phrase and the assembly template supplies the verb, so an edit reads "Relight this photograph to …" and a compose step reads "The scene is lit by …". The step kind selects the rule. |
| D3 | **Keys align by suffix with the LoRA matrix vocabulary.** `lora.vocabulary.lighting.indoor-dim` ↔ `image.preset.lighting.indoor-dim`. A cell's own axis therefore selects the preset that edits its image to the same condition, and there is one preset list rather than two. |
| D4 | **The LoRA path records which preset produced an accepted image**, and the caption is composed from that preset — never from the plan's original axis. Otherwise the dataset teaches "dim" over a bright image. |
| D5 | **No special flow.** The action is available on any image (attempt list, composition list, asset review) and reuses the existing subject-agnostic edit pipeline. No new `MediaEditOperationKind`. |
| D6 | **Lighting is a tab in Edit *and* Compose.** Compose-side conditioning works when the references do not dominate; the UI says why rather than pretending it always works. |

## 3. Functional requirements

### Preset data
- **FR-1** Preset wording lives in the one prompt store (`ImageWorkflowPromptTemplates`) under `image.preset.*`, seeded
  and operator-editable, with character-scope overrides honoured by the existing resolver.
- **FR-2** Two preserve clauses (lighting, expression) that name what must stay identical.
- **FR-3** Four assembly templates, one per axis × mode.
- **FR-4** Six lighting presets aligned with the matrix's lighting values; twelve expression presets (the matrix's six
  plus `angry`, `sad`, `afraid`, `disgusted`, `crying`, `aroused`).
- **FR-5** A detail must carry the **mechanics**, not the label: a lighting detail names light source/quality/fall-off/
  white balance; an expression detail names the facial action units. Both are enforced by test.
- **FR-6** An axis with no presets (wardrobe, pose, background, distance) is refused **by name**, never mapped to a
  plausible-looking key.

### Assembly
- **FR-7** An edit instruction = change clause + preserve clause; a compose clause = the detail with the conditioning
  verb.
- **FR-8** An unfilled slot throws naming the slot. A preserve clause is required in `Change` mode (an edit that does
  not state what to keep re-renders the person) and refused in `Condition` mode (there is nothing yet to preserve).
- **FR-9** The composer owns no wording.

### Action and surfaces (Phases 2–3)
- **FR-10** Any rendered image exposes "apply an edit pass", opening the edit step with the source image, the preset
  list, and the host's default preset selected.
- **FR-11** The host supplies the default: the LoRA cell from its own `LightingKey`/`ExpressionKey`, mapped 1:1 to the
  preset by key suffix. The operator can change it.
- **FR-12** The edited image is stored as a derivative of its source (`SourceImageId` + `Edit` operation already
  recorded by `AddDerivedImageAsync`) and appears with its source in the attempt deck.
- **FR-13** The expression tab is offered only where the framing can carry a face (close/half), and says so instead of
  spending a render.

### LoRA correctness (Phase 4)
- **FR-14** An accepted edited image records the preset key that produced it.
- **FR-15** The caption for that member is composed from the recorded preset, so caption and image agree.
- **FR-16** Curation gates measure the edited image, not the pre-edit attempt.

## 4. Acceptance

1. Picking a lighting preset on a bright attempt produces a measurably darker image with the identity intact.
2. The same preset used in Compose adds a conditioning clause and does not change any edit-mode wording.
3. A preset pick is **byte-identical** across runs (deterministic assembly), and the instruction that reached the
   model is readable in the UI.
4. A LoRA member accepted from an edited attempt carries its preset, and its caption names the condition the image
   actually shows.
5. Every refusal above has a test that asserts the message names the offending key/slot.

## 5. Out of scope

- Region/mask editing (B-130 tracks it).
- Relighting via a dedicated relight model (IC-Light) — a later capability profile if the edit pass is not enough.
- Any change to the identity/body reference pipelines.
