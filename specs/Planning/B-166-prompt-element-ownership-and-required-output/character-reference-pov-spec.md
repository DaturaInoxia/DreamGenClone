# Character, reference, and POV prompt composition — braindump analysis and specification

**State:** designed (braindump analysed 2026-10-19 against the current implementation; decisions D5–D9 open)
**Supersedes / refines:** [spec.md](./spec.md) (B-166). Where this document contradicts `spec.md`, this
document wins and `spec.md` §3.2 is corrected (see §3.1).

---

## 1. What this document is

The operator's braindump corrects the assumptions B-166 made about **characters** and adds the
**reference-image → prompt** composition rules that B-166 only half-stated. It was analysed against the
current code and turned into a specification for implementation agents.

Every point below is mapped to the file that already owns the behaviour (or must own it). The repo's
non-negotiable rules apply unchanged: one decision path per behaviour, no fallback / default substitution,
missing configuration fails fast, and no behaviour is controlled by a code-only default.

---

## 2. Braindump → current implementation map

| # | Braindump point | Where it lives today | Verdict |
|---|---|---|---|
| A | Compiler supports full text, full images, or a combination | [StepPromptElementPlan.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs), [ReferenceBindingPromptRemoval.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceBindingPromptRemoval.cs), [IntentReferenceCoverage.cs](../../../DreamGenClone.Domain/RolePlay/IntentReferenceCoverage.cs), [ReferenceRoleClauses.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs) | **Implemented.** Needs the 3-mode matrix written down (§4.1). |
| B | Location ref doesn't need full room prose, but must still be *mentioned* ("a yard, a shed, a forest, a beach") | [StepPromptElementPlan.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs) `ScopesFor(Location)` removes every room-carrying element | **Conflict.** B-166 §3.2 removes all room prose; the braindump requires a short location-type anchor to remain. See §3.1 / D5. |
| C | Face + body *are* the character; body includes face; user may bind body-front + face-3/4-left | [ImageStepBlueprintFactory.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ImageStepBlueprintFactory.cs) `ForPackIdentityComposition`, [IdentityPackReferenceResolver.cs](../../../DreamGenClone.Web/Application/RolePlay/IdentityPackReferenceResolver.cs) | **Implemented** (independent Face/Body slots, per-view resolution). Gap: no per-scene view *guidance* (§3.3 / D8). |
| D | Krea 2 has no reference images → always full text, **except** character LoRAs | [Krea2SceneImagePromptBuilder.cs](../../../DreamGenClone.Web/Application/RolePlay/Krea2SceneImagePromptBuilder.cs), [CharacterLoraRenderApplication.cs](../../../DreamGenClone.Web/Application/RolePlay/CharacterLoraRenderApplication.cs) | **Gap.** LoRA trigger tokens are prepended but appearance text is not suppressed (§3.4 / D6). |
| E | Omniscient = all characters + location; lean wide / establishing; know each character's side and place | [SceneImagePovProjection.cs](../../../DreamGenClone.Web/Application/RolePlay/SceneImagePovProjection.cs) `BuildOmniscientText`, [SceneFramingClause.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/SceneFramingClause.cs) | **Partially implemented.** Exact LEFT/CENTER/RIGHT + depth exists; the *wide-shot lean* is not a POV default (§3.5 / D7). |
| F | Character POV = that character's eyes; "the man sees his arm reaching for the woman's face, his POV is the woman" | [SceneImagePovProjection.cs](../../../DreamGenClone.Web/Application/RolePlay/SceneImagePovProjection.cs) `BuildParticipantText` | **Implemented** (first-person camera, sightline aim, viewpoint never depicted). Own-body extension is out of scope (§6). |
| G | No hard rules; user picks and chooses references or not; compiler composes from scene inputs | [ReferenceSlotPlanner.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceSlotPlanner.cs), per-blueprint `Required` flags | **Implemented.** Codify requiredness as per-blueprint data (§4.2). |
| H | Names only when fully qualified — "Becky stands beside Dean" only if the refs resolve, or the text describes both | [CanonicalCharacterAppearance.cs](../../../DreamGenClone.Web/Application/RolePlay/CanonicalCharacterAppearance.cs) ("never by name"), [ReferenceRoleClauses.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs) `SubjectSentence` ("the person on the LEFT") | **Partially implemented.** The name-qualification rule is stated exactly in §3.6 / D9. |
| I | No refs → large prompt (location, characters, clothing, positions, lighting, actions); all refs → terse prompt (brief location, relative positions, actions); Qwen's long part lives in the `REFERENCE IMAGES — AUTHORITATIVE` block | [NaturalLanguageSceneImagePromptBuilder.cs](../../../DreamGenClone.Web/Application/RolePlay/NaturalLanguageSceneImagePromptBuilder.cs), [SceneImageCompilerSystemPrompts.cs](../../../DreamGenClone.Domain/RolePlay/SceneImageCompilerSystemPrompts.cs) | **Implemented.** Codify the two-mode matrix (§4.1) with the §3.1 location anchor. |

