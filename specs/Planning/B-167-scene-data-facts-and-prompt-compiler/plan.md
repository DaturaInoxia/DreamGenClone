# B-167 — Scene-data fact model + image prompt compiler translation layer

State: `implemented (Part A D1–D3 + Part B P0-input), 2026-10-24`. Owner: operator + agent, 2026-10-24.
Amended 2026-10-24 (operator decisions: additive v4, no enrichment removals, no migration;
consumption rule 1.a "not recorded"; bearing derivation downgraded to field reads).

## 0. Implementation status (2026-10-24)

State: `implemented` for Part A D1–D3 + the Part B items that do not require new model research.
Full suite: **4630 passed / 27 failed (the pre-existing LoRA-coverage failures) / 0 skipped**.

| Increment | Delivered |
|---|---|
| **D1** | `SceneMomentBodyPosition`, `SceneMomentFacing`, `SceneMomentContact`, `SceneMomentEvent` records; `SceneMomentFrozenCharacter.At`/`Facing` and `SceneMomentFrozenStateContract.Contacts`/`Event` as OPTIONAL trailing members (v3 JSON deserializes unchanged). Contract `scene-moment-enrichment-v4`, `CurrentSchemaVersion = 2`; response schema adds `at`/`facing` per character plus `event`/`contacts`, with `from`/`to` constrained to the selected profile keys. System prompt states place-only `location`, world-relative `facing`, contacts as facts, and one event sentence. Parser accepts the fields and validates them structurally (place names no cast member; contact keys are cast keys; no self-contact; no duplicate contact; single-sentence event). |
| **D2** | `BuildStill` selects one owner per fact and no longer projects the Moment prose copies, `visualDescription`, `continuityState`, the beat continuity blocks or `videoKeyState`; a v3 record's `visibleAction` becomes the single `event.line` (verbatim, empty anchor). `PromptElementClusters` rebuilt (event line/anchor + contacts rows; per-character body anchor, posture and facing rows; the four duplicate prose rows gone). `StillBriefElements` is the enforcement point: an override key naming a dropped restatement fails fast with a diagnostic saying where the fact lives. The applier gained nested property paths and the contacts line format. `LocationScopes` trimmed to selected leaves. `SemanticSnapshotDeDuplicator` deleted. `CompositionComposer` reads dotted paths and renders contacts one per line. **The Still profile version moved `2` → `3`** in the request AND in `IsExactStillBrief` (with the guard test updated), so a pre-existing production group stops reusing its old-shape brief — without this the fact model would never reach those groups. |
| **D3** | Holder parts come from recorded `contacts` (`CameraHolderPartsFromContacts`), the prose-scraping `CameraHolderPartsExtractor` and its tests are deleted, and `CameraHolderPartsMode.Custom` — plus any persisted `CameraHolderPartsText` — is refused by name. `directlyAhead` and the subject facing classification read `facing.toward` exactly when recorded (profile-key/name equality) and fall back to the recorded sightline only for a v3 record; the pitch regex is skipped for a v4 record. The compiler input is a labelled fact sheet (`StillBriefFactSheet`) rendered after every JSON mutation, with operator-edited values marked "translate, do not copy". `DropViewpoint` also scrubs `event.line`/`event.anchor` and removes contacts naming the camera holder. |
| **Part B — budget (P2)** | The prompt budget has ONE owner. Every family instruction text now carries `SceneImageCompilerSystemPrompts.BudgetToken` (`{{maxChars}}`), `OutputTargetChars` and the two `OutputPromptTargetChars` aliases are deleted, `ImageCellPromptCompiler` resolves the token from `profile.MaxChars`, and `SceneImagePromptGenerationJobHandler` resolves it from the checkpoint profile the render actually resolved to (one helper, immediately before the text is sent). `ImageCompilerProfileRepository` migrates the baked `under 800 characters` sentence in existing rows to the token, so the instruction and the enforced budget cannot disagree. |
| **Part B — repetition (P1)** | The penalty now has a carrier: nullable `PresencePenalty` on `FunctionModelDefault` (validated −2…2), a `PresencePenalty` column with an idempotent ALTER migration, repository read/write, `ResolvedModel.PresencePenalty`, a Model Manager column, and `presence_penalty` in both completion-client payloads — sent ONLY when the row declares one, so a provider that rejects the parameter sees the request it always saw. Configured (not hardcoded) at **1.5** for `RolePlaySceneImagePreprocessor`. |
| **Part B — visibility (P1)** | `SceneSubjectVisibility { InFrame, PartiallyVisible, OutOfFrame }` in the domain, and `Projection.CameraHolderVisibility` is the single typed answer to "is the camera holder in the picture" (derived from recorded contacts). `ProjectedCharacter` carries the same enum so a partial in-frame body is a DATA change rather than a new path. No caption vocabulary was invented beyond what the contacts already prove. |
| **Config** | `RolePlaySceneImagePreprocessor`: temperature 0.7 → **0.3** and presence penalty **1.5** in the dev DB (both editable in Model Manager). |

**Not landed (deliberate, with reasons):**

1. **Thinking mode on for the preprocessor** — the assigned model (`qwen2.5-vl-7b-instruct-abliterated-local`) reports
   `SupportsThinkingControl = false`, and the no-fallback rule forbids sending an Enabled thinking mode a model cannot
   honour. It is KEPT AS IS by operator decision: the Qwen-VL model was chosen over a thinking-capable DeepSeek model
   because DeepSeek refuses NSFW prompts. Enabling thinking is a model-assignment change (Model Manager), available
   without code once an NSFW-capable thinking model is qualified — that qualification is separate research.
2. **In-frame `PartiallyVisible` derivation** — the holder case is derived from contacts; deciding that a DEPICTED
   subject is only partly in frame has no recorded fact to decide it from today, and inventing one would add caption
   vocabulary the golden set has not asked for. Deferred to D4 evidence; the enum makes it a data change when it lands.
3. **Affirmative prohibition block / intent-language coverage validation** — the compiler system-prompt prohibition
   lists are externally researched text, and `scene-image-prompt-compiler-standards.instructions.md` forbids ad-hoc
   prompt-shape edits without documented model evidence. The labelled-fact input (D3) may remove most of the need; D4
   results become the research input for a B-135-style prompt-text increment.

## 0.1 Implementation record (2026-10-24)

Everything below was executed in one run after the operator locked decision 1.a and the additive-v4 scope. It is the
as-built record: every file, every decision taken *during* implementation that the design left open, the
configuration values that now exist, and the evidence used to call it done.

### 0.1.1 Domain

| File | Change |
|---|---|
| `DreamGenClone.Domain/RolePlay/SceneMomentEnrichment.cs` | Added `SceneMomentBodyPosition(Anchor, Posture)`, `SceneMomentFacing(Toward)`, `SceneMomentContact(From, Part, To, TargetPart, Manner)`, `SceneMomentEvent(Anchor, Line)`. `SceneMomentFrozenCharacter` gained trailing OPTIONAL `At` and `Facing`; `SceneMomentFrozenStateContract` gained trailing OPTIONAL `Contacts` (nullable list) and `Event`. All v3 members are untouched, which is what makes the change migration-free. Follows the B-166 D11 `ViewpointPitch` optional-field precedent. |
| `DreamGenClone.Domain/RolePlay/SceneSubjectVisibility.cs` | NEW. `InFrame` / `PartiallyVisible` / `OutOfFrame`, with the rule that partial visibility is DERIVED from recorded contacts and can never be produced by a record that carries none. |
| `DreamGenClone.Domain/RolePlay/SceneImageCompilerSystemPrompts.cs` | `OutputTargetChars` (800) **deleted** and replaced by `BudgetToken` (`{{maxChars}}`) plus `HasBudget` / `ApplyBudget` (refuses a non-positive budget). Every family instruction text now states `{BudgetToken}`, including the previously hardcoded `under 800 characters` sentence inside `NaturalLanguageCanonical`. |
| `DreamGenClone.Domain/ModelManager/FunctionModelDefault.cs` | `PresencePenalty` (nullable double) + range validation (−2…2) inside `ValidateCharacterBodyCardDraftConfiguration`. Null means "this function declares none", which is deliberately different from 0 (0 is a declared setting that suppresses the provider default). |
| `DreamGenClone.Domain/ModelManager/ResolvedModel.cs` | `PresencePenalty` (nullable) so the resolved model carries the configured value to the client. |

### 0.1.2 Web — enrichment contract and parser

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/SceneMomentEnrichmentContract.cs` | `ContractVersion` → `scene-moment-enrichment-v4`; `CurrentSchemaVersion` → `2`. Response schema adds `characters[].at{anchor,posture}`, `characters[].facing{toward}`, `event{anchor,line}` and `contacts[{from,part,to,targetPart,manner}]`, with `from`/`to` enum-constrained to the selected profile keys (a contact can never name someone the instant does not contain). System prompt gained two paragraphs: `location` is the PLACE ONLY (no cast, no clothing, no action, no event restatement), and the world facts (`at`, `facing`, `event`, `contacts`) must be recorded exactly as evidenced and never inferred. |
| `DreamGenClone.Web/Application/RolePlay/SceneMomentEnrichmentParser.cs` | `Response`/`CharacterInput` accept the new members (the parser runs `UnmappedMemberHandling.Disallow`, so this had to land in the same change or every v4 enrichment would fail at parse). Structural validation only, per §A.4: place names no cast member (regex over authoritative names), `at.anchor`/`at.posture`/`facing.toward` required per character, `event.anchor`/`event.line` required and the line passes the existing `ValidateFrozenText` single-instant check, contacts validated by `ValidateContacts` (both ends are cast keys, no self-contact, no duplicate contact, part/targetPart/manner non-empty). |

### 0.1.3 Web — Still-brief selection (the "only what image composition uses" change)

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/DeterministicMultimodalMediaCompiler.cs` | `BuildStill` no longer projects `moment.frozenState`, `moment.visibleAction`, `frozenState.visualDescription`, `frozenState.continuityState`, the beat `continuity` blocks or `videoKeyState`. New `SelectStillFrozenState` performs the selection and, for a v3 record, copies the Moment's `visibleAction` verbatim into `frozenState.event.line` with an EMPTY anchor (nothing invented). `contacts` is left absent for v3 so consumers read "not recorded". The video projection (`BuildVideo`) is untouched. |
| `DreamGenClone.Web/Application/RolePlay/ImageStep/StillBriefElements.cs` | NEW. The single definition of which leaves the image brief carries (`FrozenStateLeaves`, `MomentLeaves`, `CharacterLeaves`) and of which restatements are dropped, each with the diagnostic that says where its fact now lives. `RequireSelectable` is called at the top of the override loop, so a stale draft fails fast instead of writing a duplicate property. `scene.*` is accepted as the applier's long-standing alias for a frozen-state element. |
| `DreamGenClone.Web/Application/RolePlay/ScenePromptOverridesApplier.cs` | Calls `StillBriefElements.RequireSelectable` before any write; `ApplyScalar` now handles nested property paths (`at.posture`, `facing.toward`, `event.line`) and creates a missing parent for an EDIT while a removal of a missing nested leaf is a no-op; `ApplyContacts` reads/writes the lossless one-line-per-contact format `from\|part\|to\|targetPart\|manner`; `SplitCharacterElement` resolves the character key against the brief's own characters (longest match) so `p0.at.posture` addresses `p0` with property `at.posture` and a dotted character key still works. |
| `DreamGenClone.Web/Application/RolePlay/ImageStep/PromptElementClusters.cs` | Rebuilt. Scene rows are now Location / Environment / Time of day / Lighting / Mood / Objects / **Event (what happens)** / **Event anchor** / **Contacts**; the four duplicate prose rows (`visualDescription`, `momentProse`, `visibleAction`, `continuityState`) are gone. Character rows gained **Body anchor**, **Posture**, **Facing** ahead of the existing stance/action/sightline/wardrobe/appearance rows. |
| `DreamGenClone.Web/Application/RolePlay/ImageStep/StepPromptElementPlan.cs` | `LocationScopes` reduced to the leaves the brief actually carries (`frozenState.location`, `environment`, `lighting`, `objects`, `moment.compositionRationale`) — addressing continuity leaves would now be an unhonourable key. The frame-wide Pose slot removes `frozenState.event.line` instead of the dropped `moment.visibleAction`. `Describe` gained `SpecialElement` so the v4 leaves are named to the compiler unambiguously (`at.anchor` vs `event.anchor` are both "anchor"). |
| `DreamGenClone.Web/Application/RolePlay/ScenePromptRemovalNotice.cs` | Element descriptions updated to the selected leaves; the dropped-restatement descriptions removed. |
| `DreamGenClone.Web/Application/RolePlay/Models/ScenePromptOverrides.cs` | Documentation of the accepted element-key set rewritten to the selection (and to say a dropped key fails fast with a pointer). |

