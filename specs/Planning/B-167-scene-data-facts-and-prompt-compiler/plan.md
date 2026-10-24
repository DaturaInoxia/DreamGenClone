# B-167 — Scene-data fact model + image prompt compiler translation layer

State: `new` (design discussion complete; no code). Owner: operator + agent, 2026-10-24.
Amended 2026-10-24 (operator decisions: additive v4, no enrichment removals, no migration;
consumption rule 1.a "not recorded"; bearing derivation downgraded to field reads).

## 1. Problem

The image-composition prompt path is failing in a recurring, self-similar way: restated
elements, self-contradictory captions, operator intent dropped or duplicated, and geometry
(position/facing/action/camera) that is either missing or stated in two competing vocabularies.

Two root causes, confirmed from the dev DB and the compiled prompts:

1. **The scene record restates one instant six times.** The Prompt Input rows
   `Visual description`, `Moment prose (enrichment)`, `Visible action`, `Continuity (global)`,
   the continuity start/end summaries, and a `location` string that contains cast + action all
   describe the same frozen moment in prose. One fact has no single home, so an operator edit
   cannot propagate and the compile LLM is handed six descriptions of one instant and drifts.
2. **Intent and prompt are the same string.** Operator-edited scene values are merged into the
   brief payload and later quoted, echoed, and word-matched as if they were prompt text. The
   compiler is never told "this is intent; translate it", so it copies it back verbatim.

The governing principle for the fix:

> **The scene record holds POV-agnostic world facts. Everything frame-relative (in/out of frame,
> toward/away from the lens, left/right in the image, crop) is derived at projection time.
> Operator input is INTENT, and the compiler is its translator — intent text never survives into
> the final prompt, and validation compares meaning, never wording.**

## 2. Operator decisions (locked)

| # | Question | Decision |
|---|---|---|
| 1 | Continuity snapshots | **Image composition only.** Video is a separate UI, so no start/end boundary snapshots are needed for the image path. Continuity is **not selected** into the Still brief. (2026-10-24) |
| 2 | Facing representation | **World-relative** (`facing: { toward: <person|object|point> }`), camera-independent, so one record serves every POV. |
| 3 | Location analysis frame labels | **Recommendation (agent, to confirm):** keep named features as world anchors; DROP the `(Left, Foreground)` / `(Center, Background)` frame-coordinate labels. They are positions inside the source photo's frame, not the composed render's frame, and they are a second position vocabulary fighting the blocking resolver. The analysis reading stays picker-only and is never emitted into the prompt. Persisted analyses keep the fields in storage; they simply stop being emitted (no migration). |
| 4 | Enrichment scope | **(2026-10-24) v4 is ADDITIVE.** The enrichment record gains place-only `location`, `contacts` and `facing`; **nothing is removed** from the enrichment data or structure. Backward compatible; no migration. Old v3 rows deserialize with the new fields null. The image path changes only in **which parts of the record it selects** into the Still brief and the Prompt Input section. |
| 5 | Missing-fact consumption | **(2026-10-24) Rule 1.a — "not recorded".** A v3 moment (or a v4 moment where the field is absent) has no `contacts`/`facing`: consumers **state nothing and derive nothing** (the B-166 D11 `ViewpointPitch` rule). Exactly one decision path; the prose-regex scrape is NOT kept as a v3 fallback. Re-enriching upgrades a moment to v4. |

## 3. Part A — scene-data fact model (additive v4)

Enrichment keeps every v3 field. v4 ADDS the fact fields below and fixes `location` to be a
place only. One owner per fact **in the image path** is achieved by selection: `BuildStill`
(DeterministicMultimodalMediaCompiler) picks exactly one leaf per fact into the Still brief's
semantic snapshot; the unselected restatements (`visualDescription`, `moment.frozenState`,
`moment.visibleAction`, `frozenState.continuityState`, continuity start/end summaries) remain
in the enrichment record for the video path and for history, but never reach the image
compiler. `SemanticSnapshotDeDuplicator` becomes unnecessary for the Still path once selection
is one-owner-per-fact, and is deleted there.

### A.1 v4 additions to the frozen-state contract

```
location   -> PLACE ONLY: no cast names, no action (contract-enforced; the existing
              SceneMomentPlaceSeedBuilder name-scrub remains the read-side safety net for
              v3 rows whose location still carries cast)
cast[]     += at:       { anchor, posture }     // world position, camera-independent
              facing:   { toward }              // recorded, world-relative, never inferred
              doing / attending                 // may reuse actionOrObservation / sightline
                                                // values; structured targets are the addition
contacts   [{ from, part, to, targetPart, manner }]   // "his hands on her hips" as a FACT
event      { anchor, line }                     // the one irreducible event sentence
povCamera  [{ profileKey, pitch, subjectDistance }]   // generalizes the B-166 D11
                                                      // viewpointPitch/viewpointSubjectDistance
                                                      // fields already on the record
```

