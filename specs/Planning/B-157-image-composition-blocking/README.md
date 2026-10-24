# B-157 — Image composition blocking & reference-role prompt assembly

State: `implemented` (design written and reviewed 2026-10-09; implemented 2026-10-11 — see
[`tasks.md`](tasks.md) → *Implementation record* for what landed, what remains, and the pre-existing double-append
defect found on the way).

The app can bind face / body / wardrobe / pose / location reference images to a render, and it sends
them with correct `<imageN>` tags. What it cannot do is tell the image model *where each person is in
the frame* or *which reference belongs to which person*. The scene prose is world-space only, and the
role clause labels references by their pack view ("3/4 Left"), which collides across characters. The
operator is therefore forced to hand-type the spatial composition the app already has data to derive.

## Documents

| File | What it is |
|---|---|
| [`spec.md`](spec.md) | Requirements and the design (SceneBlocking, FinalImagePromptPlan, role-clause rework). |
| [`plan.md`](plan.md) | Implementation plan, the gap-fill map (exact source field → target), and open questions. |
| [`research/external-validation.md`](research/external-validation.md) | External, source-verified research on Qwen-Image-2.1 multi-image prompting. |

## Evidence base (read first)

The claims in these documents were verified against the dev DB and the code on 2026-10-09, not
assumed:

- The render for the operator's reported case sent **4/4 bound references** with correct tags
  (`RolePlayDebugEvents` `NativeReferenceRenderSubmitted`, `referenceCount=4`, `taggedReferenceCount=4`);
  the Location was **not bound** in that attempt (`locationCount=0`).
- The live `Qwen-Image-2.1 (Local ComfyUI)` model row declares `MaxReferences: 10`; `16` is only the
  ComfyUI node's autogrow slot ceiling; `6` is the measured pose-carrying budget only.
- `SemanticInputSnapshotJson` for session `8bc36efb…` has `frozenState.characters[]` with
  `physicalLocation`/`position`/`sightline`, a `compositionRationale`, and a `typedReferences` list —
  but no `camera`/`shot`/`blocking` field.