### 0.1.4 Web — compiler input, projection and holder parts

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/ImageStep/StillBriefFactSheet.cs` | NEW. Renders the brief's selected facts as labelled prose (`PLACE`, `TIME OF DAY`, `LIGHTING`, `ENVIRONMENT`, `MOOD`, `OBJECTS`, `TEMPORAL ANCHOR`, `COMPOSITION RATIONALE`, `EVENT`, `CONTACTS`, `CAST`, `FRAME`, `REFERENCES`) and marks any operator-edited value with `[operator intent — translate it into picture facts, do not copy the wording]`. `contacts: null` renders as "not recorded — state no body-to-body contact and do not invent one"; an empty list renders as "none — two bodies do not touch in this instant". It renders AFTER every JSON mutation, so overrides, reference removals and the POV viewpoint scrub are all already applied. |
| `DreamGenClone.Web/Application/RolePlay/NaturalLanguageSceneImagePromptBuilder.cs` | Emits the fact sheet instead of the raw deduplicated JSON; header reworded to `CANONICAL STILL BRIEF (the selected facts this request must realize)`. |
| `DreamGenClone.Web/Application/RolePlay/PonySceneImagePromptBuilder.cs` | Same; `OutputPromptTargetChars` alias deleted. |
| `DreamGenClone.Web/Application/RolePlay/SceneImagePovProjection.cs` | (a) `directlyAhead` and `FacingOf` read `facing.toward` EXACTLY when recorded (equality against the viewpoint's name, profile key or the camera words) and only fall back to the recorded sightline sentence for a v3 record; a bare direction word claims nothing. (b) `PitchFromSightline` is skipped when `facing` is recorded. (c) `ProjectedCharacter` carries `WorldBody`, `WorldFacing` and `Visibility`; `Describe` states the world facts before the prose copy. (d) `ResolveCameraHolderParts` now reads `CameraHolderPartsFromContacts`, refuses a retired custom text by name, and refuses `CameraHolderPartsMode.Custom` by name. (e) `DropViewpoint` also scrubs `event.line`/`event.anchor` and removes every contact naming the camera holder. (f) `Projection.CameraHolderVisibility` is the single typed in/out/partial decision for the holder. |
| `DreamGenClone.Web/Application/RolePlay/ImageStep/CameraHolderPartsFromContacts.cs` | NEW, replaces the deleted `CameraHolderPartsExtractor.cs`. Pure lookup over `frozenState.contacts`: a contact the holder starts contributes its `part` (`hands (grip p0)`), a contact someone else starts contributes the `targetPart` it puts on the holder. Emits only recorded part names, so identity can never leak; no contacts → null. |
| `DreamGenClone.Web/Application/RolePlay/ImageStep/SemanticSnapshotDeDuplicator.cs` | **DELETED.** With one owner per fact there is nothing left to de-duplicate on the Still path. |
| `DreamGenClone.Web/Application/RolePlay/Models/SceneImageStudioSettings.cs` | `CameraHolderPartsMode.Custom` documented as retired; `CameraHolderPartsText` documented as refused-at-projection. |

### 0.1.5 Web — budget, penalty, version and UI

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/ImageCellPromptCompiler.cs` | Applies `SceneImageCompilerSystemPrompts.ApplyBudget(profile.SystemPrompt, profile.MaxChars)`; the user-prompt budget line no longer names a second number. |
| `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs` | New `IImageCompilerProfileResolver` dependency and `ResolvePromptBudgetAsync`, called at both prompt-build sites (beat and canonical/intent) immediately before the system prompt is sent. A text without the token is returned untouched; a text WITH the token and no profile is refused by name, so a placeholder can never reach a model. |
| `DreamGenClone.Web/Application/RolePlay/SceneImageProductionService.cs` | Still profile version **2 → 3** in the request and in `IsExactStillBrief`, with the reason recorded in place: a group created before the selection change would otherwise keep reusing its old-shape brief forever. |
| `DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs` | Populates `ResolvedModel.PresencePenalty` on both resolution paths (session override and function default). |
| `DreamGenClone.Web/Components/Pages/CompositionComposer.razor` | Prompt Input rows read dotted paths (`StringValuePath`), render a contacts array one `from\|part\|to\|targetPart\|manner` line per contact, and group the event/anchor/contacts rows under "Moment". The camera-holder control is now Off / Show-the-parts-the-moment-records; the free-text box and the `Custom` option are removed, and no custom text is written into prompt drafts or render settings. |
| `DreamGenClone.Web/Components/Pages/ModelManager.razor` | New "Repetition" column (blank = none, −2…2), `ParseNullableDouble`, wired into the edit state and the save payload. |

### 0.1.6 Infrastructure

| File | Change |
|---|---|
| `DreamGenClone.Infrastructure/RolePlay/ImageCompilerProfileRepository.cs` | `MigrateBudgetTokenAsync` rewrites only the baked `under 800 characters` sentence in existing `SystemPrompt` rows to the token (idempotent; operator edits elsewhere survive; a row already carrying the token is untouched). Seed comment updated so the Krea2 row no longer claims a literal budget must match. |
| `DreamGenClone.Infrastructure/Models/CompletionClient.cs` | `ChatRequest.PresencePenalty` → `presence_penalty`, omitted when null, applied to every payload site (primary reasoning call, both streaming calls, plain call, force-answer, continuation). |
| `DreamGenClone.Infrastructure/Models/OpenAiStructuredTextCompletionClient.cs` | Same property on its request record. |
| `DreamGenClone.Infrastructure/ModelManager/FunctionDefaultRepository.cs` | `PresencePenalty` in `SelectColumns`, in the INSERT columns/values, in the upsert SET list, and in `ReadFunctionDefault`. |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | `PresencePenalty REAL NULL` in the `FunctionModelDefaults` create, plus an idempotent `pragma_table_info` + `ALTER TABLE` migration. No default value is written, so no existing row changes behaviour. |

### 0.1.7 Decisions taken during implementation (not settled by the design)

1. **v3 event ownership.** A v3 record has no `event`, and the Moment's `visibleAction` is copied verbatim into `event.line` at selection time — the Moment copy is not selected at all, so the sentence keeps exactly one home. The anchor is empty rather than invented.
2. **`contacts` absent ≠ `contacts` empty.** The distinction is preserved end to end (record → brief → fact sheet): absent means "this record predates v4, claim no contact", empty means "these two bodies do not touch". Collapsing them would let a v3 moment silently assert an absence it cannot know.
3. **Fail fast over migration for saved overrides.** Element keys that name a dropped restatement are refused with a named diagnostic pointing at the new owner; keys are never rewritten and never silently ignored. `cameraHolderPartsText` follows the same policy.
4. **Contact line format** `from|part|to|targetPart|manner` was chosen because it is lossless, unambiguous, readable in one textarea row, and identical in what it shows and what it parses.
5. **Holder part wording** comes only from recorded `part`/`targetPart` values (`hands (grip p0)`), so a derived result can never pull the holder's face, head, hair or build into the frame — the constraint the deleted extractor had to enforce with a deny-list regex.
6. **Budget resolution is conditional on the token.** Texts are only resolved when `HasBudget` is true, so families whose instruction carries no budget (Qwen-Image-2.1) never need a profile lookup for it.
7. **Penalty is omitted, never zeroed.** A function that declares none sends no `presence_penalty` key at all, so providers that reject the parameter are unaffected.
8. **Penalty applies to continuation and force-answer payloads too**, so one completion is never generated under two different sampling policies.
9. **`scene.*` remains an accepted alias** in `StillBriefElements` because the applier has always treated it as a synonym for a frozen-state element; refusing it would have broken existing callers for no gain.
10. **The viewpoint scrub was extended to the new fact paths** (`event.line`, `event.anchor`, contacts naming the holder) — without it the v4 fields would have become a new way for the camera holder to appear in their own first-person frame, which is the exact defect B-166 fixed for the prose fields.

### 0.1.8 Configuration now in `dreamgenclone.dev.db`

| Function | Setting | Value | Why |
|---|---|---|---|
| `RolePlaySceneImagePreprocessor` | `Temperature` | `0.7 → 0.3` | The plan's 0.2–0.4 band; lower sampling variance for a translation task. |
| `RolePlaySceneImagePreprocessor` | `PresencePenalty` | `1.5` | The plan's value, now a persisted, Model-Manager-editable setting rather than a constant. |
| all other functions | `PresencePenalty` | `NULL` | Unchanged behaviour; no default was written. |

`PresencePenalty` did not exist as a column on a running database, so the app's own startup migration adds it; it was applied to the dev DB during this work with the same idempotent `ALTER` the migration uses. Both values are per-machine (the git-tracked snapshot does not carry them) until `helpers/refresh-snapshot.ps1` is run.

### 0.1.9 Tests

Added: `StillBriefFactSheetTests` (label-per-fact, no raw JSON, intent marker, recorded-vs-none contacts),
`ImageCompilerConfigurationCarrierTests` (budget token resolution for every family, non-positive refusal, penalty range,
penalty sent only when configured, holder visibility states).

Updated: `SceneMomentEnrichmentParserTests` (v4 fixture + world-fact projection, place-only refusal, contact cast-key /
self / duplicate refusals, sequential event refusal), `SceneMomentEnrichmentContractTests` (schema exactness),
`SceneMomentEnrichmentJobHandlerTests` (v4 response), `PromptElementClustersTests` (selected clusters, dropped-key
refusal, contacts write-back, nested world-fact edit), `StepPromptElementPlanTests` (trimmed location scopes, event-line
pose scope), `ScenePromptOverridesApplierTests` (dropped-key refusals, event line/contacts), 
`ReferenceBindingPromptRemovalTests`, `IntentPromptReferenceOwnershipTests`, `SdxlSceneImagePromptBuilderTests`
(fact-sheet framing/vocabulary assertions, budget token), `SceneImagePromptPreprocessorTests` (budget token),
`MultimodalMediaCompilerTests` (selection invariants: event present, continuity/duplicate prose absent),
`SceneImagePovProjectionTests` (contacts-derived parts, retired custom text, no-contacts null, v4 facing/directlyAhead,
viewpoint scrub of event/contacts), `FinalImagePromptPlanGuardTests` (version 3), `ImageCellPromptCompilerTests`
(budget resolved from the profile), `SceneImageResolutionTests` (penalty propagation and blank-means-none).

Deleted: `CameraHolderPartsExtractorTests` (the extractor it covered no longer exists).

**Result: `dotnet build DreamGenClone.sln` → 0 errors; `dotnet test` → 4630 passed / 27 failed / 0 skipped**, the 27
being the pre-existing LoRA-coverage failures named in §8. (After the same-day self-contact correction in §0.1.11 the
suite is 4631 passed / 27 failed.)

### 0.1.10 Verification this establishes against the hard rules

