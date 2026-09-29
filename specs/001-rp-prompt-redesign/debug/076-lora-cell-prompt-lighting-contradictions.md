# 076 — LoRA cell prompts: light that names the setting, stances the framing cannot show, and a subject the body reference erased

**Date:** 2026-09-27
**Reported by:** operator
**Area:** B-123 LoRA images tab (`LoraDatasetWorkspace`) — the coverage cell's render prompt

## Report

> "may have small issue with lora images, some prompts are for lower light, issue is the becky body and face
> references always seem to be bright and not using dimmer lighting, check all the lora cell prompts and review
> for contradictions and clean prompts"

Live state at the time: dataset `8d639aa57dcc47bf89bbf6243379ea3f` (character template `de351eb3-…`,
Becky), pack v9 `BodyComplete` Approved, cell model `Qwen-Image-2.1 (Local ComfyUI)`,
`LoraCellModelId` = `3f1c9a52-…`, **19 cell attempts** already rendered (2026-09-25 → 09-27).

## Analysis

Read: all 56 live `lora.%` prompt rows (byte-identical to the seed), the live plan's per-cell axes, the 19
attempts' exact prompts, and 8 rendered images.

**1. The lighting phrases named the setting, and the setting advanced on a second, independent cycle.**
`LightingCycle` and `BackgroundCycle` were two unrelated rotations, so **14 of the plan's 36 cells** paired a light
with a setting it cannot occur in:

| Live cell | Prompt the model was sent |
|---|---|
| `core.front.cu.1`, `34l.cu.1`, `34r.hb.1`, `pl.hb.1`, `pr.hb.1`, `variation.outfit.b`, `variation.expression.surprised` | "flat daylight **outdoors**. Background: a plain neutral wall" / "…a plain **studio** backdrop…" |
| `variation.expression.laughing` | "even bright **indoor** lighting" + "an **outdoor** setting with trees and open sky" |
| `core.front.fb.1`, `34l.fb.2`, `behind.hb.1`, `pl.cu.1`, `pr.cu.1`, `variation.lighting.rim` | "a hard rim light … **against a dark surround**" + "trees and open sky" / "a simply furnished living room" |

The two daylight/studio cells rendered as **bright flat high-key studio portraits** — the background phrase wins the
contradiction, and the bright studio look is exactly what the pack's reference images already are (canonical face =
blown-out white; clothed body front = bright grey studio), which Qwen-Image-2.1 carries as native references. The
cells whose pairing was coherent (bedroom+dim, kitchen+night) did render dim, so the phrase itself was working — the
contradictory pairs were the defect.

**2. Three of the eight close-up cells carried a stance the framing cannot show.** Pose advanced on a global cycle
independent of distance, so `pl.cu.2` (on all fours), `pr.cu.1` (both arms raised above the head) and `pr.cu.2`
(lying down) were prompted against "the whole head and both shoulders in frame".

**3. Negations in the positive, in all 12 render templates** — "no retouching", "nothing cropped", "no part of the
face visible". Prompt-compiler standards §3.2: negations belong in the negative, never in the positive; this pipeline
authors **no negative** for any family it renders, so that text had nothing to act on.

**4. The subject disappeared when the body reference was bound.** D4 omits `{BodyCard}` when the Body slot is
supplied by a reference — correct for the *build* — but the slot held the WHOLE person description, so the lead
collapsed to "Photorealistic close-up photograph." with no subject at all (6 of the 19 attempts). A model shown no
subject fills the gap with its own prior; §2.1 of the standards puts the subject first.