All new record members are **optional with defaults** (trailing positional parameters, the
`ViewpointPitch` precedent in `SceneMomentFrozenCharacter`), so persisted v3
`FrozenStateContractJson` deserializes unchanged. In the **v4 response schema** the new fields
are REQUIRED (an empty `contacts` array is the valid answer for a no-contact moment) — that
constrains only new enrichments, never stored rows.

**Not selected into the Still brief** (still stored, still used by video/history):
`visualDescription`, `moment.frozenState`, `moment.visibleAction`,
`frozenState.continuityState`, continuity start/end summaries. The Prompt Input inspector's
four duplicate prose rows disappear because the brief no longer carries them — not because the
enrichment stopped producing them.

### A.2 Concrete example (the operator's reported scene)

Today: `location = "Becky is bent over the gritty workbench, palms flat on the boards, naked."`

After (v4 enrichment of the same moment):
```
location   "the shed behind the trailers"          // place only
cast[p0]   { at: { anchor: "workbench", posture: "bent forward, palms flat" },
             doing: "bracing on the boards", facing: { toward: "workbench boards" },
             wardrobe: "nude" }                     // existing clothing field
contacts   [{ from: p1, part: "hands", to: p0, targetPart: "hips", manner: "grip" }]
event      { line: "Dean bends Becky over the workbench." }
```
The v3 fields (`position`, `sightline`, `visualDescription`, ...) remain on the same record,
unselected by the image path.

### A.3 Touchpoints that must change together

- `SceneMomentEnrichmentParser` uses `UnmappedMemberHandling.Disallow`: its `Response` record
  must accept the new fields in the SAME change the response schema emits them, or every new
  enrichment fails at parse.
- `SceneMomentEnrichmentContract.CreateResponseSchema` marks every property required (the
  `Object` helper): add the new fields as required-in-response; bump `ContractVersion` to
  `scene-moment-enrichment-v4` and `CurrentSchemaVersion` to 2. Rows persist
  `PromptContractVersion`, so old rows self-identify as v3 — the version gate for rule 1.a.
- `SceneBlockingBuilder` may switch its input from prose `position` to `cast.at`/`involvement`
  when present (v4), keeping the v3 read for old rows — one selector keyed on contract version.

### A.4 Validator (structural only)

The composition-load/enrichment validator checks STRUCTURE, never prose implication (the
"missing when prose implies them" rule is dropped as non-deterministic):

- `contacts.from` / `contacts.to` name real cast profile keys; `part`/`targetPart` non-empty.
- `facing.toward` names a cast member, an object key, or a declared point.
- `location` contains no cast name (v4 responses; v3 rows are handled by the existing
  name-scrub, not by validation failure).
- `event.line` is a single sentence, no sequential-state markers (reuse the parser's existing
  `SequentialStateMarkers` check).

### A.5 Increments

- **D1 — v4 schema (additive) + parser + validator.** Contract v4, required-in-response new
  fields, optional-in-record so v3 JSON reads unchanged. No behaviour change for existing rows.
- **D2 — Still-brief selection.** `BuildStill` selects one owner per fact; continuity and the
  duplicate restatements are not selected; `PromptContractVersion` on the brief bumps;
  `SemanticSnapshotDeDuplicator` deleted from the Still path. Prompt Input rows rebuilt: the
  four duplicate prose rows vanish; `contacts` and `facing` become editable rows (new
  `ScenePromptOverrides` element keys). **Overrides decision:** a saved draft/override whose
  element key names a leaf the Still brief no longer selects fails fast with a named
  diagnostic listing the dropped key (repo fail-fast policy; no silent ignore, no key
  rewriting).
- **D3 — consume the facts (rule 1.a).** Holder toggle = visibility bool over `contacts`;
  v3 row → Derived yields null ("not recorded"), and `CameraHolderPartsExtractor` is DELETED
  (no prose-regex fallback path). Participant-POV geometry reads `at`/`facing` when present,
  states nothing when absent. Compiler input = labelled facts.
- **D4 — re-enrich + golden-set diff + pinned-seed render A/B.** Re-enriching upgrades a
  moment to v4; v3 moments keep composing under "not recorded" semantics indefinitely.

## 4. Part B — compiler / translation fixes (carried from the 2026-10-24 review)

Sit on top of Part A; the prose/settings items ride on D2, the contacts-derived items on D3.

