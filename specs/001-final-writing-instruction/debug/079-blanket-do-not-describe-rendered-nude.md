# 079 — A blanket "do not describe supplied elements" rendered the figure nude

**Reported:** 2026-10-18
**Surface:** Composition Composer → User Intent prompt source
**Render:** `SceneImage c0d77b66-6263-4beb-a4c1-25e58af798bf`
**Prompt record:** `5092a03a-fd7f-4ef5-ae82-4b537a0722cd` (`PromptSource = 1`, intent 1443 chars)
**Regression of:** debug record 078.

## Report

Operator: *"going in circle you are not fixing anything without breaking something else, c0d77b66 not she is naked."*

After 078, the invented location prose was fixed — but the render became fully nude, where the previous render
(`2ecd4862`) had at least worn shorts.

## Analysis

Both halves compared from stored prompts:

| | `2ecd4862` (before 078) | `c0d77b66` (after 078) |
|---|---|---|
| Location prose | "between two posts … a low fence and scuffed ground" (invented) | "exactly as the location reference supplies it" ✅ fixed |
| Clothing prose | "a pair of blue denim jean shorts … **so her shoulders, arms and midriff are bare**" | "Her clothing is exactly as the wardrobe reference shows it … **and she wears nothing over it**" |
| Outcome | shorts, no top | nude |

**Root cause — my own instruction was too broad.** 078 emitted a single blanket rule: *"Do not describe any of them in
words."* It applied to **clothing as well as setting and appearance**. The compiler obeyed literally: it stopped naming
any garment at all, leaving only the operator's carried-over clause *"and she wears nothing over it"*.

That exposed the real distinction. Two kinds of reference-supplied element behave in opposite ways:

- **Setting / appearance / stance** — the image IS the element. Prose beside it competes with it, so prose must stay
  silent. (This is what fixed the location.)
- **Clothing** — the image is a photograph of GARMENTS, not of a dressed figure. Something must be *said* for them to
  be put on the body. Silence dresses nobody.

Also relevant, from the same render: `<image1>` is the identity pack's **`Profile Right · Unclothed`** plate and
`<image2>` is a **flat-lay** product shot (tank top + shorts laid on grey). A nude person plate is the strongest
concrete visual in the set, so anything that leaves the outfit unstated yields nudity.

## Plan

1. `StepPromptElementPlan` — add `PromptElementOwnership` (`SuppliedSilently` / `SuppliedWorn`) and `OwnershipOf(scope)`,
   beside `ScopesFor`/`Describe`, so the treatment of an element is decided with the scope set and an unmapped scope
   fails fast.
2. `NaturalLanguageSceneImagePromptBuilder` — split the notice into a silent list and a WORN list; for clothing, instruct
   that the outfit be named from the reference and put on the figure.
3. Same method — add an `OMIT CONTRADICTIONS` instruction that names the exact phrases observed ("she wears nothing over
   it", "her shoulders are bare", "nothing over them") and orders them left out of the compiled prompt.
4. `IntentAuthorityRules` — state that anything a clothing reference shows is WORN.

Blast radius: `PromptSource == UserIntent` only.

## Resolution

- `DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs` — `PromptElementOwnership` enum and
  `OwnershipOf`; every scope classified, unknown scope throws.
- `DreamGenClone.Web/Application/RolePlay/NaturalLanguageSceneImagePromptBuilder.cs` — `AppendReferenceSuppliedElements`
  now emits the silent list, the WORN list and `OMIT CONTRADICTIONS`; `IntentAuthorityRules` states the worn rule.

Resulting compiler input for `c0d77b66`'s exact bindings (verified by dumping through the builder):

```
REFERENCE-SUPPLIED ELEMENTS — AUTHORITATIVE:
These elements arrive as IMAGES in this request, and an image is exact where prose beside it is not.
Do NOT describe these in words and do not re-derive them from another description:
- <image1> supplies p0's appearance — face, hair, skin tone, build and proportions.
- <image3> supplies the setting — its structure, surfaces and objects; the surroundings; the lighting.
These are WORN. Say what she has on, naming each garment the image actually shows, and dress
the figure in it: a clothing reference is a photograph of garments, not of a body, so saying
nothing about it leaves her with nothing on:
- <image2> supplies p0's clothing.
OMIT CONTRADICTIONS: leave out of the prompt every statement about what she is NOT wearing or
what is NOT added on top — "she wears nothing over it", "her shoulders are bare", "nothing
over them" and any remark like them. Do not copy such a sentence from the intent: the clothing
reference already shows the truth, and carrying the sentence over undresses her.
```

Tests:
- `IntentPromptReferenceOwnershipTests` — added `UserPrompt_NeverTellsTheCompilerToStaySilentAboutClothing` (pins that
  the clothing line sits under the WORN heading, never the silent one), `UserPrompt_OrdersTheAbsenceClauseDropped`,
  `UserPrompt_KeepsSilentElementsOutOfProse`; `SystemPrompt_CarriesTheReferenceOutranksProseRule` now pins the worn rule.
- `StepPromptElementPlanTests` — `Ownership_EveryScopeResolvesAndClothingIsTheOnlyWornElement` (clothing is the only
  WORN scope; location is silent) and `Ownership_UnknownScope_FailsFast`.

## Validated

- [ ] pending — requires a fresh intent compile + render. Watch for: the location still matching the plate, AND the
  prompt naming the tank top and shorts, AND no "nothing over" clause surviving into the prompt.

## Still true, and worth stating plainly

The bound `<image1>` is an **Unclothed** body plate and `<image2>` is a **flat-lay**. That combination asks the model to
strip a nude figure from its clothes and dress it from a product photo, and no wording makes that reliable. If the fix
above does not hold, the next lever is the reference SET, not the prose: bind the identity pack's clothed view, or drop
the body plate and let `<image2>` plus a face reference carry the figure (measured good earlier in this session).