---

## 3. Corrections and new requirements

### 3.1 Location reference: keep a *type anchor*, remove the *geometry* (D5)

**B-166 §3.2 is corrected.** It said a bound location image removes *all* room-carrying elements. The
braindump is right that removing everything produces a caption that reads as if the scene floats in
nowhere. The room prose splits into two parts that must be owned differently:

| Part | Owner when a location image is bound | Rule |
|---|---|---|
| **Location type** — a coarse category noun phrase: "a yard", "a shed", "outdoor forest", "a beach", "a bedroom" | **Text, always** | One to four words naming the setting *category*. Never removed. It makes the caption coherent ("two people in the yard") without describing geometry. |
| **Room geometry** — walls, floor, surfaces, fixtures, objects, lighting | **Location image** | Removed exactly as B-166 §3.2 lists (all `frozenState.location`, `environment`, `objects`, `visualDescription`, `continuityState`, both continuity blocks, `physicalLocation` anchors, `compositionRationale`). |

**Implementation consequence.** The type anchor is a *new* element that does not exist in the brief as a
separate field today — `frozenState.location` carries full prose. The anchor must be derived
deterministically from the brief's own location fact (a category word already present, or the operator's
own asset label for the location reference), never invented by the compiler. Precisely one source, and a
missing category must fail fast rather than be guessed. The `REFERENCE IMAGES — AUTHORITATIVE` block's
location line and the `AppendRoomOwnership` projection text are updated to say "state the setting only as
its category (the yard, the shed) — never its structure, surfaces or lighting" instead of "do not describe
the room".

### 3.2 Face and body are the character; the body includes the face

Already true in code — [ImageStepBlueprintFactory.ForPackIdentityComposition](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ImageStepBlueprintFactory.cs)
declares independent `Face` and `Body` slots per character, and
[IdentityPackReferenceResolver](../../../DreamGenClone.Web/Application/RolePlay/IdentityPackReferenceResolver.cs)
matches the face by `SceneImageReferenceFaceView` and the body by `SceneImageReferenceBodyView` +
`SceneImageReferenceBodyState`. This document locks the contract:

- Binding `Body` alone supplies the whole figure including the face (the body reference *is* the character).
- Binding `Face` + `Body` together is legal and means "this body build at this angle, with this face at the
  angle the scene shows" — the operator may bind a front body and a 3/4-left face when the scene turns the
  head. Both map to the one `character:{key}.appearance` removal, de-duplicated.
- The view each reference depicts is **operator-chosen data**, not inferred.

### 3.3 The operator needs to know *which view* to pick for Qwen 2.1 (D8)

`ForPackIdentityComposition` already lets the operator pick any approved face/body view, but nothing tells
them **which** view the scene actually shows. The gap: a moment whose blocking places a character turned
3/4-left still pre-fills / defaults toward the front view, and the operator discovers the mismatch only in
the render.

- Each per-character `Face` and `Body` slot must surface a **recommended view** derived from the compiled
  brief's own recorded facts (the blocking subject's head yaw / facing, or the moment's recorded stance and
  sightline). The recommendation is **guidance only** — the operator always overrides it — and a slot with
  no recorded angle states "no angle recorded on this brief" rather than guessing.
- The vocabulary offered is the pack's own approved views only (`AvailableFaceViews` /
  `AvailableBodies`), never the full enum.
- No view may be *substituted*: if the recommended view has no approved asset, the step says so by name
  ("shoot/approve 3/4-left on the Faces tab"), it does not fall back to front.

### 3.4 Character LoRA is a reference channel for models with no native slots (D6)

A model with no native multi-reference slots (Krea 2) always compiles a full text description — **except**
for a character carried by a character LoRA. Today
[CharacterLoraRenderApplication](../../../DreamGenClone.Web/Application/RolePlay/CharacterLoraRenderApplication.cs)
prepends the trigger token but the prompt still writes the character's full appearance, so the text
competes with the LoRA.

