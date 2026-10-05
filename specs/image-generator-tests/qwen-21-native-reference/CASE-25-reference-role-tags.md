# CASE-25 — The prompt must name the reference images (measured 2026-10-03)

**Question:** the operator bound the approved Maintenance Shed location *and* a character's face and body, and the
shed was not reproduced — while the *same* shed bound alone was faithful. Is the location being dropped, starved of
pixels, or out-competed?

**Answer: out-competed, because the prompt never said which image was which.** The app's scene prompt carried no
`<imageN>` tags and no role text at all. Naming the references moved the same two files from **outer-ring histogram L1
1.574 → 0.887** against the bound shed — better than the shed-alone control (0.936) — with the photometry landing on
the shed. Two rival explanations were tested and refuted in the same rig: the reference **pixel budget** (1.521 →
1.530, and *pixel-identical* at one reference) and **slot order** (1.500).

## Method

Reproduces the app's render `038dc86f` (session `8bc36efb…`, turn `32244bb9…`) on the same host, with the app's OWN
three reference files, its OWN prompt verbatim, and its re-qualified envelope:

| | |
|---|---|
| References | identity face `282f5b91…` ("Front"), identity build `37f0fa3f…` ("Front · Unclothed"), approved scene asset image `4371dc7f…` ("Workbench Back", Maintenance Shed) |
| Envelope | cfg 3, `er_sde`, `beta`, 30 steps, 1024×1024, seed **20261003** (pinned; the app's own renders logged `seed: random` and never persisted it, so they cannot be compared to each other) |
| Runner | `helpers/local-comfyui-host/run-qwen-2-1-proof.ps1` — cells `appThreeRefs`, `appLocationOnly`, `appThreeRefsLocFirst`, `appTwoRefsBodyLoc`, `appTwoRefsBodyLocLabelled`, `appThreeRefsLabelled` |
| Host | ComfyUI 0.37.1, RTX 5080, `qwen_image_2.1_int8_convrot.safetensors` |

Every reference was SHA-256 verified by the runner before upload. The app's stored prompt for both renders is
byte-identical, so the reference set and the prompt are the only variables.

Metrics follow CASE-20's table so the numbers are comparable: `mean` / `std` / `edge energy` (mean |Laplacian| on
grayscale). `room L1` is an L1 colour-histogram distance over the OUTER 15% ring of the render — the band where the
room shows rather than the subject — against the shed reference, 16 bins per channel.

## Results

| arm | refs | mean | std | edge | room L1 vs shed |
|---|---|---|---|---|---|
| **shed reference** | — | 68.5 | 46.7 | 9.1 | — |
| LIVE app `0c2e8a4e` | location only | 68.8 | 55.1 | 4.2 | **0.772** |
| LIVE app `038dc86f` | face+body+loc | 83.7 | 74.0 | 3.5 | 1.497 |
| shed alone (control) | location only | 59.3 | 50.4 | 4.2 | 0.936 |
| shed alone, `resolution 0` | location only | 59.3 | 50.4 | 4.2 | 0.936 |
| 3 refs, `resolution 1024` | face+body+loc | 82.8 | 76.9 | 3.1 | 1.521 |
| 3 refs, `resolution 0` | face+body+loc | 83.2 | 76.0 | 3.0 | 1.530 |
| 3 refs, shed in **slot 1** | loc+face+body | 82.8 | 74.6 | 2.9 | 1.500 |
| 2 refs, app prompt, **no role text** | body+loc | 79.9 | 73.7 | 3.2 | 1.574 |
| 2 refs, **LABELLED** `<image1>`/`<image2>` | body+loc | 61.8 | 54.3 | 4.4 | **0.887** |
| 3 refs, **LABELLED** | face+body+loc | 65.9 | 62.8 | 3.5 | 1.183 |

## What each observation establishes