**5. Two rows are dead data.** `lora.cell.references` (which is where "do not take the lighting from either
reference" was written) and `lora.cell.edit.tweak` are never resolved by any code.

## Plan

Approved by the operator (three choices: cleanup + coherent pairing; fix the close-up stances; thread the stated
gender in as the subject noun).

## Resolution

| File | Change |
|---|---|
| `DreamGenClone.Infrastructure/RolePlay/ImageWorkflowRepository.cs` | 12 render templates: slot `{BodyCard}` → `{Subject}`; tails lose every negation ("fine detail." replaces "no retouching.", "nothing cropped" and "no part of" removed); no render template is a byte longer than the 800-char qualified ceiling. 6 lighting phrases re-worded to state the LIGHT only (intensity, direction, what stays dark) — no setting, and no "dark surround" claim that contradicts a lit background. |
| `DreamGenClone.Web/Application/RolePlay/CharacterLoraCoveragePlanGenerator.cs` | `LightingCycle` + `BackgroundCycle` replaced by `LightingBackgroundPairs`: 18 coherent (light, setting) pairs, each of the six backgrounds three times and each of the six lights two-to-four times; `BackgroundsForLighting` re-derives a coherent setting for a variation cell that overrides the light. `CloseUpPoseCycle` (standing/sitting/kneeling) for close-ups; variation cells keep their own index-based sweep. Class doc gains the "Coherent" property. |
| `DreamGenClone.Web/Application/RolePlay/LoraCellPromptComposer.cs` | New `{Subject}` slot. When the Body slot is supplied by a reference, the slot takes the **subject noun** instead of being omitted — the one thing a body image does not supply. With no noun stated the slot is omitted exactly as an unknown face is (never guessed). |
| `DreamGenClone.Web/Application/RolePlay/BodyReferencePromptCompiler.cs` | `SubjectNounPhrase` made public: one spelling of the noun, reused rather than re-derived. |
| `DreamGenClone.Web/Application/RolePlay/ICharacterIdentityBodyService.cs` + `CharacterIdentityBodyService.cs` | `CharacterBodyTexts` gains `SubjectNoun` (stated gender + age band), filled from the same brief as the description. |
| `DreamGenClone.Web/Components/Editing/LoraDatasetWorkspace.razor` | Passes `SubjectNoun` into the composition. |
| Tests | `LoraCellPromptComposerTests` (subject survives a bound body reference; omitted rather than guessed when no noun is stated), `CharacterLoraCellTemplateSeedTests` (lighting names the light not the setting; no negation in the positive; `{Subject}` slot; re-worded seed body), `CharacterLoraCoveragePlanGeneratorTests` (never pairs a light with a setting it cannot occur in; never gives a close-up a stance its framing cannot show). |
| `DreamGenClone.DbQuery/queries/lora-cell-prompt-cleanup.sql` | Forward UPDATE of the 18 live dev-DB rows (`INSERT OR IGNORE` never rewrites a seeded row), guarded by `Body = SeedBody` so a hand-edited row is never overwritten. Verified 0 rows had been edited first; 18 rows affected; `Body = SeedBody` holds afterwards. Review queries: `lora-lighting-review-*.sql`. |

### What takes effect when

- **Template bodies are resolved per cell**, so the new template wording and the subject noun apply to the EXISTING
  dataset without a restart.
- **The lighting phrases and the pairing live in the plan snapshot** (`CoveragePlan.Vocabulary` + each record's
  keys), so the re-worded light phrases and the coherent pairings need a **new plan — i.e. a new dataset**. The
  existing plan keeps its snapshot by design (`PhraseFor` refuses a key it does not carry rather than substituting).

## Validated

- Build: `dotnet build DreamGenClone.sln --no-restore -p:OutDir=artifacts\build-check\lora-prompts\` → **succeeded**
  (a separate output folder, so the running app's `bin` was never locked and the app was not stopped).
- Tests: `--filter "FullyQualifiedName~RolePlay"` → **2594 passed, 3 failed**; the 3 are the documented
  pre-existing `SdxlSceneImagePromptBuilderTests` failures (`BuildCanonicalMessages_SystemPrompt_…`,
  `BuildCanonicalMessages_WithUserRemovals_…`, +1 — see `pre-existing-test-failures.md`, 2026-09-11), in a class
  this change does not touch.
- Renders: **not yet re-shot with the new wording** — the cells are shot one at a time from the LoRA images tab.

- [ ] pending operator confirmation (re-shoot the daylight/studio cells and the 4 dim cells on a NEW dataset)