| # | Finding | Fix |
|---|---|---|
| P0 | Prompt mixes description + instruction | Caption carries picture facts only; reference tags bound possessively ("the room in `<image2>`"); prohibitions become a compact affirmative render block |
| P0 | Brief JSON handed over raw | Compiler input = labelled prose sections rendered AFTER all JSON mutations (overrides, suppression, blocking replace); operator-edited values quoted as "translate, do not copy" — removes the `Becky: a; b; c` recap at the source |
| P1 | Preprocessor at temp 0.7 / thinking off | Temperature 0.2–0.4 + thinking on are **Model Manager config changes** (FunctionModelDefault row for `RolePlaySceneImagePreprocessor`; DB currently 0.7 / Default). **Presence penalty 1.5 has NO carrier today**: it requires a new `PresencePenalty` field on `FunctionModelDefault` + repository + Model Manager UI + the completion-client request DTO, then is CONFIGURED (never hardcoded) — per the repo rule that behavior controls live in UI-backed persisted data. |
| P1 | Negative framing load-bearing | State what the frame IS; <=2 prohibitions; enforce the rest in the validator |
| P1 | Partial bodies binary in/out | `SubjectVisibility { InFrame, PartiallyVisible, OutOfFrame }` over `contacts`; entry edge + visible extent derived, never asked of the operator. v3 row (no contacts): InFrame/OutOfFrame only, as today. |
| P2 | Two owners per number | Profile `MaxChars` becomes the single owner. `OutputTargetChars` is NOT orphaned: it is interpolated into seeded `ImageCompilerProfile.SystemPrompt` DB rows (via `SceneImageCompilerSystemPrompts`) and read by `ImageCellPromptCompiler`. The fix is a `{{maxChars}}` template token substituted per-profile at compile time (the `{{style}}`/`{{size}}` precedent) PLUS a reseed/migration of existing profile rows whose text has "800" baked in — a budget in the instruction text and a budget enforced by the profile that disagree is a cell that can never pass. |
| P2 | Validation stops at the caption | Coverage per intent item in intent language (paraphrase passes); lexical absence checks for room nouns / removed items |

## 5. Participant-POV geometry from v4 facts (right-sized)

NOT a new derivation engine — three existing lexical heuristics in `SceneImagePovProjection`
become exact field reads when the v4 fields are present, and "not recorded" (state nothing)
when absent:

| Today (prose string-matching) | v4 read |
|---|---|
| `directlyAhead` = `sightline.Contains(character.Name)` | `attending`/`facing.toward` target key equality |
| `ClassifyViewDirection` scans position/sightline/action prose for direction words ("toward the camera" / "away") | compare the subject's `facing.toward` against the POV profile key |
| `PitchFromSightline` regex-reads "directed up/down" out of the sightline | already superseded by recorded `viewpointPitch` (B-166 D11); regex deleted for v4 rows |

No bearing math between world anchors is introduced. A fact that is not recorded is not
invented — the projection's existing refusal semantics are unchanged.

## 6. Part C — camera-holder toggle (final shape)

Toggle only. No extra enrichment, no extra text field.

```
off  -> show what the POV character sees without their body in frame
        (and the caption must NOT invent it - a new check)
on   -> parts = contacts from the v4 record
        v3 record (no contacts) -> null: "not recorded", state nothing (rule 1.a)
```

`CameraHolderPartsExtractor` (body-part regex + `PossessiveOf` pronoun heuristic) is deleted
at D3. `CameraHolderPartsMode.Custom` and persisted `cameraHolderPartsText`: keep as scratch
during D1–D2, retire at D3 (agent recommendation, unchanged); persisted drafts carrying
`cameraHolderPartsText` after retirement fail fast with a named diagnostic, same policy as
dropped override keys.

## 7. Open questions (one remains)

1. **Compiler output form** (needed for D3/B): free prose + deterministic post-pass (current),
   or clause-list -> deterministic assembly. **Agent recommendation: clause-list -> assembly**,
   now that structured facts exist, because it makes the "did the picture realize this intent"
   check exact instead of approximate.

(Resolved 2026-10-24: holder text-field transition — keep as scratch D1–D2, retire at D3.)

## 8. Sequencing and gates

Part A first (D1 -> D2 -> D3 -> D4), Part B rides on D2 onward. Gates:

- Golden set x POVs at fixed seed, diffed for: recap/list shape, imperative tokens, element
  coverage, reference tag count/order.
- Render A/B at pinned seeds for identity and appendage presence (CASE-25 method: outer-ring
  histogram L1 vs bound reference).
- Full suite green; the 27 pre-existing LoRA-coverage failures reported separately per repo rule.

## 9. Out of scope

- Video composition (separate UI and data model). The video brief keeps reading
  `moment.FrozenState`/`VisibleAction`, the full frozen-state contract and both continuity
  blocks — untouched by the additive v4 fields and by Still-brief selection.
- Removing or rewriting ANY enrichment field, and any DB migration of stored enrichment rows
  (decision 4: additive only, backward compatible).
- The blocking resolver's frame ladder, reference numbering, render-time tag composition,
  RP-engine prompt slots — all sound and unchanged.
- LoRA-coverage plan generator (pre-existing failing tests, unrelated).

## 10. Related

- B-166 (prompt element ownership / POV framing / contract validation) — immediate predecessor;
  its D11 optional-field pattern (`ViewpointPitch`) is the precedent for the v4 additions and
  for rule 1.a.
- B-157 (blocking + reference-role assembly), B-162 (placement integrity), B-158 (default
  reference auto-pick), B-148/B-160 (location images), B-135 D17 (POV/lighting text-only).