- Binding a character LoRA for a character removes that character's `appearance` prose from the compiled
  prompt, exactly as a bound `Face`/`Body` reference would — the LoRA supplies the identity, the text states
  only what the LoRA does not (clothing state, pose, frame placement, action).
- The trigger token is the character's name in the caption (stated once by
  [CharacterLoraPromptTokens.Prepend](../../../DreamGenClone.Web/Application/RolePlay/SceneImageCharacterLoraResolver.cs)),
  and the caption must not re-describe the identity the token names.
- This is a **slot-derived removal**, not a family special-case: the same `StepPromptElementPlan` /
  `ReferenceBindingPromptRemoval` machinery must produce it, so there is one ownership table.

### 3.5 Omniscient POV leans wide / establishing (D7)

`SceneImagePovProjection.BuildOmniscientText` already states exact LEFT/CENTER/RIGHT + depth. It does not
yet lean the shot size wide. Because the repo forbids code-only defaults, the lean is a **configured
staging-profile value** (an omniscient shot-size preference), applied through the one framing entry point
`SceneBlockingResolver.ApplyForPrompt` — the same path B-161's `SceneBlockingCameraOverride` uses. When the
brief's blocking already pins a shot size, that recorded value wins; the omniscient preference applies only
when the brief records none. The camera is still external third-person, still shows every in-scene
character, still states each person's side and depth.

### 3.6 Name qualification (D9)

The caption may name a character ("Becky") **only when that character is depicted by**:

1. a bound reference that resolves to that character (face/body/wardrobe/pose/character-pose/LoRA), **or**
2. a full text description of that character's appearance in the same prompt.

A character whose appearance is neither referenced nor described must not be named — the caption refers to
them by placement ("the person on the LEFT") and traits instead. This replaces the blanket "never by name"
in [CanonicalCharacterAppearance](../../../DreamGenClone.Web/Application/RolePlay/CanonicalCharacterAppearance.cs)
with the precise rule, and it is what makes "Becky stands beside Dean" correct when — and only when — the
references resolve or the text describes both.

---

## 4. The single ownership matrix (normative)

### 4.1 Prompt volume modes

| Mode | Condition | Caption content | Where Qwen 2.1's long text lives |
|---|---|---|---|
| **Full text** | No reference images bound for the element class | Location (geometry + type), each character's appearance + clothing + pose + frame placement, lighting, time of day, mood, actions | Entirely in the caption |
| **Mixed** | Some element classes referenced, others text | Referenced elements drop to their anchor/type/name form; unreferenced elements stay full | Caption for the text-owned elements; `REFERENCE IMAGES — AUTHORITATIVE` block for the image-owned elements |
| **Full image** | Every class referenced | Location type anchor + relative positions + actions + frame placement; appearance/clothing/pose stated only as "supplied by the references" | `REFERENCE IMAGES — AUTHORITATIVE` block |

The mode is not chosen by a switch — it is the *sum* of the per-element ownership decisions below, so
there is exactly one path.

### 4.2 Per-element ownership

| Element | Owner when referenced | Owner otherwise | Text rule |
|---|---|---|---|
| Location type | **Text, always** | Text | One-to-four-word category; never removed (D5). |
| Room geometry (walls, floor, surfaces, fixtures, objects, lighting) | Location image | Text | Image owns ⇒ removed; text owns ⇒ full prose. |
| Time of day | Text (narrative anchor) | Text | Never removed. |
| Mood | Text | Text | Never removed. |
| Lighting (rendered light) | Text by default; `FromReference` setting | Text | Per B-166 D1. |
| Character appearance (face, hair, skin, build) | Face / Body image **or** character LoRA | Text | Image/LoRA owns ⇒ appearance removed (D6). |
| Character clothing | Wardrobe image | Text | Image owns ⇒ named + worn (`SuppliedWorn`), never dropped. |
| Character stance | Pose image | Text | Image owns ⇒ stance prose removed. |
| Character frame placement | **Text, always** | Text | No reference may remove or suppress it. |
| Character action / people count | **Text, always** | Text | Never removed. |

### 4.3 The single decision paths (unchanged, restated)

