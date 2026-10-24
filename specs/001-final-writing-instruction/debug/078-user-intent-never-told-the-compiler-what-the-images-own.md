# 078 — User Intent drafts never told the compiler which elements the images own

**Reported:** 2026-10-17
**Surface:** Composition Composer → User Intent prompt source
**Render:** `SceneImage 2ecd4862-9d69-4c10-9b76-d8346a37432e`
**Prompt record:** `b9336f6a-c951-47f7-81b3-7f1e8071830c` (`PromptSource = 1` User Intent, intent 1443 chars)
**Model:** `qwen_image_2.1_int8_convrot.safetensors` · `NativeReference` · Composition · 1024×1024

## Report

Operator: *"now no shirt and not correct location."*

Two symptoms on one render:

1. **No shirt.** The wardrobe reference (`Jean Shorts`, `sceneAssetImageId fd8413fe…`) actually contains a **white tank
   top and denim shorts** — verified by viewing the stored image. The render shows her topless in shorts only.
2. **Wrong location.** The bound location reference `Clothesline Close 3` (`f33295f8…`) is a **porch with a rotary
   clothesline, a flowerbed and lawn**. The render shows a **wooden fence, two posts and dirt ground**.

## Analysis

The plumbing was correct — this was not a numbering, crop or binding defect. From the DB:

- Bindings carried `ordinal` **1, 2, 3** (no gap): `<image1>` Body (Profile Right · Unclothed), `<image2>` Wardrobe
  (Jean Shorts), `<image3>` Location (Clothesline Close 3).
- The intent text maps to those tags correctly.
- `NativeReferenceRenderSubmitted` shows `taggedReferenceCount: 3` and the appended reference block states
  `<image2> supplies that same person's clothing` and `<image3> (Clothesline Close 3) is the LOCATION reference: this
  image IS the room`.

The failure was in the **compiled prose**, which contradicted both references:

- `"Her clothing is a pair of blue denim jean shorts … and she wears nothing over them, so her shoulders, arms and
  midriff are bare."` — reduced from the intent's *"and she wears nothing over it"*, meaning "no extra layer on the
  outfit".
- `"Behind her the clothesline runs across the frame between two posts … To the left, a low fence and scuffed ground."`
  — none of this is in the intent and none of it is in the location plate; elaborated from the phrase "the clothes
  line".

**Root cause — one asymmetry.** The scene path protects itself from describing a supplied element by **removal**:
`SceneImagePromptGenerationJobHandler` calls `ReferenceBindingPromptRemoval.Merge(...)` and
`ScenePromptOverridesApplier.Apply(brief, effectiveOverrides)`, which strips the element out of the brief before the
compiler reads it. The `UserIntent` branch passes `referenceApplications` to
`NaturalLanguageSceneImagePromptBuilder.BuildIntentMessages`, where the bindings were used **only** to build the
numbered `<imageN>` frame-context list. There is no brief on that path, so nothing removed the elements the images
supply and nothing told the compiler they were supplied — leaving it free to write its own setting and its own
clothing, which then outranked the shorter, later reference block.

This is the documented "prose beats the image" failure mode, but here the prose did not merely duplicate the reference
— it **contradicted** it, which is why the tank top and the porch both vanished.

## Plan

1. `StepPromptElementPlan.Describe(scope)` — one readable name per element scope, in the file that already owns the
   scope set, so the intent notice and the scene removals cannot drift. Unmapped scope fails fast.
2. `NaturalLanguageSceneImagePromptBuilder.BuildIntentMessages` — append a `REFERENCE-SUPPLIED ELEMENTS —
   AUTHORITATIVE` block naming, per reference, the elements that reference owns (from
   `ReferenceBindingPromptRemoval.Derive` + `StepPromptElementPlan.ScopesFor`, the same functions the scene path uses),
   with instruction not to describe or re-derive them.
3. `NaturalLanguageSceneImagePromptBuilder.IntentAuthorityRules` — a standing rule appended to the intent path's system
   prompt: where the intent is silent or ambiguous about a reference-supplied element the image decides, and ambiguity
   is never settled by declaring the element absent.

Blast radius: `PromptSource == UserIntent` only. The scene/canonical call site and its payload are unchanged.

## Resolution

- `DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs` — added `Describe(string scope)`.
- `DreamGenClone.Web/Application/RolePlay/NaturalLanguageSceneImagePromptBuilder.cs` — added `IntentAuthorityRules`;
  `BuildIntentMessages` now appends the ownership notice via private `AppendReferenceSuppliedElements`, and returns
  `BuildCanonicalSystemPrompt() + CanonicalAuthorityRules + IntentAuthorityRules`.
- No handler change: the notice derives through `ReferenceBindingPromptRemoval.Derive`, which is the function
  `Merge` itself calls, so there is exactly one removal rule and no second copy of it.

Tests:
- `DreamGenClone.Tests/RolePlay/IntentPromptReferenceOwnershipTests.cs` — 5 tests over the exact `2ecd4862` binding set
  (ownership lines for `<image1>/<image2>/<image3>`, the no-describe instruction, the system-prompt precedence rule,
  text-only bindings owning nothing, and no notice with no bindings).
- `DreamGenClone.Tests/RolePlay/StepPromptElementPlanTests.cs` — 3 tests: every emittable scope is named, an unknown
  scope fails fast, a blank scope fails fast.

Build: 0 errors. Suite: see the completion report.

## Validated

- [ ] pending — requires a fresh intent compile + render by the operator.