- **No fallback / no invented values**: a v3 record yields "not recorded" rather than a derived guess; a missing profile
  with a budget token fails by name; a non-positive budget is refused; a retired custom text is refused; unknown element
  keys are refused. No new `?? default` path was introduced in the image pipeline.
- **One decision path per behaviour**: element selection → `StillBriefElements`; image payload shape → `BuildStill`;
  budget → `ApplyBudget`/`ResolvePromptBudgetAsync`; holder parts → `CameraHolderPartsFromContacts`; holder visibility →
  `Projection.CameraHolderVisibility`; penalty → `FunctionModelDefault.PresencePenalty`.
- **No silent downgrade**: a reference, override or fact the model cannot execute fails with a named diagnostic; nothing
  is dropped to make a render succeed.
- **No negative prompt** was introduced; no compiler gained one.
- **Additive only**: no enrichment field was removed or rewritten, and no stored enrichment row was migrated.

### 0.1.11 Correction shipped after live use (2026-10-24, same day)

The first build refused a contact whose two ends were the same profile key
(`scene_moment_enrichment_output_invalid — Moment enrichment contact 0 pairs 'p0' with itself`), which failed a real
enrichment in Scene Image Studio. That rule was an implementation invention, not an operator requirement, and it was
wrong:

- a character's own body touching itself is a FACT of the instant (a hand on one's own thigh, one's own hands clasped,
  a hand on one's own body), and it is the only way a first-person frame learns about the camera holder's own body;
- failing the whole record over it turned a valid enrichment into a blocked render.

Changes made:

| File | Change |
|---|---|
| `SceneMomentEnrichmentParser.cs` | Self-contact accepted. A repeated contact is de-duplicated (first occurrence wins) instead of failing the record — model redundancy carries no meaning and must not block a render. |
| `SceneMomentEnrichmentContract.cs` | The system prompt now states that `from` and `to` MAY be the same profile key when a character's own body touches itself, and that it must be recorded that way rather than omitted or mis-attributed. |
| `CameraHolderPartsFromContacts.cs` | A self-contact emits ONE clause (`hand (press own thigh)`) instead of contributing both `part` and `targetPart`, which would have described the same limb twice. |
| `StillBriefFactSheet.cs` | A self-contact renders as `p0 hand on own thighs (press)` rather than naming the same person twice, which reads to a caption model as two people. |
| Tests | `Parse_AcceptsASelfContactAndDeduplicatesARepeatedOne` (was `...Refuses...`) and `Resolve_CameraHolderParts_DeriveFromASelfContactWithoutNamingTheHolderTwice`. |

Suite after the correction: **4631 passed / 27 pre-existing failures / 0 skipped.**

### 0.1.12 Correction shipped after live use — caption SHAPE (2026-10-24, same day)

A second live failure: *"The generated image prompt contradicts the request, and one corrective pass did not fix it:
The prompt must be ONE continuous paragraph, but it came back as 5 separate lines."*

Nothing was wrong with the caption. `ValidateCaptionShape` counted LINES and blocked on more than one, so an answer
whose five lines were five sentences of one paragraph was refused, and the corrective pass could not fix it because
the input it was shown (the fact sheet) is itself a list — the model mirrored the input's shape.

The rule was measuring the wrong thing: **an image prompt's line breaks carry no meaning**. What the operator
actually complained about originally was a per-person element RECAP (`Becky: a; b; c`) restating the paragraph, not
wrapping. Changes:

| File | Change |
|---|---|
| `NaturalLanguageSceneImagePromptBuilder.cs` / `PonySceneImagePromptBuilder.cs` | `ParseOutput` collapses the produced caption to one paragraph (`CollapseToSingleParagraph`: newlines to spaces, whitespace runs squeezed). Lossless, and it happens before validation, length checks and storage. |
| `SceneImagePromptContractValidator.cs` | `ValidateCaptionShape` no longer counts lines. It refuses CONTENT shape only: an explicit bullet/numbering marker at a sentence start, or an element recap (`Name:` … `;`). Both still block, with the corrective text saying what to remove. The dead line-based helper `IsElementRecapLine` was deleted. |
| `StillBriefFactSheet.cs` | States that the list is the INPUT shape and the answer is ONE continuous paragraph — so the model stops mirroring the fact list, instead of relying on a corrective pass. |
| Tests | `ValidateCaptionShape_AcceptsAHardWrappedAnswer`, `ValidateCaptionShape_RefusesABulletedOrNumberedList`, `CollapseToSingleParagraph_JoinsLinesAndSqueezesWhitespace`; the recap test still passes. |

Suite after this correction: **4634 passed / 27 pre-existing failures / 0 skipped.**

### 0.1.13 Correction shipped after live use — affirmative wardrobe (2026-10-24, same day)

Operator: *"prompt should not use negative like what is happening with Becky Clothing, 'Naked; no tank or cutoffs on.'
the very mention of the 'tanks and cutoffs' will encourage the model to render them. this should be fixed in the
enrichment if possible."*

The enrichment was recording the REMOVAL instead of the RESULT. An image model renders whatever the text names, so
"no tank or cutoffs" puts a tank and cutoffs in the picture. Fixed at the enrichment, in two layers:

| File | Change |
|---|---|
| `SceneMomentEnrichmentContract.cs` (system prompt) | New wardrobe paragraph: `character.clothing` must state what the body IS wearing, or that it is nude/bare and where. It must never name an absent garment in ANY form ("no tank or cutoffs", "without her shorts", "not wearing shoes", "no longer dressed"), and when the evidence shows something removed or absent it records the RESULTING state ("nude", "barefoot", "topless") — never the removal. The operator's failing string is used as the example, so the model sees the exact shape to avoid. |
| `SceneMomentEnrichmentParser.cs` | `NormalizeWardrobe` strips garment negations from the value (`no X`, `not wearing X`, `is/are not wearing X`, `doesn't have/wear X`, `without X`, `stripped of X`, `has no X`) and tidies the separators the removal leaves. It only REMOVES text — never adds or rewrites — so nothing is invented; the affirmative clause already carries the meaning. A value that is ONLY a negation is refused by name, because choosing between "nude" and "bare-chested" would be an invented fact. Applied to every character's `clothing` at parse time, so the brief, the fact sheet and every prompt compiler see the clean value. |
| Tests | `Parse_StripsANegatedGarmentFromTheWardrobe` (the operator's exact string → `Naked`), four `NormalizeWardrobe_KeepsTheAffirmativePart` cases, `NormalizeWardrobe_LeavesAnAffirmativeStatementAlone` (including "nothing on", which states nudity and names no garment), `NormalizeWardrobe_RefusesAValueThatIsOnlyANegation`. |

Suite after this correction: **4641 passed / 27 pre-existing failures / 0 skipped.**

Why normalise rather than refuse: refusing every negated wardrobe would fail enrichments constantly (models phrase
absence this way all the time), and the harmful part is removable without losing a usable fact. The refusal is kept
only for the case where removal leaves nothing to say.

### 0.1.14 Correction shipped after live use — re-enrichment must not orphan takes (2026-10-24, same day)

Operator: *"when i did the re-enrichment, the previous images from the previous enrichment do not show in the review
deck or production studio... the user should not have to do anything, the previous images from previous enrichment
should appear and behave just like any image created with the current enrichment."*

**Root cause (data, not prompt).** A production group is keyed `(MomentEnrichmentId, Pov)`, and every reading path
filtered `WHERE ProductionGroupId = <current group>`. Re-enriching a moment therefore created a *second* group for the
**same moment at the same POV** — verified live on session `8bc36efb…`, moment `m2`, Omniscient: group `15b3b019…`
(enrichment `c7a5e73c…`, revision 2, 20 takes) and group `4e117d4b…` (enrichment `e1d4b9d7…`, revision 3, 6 takes),
with identical `MomentSetId 023a43a3…` / `MomentId m2`; only the enrichment id and revision differ. The earlier
enrichment is `Superseded`, and no surface had a path to the older group, so 20 completed takes became unreachable.

**Rule adopted (one decision path).** A group is the operator's composition of **one moment at one POV**. Re-enriching
restates that moment's *facts*; it does not make a different *composition*. So membership in a group is decided by the
moment lineage both records carry — `MomentSetId` + `MomentId` + `Pov`, with `ProductionGroupId` as the fast path for
rows that carry no lineage — and by nothing else.

| File | Change |
|---|---|
| `SceneImageProductionGroup.cs` | New `SceneImageGroupMembership.Contains(group, image)` — THE rule, documented with what it deliberately does not relax (a different moment set / moment / POV is never a member). `SceneImageFrameApprovalGate.BlockerFor(group, image)` now asks it instead of comparing `ProductionGroupId`. |
| `SceneImageRepository.cs` | `ListImagesByProductionGroupAsync` and `TrySetDispositionAsync` take the **group** (not its id) and match `ProductionGroupId = $g OR (MomentSetId/MomentId/Pov = $s/$m/$p COLLATE NOCASE)`; the lineage guard drops the widening entirely when a group records none, so an unknown/legacy id behaves exactly as before. The group is a parameter rather than a sub-select because `SceneImageRepository` also runs against stores that hold only `SceneImages` — a cross-table reference made listing fail outright on such a store. |
| `SceneImageProductionGroupRepository.cs` | `ValidateApprovalImage` asks the same rule and keeps every provenance equality that matters (catalogue, beat, plan + version, moment set + version, moment). `MomentEnrichmentId`/`Revision`/`ProductionGroupId` equality is gone — that is exactly what made an earlier take unapprovable. `LoadApprovalImageAsync` now reads `Pov`, which the rule needs. |
| `SceneImageService.cs` / `SceneImageProductionService.cs` | `ListImagesByProductionGroupAsync` / `ListAttemptsAsync` / `SetDispositionAsync` resolve the group and pass it through; `SceneImageService.ListImagesByProductionGroupAsync` fails fast by name when the group does not exist instead of silently listing nothing. |
| Untouched (deliberately) | Every *brief → group* check (`EnsureBriefMatchesGroup`, `IsExactStillBrief`, `SceneImagePromptGenerationJobHandler`) still requires the EXACT enrichment: compiling a NEW prompt must use the current facts. Membership answers "is this take part of this composition", never "was it rendered from the current facts". |
| UI | **No change.** `CompositionComposer`, `ReviewDeck`, `CompareDeck`, `SceneImageStudio`, `SceneImageIdentity`, `SceneImageFinish` and `RunTray` already read through `ListAttemptsAsync` / `ListImagesByProductionGroupAsync`, so all 26 takes appear with their existing disposition, approve, edit and regenerate controls. Each take keeps its own enrichment id and brief id, so provenance is never restated. |
| Tests | `SceneImageGroupMembershipTests` (7: earlier revision is a member, own take is a member, legacy row with owning group is a member, different set/moment/POV is not, group without lineage does not widen, gate rejects a stranger, gate accepts an earlier take); `ReEnrichedMoment_KeepsEarlierTakesListedDispositionedAndApprovable`; `ReEnrichedMoment_DoesNotAdoptTakesOfADifferentMomentOrPov`. |

Suite after this correction: **4651 passed / 27 pre-existing LoRA-coverage failures / 0 skipped** (`RunPodServerlessImageClientTests.GenerateAsync_CancellationDuringPolling_…` failed once under full-suite parallelism and passes in isolation — a pre-existing timing flake, not related to this change).

### 0.1.15 Correction shipped after live use — participant keys must never reach the caption (2026-10-25)

Operator, pointing at render `57fcc461` (Krea 2, Dean POV): *"intention is looking down at womans rear end, a man is
penetrating her from behind his hands clenching her thighs pulling her blouse."* The stored prompt read:

> … The camera holder stands behind her with his penis buried to the root **(p0)**, visible only as part of this
> person's body in frame. …

The composition mismatch itself was **data and POV, not code** (see below), but the key in the caption is a defect:
`p0` is the brief's internal participant identifier, and in ComfyUI/SDXL a parenthesised token is WEIGHT syntax, so the
leak reweights a meaningless token instead of being ignored. It reached the model from three places, all fixed:

| File | Change |
|---|---|
| `ImageStep/ParticipantNames.cs` | NEW. One resolver from `profileKey` → recorded `name` (falling back to a person phrase, never the key), so every renderer that writes a person into prose asks the same question. |
| `ImageStep/StillBriefFactSheet.cs` | `CONTACTS` renders `Dean's penis on Becky's vagina (buried to the root)` / `Becky's teeth on own forearm` instead of `p1 penis on p0 vagina`; the cast row is the NAME only; the FRAME subjects line is the name, not the subject key. Override keys still use `profileKey` internally, so operator edits are unaffected. |
| `ImageStep/CameraHolderPartsFromContacts.cs` | The holder note names the other person (`hands (grip Becky)`), not `p0`. |
| `Evaluation/SceneImagePromptContractValidator.cs` | `ValidateCaptionShape` now also refuses a bare `pN` token as a blocking violation ("a profile key is not a word the image model knows, and a parenthesised one is weight syntax"), so a future leak fails the compile by name instead of rendering. |
| Tests | `Render_ResolvesContactKeysToRecordedNames` (both contact ends resolve; no `p0`/`p1` anywhere in the sheet), `Render_StatesEachFactOnceUnderALabel` asserts the sheet carries no key, `Resolve_CameraHolderParts_ComeFromTheRecordedContacts` asserts the note's wording and that no key appears, `ValidateCaptionShape_RefusesAParticipantKey` (theory) + `…_AcceptsDigitsThatAreNotParticipantKeys`. |

Suite after this correction: **4656 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.**

**Why the render did not match the intent (operator-facing, no code change):**

1. **POV.** The group is `Dean` — the camera IS the holder, so a first-person frame can only ever show his penis/forearms
   entering frame; "a man penetrating her from behind, his hands clenching her thighs" is an EXTERNAL view and belongs to
   the **Omniscient** POV group of the same moment.
2. **The intent never entered the request.** `SceneImagePrompts.UserIntent` is empty and the group draft is
   `{"source":0,"userIntent":""}` — `SceneImagePromptSource.Scene` (0) drafts from the brief alone. Nothing translated
   the sentence because nothing received it.
3. **Two clauses contradict the record.** Contacts are `teeth→own forearm` and `penis→vagina` — no hands on thighs; and
   Becky's `clothing` is "Nude, bare all over" — there is no blouse. Rule 1.a forbids inventing either. Both inputs are
   operator-editable (`frozenState.contacts`, `character.clothing`), which is the designed path.

**Open observation (not changed):** the caption ends with `CAMERA — AUTHORITATIVE: this frame is a tightly framed shot
at high angle … high angle, camera just above eye level, looking down` — a framing DEMAND, which the Krea 2 system
contract itself forbids ("an explicit framing DEMAND is unreliable on this model and usually makes it worse"), and it
mixes a high angle with eye level because the operator override set `Height: above` while `Angle` stayed null (the
projection then fills the angle from the eye-height rule). Worth a Krea-specific decision.

### 0.1.16 Family-aware camera statement (2026-10-25)

Operator: *"there should be krea2 specific compiler, dont we have seperate compilers for different models types"* … *"the
user should still be choosing from the same drop down if they want to change the viewport, the drop down either needs to
change or be translated to what krea wants."*

**Finding.** Only HALF the compiler was per-model. `SceneImageCompilerSystemPrompts.For(family, canonical)` picks a
distinct researched system prompt per family (NaturalLanguage / Pony / Qwen-2.1 / Krea 2, beat + canonical), and the
Krea 2 text forbids framing demands in its own rule 4 — but the FINISHING half was one family-blind path:
`FinalImagePromptPlanBuilder` appended `SceneFramingClause.LineFor(blocking)` to every render on every family, so a
Krea 2 prompt that says "never write a framing demand" was then handed
`CAMERA — AUTHORITATIVE: this frame is a tightly framed shot at high angle…`.

| File | Change |
|---|---|
| `ImageStep/SceneCameraStatement.cs` | NEW — the ONE owner of the trailing camera statement, worded per family. **Krea 2**: describes the viewpoint and what fills the frame ("the picture is taken from above the subject's eye line, looking down. The subject fills most of the frame.") — no shot-size term, no instruction about how much of the subject to include. **Documented-terms families** (Sdxl/Flux/Qwen/Pony/Api): the existing demand form, unchanged. Both forms keep the `CAMERA — AUTHORITATIVE` label so the ONE-camera-statement guard is untouched. |
| `ImageStep/SceneFramingClause.cs` | Says the pitch ONCE. Angle and height are both derived from the same pitch, so "at high angle, viewed from above, high angle, camera just above eye level, looking down" printed it twice (the live render). The angle is dropped when the height wording already carries its leading term; an operator-supplied angle ("three-quarter angle") is kept. |
| `ImageStep/FinalImagePromptPlan.cs` | `Build` now REQUIRES `SceneImageModelFamily` — never defaulted, because a default would silently give one family another family's wording. Uses `SceneCameraStatement.For`. |
| Call sites | Render path (`RenderNativeReferenceAsync`, `RenderNativeIdentityAsync`), asset path (`RenderNativePoseAsync`, `RenderConditionedAsync`, `RenderIdentityConditionedAsync`) and the prompt-generation handler (participant + User-Intent framing) all pass the resolved family they already had. |
| Preview surfaces | `StepPromptFramePreview` / `StepPromptPanel` take a `Family` and pass it through (`ImageStepComposer` → `SelectedChoice.Family`, `CompositionComposer` → `SelectedCompositionModelFamily`), so the preview keeps showing what the render will actually send. |
| UI | **No change to the controls.** The operator still picks from the same five camera heights and the same profile framing choices; the selection is TRANSLATED, not removed. |
| Tests | `SceneCameraStatementTests` (10): Krea describes the viewpoint for the live "tightly framed shot + above" case, every dropdown height translates, wide-frame scale stated as picture content, coverage-only with no height, nothing claimed with neither, documented-terms families unchanged, and the pitch is stated once (theory over three heights) while an operator angle survives. |

Suite after this change: **4670 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.**

### 0.1.17 Operator edits and the participant position (2026-10-25)

Operator, on the caption *"The camera holder stands directly in front of her with their penis buried to the root…"*:
*"did i enter something wrong … it should be behind, also where is 'buried to the root' I removed all of these from the
prompt inputs."* Two independent defects, both in the POV projection; the operator's inputs were correct.

**Defect 1 — a scene-level operator edit never reached the projection.** `ReadFrozenState` built the frame's facts from
`ScenePromptOverridesApplier.ApplyOperatorCharacterEdits`, whose filter is the KEY PREFIX `character:`. Every
scene-level fact was dropped — including `frozenState.contacts`, which the projection reads to derive the camera
holder's visible body. The operator's draft had replaced the contacts with `p1|hands|p0|thighs|clenching tightly`; the
compiler's fact sheet honoured it (`CONTACTS: none …` after POV scrub) while the holder note still read
`"penis (buried to the root Becky)"` from the enrichment's original contact. One fact, two readers, two answers, and the
stale one reached the image.

**Defect 2 — name substitution left an invertible sentence.** The holder is anonymised `Dean` → `the camera`, but
pronouns were left behind, so the fact sheet carried:

> position: "Becky is bent over … **directly in front of the camera** and facing away from **him** … pressed tightly
> against **the camera's** hips."

The caption writer resolved that ambiguity by inverting the relation — "the camera holder stands directly in front of
her" — for a frame taken from behind. Nothing checked the relation structurally.

| File | Change |
|---|---|
| `ScenePromptOverridesApplier.cs` | New `ApplyOperatorProjectionEdits` — the non-removed VALUE edits to every fact the projection READS: `character:{key}.*` plus `frozenState.contacts`. The key list is explicit (`IsProjectionContentKey`), not a wildcard, so a future scene-level reader cannot be added without being added here. Removals and the scene anchors (`location`, `objects`) stay excluded: they are geometry the frame is derived from, and an operator edit must not move the camera. `ApplyOperatorCharacterEdits` is retained unchanged for its other meaning. |
| `SceneImagePovProjection.cs` | ONE read: `ProjectionSnapshot(brief, suppression)` feeds both `ReadFrozenState` and `ResolveCameraHolderParts`, which now takes the SNAPSHOT instead of the brief (and fails fast by name on an empty one). New `OmitHolderRelationClauses` for a participant POV's in-frame `position`: the object pronoun is resolved to the placeholder ("away from him" → "away from the camera" — a true spatial fact), and only clauses naming or contacting the HOLDER'S BODY are dropped. "waist" is excluded from the body list on purpose — "her hips are raised at about the camera's waist height" is a height relation the render needs. Omniscient frames are untouched (external view, the relation is real content). |
| Tests | `SceneImagePovProjectionOperatorEditsTests` (5): the operator contact edit produces `hands (clenching tightly Becky)` and no "buried to the root"; a character edit alone leaves the contact note alone; a participant position keeps "directly in front of the camera" and the waist-height relation while dropping "the camera's hips" and every "him"; the subject's own posture survives; an Omniscient position is not narrowed. |

Suite after this correction: **4675 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.**

### 0.1.18 Two follow-ups from the same live render (2026-10-25)

Operator, on SceneImage `36857035` (Krea 2, Dean POV): *"it lost Becky wardrobe (naked), it is not showing the camera
holder p1|hands|p0|thighs|clenching tightly."*

The compiler INPUT was correct this time — the §0.1.17 fixes are live:

```
- Becky — … clothing: "Nude, bare all over"
- CAMERA HOLDER'S VISIBLE BODY: "hands (clenching tightly Becky)"
- position: "… directly in front of the camera and facing away from the camera … at about the camera's waist height …"
```

So the operator's contact edit reached the note (defect 1 of §0.1.17 fixed) and the spatial relation is coherent (defect
2 fixed). Two NEW causes, one per symptom:

**1. The note named the person but not the body area.** `CameraHolderPartsFromContacts.Describe` emitted
`hands (clenching tightly Becky)` — no `targetPart`. With no idea what the hands were clenching, the caption writer
borrowed the nearest body part in the record and wrote *"Hands clenching tightly around her forearm"* for a contact that
is on her thighs. Fixed: the note is `hands on Becky's thighs (clenching tightly)` — the same shape the fact sheet's own
CONTACTS line uses, naming both ends so nothing is guessed. File: `ImageStep/CameraHolderPartsFromContacts.cs`.

**2. The wardrobe was dropped to satisfy the Krea profile's character budget.** `profile-krea2-turbo` carries
`MaxChars = 800` / `MaxTokens = 300`, and this build is the first in which `{{maxChars}}` actually resolves into the
system prompt ("13. Keep the ENTIRE prompt under 800 characters"). The run produced an 824-character caption — it obeyed
the ceiling and paid for it by deleting the nudity clause, which is the fact with no redundancy anywhere else. Two
consequences worth recording:

- **800 is too small for a fact-carrying still brief.** The facts alone (place, light, event, posture, facing, action,
  wardrobe, the holder's body, the frame) need ~1100-1300 characters of prose to be stated once each.
- **`MaxTokens = 300` cannot cover even the current ceiling.** HARD RULE 12 already records the arithmetic (~300 tokens
  ≈ 1200 characters); a raised `MaxChars` needs a matching `MaxTokens` or the completion is cut off mid-sentence.

Both are PROFILE DATA, not code: the value is owned by `ImageCompilerProfiles` and edited in Model Manager, so the fix is
a configured value change (operator decision, evidence = this render). No code fallback was added — the compiler still
reads the single configured ceiling.

Suite after the note fix: **4675 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.**

**Applied for symptom 2 (operator decision, 2026-10-25):** `profile-krea2-turbo` is now
`MaxChars = 1600`, `MaxTokens = 600` (MinChars 120 unchanged), in BOTH places the value lives:

- the live row in `dreamgenclone.dev.db` (updated in place, `Version` 1 → 2);
- the SEED in `ImageCompilerProfileRepository` (git-tracked): this table's seed is
  `ON CONFLICT(CheckpointIdentifier) DO NOTHING`, so the code seed is what a fresh clone gets, and an existing host that
  has tuned its own budget is never silently overwritten.

The tracked `dreamgenclone.snapshot.db` deliberately does NOT receive it: that snapshot predates the Krea 2 row entirely
(the row was created 2026-10-01, the snapshot is 2026-09-02), and copying `dev.db` over it is forbidden because the live
database holds encrypted provider keys and session data. The portable form of this setting is the seed.

### 0.1.19 Three render failures from one prompt (2026-10-25)

Operator: *"i removed the biting the arm part, it keeps coming back, 8ab2a51a shows facing the camera, f2acd4fb shows her
hands grabbing her thighs, 34b95f2f does not show any hands on thighs."*

**All three images share ONE prompt record** (`50e778e5`), compiled at 15:31 — the operator's edit was saved at 15:42, so
none of the three could reflect it. Two of the three symptoms were nonetheless real defects, one was a fact the operator
had not yet edited:

| Symptom | Cause | Fix |
|---|---|---|
| The bite keeps returning | It lived in THREE facts. The operator edited `actionOrObservation` (15:42), but `sightline` still reads "down toward her own forearm at her mouth", and `moment.compositionRationale` — which has **no operator-editable row at all** — restated "Becky's silent climax and forearm bite" and was rendered into the fact sheet. | `StillBriefFactSheet` no longer renders `COMPOSITION RATIONALE`: it restates the event, it cannot be edited, and B-167 makes `event.line` the single owner of the moment's prose. The remaining source (`sightline`) is an editable Prompt Input row. |
| Her hands instead of his (`f2acd4fb`), or no hands (`34b95f2f`) | The note said `hands on Becky's thighs (clenching tightly)` — a bare "hands" reads as the subject's own. The caption wrote "hands clenching tightly around her thighs to steady herself". `ValidateCameraHolderBody` passed BOTH renders: its coverage branch only asked whether one of the note's words appears somewhere. | The note now names the owner (`the camera holder's hands on Becky's thighs (clenching tightly)`), and the validator requires ATTRIBUTION as well as coverage — a part word with no second-person owner is a blocking violation with its own message ("…it reads as the pictured subject's own body"). |
| Facing the camera (`8ab2a51a`) | The input was right (`facing: "the tin wall beyond the workbench"` plus "FACING — this person faces AWAY from the camera: show their BACK"), but the caption came back in the FIRST person: *"In front of me is Becky … her back faces away from my eye-level camera as I stand behind her … behind us."* A selfie reads as a front view to the image model. The projection's rule forbids it; nothing checked it. | New blocking `ValidateThirdPerson` (wired into both compile passes): first-person pronouns in a shot description are refused by name. |

Suite after these corrections: **4680 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.**

**Not a code matter:** the operator's `frozenState.event.line` override still contains "her teeth sunk into her own
forearm". It does not reach a PARTICIPANT POV (the projection drops a prose fact that names the camera holder), but it
will reach the **Omniscient** group of the same moment, so the same bite will reappear there until that override is
edited too.

### 0.1.20 The camera holder's wardrobe in his own note (2026-10-25)

Operator: *"now i want to add that the camera holder Dean thighs are pressed tightly against her ass, keeping what he is
wearing (undone jeans)."*

The thighs are a CONTACT (`p1|thighs|p0|ass|pressed tightly`), so the operator can add them from the Contacts row. The
**wardrobe had no route at all**: a participant POV drops the camera holder's own character rows (they are the camera, so
they are not in the cast), so his `clothing` fact never reached the compiler — editing it changed nothing in that frame.

| File | Change |
|---|---|
| `SceneImagePovProjection.cs` | New `AppendHolderWardrobe`: when the frame shows a part of the holder, his own `clothing` fact is appended to the visible-body note — `… (pressed tightly); the camera holder's wardrobe: Rugged casual clothing, disheveled, trousers open at the waist`. Read from HIS operator-editable row, emitted only when a part is in frame, and omitted when the value is blank or the operator removed it — so the wardrobe keeps one owner and nothing is invented. |
| Tests | `Resolve_CameraHolderNoteCarriesHisWardrobeWhenHisBodyIsInFrame`, `Resolve_CameraHolderNoteCarriesNoWardrobeWhenHisBodyIsOutOfFrame` (no contact ⇒ no part ⇒ no wardrobe), `Resolve_CameraHolderWardrobeIsDroppedWhenTheOperatorRemovesIt`; the existing note expectations gained the wardrobe clause. |

Suite after this change: **4683 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.**

### 0.1.21 A model switch must not carry the previous model's LoRAs (2026-10-25)

Operator: *"i switched to qwen 2.1 it seems there are leftover lora's trying to be added for previous model picked. The
LoRA for de351eb3 … was trained on base model 'krea2_raw_bf16.safetensors' and is not declared loadable under
'qwen_image_2.1_int8_convrot.safetensors'."*

Both pickers already refused to OFFER a LoRA the selected model cannot load, and both already warned next to a stale
selection — but neither cleared it, so the value stayed on the settings object and the render refused it after the
operator had pressed render:

| File | Change |
|---|---|
| `Shared/CharacterLoraPicker.razor` | When the model identifier changes and the qualified list LOADS, selections the new checkpoint cannot load are removed from the selection and **named in a visible warning** ("Cleared 1 LoRA … because it does not load under `qwen_image_2.1_…`: <token> (v1, trained on krea2_raw_bf16.safetensors)"). A failed read never prunes (`_error` guard), so an outage cannot look like "nothing qualifies". |
| `Shared/SceneLoraPicker.razor` | The same rule for family-catalogued scene LoRAs, keyed on the family's catalog instead of a checkpoint. |
| Untouched | `SceneImageCharacterLoraResolver` still refuses an unloadable artifact by name — the defence in depth stays, and this only stops the control carrying a value it cannot express. Every host (Composition Composer, Scene Image Studio, Playground, Asset Creator, Body panel) gets the fix because the rule lives in the one shared picker. |

Suite after this change: **4683 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.**

### 0.1.22 A location reference does not own the light (2026-10-25)

Operator, on Qwen-Image-2.1 with a Location reference bound: *"it is disabling too many fields that do not correlate to
the Location references image. Environment, Lighting — these should not be locked and be allowed to be changed by the
user."*

Correct. A location reference supplies WHERE the people are — the room's structure, its surfaces, what stands in it — and
the room-scope list exists to stop the prompt repeating that. It does not decide the LIGHT: the same shed at dawn, under
slat light or in the dark is a different photograph, and the operator asking for a lighting or atmosphere change found the
row locked with no way to say it.

| File | Change |
|---|---|
| `ImageStep/StepPromptElementPlan.cs` | `LocationScopes` is now `frozenState.location`, `frozenState.objects`, `moment.compositionRationale`. `frozenState.environment` and `frozenState.lighting` are no longer removed, so both rows stay editable and their values reach the compiler. |
| `SceneImagePovProjection.cs` | `ReferenceSuppliedRoomRule` and `AppendRoomOwnership` narrowed to match: the image supplies the room's STRUCTURE, surfaces, fixtures and objects; a LIGHTING or atmosphere value the request records is still written. |
| `SceneImageCompilerSystemPrompts.cs` | `Qwen21CanonicalActionOnly`: the `Setting` class now says "the room's structure comes from a bound image; its LIGHTING does not", and the rule reads "DO NOT DESCRIBE THE ROOM'S STRUCTURE … LIGHTING and ATMOSPHERE the intent states ARE written". |
| `NaturalLanguageSceneImagePromptBuilder.cs` | `CoverageOf` no longer maps `environment`/`lighting` scopes to the supplied `Setting` class — a class reported as covered is a class the intent contract forbids the operator to state, so it must follow the removal list exactly. |
| Tests | `Derive_LocationBinding_…` now asserts the two rows are NOT removed; `Merge_OperatorOverrideForTheSameElement_Wins` asserts the untouched elements are the ones the reference really supplies; the plan tests track the new scope list. |

Suite after this change: **4683 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.**

### 0.1.23 P2 — the inclusion contract (2026-10-25)

Operator: *"missing Becky's head 8bb09d13 … i feel like we are going in circles many hack-like fixes, there should be a
holistic approach … this is about creating the pipeline for any moment, multiple POV, generated reliably with only minor
modifications from the user."*

Holistic design: [holistic-prompt-pipeline.md](./holistic-prompt-pipeline.md) — a typed `PromptPlan` per (moment, POV) as the
single owner of meaning, one deterministic renderer per family, and structural contracts instead of prose heuristics.
P2 of that plan ships first because it needs no renderer change.

**The failure, corrected after operator pushback.** The caption for `8bb09d13` read *"A close-up shot captured from
above … Her head is not visible due to being positioned below the frame line where the image cuts off at about waist
height."* My first diagnosis — that a close overhead shot of a bent-over subject *cannot* contain a head — was WRONG and
is retracted: a high, close view frames the whole body with the head lower in the picture. **A shot size describes how
much of the picture the subject occupies; it never removes a body part.** The caption invented the crop, and our own
wording licensed it: "close-up shot" + "the subject fills the frame" says how full the picture is and never what must be
IN it.

| File | Change |
|---|---|
| `ImageStep/FrameInclusion.cs` | NEW. One sentence stating what the frame CONTAINS — "Every person in frame is in frame from crown to feet, whole and uncropped by the edge of the picture." Default extent is the whole person for EVERY shot size; the per-subject operator override lands with the plan (P1). |
| `ImageStep/SceneCameraStatement.cs` | Every shot-size arm now states the inclusion BEFORE any fullness claim, so "close" can never be read as a licence to crop. |
| `SceneImagePovProjection.cs` | The participant SHOT block carries an `- Inclusion:` line ahead of the cast, with the rule spelled out: a shot size never removes a body part. |
| `Evaluation/SceneImagePromptContractValidator.cs` | New blocking `ValidateNoFrameExclusions`, wired into both compile passes, refuses descriptions of the frame EDGE: "not visible", "cut(s) off", "below the frame", "frame line", "outside the frame/picture", "off-screen", "hidden by the frame". A back turned to the lens stays describable — the rule is about the edge, not about which way someone faces. |
| Tests | The live caption is refused by name; a whole-body caption and a "back to the lens" caption are accepted; camera-statement expectations updated. |

Suite after this change: **4686 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.**

### 0.1.24 P1 — the typed PromptPlan, in shadow mode (2026-10-25)

Operator: *"go"* — proceed with the holistic design, strategy **skeleton + verified polish** for the later renderers.

| File | Change |
|---|---|
| `Domain/RolePlay/PromptPlan.cs` | NEW. The single owner of a prompt's meaning: `Subjects[]` (appearance only when no reference supplies it, wardrobe, world pose, facing, frame placement, **in-frame extent**), `Contacts[]` with **ownership as a field** (`FromProfileKey/Part → ToProfileKey/TargetPart`), `ActionLine` (the one prose fact), `SettingKind`, `TimeOfDay`, `Lighting`, `Atmosphere`, `HolderBody` (parts + wardrobe), `PromptPlanFrame` (shot/angle/height/**inclusion**/first-person), `MustAppear` (the coverage set) and `OperatorEditedKeys`. Plus `FrameInclusionExtent` — one vocabulary, whole-body default. |
| `ImageStep/PromptPlanBuilder.cs` | NEW. Pure builder from the SAME operator-edited snapshot the projection reads, plus the projection and the frame. Derives subject facts, cast-to-cast contacts, the holder's own body from contacts (not by parsing the note string), the frame, the coverage set and the edited keys; plus a compact `Summarise` for the debug record. |
| `SceneImagePromptGenerationJobHandler.cs` | SHADOW recording: `SceneImagePromptPlanBuilt` on every scene-path generation (nothing consumes the plan yet), and `SceneImagePromptPlanFailed` with the reason if the plan cannot be built — a diagnostic never fails a render, and never skips silently. The effective blocking is hoisted so the plan records the frame the render will actually use. |
| Tests | `PromptPlanBuilderTests` (9): each subject stated once with its own facts; **no fact appears twice**; contacts name both owners and the holder's contact is NOT a cast contact; holder body derived from contacts; frame states its inclusion rather than deriving an extent from a shot-size word; an operator wardrobe edit reaches the plan; extent is the whole-body default; `MustAppear` names every required class; the plan's only identifier is the profile key field. |