1. **The reference was never dropped.** `RolePlayDebugEvents.EventKind = 'NativeReferenceRenderSubmitted'` records
   `referenceCount: 3` and the three semantic roles, in order (identity face, identity build, location continuity).
   `SceneImages.AppliedReferenceBindingsJson` carries the location at `ordinal: 3`, and
   `ComfyUIImageClient` fails the render outright if the count exceeds `MaxReferences`. Nothing is silently discarded.
2. **The pixel budget is refuted.** `TextEncodeQwenImage21.resolution` IS a total budget — with ONE reference, 1024
   and 0 are **pixel-identical** (`mean|diff| 0.0`), because a 1024×1024 reference already fills a 1024 budget. But at
   three references, removing the budget changes the pixels (`mean|diff| 16.0`) without moving the room at all
   (1.521 → 1.530). `Resolution` was temporarily set to 0 in the model row to run this arm and **reverted the same
   day**.
3. **Slot order is refuted.** Putting the shed in slot 1 shifts the metric 1.4% (1.521 → 1.500). Consistent with
   CASE-22's finding for the pose signal: position is not the variable.
4. **Role text is the lever.** The two-reference arm is the clean control: same two files, same order, same seed,
   same envelope, only the prompt differs — and it moves 1.574 → 0.887, i.e. from *indistinguishable from the failing
   3-ref case* to *better than the shed-alone control*, with brightness and detail landing on the shed (mean 61.8 vs
   68.5; edge 4.4 vs the control's 4.2).
5. **Fewer identity references is NOT the lever.** Dropping the face while the prompt stayed unlabelled changed
   nothing (1.574 against the 3-ref 1.521). The count only matters once the references are named: 2 labelled (0.887)
   beats 3 labelled (1.183), consistent with Qwen-Image-Edit-2509's official "optimal performance is currently
   achieved with 1 to 3 input images".

## Why the app got away with it

Qwen's own requirement is conditional on the count. `QwenLM/Qwen-Image-2.1`
`prompt_rewrite/prompts/system_prompt_edit.txt`:

> "For Multi-Image Input (N >= 2), the rewritten instruction MUST use `<image1>`, `<image2>`, ... to refer to each
> input image. Do not use natural language references like "the first image" ... **This tagging format is mandatory and
> non-negotiable.** For single-image input (N = 1), do NOT use tags — refer to the image naturally."

and, same file:

> "State each image's **role** explicitly — which one is the canvas whose composition and untargeted content survive,
> and which supply material to transfer."

So **N = 1 is the exempt case, and every location-only render is N = 1** — which is exactly why the app's prompts
looked fine. ComfyUI's `TextEncodeQwenImage21` passes the user prompt through unchanged (the 2509/2511 nodes instead
auto-prepend `Picture N:`), so ordering alone can never bind an image to a description: with no tags in the text the
model receives no index at all.

Confirmed against Qwen's own repo, ComfyUI's official 2.1 template/docs and the diffusers `QwenImage21Pipeline`
sources — full citations in `.github/instructions/scene-image-prompt-compiler-standards.instructions.md` §6 and its
HARD RULE 10.

## Consequences for the app

`ReferenceBindingPromptRemoval` removed the element prose a reference supplies (a Location binding clears
`frozenState.location` / `frozenState.environment`) and nothing ever stated which image supplied it. That is half a
contract. Fixed 2026-10-03 by `ReferenceRoleClauses` + `ReferenceBindingShape`:

- `ReferenceBindingShape.InSendOrder` is the **single owner** of the order references reach the model in (pack
  images by ordinal, then approved-asset ones). The render path and the role clause both read it, because the planned
  order and the sent order differ as soon as a pack binding and an asset binding are mixed — and Qwen's own docs warn
  that reordering the images makes "every reference in the rewrite silently re-point".
- **The numbered block is composed at RENDER time**, in `SceneImageRenderingJobHandler.RenderNativeReferenceAsync`,
  from the images the render is about to send. The role list is built ALONGSIDE the reference list — one entry per
  image actually added, including the pose appended last — so the numbering cannot diverge from the send order by
  construction, and every route into a render gets it.
