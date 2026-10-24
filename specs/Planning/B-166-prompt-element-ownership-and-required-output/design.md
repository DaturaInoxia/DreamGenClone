# Character, reference, and POV prompt composition — implementation design

**State:** designed (2026-10-19; D1–D11 resolved)
**Audience:** implementation agents.
**Sibling docs:** [spec.md](./spec.md) (B-166 origin), [character-reference-pov-spec.md](./character-reference-pov-spec.md)
(braindump analysis — read first; this document is the buildable design that follows from it).

---

## 0. Summary of resolved decisions

| Id | Decision |
|---|---|
| D1 | Lighting owner default `FromBrief` |
| D2 | Enforce: validate → one retry → hard fail with a named diagnostic |
| D3 | Staleness: keep text, mark stale, block render until regenerated |
| D4 | No new model/provider, no second pipeline |
| D5 | Location type anchor = the brief's own recorded location category; missing → fail fast |
| D6 | LoRA removes `appearance` only (clothing stays text) |
| D7 | Omniscient wide lean = persisted `OmniscientShotSize` staging-profile value |
| D8 | View guidance = blocking subject head yaw, then recorded stance/sightline |
| D9 | Name qualification = compiler instruction + output validation |
| D10 | Appearance splits into ownable **face** and **build** elements (§3) |
| D11 | Participant POV pitch/distance are **recorded facts**; unrecorded = not stated, never invented (§4) |

The repo rules apply throughout: one decision path per behaviour, no fallback / default substitution,
missing required data fails fast, and every behaviour is backed by persisted / recorded data.

---

## 1. Reference slot model and ordering

### 1.1 The slot taxonomy (unchanged — restated as the contract)

`ImageStepSlotKind`: `Face`, `Body`, `Wardrobe`, `Location`, `Pose`, `CharacterPose`, `Prop`.

Actor-key classification (`ImageStepSlotBlueprint.ActorKeyRequirementFor`):

| Slot | Actor key | Meaning |
|---|---|---|
| `Face` | Required | A face reference belongs to one character |
| `Body` | Required | A body reference belongs to one character |
| `Wardrobe` | Required | A garment belongs to one character |
| `CharacterPose` | Required | A character-pose image belongs to one character |
| `Location` | Forbidden | The place is scene-level |
| `Pose` | Optional | A skeleton steers one character's stance, or the whole frame |
| `Prop` | Optional | An object may belong to a character, or stand in the scene |

### 1.2 How a character is defined, and the send order

A character is defined by, **in this fixed order**:

1. **Face** — the identity anchor. Declared FIRST because the first reference anchors the frame.
2. **Body** — the build (and, at its own angle, the face the body shows).
3. **Wardrobe** — the garments, zero or more (`AllowsMultiple`).

Pose is **not part of "the character"** — it is the stance, and in the Composition blueprint it is
scene-level (a skeleton can steer one character or the frame). `CharacterPose` is the combined
alternative: one photoreal image carrying appearance + clothing + stance.

**Ordinal rule.** Reference positions in the wire are assigned by the blueprint's declared slot order,
never by the order the operator happened to bind them. `ReferenceSlotPlanner.Plan` walks
`blueprint.Slots` and assigns `Ordinal = bindings.Count + 1`. So the send order is:

```
character 1: Face, Body, Wardrobe(+)
character 2: Face, Body, Wardrobe(+)
scene:       Location(+), Prop(+), Pose
```

`ReferenceRoleClauses` numbers `<imageN>` from the same `InSendOrder` list, so the tag and the wire
cannot disagree (one source, [ReferenceRoleClauses.BlockFor](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs)).

### 1.3 One or many references — the answer

- A slot that does **not** declare `AllowsMultiple` accepts exactly one image; a second binding is refused
  loudly ([ReferenceSlotPlanner.Plan](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceSlotPlanner.cs)
  throws "was filled more than once"). This is what stands between "the operator bound two" and "the render
  silently dropped one".
- Three slots opt into many: **Wardrobe** (dress + shoes), **Location** (several views of one place), and
  **Prop** (several objects). A multi-slot keeps host-supplied order, so the first image bound stays `<imageN>`.