**Deviation, stated:** the per-subject extent override is NOT read yet. An override key must be a selectable brief element
(`ScenePromptOverridesApplier` refused `character:p0.framing.extent` by name — verified), so the override needs an
additive `character.framing.extent` brief field plus its Prompt Input row. That is **P1b**; P1 ships the derived default,
which is the value that fixes the crop class anyway.

Suite after P1: **4695 passed / 27 pre-existing LoRA-coverage failures / 0 skipped** (`CharacterLoraCoverage*`,
`CharacterLoraCellTemplateSeedTests` — unchanged from §0.1.23 and unrelated to this path).

### 0.1.25 P1 acceptance gates — the plan carries every fact, and the extent has a row (2026-10-25)

P1's remaining gates were "prove the plan cannot silently lose a fact the fact sheet states" and "give the operator's
in-frame extent a real home". Both are now closed.

**Gate 1 — no fact can be lost by the P3 switch.** `EveryFactTheSheetStates_HasATypedHomeInThePlan` renders the fact
sheet for the fixture brief, PARSES THE LABELS THE SHEET ACTUALLY EMITTED, and requires each one to map to a plan slot
that is non-empty. A fact class added to the sheet later fails this test instead of vanishing from prompts when the
family renderers start reading the plan instead of the sheet. Building the test found the plan was already lossy, so the
record grew the slots the sheet needed:

| Plan slot (new) | Carries |
|---|---|
| `PromptPlan.Setting` | the recorded place in words (was: kind only) |
| `PromptPlan.SettingSuppliedByReference` | whether a location image owns the room, so a renderer states the KIND and never the structure |
| `PromptPlan.TemporalAnchor` | the moment's temporal anchor |
| `PromptPlan.Mood` | the recorded affect ("Clenched, breathless, secretive.") |
| `PromptPlan.Objects` | the recorded props |
| `PromptPlan.ActionAnchor` | where the action happens ("at the workbench") |
| `PromptPlanSubject.Action` / `.Sightline` / `.Place` | the per-person action, gaze and location |

Two exclusions are DECLARED, not forgotten (`FactsNotOnThePlan`, asserted by the same test): `visible to them` and
`involvement` are not observable in a photograph — the first is cast membership, which `Subjects` has already applied.

`PromptPlanFrame.Inclusion` is now `string?`: the extent every subject shares, or null when they disagree or nobody is
in frame. A frame cannot state "crown to feet" for one person and "waist up" for another, and an empty frame has no body
whose inclusion it could state.