- `ReferenceRoleClauses.ForPreprocessor` states the same roles in the prompt-generation message, so the pre-processor
  can write a prompt that names them instead of describing them for the first time.
- Nothing is emitted at N = 1, per the same rule.

**Why the block is NOT composed at prompt generation — the first implementation was wrong.** It was originally
appended to `SceneImagePromptRecord.OutputPrompt` from the job payload's `ReferenceApplications`. Driving the real UI
(Composition Composer → Generate Prompt) proved it never fires for the reported flow: the composer's prompt
generation does not carry reference applications, and neither did the prompt the failing render actually used
(`6b7f5bad`, 244 chars — it *describes* the shed in prose rather than having had it removed, so nothing upstream of
the render ever knew about the three bindings). The bindings live in `SceneImages.AppliedReferenceBindingsJson`,
attached at the **enqueue/render** step. Measured on the live app: the new prompt's system message contained
`USER REMOVALS ARE AUTHORITATIVE` (my `CanonicalAuthorityRules`, present in the same build at offset 4215, against 0
in the pre-change events) while its user message contained no `REFERENCE IMAGES` clause at all. The injection point,
not the clause, was the defect — which is exactly what an end-to-end check is for, and why unit tests that feed the
clause its own bindings could not have caught it.

**Known remaining gaps (all three call sites now closed 2026-10-04):**
1. ~~`SceneImageRenderingJobHandler.RenderIdentityConditionedAsync`~~ — **done**: builds its role list alongside its
   reference list (pack face per depicted character, then the bound assets, then the pose) and numbers in send order.
2. ~~`SceneAssetGenerationJobHandler`~~ — **done**: `ReadAssetReferencesAsync` returns the roles paired by position
   with the resolved inputs (and fails if the resolver returns a different count), and all three routes that carry
   them — the identity route, the native-pose route and the asset-only route — number in send order.
3. ~~The Pony builder keeps its own copy of the canonical user prompt~~ — **done**: it now carries
   `CanonicalAuthorityRules`, the USER REMOVALS notice and the role clause, and no longer sends the provider-request
   snapshot (which restates the brief and hands back removed elements — debug 052).

**What is still NOT verified:** no live app render has been observed with the tags reaching ComfyUI. The tag
mechanism itself is measured at a pinned seed (the A/B above), and the wiring is unit-tested per route, but the
end-to-end path (composer → prompt job → render job → `TextEncodeQwenImage21.prompt`) has not been watched. The
composition composer needs at least two references BOUND before the block is emitted at all — with one reference the
app issues no tags, which is Qwen's rule for N = 1.

## Reproduce

```powershell
# the label test - the only variable is the prompt
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 `
    -Cells @('appTwoRefsBodyLoc','appTwoRefsBodyLocLabelled','appThreeRefsLabelled') `
    -Seed 20261003 -Steps 30 -Cfg 3 -Sampler er_sde -Scheduler beta -Resolution 1024 `
    -OutRoot artifacts/tmp/qwen-2-1-app-ab-labels

# the two refuted arms
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells @('appThreeRefs','appLocationOnly') `
    -Seed 20261003 -Steps 30 -Cfg 3 -Sampler er_sde -Scheduler beta -Resolution 1024 `
    -OutRoot artifacts/tmp/qwen-2-1-app-ab-budget1024
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells @('appThreeRefs','appLocationOnly') `
    -Seed 20261003 -Steps 30 -Cfg 3 -Sampler er_sde -Scheduler beta -Resolution 0 `
    -OutRoot artifacts/tmp/qwen-2-1-app-ab-budget0
& helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells @('appThreeRefsLocFirst') `
    -Seed 20261003 -Steps 30 -Cfg 3 -Sampler er_sde -Scheduler beta -Resolution 1024 `
    -OutRoot artifacts/tmp/qwen-2-1-app-ab-locfirst
```

Contact sheet: `artifacts/tmp/qwen-2-1-app-ab/sheets/app-ref-budget-ab.png`.