- Total count is bounded by two limits, both validated at plan time:
  - the model's `MaxReferences` (acceptance limit), and
  - `PoseCarryingReferenceBudget = 6` when a `Pose` skeleton is present (measured CASE-22 quality limit).

---

## 2. The ownership matrix — consolidated (with the face/build split, D10)

### 2.1 The split the braindump requires

Today `Face` and `Body` both map to the single element `character:{key}.appearance`
([StepPromptElementPlan.ScopesFor](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs)),
and `CanonicalCharacterAppearance` emits one appearance line per character. That makes a face-only binding
delete the **build** prose it does not supply, and a body-only binding delete the **face** prose — the
"characters are off a little" defect.

**Design (D10):** appearance becomes two independently ownable elements:

| Element key | Owned by | Content |
|---|---|---|
| `character:{key}.appearance.face` | `Face` image **or** `Body` image (the body includes the face) **or** `CharacterPose` **or** `Face`/`Body` character LoRA | face, features, hair, skin tone |
| `character:{key}.appearance.build` | `Body` image **or** `CharacterPose` **or** a LoRA trained with the body | build, body shape, proportions, skin tone |

Consequences (the "when does the face text appear" answer):

- **No face and no body reference** → both lines are written (full appearance text).
- **Face only** → face line removed; **build line still written** (a face image does not supply the build).
- **Body only** → both lines removed (the body image shows the face too).
- **Face + Body** → both lines removed (the face reference overrides the body's face at the angle the scene shows).
- **LoRA (Krea 2)** → face line removed; build line removed only when the LoRA carries the body (D6: start with
  face only; see §3).

The authored appearance block is emitted as **two sub-lines** per character (`FACE:` and `BUILD:`), so the
applier can remove one without the other. The split source is the authored canonical text split into its
face-bearing and build-bearing parts, falling back to `PhysicalAttributesFormatter`'s already-separate face
vs. figure fields — **never** a guessed split of one prose blob (see §3 for the exact source resolution).

### 2.2 The full matrix (normative)

| Element | Owner when a reference is bound | Owner otherwise | Text rule |
|---|---|---|---|
| Location type (category noun) | **Text, always** | Text | 1–4-word category; never removed (D5) |
| Room geometry (walls, floor, surfaces, fixtures, objects) | Location image | Text | Image owns ⇒ removed; text owns ⇒ full prose |
| Rendered lighting | Text by default; `FromReference` setting | Text | Per D1 |
| Time of day | Text (narrative anchor) | Text | Never removed |
| Mood | Text | Text | Never removed |
| Character face (features, hair, skin) | Face **or** Body **or** LoRA | Text | Removed when any of the three supplies it |
| Character build (shape, proportions) | Body **or** CharacterPose **or** body-carrying LoRA | Text | A Face image never removes it |
| Character clothing | Wardrobe image | Text | `SuppliedWorn`: named and put on, never dropped |
| Character stance | Pose / CharacterPose image | Text | Image owns ⇒ stance prose removed |
| Character frame placement | **Text, always** | Text | No reference may remove or suppress it |
| Character action / people count | **Text, always** | Text | Never removed |
| Prop | **Additive** — no element removed | Text | Named and placed, never omitted |

### 2.3 The single decision paths

1. **Removals** — [StepPromptElementPlan.ScopesFor](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs)
   is the one table; [ReferenceBindingPromptRemoval.Merge](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceBindingPromptRemoval.cs)
   merges user overrides (which always win) with binding-derived removals.
2. **Frame** — [SceneImagePovProjection.Resolve](../../../DreamGenClone.Web/Application/RolePlay/SceneImagePovProjection.cs)
   is the one projection, read from the compiled brief, with `suppression` carrying removals so the projected
   text stops restating an image-owned element while the geometry stays truthful.
3. **Reference naming** — [ReferenceRoleClauses](../../../DreamGenClone.Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs)
   is the one role/tag block, numbered from the same `InSendOrder` list the render sends.
4. **Appearance** — [CanonicalCharacterAppearance.BuildBlock](../../../DreamGenClone.Web/Application/RolePlay/CanonicalCharacterAppearance.cs)
   is the one appearance emitter; it becomes face/build-split (§3).

---

## 3. Appearance source resolution (D10)

`CanonicalCharacterAppearance.BuildBlock` currently resolves, in order: authored canonical text (B-132) →
`PhysicalAttributesFormatter.FormatVisualBlock` → scenario `Description` → appearance override. That order
stays; it gains the split.

- **Face text source** (one decision path): the authored canonical text's face-bearing fields if the
  authored text is stored split; otherwise `PhysicalAttributesFormatter` face fields (hair, eye/skin,
  face shape); otherwise absent.
- **Build text source** (one decision path): the authored canonical text's figure-bearing fields;
  otherwise `PhysicalAttributesFormatter` figure fields (height, body type, build); otherwise absent.
- If the authored text is one undivided blob, the blob is **declared face+body owned together** and a face-only
  binding removes the whole blob (the documented, pre-existing behaviour) — the split is only available where
  the source is splittable. This is not a fallback: it is the honest consequence of a source that carries no
  split, and it must be stated in the prompt notice rather than guessed.

`ScenePromptOverridesApplier` must learn the two new keys (`character:{key}.appearance.face`,
`character:{key}.appearance.build`) as removal-capable, and continue to treat the legacy
`character:{key}.appearance` as removing both (backward-compatible, one path).

---

## 4. Participant POV camera — pitch and distance (D11)

`SceneImagePovProjection.BuildParticipantText` already gives a first-person camera at the recorded stance
(camera height) aimed along the recorded sightline. The braindump correctly observes this is not "always
eye-level front close-up". The design adds two recorded dimensions, **stated only when recorded**:

| Dimension | Recorded fact | Projection text |
|---|---|---|
| **Pitch** (looking up / level / down) | new optional frozen-state field `ViewpointPitch` | "Camera pitch: looking down at …" / "level" / "looking up at …" |
| **Subject distance** (close / mid / far) | new optional frozen-state field `ViewpointSubjectDistance`, or the sightline target's recorded depth | "The in-frame subject is near / mid-frame / far across the setting." |

Rules:

- Both are **optional** on the frozen state. When absent the projection emits "Camera pitch and subject
  distance are not recorded on this brief: aim along the recorded sightline and do not invent a pitch or a
  distance." — this is *stating that the fact is absent*, not substituting a default.
- When present, they are emitted verbatim. There is exactly one source for each; nothing is derived from
  sightline prose.
- The viewpoint remains never depicted. The own-body extension (the man seeing his own arm) is **out of
  scope** for this spec and is a future, opt-in recorded fact — it is not implied by pitch/distance.

`SceneMomentFrozenCharacter` is the contract that carries these fields; the enrichment that fills it must be
extended to record them (a data-contract change, not a prompt-composition change). Missing fields do not fail
a render — they are a legitimate "not recorded" state — but they must never be invented.

---

## 5. Correct references for the correct characters and locations

The join that keeps a face from going on the wrong person, or a location from being read as a person, is a
**profile-key chain** that is already enforced end-to-end; the design makes it explicit and closes the two
gaps:

1. `ImageStepBlueprintFactory.RequireActor` refuses any actor key that is not the compiled brief's profile key.
2. `ReferenceSlotPlanner.Plan` copies the slot's `ActorKey` onto the binding (verified, not assumed).
3. `ReferenceRoleClauses.SubjectKeyOf` returns that profile key; `SubjectFor(blocking, subjectKey)` matches
   it to the blocking subject to write "the person on the LEFT" instead of a name.
4. The render resolves each binding to image **bytes by its own source fields** (`IdentityPackId` +
   `ReferenceAssetId`, or `SceneAssetId`/`SceneAssetImageId`/version, or `ReferenceImageId` +
   `ReferenceImageSha256`) and sends them in `InSendOrder`, which is the same list the role clause numbers.
5. A resolved image whose bytes do not match the bound checksum **refuses the run** — a reference is never
   silently swapped.

**Gaps this design closes:**

- **Gap 1 — the slot plan and the role clause already share order, but nothing proves a *Face* image and a
  *Body* image for the same character were both addressed to that character after a re-plan.** `FromBinding`
  round-trips every field (documented), and `RequireActor` refuses foreign keys; the design adds one
  validation: a per-character slot whose `ActorKey` does not appear in the compiled brief's frozen cast is
  refused at plan time (today it is only refused at render). This is a fail-fast move, not a new path.
- **Gap 2 — a location reference vs. several views vs. several different places.** `CarriesSeveralPlaces`
  already distinguishes by `ContainerKey`. The design adds the location type anchor (D5) to the location
  line so the caption names the *kind* of place even when the geometry is image-owned.

---

## 6. Props — per-character, per-location, one-to-many

`Prop` already has `ActorKeyRequirement.Optional` and `AllowsMultiple: true`, and
`StepPromptElementPlan.ScopesFor(Prop)` returns `[]` (additive — a prop never removes an element). The
design expresses the braindump's "props for characters and locations, one to many" as **data on the slot and
the binding**, not as new slot kinds:

| Prop owner | How it is declared | Clause the render emits |
|---|---|---|
| A character | `Prop` slot bound **with** an actor key | "<imageN> is the OBJECT for <character>: placed in their hand / on their person as the instruction and the operator's direction note describe it." |
| The location / scene | `Prop` slot bound **without** an actor key, `ReferenceDirections.Note` names the anchor ("by the shed wall") | "<imageN> is the OBJECT in this frame: placed where the composition and the operator's direction note put it." |
| Several props | multiple `Prop` bindings (the slot is `AllowsMultiple`) | one line per prop, each numbered |

The `ReferenceDirections` machinery (B-159) is the single place an operator states *where* a prop goes; a
per-character prop uses the actor key for *whose* it is, and the direction note for *where*. Nothing is
inferred from the image.

---

## 7. Required-output contract (D2, D9)

After the pre-processor returns a prompt, it is validated against the **request** (bindings + POV + frame):

- every depicted person: gender + short appearance + frame placement (when the projection states one) + clothing state;
- no room geometry / objects / lighting when the room (or lighting, per D1) is image-owned;
- the location **type** present when a location image is bound;
- no person the projection puts out of frame;
- **name qualification (D9):** a caption naming a character ("Becky") is valid only if that character has a
  bound reference that resolves to them, **or** a full appearance description in the same prompt.

Enforcement: validate → one corrective retry naming the missing element → if still missing, fail the prompt
record with a diagnostic naming the element and the person.

---

## 8. Implementation plan (file-by-file)

Ordered; each step compiles and tests green before the next.

1. **`Domain/RolePlay/SceneMomentEnrichment.cs`** — add optional `ViewpointPitch` and
   `ViewpointSubjectDistance` to `SceneMomentFrozenCharacter` (D11). Additive, default-null.
2. **`Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs`** — split the Face/Body mappings:
   - `Face` → `character:{key}.appearance.face`
   - `Body` → `character:{key}.appearance.face` + `character:{key}.appearance.build`
   - `CharacterPose` → face + build + clothing + position
   - add `OwnershipOf` and `Describe` entries for `appearance.face` / `appearance.build`.
3. **`Web/Application/RolePlay/ScenePromptOverridesApplier`** (and its key vocabulary) — accept the two new
   keys; keep legacy `appearance` = both.
4. **`Web/Application/RolePlay/CanonicalCharacterAppearance.cs`** — emit `FACE:` / `BUILD:` sub-lines and
   resolve each from its own source (§3).
5. **`Web/Application/RolePlay/SceneImagePovProjection.cs`** —
   - participant: emit pitch and distance lines when recorded, else the "not recorded" line (D11);
   - omniscient: apply the `OmniscientShotSize` preference through the framing path (D7);
   - location type anchor (D5): keep a category noun even when the room is image-owned.
6. **`Web/Application/RolePlay/ImageStep/ReferenceRoleClauses.cs`** — location line states the type anchor;
   prop lines distinguish per-character vs scene-level (§6); add the D9 name-qualification instruction.
7. **`Web/Application/RolePlay/SceneImagePovProjection`/`SceneImageRenderingJobHandler`** — LoRA removal (D6):
   a character LoRA binding removes `character:{key}.appearance.face` (and build when the LoRA carries the
   body), using the same `ReferenceBindingPromptRemoval` machinery (one ownership table).
8. **Staging profile (`ImageStagingProfile`)** — add persisted `OmniscientShotSize` (D7), UI-backed.
9. **View guidance (D8)** — a per-character Face/Body recommended view derived from the blocking subject head
   yaw, surfaced in the composer as overridable guidance; never substituted.
10. **Required-output validation** — a validator that runs the §7 contract with one corrective retry and a
    named diagnostic (D2, D9).

## 8. Implementation status (2026-10-20)

All ten steps are implemented. Where the build differs from the sketch above, the build is authoritative:

- **D8** — the pure decision path is `SceneViewRecommender.Resolve(sightline, stance)` returning
  `SceneViewRecommendation`; the hint travels as `ImageStepActor.ViewHint` → `ImageStepSlotBlueprint.ViewHint` and is
  rendered beside the slot in `ImageStepComposer`; `CompositionComposer` derives it from the frozen cast.
- **D7** — the lean is snapshotted onto `SceneBlockingCamera.OmniscientShotSize` at compile time (so it travels with
  the brief), and applied by `SceneBlockingResolver.ApplyForPrompt(..., omniscientShotSize)` in both the prompt builder
  and the render handler. An older brief carries no value and keeps its recorded framing.
- **D2/D9** — enforcement lives in `SceneImagePromptContractValidator` and runs in
  `SceneImagePromptGenerationJobHandler`: validate → one corrective pass → named refusal.
  **Corrected 2026-10-20 after three failed compiles:** only CERTAIN contradictions are blocking (a person the frame
  excludes; a name with nothing to anchor it). Placement wording and "is the setting named" are prose judgements and
  are recorded as `SceneImagePromptContractAdvisory` diagnostics, never fatal. The room-restatement check was removed
  entirely — the braindump explicitly requires the setting to be mentionable, and matching recorded fragments flagged
  legitimate prose ("rust"). An OVERLONG answer now gets one shorter rewrite instead of failing outright.
- **D11** — `viewpointPitch` / `viewpointSubjectDistance` are optional frozen-state fields. The projection states them
  when recorded and says "not recorded" when not; nothing is inferred.
- **POV fix (2026-10-20, after "no difference between Omniscient, Dean and Becky")** — the projection text was correct
  and reached the compiler, but the brief JSON still handed it the **camera holder's own frozen entry**, so it wrote an
  external two-person scene every time. Two changes close it: `SceneImagePovProjection.DropViewpoint` removes the
  viewpoint from `frozenState.characters`, from both continuity state lists and from every other character's
  `visibleCharacterNames`; and `ParticipantPovRule` is appended to the SYSTEM prompt stating that this is a first-person
  photograph and the viewpoint is never depicted. Also fixed: an ordinary participant was described as "outside the
  active scene" merely because their location phrase differed from the scene's — that is now decided by the recorded
  `observer` involvement, not by string comparison.

Full suite: **4514 passed / 27 pre-existing LoRA-coverage failures / 0 skipped**.

## 9. Test plan
- `StepPromptElementPlanTests` — face/build split: face-only removes face keeps build; body-only removes both;
  face+body de-duplicates; CharacterPose removes all three.
- `ReferenceBindingPromptRemovalTests` — LoRA-derived removals; user override still wins; text-only binds nothing.
- `SceneImagePovProjectionTests` — participant pitch/distance emitted when recorded, "not recorded" line when
  absent; omniscient wide lean from `OmniscientShotSize`; recorded shot size still wins.
- `CanonicalCharacterAppearance` tests — split lines, per-source resolution, undivided-blob documented path.
- `ReferenceRoleClausesTests` — location type anchor present; per-character vs scene prop lines.
- Validator tests — name-qualification failure names the person; missing placement fails with a diagnostic;
  one corrective retry happens first.
- Full suite green (the 27 pre-existing LoRA-coverage failures reported separately, per repo rule).

## 10. Out of scope

- Participant POV own-body extension (future, opt-in recorded fact).
- Enrichment contract changes beyond recording the two D11 fields.
- Reference-image analysis quality (B-160) and staging-profile editing (B-157/B-162).

## 11. 2026-10-23 — the caption contract was self-contradictory (operator report, SceneImage 3bbad8c9)

The operator reported a compiled Qwen caption that (a) did not carry their own element text into the picture,
(b) repeated itself in a per-person list, (c) contradicted itself ("the subject is positioned directly behind the
camera"), (d) named a lone in-frame person "the person on the LEFT", and (e) carried edit wording
("Where the instruction explicitly changes that person's view…") into a plain generate. Each traced to a specific line:

| Symptom | Cause | Fix |
|---|---|---|
| Caption thin, custom text compressed | `Qwen21Canonical` demanded "400-500 words" AND "between 300 and 1600 characters"; 400 words is ~3000 characters, so the cap won and the model dropped detail to fit | the character range left the contract; the budget is the checkpoint profile's `MinChars`/`MaxChars` (raised to 3400/700 for both Qwen-Image-2.1 rows, seed + live DB) |
| Per-person recap ("Becky: …, …; …") | no output-SHAPE check existed; the model mimicked the element list it was handed | `ValidateCaptionShape` — one continuous paragraph, no recap line; BLOCKING, with the recap quoted in the corrective pass |
| "positioned directly behind the camera" | the camera-holder rule said "DIRECTLY BEHIND … keep the word 'behind' exactly"; the model kept the word and flipped the subject | the rule and the appended block now state the relation as a frame fact and forbid the impossible inversion; the `Camera is at:` line no longer doubles its period |
| "the person on the LEFT" for one visible person | seats were compiled over the whole cast; the POV character (never in their own shot) and remote observers still held seats | `SceneBlockingResolver.ReseatForCast` + `SceneImagePovProjection.BlockingForFrame` — ONE path used by both the compiler and the render |
| Edit wording in a generate | `InstructionPrecedence` was emitted unconditionally | `PrecedenceFor(leadingImages)` — only an EDIT (source image in slot 1) carries an instruction to defer to |

Also: the room/lighting instructions contradicted the observer procedure (which mandates a background and a lighting
sentence) — the reference-supplied-room rule now explicitly REPLACES those parts of the procedure, and the
participant-POV rule states it outranks the procedure where they disagree.

Full suite: **4603 passed / 27 pre-existing LoRA-coverage failures / 0 skipped**.

### 2026-10-23 follow-up — the holder's body belongs IN the caption

The operator's own sentence about the camera holder ("a man who is clearly a separate person entering the frame from
behind … his hands grip her hips … his penis rests against the cleft of her buttocks") was reaching the render only as a
`CAMERA HOLDER BODY PARTS` block appended AFTER the compiled caption. The image model treated it as an addendum and drew
the woman alone; the compiler had never even been given the text, only a pointer to the block it would never write.

The reason for the append was sound for the WORDING — a writer that never writes "behind" cannot invert it — and wrong for
the BODY. Both are now handled where each belongs:

- The caption writer receives the parts **verbatim** in the shot block (`CAMERA HOLDER'S VISIBLE BODY`) and is told to
  state them INSIDE the same paragraph, at the point where the holder's body sits in the frame, as a second, separate
  person — never as a trailing sentence or a list.
- `CameraHolderPartsRule` states the obligation in the SYSTEM prompt and forbids the impossible inversion in words
  ("the subject is never behind the camera"), which is what the old "keep the word 'behind' exactly" instruction failed
  to prevent.
- `ValidateCameraHolderBody` ENFORCES it: a caption that carries none of the recorded body's distinctive words is refused
  and corrected once, then fails loudly. Coverage of the operator's own words, not a phrase match, so a paraphrase passes.
- The render no longer appends the block (that would repeat the body), and `CompositionComposer`'s "as sent" preview no
  longer composes one either, so the preview and the render agree.

Full suite: **4606 passed / 27 pre-existing LoRA-coverage failures / 0 skipped**.