**Gate 2 — the extent has a home in the brief, and its control waits for the renderer (P1b).**
`character:{key}.framing.extent` is an additive brief element (no migration: a record without the leaf derives the
whole-body default, which is the frame's own rule rather than a substitute for a missing setting).
`PromptPlanBuilder.ExtentOverrides` reads it and validates through `FrameInclusionExtent.Require`, so an extent outside
the recorded vocabulary fails by name rather than reaching a prompt as words the model has to interpret.

**The Prompt Input row is deliberately NOT shipped yet, and here is why.** It was built and verified live (a working
dropdown on both character rows, showing the derived "crown to feet" and the four recorded extents), then WITHDRAWN: the
caption path does not read the plan until P3, and the camera statement still ends every prompt with "Every person in
frame is in frame from crown to feet". A dropdown that narrows the plan while the prompt keeps asserting the opposite is
worse than no dropdown — it is two answers to one question, the defect class this whole item exists to remove. The row
lands WITH the renderer that honours it, so every control on that panel does what it says.

**Gate 3 — the plan on a REAL record, at all three POVs.** `PromptPlanRealBriefTests` embeds the verbatim
`CompiledMediaBriefs` snapshot for moment `m2` (enrichment revision 3) and asserts what the plan says at Omniscient, Dean
and Becky. The record is more awkward than the hand-written fixture and exercises paths it could not: an empty
`visibleCharacterNames` (Becky's own POV shows nobody, and the plan says so rather than inventing a subject), a
self-contact beside a cast-to-cast contact, four recorded props, and a blocking camera that a participant POV must
ignore. Dean's POV proves his contact with Becky is HIS body (`HolderBody.Parts = ["penis"]`, his wardrobe attached)
rather than two pictured people touching — the defect class that produced "her hands on her thighs".

Suite after P1b: **4704 passed / 27 pre-existing LoRA-coverage failures / 0 skipped** (same
`CharacterLoraCoverage*` / `CharacterLoraCellTemplateSeedTests` set as §0.1.23, untouched by this path).

### 0.1.26 The compiler was right and the data was wrong — five fixes at the right layers (2026-10-25/26)

Operator, reading the compiled prompt of SceneImage **3ade4908** (Qwen-Image-2.1, Dean POV, identity + location bound):
*"Pine sap and rust in the air does not help to create the image… 'which may have been removed for privacy?'… why is the
compiler not [following a defined structure]… if I were to write this prompt it would be small and simple: a photo
realistic medium shot eye level image of a woman `<image1>` bending over a workbench in a shed `<image2>`, her feet on the
ground, her chest is on the workbench, she is facing away from the camera"* — and *"always consider making changes to the
enrichment or prior phases instead of adding workarounds to the prompt compilers."*

**Root cause, verified.** The vendor documents TWO prompt shapes
(`research/qwen-2-1-prompt-enhancer.md`): §4 `t2i` wants ~20 sentences and says "a short brief means inventing most of
the frame, not writing less" — because there the PROMPT is the only carrier; §5 `edit` says the opposite — "anchor on the
image. Nothing invented; an uncertain detail is omitted rather than specified". The canonical path was compiled under §4
while identity/body/location plates removed three element classes from the brief, so the compiler obeyed the length and
invented: "hips raised to about chest level", the privacy disclaimer, "detectable in the air". `BuildCanonicalSystemPrompt()`
took no coverage at all; the reference-aware contract existed and was wired to User Intent drafts only.

| File | Change |
|---|---|
| `SceneImageBeatAnalysisService.cs` (the BEAT/enrichment phase) | `environment` may record **only what a camera in that space could see** — surfaces, materials, colours, fixtures, objects; never a smell, a sound, a temperature or anything "in the air". `position` must be stated **against the space or an object** ("hips level with the workbench top"), **never against another character's body** ("at Dean's waist height") — that character is the camera in their own POV, the referent is deleted, and the measurement dies. The JSON shape line for both fields was updated to match. |
| `ImageStep/PromptPlanBuilder.cs` | Per-POV placement: one in-frame subject **or** a participant POV ⇒ CENTER. The compiled blocking stages the whole cast for the external frame, so inheriting its slot put a lone subject at the LEFT edge. |
| `SceneImagePovProjection.cs` | The omniscient SHOT block states "Center Foreground, the only person in the picture" when one subject is in frame, instead of echoing the two-shot slot. |
| `SceneImageCompilerSystemPrompts.cs` | Both Qwen texts rewritten: no model name and no dialect names (a small local instruction model cannot act on "Qwen-Image-2.1" or "Pony vocabulary" — operator 2026-10-25), the anatomy promoted to "what makes a prompt of this kind good", every prohibition kept as the LITERAL token list, a worked EXAMPLE of the target shape, and the length contradiction ("about 20 sentences" beside "never pad to reach a length") resolved. NEW `Qwen21CanonicalAnchored(coverage)`: the operator's own slot order (medium+shot+angle → subject `<imageN>` → action+surface → location `<imageN>` → one clause per pose fact → orientation → wardrobe/nude → light source+direction → one closing clause), governed by what the references supply, with "length follows the facts that remain" and "never write a smell". |
| `NaturalLanguageSceneImagePromptBuilder.cs` + `QwenSceneImagePromptBuilder.cs` | `BuildCanonicalSystemPrompt(IntentReferenceCoverage coverage)` — the canonical path now selects by what the bound images supply (coverage from `ReferenceBindingPromptRemoval.Derive`), exactly as the intent path already did. Qwen: no coverage ⇒ §4 long-form; any coverage ⇒ §5 anchored. |
| `ImageCompilerProfileRepository.cs` + dev DB | Qwen + NoctQ character FLOOR 300 → 120 (MaxChars 3400 / MaxTokens 700 unchanged, Version 4): the floor was rejecting the operator's own ~200-character working prompt, so the model padded to reach it. |

Tests: `QwenSceneImagePromptBuilderTests` asserts the anchored contract's slot order, tags, "length follows the facts",
the photographable-only rule, coverage-driven suppression and the worked example; `SceneImagePovProjectionTests` asserts
a one-person frame is CENTER (the old assertion encoded LEFT and was replaced). Suite: **4706 passed / 27 pre-existing
LoRA-coverage failures / 0 skipped.**

**Deliberately not done:** Krea 2's text still carries the dialect names and has no worked example — same treatment due
next, together with its own anchored contract.

### 0.1.27 Instructions cannot carry data — the STAGING block (2026-10-26)

Operator, on render **4b627c8a** (Omniscient, NoctQ): *"2 renders had Dean standing behind but body facing the camera not
directly behind Becky, one had Becky facing to the right"* and *"the prose does not say what position and relation they are
to each other and where they are on the image; the reference image mapping has the correct character and left and right."*

**The compiler input was complete and the output threw it away.** Verified from the `SceneImagePromptProjected` debug
event: the fact sheet carried `facing: the tin wall beyond the workbench`, `body: bent over at the waist, torso low over
the boards, hips raised, feet on the shed floor`, Dean's `facing: Becky's back` and `hips pressed to Becky's rear`, plus
`FRAME: Becky — LEFT foreground / Dean — RIGHT foreground` — and the deployed contract (verified present in the stored
system prompt: rules 4b, 4c, "PLACEMENT AND RELATION ARE FACTS", "A CONTACT IS A SENTENCE", "DECOMPOSE THE POSE") demanded
all of it. The caption returned 455 characters naming **none** of the placement, **neither** facing, and no hips-raised or
torso-low. A 7B instruction model does not reliably carry a fact it is merely asked to carry, however the contract is
worded.

| File | Change |
|---|---|
| `ImageStep/SceneStagingStatement.cs` | NEW. The recorded GEOMETRY stated as DATA at the end of the prompt: each in-frame person's placement (LEFT/CENTER/RIGHT + depth), their `at.posture`/position clause **verbatim** (so the pose is decomposed, not summarised), their recorded world `facing`, their wardrobe, and every recorded contact as owner → part → target → manner. Appended like `SceneCameraStatement` and the camera-holder body, because those exist for the same reason. People are addressed by place + dress ("the nude person on the left"), never by story name; a facing target that names the other person becomes "the other person's back", and one that names a participant POV's camera holder becomes "the camera". A recorded fact that is absent is omitted, never guessed. |
| `SceneImagePromptGenerationJobHandler.cs` | Appends the staging block after the caption (and after the framing line), for every scene-path POV including Omniscient. |
| Tests | `SceneStagingStatementTests` (7): placement + facing + decomposed pose for an external camera; contacts with owner and target; no story name anywhere; no contact when none recorded; empty for a User Intent draft; camera-holder name scrubbed in a participant POV; a single subject is centred. The first run of these caught two defects in the change itself (a facing target leaked "Becky's back", and a participant-POV projection was resolved against the wrong snapshot). |

Suite: **4713 passed / 27 pre-existing LoRA-coverage failures / 0 skipped.** Published as `20261026-201903`.

**Still to do, in order:** Krea 2's contract has not had the same treatment (dialect names, no worked example, no anchored
form); and the enrichment-side `environment`/`position` fixes only affect NEW moments — stored moments keep their
"pine sap and rust in the air" text until the operator edits the row.

### 0.1.28 A tag number inside an example is copied, not derived (2026-10-27)

Operator, comparing two renders of the same moment/beat: *"why does the latest changes we did to the image compiler work
for krea f4172d21 and not for qwen a53cd560"*. Both compiles ran on the same code and the same compiler model; the
difference was the RENDER SURFACE.

- **Krea `f4172d21`** — Krea 2, `PromptOnly`: no reference images, so the caption carries **no tags at all**. The new fact
  sheet, framing line and STAGING block were the whole job, and the render honoured them.
- **Qwen `a53cd560`** — Qwen-Image-2.1, `NativeReference` with three bound images (p0 body, p1 body, Indoor Back
  location), so the place is **`<image3>`**.

**Root cause, verified from the `SceneImagePromptProjected` event of prompt record `66ac8ba1`.** The anchored contract's
worked example was written from the operator's own brief for a scene with exactly TWO bound images, so it showed the place
as `<image2>`. The request's own REFERENCE IMAGES block correctly said "`<image3>` (Indoor Back) is the LOCATION
reference", and the compiler ignored it in favour of the example:

```
The setting of this picture is the place shown in <image3>: …        (deterministic anchor, correct)
A photorealistic medium shot at eye level of a woman <image1> bending over a workbench in a shed <image2>, …
                                                                    (compiler — points the shed at Dean's body plate)
<image2> is the person on the RIGHT … their build, body shape …      (role block, correct — contradicts the sentence above)
```

Range validation passed: `<image2>` was a real, in-range tag. The finished render came back with a tool-racked room, no
contact between the figures and mangled bodies — the Qwen graph was obeying three mutually exclusive statements about the
same image.

| File | Change |
|---|---|
| `SceneImageCompilerSystemPrompts.cs` | `Qwen21CanonicalAnchored` no longer writes tag NUMBERS anywhere in its worked example: the example uses `[the person's tag]` / `[the room's tag]` and states that the number always comes from the request's REFERENCE IMAGES block, including the two-people case ("the two people take the first two tags and the place takes the third"). Two rules added: THE NUMBER COMES FROM THE REQUEST, NOT FROM THIS TEXT; and A PLACE IS POINTED AT THE LOCATION IMAGE'S TAG. The slot-order lines lost their `("of a woman <image1>")` / `("in a shed <image2>")` illustrations for the same reason. |
| `Evaluation/SceneImagePromptContractValidator.cs` | NEW `ValidateReferenceTags(prompt, bindings, family)`, BLOCKING, wired into both the first pass and the corrective pass of `GenerateWithContractAsync`. Qwen-Image-2.1 only (it is the family whose contract writes tags into the caption; the other families state the reference block at render). It refuses (a) a place pointed at a tag whose slot is not Location ("in a shed <image2>"), and (b) a caption that never names the location tag at all. Relation phrases ("in front of <image2>") and frame-placement words ("in the foreground <image1>") are excluded, because those are facts about people rather than claims about the room. |
| Tests | `QwenSceneImagePromptBuilderTests` +2: the example contains no `<imageN>`, and the two-person numbering rule is stated. `SceneImagePromptContractValidatorTests` +5: the reported prompt is refused with the correct tag named; the corrected prompt passes; a relation phrase is not a place; a tag-free family is untouched; a single untagged reference is skipped. |

The first run also showed the older class of defect in the same prompt: the compile input's `CONTACTS: none — two bodies do
not touch in this instant` contradicted its own CAST stance ("hips pressed to Becky's rear"), so the caption dropped the
contact while the STAGING block restored it. That is data, not contract — recorded here as still open.

Suite (affected classes, 222 tests): **0 failed.** Full solution builds with 0 errors.

### 0.1.29 The camera holder's parts are the CAMERA's, not a second person's (2026-10-27)

Operator, on SceneImage **516cc462** (Qwen-Image-2.1, Dean POV, Camera Holder parts = Derived): *"body parts are shown but
they are not from 'off camera' so it is a mutated image, can this be fixed without breaking the Omniscient prompt"*. The
render showed the subject from behind with **two disembodied hands growing out of her hips** and no holder anywhere.

**Root cause — two statements, both true of an EXTERNAL frame, applied to a first-person one.**

1. The compiled caption read "The camera holder's hands are clenching hard against her thighs while their hips press
   tightly into hers … they stand directly behind Becky near the viewer." That is a correct third-person description, and
   to an image model it describes a *partly drawn person behind her*.
2. The camera line said `CAMERA — AUTHORITATIVE: … Every person in frame is in frame from crown to feet, whole and
   uncropped by the edge of the picture.` An image model holding a half-described person and an instruction that every
   person is whole will **complete that person out of the only parts it was given** — which is exactly the mutation.

The holder is the CAMERA, so "every person is whole" is impossible for them; the SUBJECT half of that rule is still
right (it exists so a shot-size word is never read as a licence to crop, SceneImage 8bb09d13).

| File | Change |
|---|---|
| `ImageStep/CameraHolderBodyStatement.cs` | NEW. `CAMERA HOLDER — AUTHORITATIVE`, appended at render after the staging block, participant-POV only: this photograph is taken from the holder's own position; **the only parts of them in the picture are `{hands and hips}`**, entering from the frame's **near edge**, foreshortened toward the subject, **attached to the viewpoint and never to the subject's body**; the rest of them is outside the frame (no head, face, hair, shoulders, chest, torso) and *whoever stands behind the subject IS this camera*. Derived from the same recorded contacts as the projection's note, never from the caption. |
| `ImageStep/FrameInclusion.cs` | NEW `ParticipantSentence(parts)`: the subject is whole and uncropped; **the camera holder is the camera, not a second person** — only the named parts are in the picture, entering from the near edge. `Sentence` (every person whole) is unchanged and remains the default. |
| `ImageStep/SceneCameraStatement.cs`, `SceneFramingClause.cs` | Both take an optional inclusion sentence. Default = `FrameInclusion.Sentence`, so every external (Omniscient) render is byte-identical; the participant branch in `SceneImagePromptGenerationJobHandler` passes the participant one when the holder's parts are in frame. |
| `SceneImagePovProjection.cs` | The participant SHOT block's `- Inclusion:` line uses the participant sentence when `CameraHolderVisibility == PartiallyVisible`, and the old whole-person sentence otherwise. An Omniscient projection never had this line and is untouched. |
| `ImageStep/CameraHolderPartsFromContacts.cs` | NEW `PartNamesOf(note)` — reads back the two note shapes this class itself produces (part-owns-contact and self-contact), excludes wardrobe, dedupes. Every caller that lists the parts uses it, so the inclusion sentence and the holder block cannot disagree. |
| `SceneImagePromptGenerationJobHandler.cs` | Appends `holderBodyLine` after the staging block (participant only) and passes the participant inclusion to the camera statement. |
| Tests | `CameraHolderBodyStatementTests` (8): the block places the parts at the camera and names no wardrobe; empty for Omniscient, for a participant with no parts, and with no projection; the participant inclusion scopes the whole-body rule to the subject; the camera statement states the external inclusion **by default** and the participant one only when asked (the Omniscient guarantee); the projection text scopes the inclusion only for a participant with parts; both note shapes parse. |

Suite (affected classes, 142 tests): **0 failed**, including the pre-existing `SceneImagePovProjectionTests`,
`SceneCameraStatementTests`, `SceneStagingStatementTests`, `ImageCompilerConfigurationCarrierTests` and the prompt-plan
tests — the Omniscient prompt is unchanged by construction and by test.

**Still open (same report, not fixed here):** the caption's own phrasing is third-person by design (the compiler is told
to write the holder as a separate person so the parts are never merged into the subject's body — SceneImages f2acd4fb /
34b95f2f). The new block reconciles that at render time; making the *writer* describe the near-edge geometry is a
compiler-side improvement, and the `CONTACTS: none` vs cast-stance contradiction noted in §0.1.28 is still an enrichment
data defect.

### 0.1.30 The angle was the whole failure — "bent over" read as STAND (2026-10-27)

Operator, after §0.1.29 shipped: *"nothing changed, the hands are still not coming from off camera"* — and then, one render
later: *"9421194d giving the proper angle works"*. The operator was right, and the comparison proves it: that render differs
from the two failures in exactly ONE input.

| | working `9421194d` | failing `5b6a1cdf` / `b5ea6fb4` |
|---|---|---|
| `SceneBlockingOverride.Camera` | `ShotSize: close shot, Height: overhead` | absent, or `Height: null` |
| Compile SHOT block | *"close shot, **high angle, viewed from above**… Camera height: **bird's eye perspective, looking straight down**"* | *"close shot / medium shot, **eye level**… Camera height: **the subject's own eye height**"* |
| Inclusion + holder block + prose | identical | identical |

At "the subject's own eye height" the lens is level with a woman bent over the bench, and there is no geometry in which the
holder's hands enter the frame from the camera — so the model attaches them to the nearest body. **Three defects produced
that height**, all fixed here:

1. **`StanceOf` read "bent over" as STAND**, because "over" was one of its STAND keywords. `CameraHeightText` therefore
   mapped (holder Stand, subject Stand) → "the subject's own eye height" for every bent-over subject. `Stance` now has its
   own **Bent** value (bent / on all fours / doubled over / folded over), checked BEFORE the STAND keywords, and
   (Stand, Bent|Kneel|Sit|Lie) → **Above**, "above the subject — the shot looks down on them".
2. **`PitchFromSightline` missed a leading direction.** Dean's recorded sightline is *"down toward Becky's back"*, which has
   no space before "down", so the pattern missed it, `EffectivePitch` stayed null and the SHOT block said *"Camera aim:
   straight ahead"*. It now also matches a leading "up"/"down" as a whole word.
3. **The pitch was gated on the absence of a `facing` fact.** `EffectivePitch` was
   `ViewpointPitch ?? (Facing is null ? PitchFromSightline(sightline) : null)`, so a recorded *"facing: Becky's back"* — a
   HORIZONTAL target that says nothing about pitch — suppressed the vertical aim entirely. The sightline fallback is now
   unconditional.

And the render-half consequence of (1): the trailing camera statement inherited the SCENE's compiled height, so even a
correct SHOT block would have printed "high angle, viewed from above, eye level perspective". New
`SceneImagePovProjection.CameraHeightKey(projection)` returns the `CameraHeights` value the participant geometry implies
(operator pick first, else the stance pair), and `SceneImagePromptGenerationJobHandler` states it in the camera statement.
Omniscient returns null and never reaches that branch.

**Prompt research (why the wording also changed).** The documented POV cue is structural, not descriptive: the Qwen Image
Edit / Kontext view-prompt guide's working entries are *"change first-person view showing hands"* and *"…showing hands
holding a weapon"* ([Aidec](https://blog.aidec.tw/post/ai-view-prompt)), and the same lesson appears for Nano Banana —
"POV" alone is too vague, the model needs *"first-person view"*, *"hands visible in the foreground/bottom of the frame"*
and an explicit "no face/body" constraint ([NanoCloud](https://nanobananapro.cloud/posts/why-you-cant-generate-pov-shots-with-nano-banana-2)).
Krea's own guide is natural-language and viewpoint-first (*"immense rocket launch exhaust as seen from extremely close up"*,
[krea-ai/krea-2](https://github.com/krea-ai/krea-2/blob/main/docs/prompting.md)), and its expansion prompt asks for
"grounded phrasing for poses, interactions, and spatial layout". So: `CameraHolderBodyStatement` now opens
*"This is a FIRST-PERSON POV photograph"* and states the parts are *"in the NEAR FOREGROUND… entering from the BOTTOM EDGE
of the frame… never out of the subject's own arms"*; `FrameInclusion.ParticipantSentence` leads with `FIRST-PERSON POV:`;
and the compile contract no longer says the holder is *"a SECOND, SEPARATE PERSON behind the subject"* — that sentence is
what made the writer produce "this person standing directly behind Beck" and is replaced with the CAMERA's-own-body
geometry.

| File | Change |
|---|---|
| `SceneImagePovProjection.cs` | `Stance.Bent` + mapping; leading up/down in `PitchFromSightline`; unconditional sightline fallback for `EffectivePitch`; `HeightFor` returns (key, text) so the compile SHOT block and the render camera cannot disagree; NEW public `CameraHeightKey(projection)`; `CameraHolderPartsRule` rewritten to the first-person bottom-edge geometry. |
| `SceneImagePromptGenerationJobHandler.cs` | The participant camera statement now states the derived `Height` as well as the `Angle`. |
| `ImageStep/CameraHolderBodyStatement.cs`, `FrameInclusion.cs` | First-person POV wording, bottom-edge/foreground geometry, "not out of the subject's own arms". |
| `SceneImageCompilerSystemPrompts.cs` | The anchored example no longer contains `[the person's tag]` placeholders — the compiler had echoed them verbatim into a live caption (`b5ea6fb4`) — plus a rule to write each tag as plain text. |
| Tests | `CameraHolderBodyStatementTests` +2: a standing holder over a bent-over subject derives `Above` with "Camera aim: downward" and "high angle, viewed from above"; the operator's own height still wins. Wording assertions updated to the documented cues. |

Suite: **4731 passed / 27 failed** — the 27 are the pre-existing `CharacterLoraCoverage*` / `CharacterLoraCellTemplateSeedTests`
set already recorded in §0.1.23 and unrelated to this path.

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

**As-built deviations from the target schema above** (deliberate, recorded so the diagram is not
mistaken for the delivered record):

- `povCamera` was NOT added as a separate array. The B-166 D11 `viewpointPitch` /
  `viewpointSubjectDistance` fields already carry that data per character and already reach the
  projection, so adding a second home for the same numbers would have violated the one-owner rule
  this item exists to enforce.
- `delta` was NOT added. Continuity deltas are not selected into the image path at all (decision
  1), so a field nothing reads would be dead weight in the enrichment record.
- `conditions` and `objects` were NOT restructured into new groups: `timeOfDay`, `lighting`,
  `environment`, `mood` and `objects` already had exactly one owner each, and the defect was never
  their shape.
- `doing` / `attending` were NOT added as new members. `actionOrObservation` and `sightline` keep
  those facts (one owner), and `ProjectedCharacter` exposes them as world facts where the frame
  needs them. What v4 adds is the structured TARGET (`facing.toward`), which is what the old prose
  heuristics could not decide.

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

- `contacts.from` / `contacts.to` name real cast profile keys; `part`/`targetPart`/`manner` non-empty.
- **`clothing` is affirmative.** A garment the person is NOT wearing must never be recorded, because an image model
  renders whatever the text names ("no tank or cutoffs" puts a tank and cutoffs in the picture). The parser strips
  those negations and keeps the affirmative remainder, and refuses a value that is only a negation (see §0.1.13).
- **A SELF-contact is valid.** `from == to` means one body touching itself (a hand on one's own
  thigh, one's own hands clasped). It is a real fact of the instant and it is the only way a
  first-person frame learns about the camera holder's own body. (As first shipped this was refused
  as "pairs 'p0' with itself", which blocked a legitimate enrichment outright on 2026-10-24 — see
  §0.1.11.)
- **A repeated contact is de-duplicated, not refused.** The same `from|part|to|targetPart` stated
  twice is the same fact; the first occurrence wins. Model redundancy carries no extra meaning and
  must not block a render.
- `facing.toward` names a cast member, an object key, or a declared point.
- `location` contains no cast name (v4 responses; v3 rows are handled by the existing
  name-scrub, not by validation failure).
- `event.line` is a single sentence, no sequential-state markers (reuse the parser's existing
  `SequentialStateMarkers` check).

### A.5 Increments (as built — see §0.1 for the file-level record)

- **D1 — v4 schema (additive) + parser + validator. DONE.** Contract v4, required-in-response new
  fields, optional-in-record so v3 JSON reads unchanged. No behaviour change for existing rows.
- **D2 — Still-brief selection. DONE.** `BuildStill` selects one owner per fact; continuity and the
  duplicate restatements are not selected; the Still profile version moved 2 -> 3 (in the request AND
  in `IsExactStillBrief`, with the guard test updated) so pre-existing production groups stop reusing
  their old-shape brief; `SemanticSnapshotDeDuplicator` deleted from the Still path. Prompt Input rows
  rebuilt: the four duplicate prose rows vanish; `contacts`, `facing`, body anchor and posture became
  editable rows. **Overrides decision (implemented):** a saved draft/override whose element key names a
  leaf the Still brief no longer selects fails fast with a named diagnostic that says where the fact
  now lives (no silent ignore, no key rewriting).
- **D3 — consume the facts (rule 1.a). DONE.** Holder toggle = visibility over `contacts`; a v3 row
  yields null ("not recorded"), and `CameraHolderPartsExtractor` is DELETED (no prose-regex fallback
  path), with the retired `Custom` mode and its text refused by name. Participant-POV geometry reads
  `at`/`facing` when present and states nothing when absent. Compiler input = labelled fact sheet.
- **D4 — evidence gate. PENDING.** Re-enrich one moment to v4, then golden-set diff and pinned-seed
  render A/B. Re-enriching upgrades a moment to v4; v3 moments keep composing under "not recorded"
  semantics indefinitely.

## 4. Part B — compiler / translation fixes (carried from the 2026-10-24 review)

Sit on top of Part A; the prose/settings items ride on D2, the contacts-derived items on D3.
Status column is the as-built state (details in §0.1).

| # | Finding | Fix | Status |
|---|---|---|---|
| P0 | Prompt mixes description + instruction | Caption carries picture facts only; reference tags bound possessively ("the room in `<image2>`"); prohibitions become a compact affirmative render block | partly done: the labelled fact sheet removes description/instruction mixing at the input; the researched prohibition rewrite is deferred (§0 item 3) |
| P0 | Brief JSON handed over raw | Compiler input = labelled prose sections rendered AFTER all JSON mutations (overrides, suppression, blocking replace); operator-edited values quoted as "translate, do not copy" — removes the `Becky: a; b; c` recap at the source | **done** (`StillBriefFactSheet`) |
| P1 | Preprocessor at temp 0.7 / thinking off | Temperature 0.2–0.4 + thinking on are **Model Manager config changes** | **temperature done** (0.3, configured). **Presence penalty 1.5 now HAS a carrier** and is configured: `FunctionModelDefault.PresencePenalty` + repository + Model Manager column + both completion-client DTOs, sent only when declared. **Thinking NOT enabled by operator decision** — the Qwen-VL compiler model is the NSFW-capable one (DeepSeek refuses NSFW) and reports `SupportsThinkingControl = false`; switching is config-only once an NSFW-capable thinking model is qualified (separate research). |
| P1 | Negative framing load-bearing | State what the frame IS; <=2 prohibitions; enforce the rest in the validator | deferred (§0 item 3) |
| P1 | Partial bodies binary in/out | `SubjectVisibility { InFrame, PartiallyVisible, OutOfFrame }` over `contacts`; entry edge + visible extent derived, never asked of the operator | **type + camera-holder decision done** (`SceneSubjectVisibility`, `Projection.CameraHolderVisibility`, contacts-derived parts). In-frame `PartiallyVisible` deferred to D4 evidence (§0 item 2). |
| P2 | Two owners per number | Profile `MaxChars` becomes the single owner; `{{maxChars}}` token substituted per-profile PLUS a migration of profile rows whose text has "800" baked in | **done** (`BudgetToken` + `ApplyBudget` + profile-row migration; `OutputTargetChars` and both aliases deleted) |
| P2 | Validation stops at the caption | Coverage per intent item in intent language (paraphrase passes); lexical absence checks for room nouns / removed items | deferred (§0 item 3) — false failures cost a retry and then a refused render |

## 5. Participant-POV geometry from v4 facts (right-sized)

NOT a new derivation engine — three existing lexical heuristics in `SceneImagePovProjection`
become exact field reads when the v4 fields are present, and "not recorded" (state nothing)
when absent:

| Today (prose string-matching) | v4 read | Status |
|---|---|---|
| `directlyAhead` = `sightline.Contains(character.Name)` | `facing.toward` target equality (name / profile key / camera words) | **done**; the sightline sentence is read only for a v3 record |
| `ClassifyViewDirection` scans position/sightline/action prose for direction words ("toward the camera" / "away") | compare the subject's `facing.toward` against the POV profile key | **done**; a bare direction word claims nothing, so no facing is stated rather than guessed |
| `PitchFromSightline` regex-reads "directed up/down" out of the sightline | recorded `viewpointPitch` (B-166 D11) first; the regex is skipped once `facing` is recorded | **done** |

No bearing math between world anchors is introduced. A fact that is not recorded is not
invented — the projection's existing refusal semantics are unchanged.

## 6. Part C — camera-holder toggle (final shape — as built)

Toggle only. No extra text field.

```
off  -> show what the POV character sees without their body in frame
on   -> parts = contacts from the v4 record   (CameraHolderPartsFromContacts)
        v3 record (no contacts) -> null: "not recorded", state nothing (rule 1.a)
```

`CameraHolderPartsExtractor` (body-part regex + `PossessiveOf` pronoun heuristic) is DELETED, with
its tests. `CameraHolderPartsMode.Custom` and persisted `cameraHolderPartsText` are retired and
refused by name with a diagnostic pointing at `frozenState.contacts`; the composer no longer offers
the option or writes a custom text into drafts or render settings. `Projection.CameraHolderVisibility`
is the one typed answer to "is the holder in the picture", derived from the same contacts.

## 7. Open questions (one remains)

1. **Compiler output form**: free prose + deterministic post-pass, or clause-list -> deterministic
   assembly. **Agent recommendation: clause-list -> assembly.** The implementation deliberately did not
   settle this: the compiler is still asked for free prose, but it is now handed a LABELLED FACT SHEET
   instead of a JSON brief, which was the precondition for either answer. Everything needed to switch to
   clause-list later already exists (the facts are typed), so this is a post-D4 decision rather than a
   blocker.

(Resolved 2026-10-24: holder text-field transition — `Custom` and its text were retired at D3, and a
persisted draft carrying either is refused by name.)
(Resolved 2026-10-24: budget ownership — one owner, `ImageCompilerProfile.MaxChars`, stated to the model
through `{{maxChars}}`.)

## 8. Sequencing and gates

D1 -> D2 -> D3 are **done** (§0.1). Remaining:

- **D4 — evidence gate.** Re-enrich one moment to v4, then: golden set x POVs at fixed seed diffed for
  recap/list shape, imperative tokens, element coverage and reference tag count/order; render A/B at
  pinned seeds for identity and appendage presence (CASE-25 method: outer-ring histogram L1 vs bound
  reference). This gate is what decides the two deferred items in §0 (`PartiallyVisible` for a depicted
  subject, and whether the researched prohibition rewrite is still needed at all).
- **Suite rule.** Full suite green; the 27 pre-existing LoRA-coverage failures reported separately.
  As-built: 4630 passed / 27 failed / 0 skipped, and `dotnet build DreamGenClone.sln` reports 0 errors.

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
