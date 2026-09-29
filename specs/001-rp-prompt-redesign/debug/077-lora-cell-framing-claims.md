# 077 — LoRA cells claimed things their framing could not show

**Date:** 2026-09-27
**Trigger:** operator report — "`core.front.cu.1` says standing, jeans and t-shirt but the prompt is just head and
shoulders; `core.front.cu.2` is the same except naked, but it is only head and shoulders so not really naked; sitting
vs standing and head and shoulders do not make sense."

## Root cause (three findings, in the order they matter)

### 1. The plan the operator was reading was two days stale

`CharacterLoraDatasets.PayloadJson.coveragePlanJson.generatedUtc = 2026-09-25T05:20:14Z`. The lighting/setting
cleanup landed 2026-09-27. The plan is generated **once** in `LoraDatasetWorkspace.CreateDatasetAsync` and never
rebuilt, so no code fix had ever reached that dataset. Measured on the stored plan: **23 of 36 cells** sat on
light/setting pairs the current generator cannot emit, including 5 × `outdoor-day` + `plain-wall` (the exact
contradiction doc 076 fixed) and 5 × `hard-rim` + `outdoors`.

### 2. Two real defects that regeneration would NOT have fixed

- **Wardrobe was framing-blind.** `wardrobe = index % 2` alternated over the whole plan with no reference to
  distance, and the outfit vocabulary is written for a full-body frame — `lora.vocabulary.outfit.casual` =
  *"wearing a plain t-shirt and jeans"*, `.unclothed` = *"completely unclothed, with no clothing at all and nothing
  covering the body"*. Both were poured into templates whose words are *"the whole head and both shoulders in frame"*.
  All 10 close-up cells claimed a full outfit; 5 of them claimed the subject was completely unclothed.
- **The stance claim at close-up was unrenderable, and the previous "fix" was wrong.** An earlier pass restricted the
  close-up cycle to standing/sitting/kneeling and treated that as solved. It is not: those three are
  **indistinguishable** inside a head-and-shoulders frame. The defect was never which stances were listed, only that
  the frame shows none of them. The stale plan still carried poses 4/5/6 (Lying/AllFours/HandsRaised) at close-up.
- **The caption carried the same contradiction.** `lora.cell.caption` = `{TriggerToken}, {Wardrobe}, {Angle},
  {Distance}, {Pose}, …`, so `core.front.cu.2` captioned as *"<token>, nude, front view, close-up, sitting, …"* over a
  head-and-shoulders image — a tag teaching a state the picture does not show.

### 3. The missing rule

Nothing enforced **"a cell may only claim what its framing can show."** Three waves of this bug (light vs setting,
stance, wardrobe) were each patched individually; the principle was never encoded, so it kept reappearing in a new
axis.

## Fix

| File | Change |
|---|---|
| `CharacterLoraCoverageWorkflowKeys.cs` | 8 frame-scoped wardrobe rows; `FramingShowsStance`, `StancesFor(distance)`, `OutfitKeyForDistance(declared, distance)` — the one place that knows what a frame can show |
| `CharacterLoraCoverageModels.cs` | `PoseClass` → `LoraCoveragePoseClass?`; `Validate()` refuses a stance the framing cannot show AND a body framing that claims none, naming both |
| `CharacterLoraCoveragePlanGenerator.cs` | `CloseUpPoseCycle` and `PoseCycle` deleted (superseded); stance comes from `StancesFor` (empty ⇒ `null`); outfit phrase mapped per distance; rotation continues only where the frame can tell garments apart |
| `LoraCellPromptComposer.cs` | the stance element is **omitted** from the prompt when the cell claims none; the caption drops the stance tag together with its comma |
| `LoraDatasetWorkspace.razor` | shows "— (this framing shows no stance)" instead of a blank |
| `ImageWorkflowRepository.cs` | the 8 new vocabulary rows, as editable store data |
| `lora-dataset-design.md` | §3.1 "the framing invariant" — the rule written down so it is no longer rediscovered per axis |

**Consequence for the dataset:** no stance tag on close-ups (the frame cannot show one); wardrobe stays 50/50 clothed /
nude at every distance, but a close-up now says the visible thing (*"bare at the neckline, the shoulders and the
collarbone, with no garment visible anywhere in the frame"*) instead of "completely unclothed".

**Evidence:** build clean; `CharacterLora|LoraCell|ImageWorkflow|ImageStep` 234/234; wider set
`Lora|ImageStep|MediaEdit|SceneImage|Identity|ImagePreset` 1024 passed / 3 failed — the 3 are the known pre-existing
`SdxlSceneImagePromptBuilderTests`. 54 test failures during the change were all fixtures that had **encoded the
defect** (a close-up with `PoseClass = Standing`); they now assert the invariant instead.

## Open

- The live dataset's plan is still the stale one. It is regenerated only by creating a new dataset version.
- No cell has been rendered yet, so the corrected prompts are unproven against a real image.