- Removals: [StepPromptElementPlan.ScopesFor](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs) (the one table) → [ReferenceBindingPromptRemoval.Derive](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceBindingPromptRemoval.cs) (the one merge with user overrides).
- Frame: [SceneImagePovProjection.Resolve](../../../DreamGenClone.Web/Application/RolePlay/SceneImagePovProjection.cs) (the one projection, read from the compiled brief).
- Reference naming: [ReferenceRoleClauses](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs) (the one role/tag block).
- No second pipeline, no second table, no fallback.

---

## 5. Decisions

Resolved 2026-10-19 ("use recommended"). `D1–D4` carry over from `spec.md` at their recommended values.

- **D1** — lighting default `FromBrief` (same room, different time of day).
- **D2** — enforcement = validate → one corrective retry → hard fail with a named diagnostic.
- **D3** — staleness UX = keep the text, mark stale, block render until regenerated.
- **D4** — non-goals confirmed: no new model/provider, no second pipeline, the pre-processor remains the prose writer.
- **D5** — location type anchor source = **the brief's own recorded location fact** (the category word already in
  `frozenState.location`). Missing → fail fast, never guessed.
- **D6** — LoRA removal scope = **`appearance` only first**; clothing stays text-controlled until measured.
- **D7** — omniscient lean = **new persisted staging-profile setting `OmniscientShotSize`**, applied through
  `SceneBlockingResolver.ApplyForPrompt`; never a code-only default.
- **D8** — view guidance = **derive the recommended face/body view from the blocking subject's head yaw**
  (`SceneBlockingSubject`), falling back to the moment's recorded stance/sightline only when yaw is unrecorded.
- **D9** — name qualification = **compiler instruction AND a required-output validation check** (a caption that
  names an unqualified character fails the contract).

New decisions opened by the design (see [design.md](./design.md)):

- **D10** — appearance ownership split: the authored appearance is one blob today; the design needs independently
  ownable **face** and **build** elements. Source split is decided in the design.
- **D11** — participant-POV pitch and subject distance: recorded facts on the frozen state vs. derived from the
  sightline prose. Decided in the design.

## 6. Out of scope

- The character-POV **own-body** extension ("the man sees his own arm reach out") — future, not this spec.
- Reference-image analysis quality (B-160), staging-profile editing (B-157/B-162), LoRA training.
- Any change to the enrichment contract or the beat/moment pipeline.
- No new model, provider, or prompt pipeline.

## 7. Acceptance tests (draft)

1. Location image bound, no other refs → caption contains the location *type* ("the shed"), contains no room
   geometry/lighting, states every person's placement and action.
2. Location image unbound → full room prose restored after regeneration (staleness per B-166 D3).
3. Krea 2, character LoRA bound → that character's trigger token present once, that character's appearance
   prose absent, clothing/placement/action present.
4. Krea 2, no LoRA, no references → full text description of location + characters + clothing + positions.
5. Qwen 2.1, face + body + wardrobe + pose bound for a character → appearance/clothing/stance suppressed or
   named-worn, placement still present, `REFERENCE IMAGES — AUTHORITATIVE` block carries the role sentences.
6. Body-front + face-3/4-left both bound → one appearance removal, both references named in the block.
7. A character with no reference and no appearance text → caption names them only by placement/traits,
   never by name.
8. A character with a resolved reference → caption may name them.
9. Omniscient POV with no recorded shot size → wide/establishing framing from the configured preference;
   with a recorded shot size → the recorded value.
10. A view recommendation with no approved asset fails by name; it never falls back to another view.

## 8. References

- [spec.md](./spec.md) — B-166 original spec (superseded where this document says so).
- [StepPromptElementPlan.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs)
- [ReferenceBindingPromptRemoval.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceBindingPromptRemoval.cs)
- [ReferenceRoleClauses.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs)
- [SceneImagePovProjection.cs](../../../DreamGenClone.Web/Application/RolePlay/SceneImagePovProjection.cs)
- [ImageStepBlueprintFactory.cs](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ImageStepBlueprintFactory.cs)
- [IdentityPackReferenceResolver.cs](../../../DreamGenClone.Web/Application/RolePlay/IdentityPackReferenceResolver.cs)
- [NaturalLanguageSceneImagePromptBuilder.cs](../../../DreamGenClone.Web/Application/RolePlay/NaturalLanguageSceneImagePromptBuilder.cs)
- [SceneImageCompilerSystemPrompts.cs](../../../DreamGenClone.Domain/RolePlay/SceneImageCompilerSystemPrompts.cs)
- [CharacterLoraRenderApplication.cs](../../../DreamGenClone.Web/Application/RolePlay/CharacterLoraRenderApplication.cs)
