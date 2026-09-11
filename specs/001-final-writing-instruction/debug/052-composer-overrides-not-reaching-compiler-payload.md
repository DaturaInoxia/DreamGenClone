# 052 — Composer element removals do not reach the compiler payload (removed content is still rendered)

**Created:** 2026-09-10
**Component:** B-111 Composition Composer → canonical Still prompt generation (LLM pre-processor)

## Report

Session `8bc36efb-b235-485b-8675-b98ad7754e59`, interaction `85da7c1b-cb10-413e-b40d-b445ea1e67b3`,
production group `c86ff5fc-7e11-40f3-a935-41ac0dab3fb2`, composition page
`/roleplay/studio/{sessionId}/{interactionId}/production/{productionGroupId}/composition`.

User removed every prompt-input element **except** Location, Environment, Time of day and Lighting
(in order to get a location-only payload for possible reference), then generated the prompt. The
generated prompt still contained the full moment action and the removed characters:

> "Tight low-angle close shot from beside the sun-warmed stone: a woman with long dark hair and fair
> flushed skin lies back on the flat rock in a quiet pine clearing, her spine arched hard off the
> stone, knees spread wide, thighs clamped around her own wrist, two fingers buried inside herself
> with the heel of her palm pressed hard against her clit … A discarded blue swimsuit lies crumpled
> on the rock beside her hip. Low afternoon sunlight slants through the pines …"

Last prompt record: `09caea87-9991-42c4-86b3-a312ed31de24` (2026-09-10T12:47:07Z), brief
`be58e0e4-6678-4842-bb46-d3e66e85d516`.

## Analysis

Traced the exact compiler prompt via the `SceneImagePromptProjected` debug event
(`1f4a2e2268984f04aeef9f7ecf515c4b`, 2026-09-10T12:46:45Z, 14 352 chars). The user's persisted
overrides were:

```json
{"Fields":[{"ElementKey":"scene.mood","Removed":true},
           {"ElementKey":"moment.visibleAction","Removed":true},
           {"ElementKey":"frozenState.continuityState","Removed":true}],
 "RemovedCharacters":["f58f959a-…(Becky)","faee1ec0-…(Dean)"]}
```

`ScenePromptOverridesApplier.Apply` **did** work as designed on the semantic snapshot — the patched
`CANONICAL STILL BRIEF` section has no `mood`, no `continuityState`, no `moment.visibleAction`, and
`frozenState.characters = []`. The failure is that the removed content survives through **four other
channels that the applier never touches**, and the inspector never exposes them.

### RC-1 — The provider-request snapshot is a second, unpatched copy of the whole payload

`SdxlSceneImagePromptBuilder.BuildCanonicalUserPrompt` emits **both** snapshots:

```
CANONICAL STILL BRIEF (immutable; this is the complete semantic source):
<brief.SemanticInputSnapshotJson>            <- patched by overrides
CANONICAL PROVIDER REQUEST SNAPSHOT (immutable):
<brief.ProviderRequestSnapshotJson>          <- NOT patched
```

`ProviderRequestSnapshotJson` = `{"contractVersion":"canonical-request-v1","mediaKind":"StillImage",
"semanticInput":{…}}` where `semanticInput` is a byte-for-byte duplicate of the *original* semantic
snapshot. Evidence from the extracted prompt: the only `mood` occurrence in the whole prompt is
inside this block (original text `Raw, shameless, open release under Dean's unbroken gaze…`), and the
block still carries `moment.visibleAction`, `frozenState.continuityState`, and both removed
characters with `position` / `actionOrObservation` / `clothing` / `visibleCharacterNames`.
`CanonicalCharacterAppearance` also has no effect here. **This is the dominant cause of the reported
output** — the model is handed back everything the user removed.

### RC-2 — Whole-character removal leaves `participantSummary` intact

`ScenePromptOverridesApplier.RemoveCharacters` removes entries from `frozenState.characters`,
`visibleCharacterNames`, and `moment.participantSummary` by matching `characterId` / `name` /
`profileKey`. `participantSummary` entries carry **only** `profileKey` (`p0`, `p1`) and the UI sends
character **ids**, so nothing matches and the summary survives:
`[{"profileKey":"p0","involvement":"active"},{"profileKey":"p1","involvement":"observer"}]`.
The payload therefore still tells the model "two participants, one active, one observing" even
though both characters were removed.

### RC-3 — The inspector exposes the wrong key and omits the real content carriers

Rows are built in `CompositionComposer.BuildPromptRows`. Against the **real** snapshot shape
(`lineage`, `moment`, `frozenState`, `continuity`, `typedReferences`, `videoKeyState`):

* `moment.visualDescription` **does not exist** in the semantic snapshot (the real element is
  `frozenState.visualDescription`), so that row is always empty and its override is a no-op, while
  the actual full-prose description is unexposed and always passes through.
* Not exposed at all: `moment.frozenState` (the blocking line — the same text as
  `frozenState.continuityState`), `moment.compositionRationale`
  (`"A tight low shot from Dean's side centers Becky's arched torso, open throat, and buried hand…"`
  — the direct source of the "Tight low-angle close shot …" framing in the output),
  `moment.participantSummary`, `moment.productionRoles`, `frozenState.visualDescription`,
  `continuity.start|end` (`location`, `characterStates`, `wardrobeStates`, `objectStates`,
  `lighting`, `stateSummary`), `typedReferences` (`identity-p0`, `wardrobe-p0`, `identity-p1`, …),
  `videoKeyState`.
* Exposed and correct: `scene.location`, `scene.environment`, `scene.timeOfDay`, `scene.lighting`,
  `scene.mood`, `scene.objects`, `frozenState.continuityState`, and the per-character rows.

### RC-4 — Nothing tells the model that removals are authoritative

Both blocks are labelled "immutable; this is the complete semantic source". A narrowed payload with
those labels invites the model to re-expand whatever it can still see (and RC-1/RC-3 give it plenty).
There is no "removed by user — do not reintroduce" signal in the prompt.

### Net effect

Five sources of the same instant exist in the LLM prompt (`moment.frozenState`,
`moment.visibleAction`, `frozenState.visualDescription`, `frozenState.continuityState`, per-character
`position` / `actionOrObservation`), plus the unpatched provider snapshot. Removing one or two of
them cannot produce a location-only payload.

## Plan (proposed — awaiting confirmation)

1. **RC-1 (single semantic source).** Stop emitting `ProviderRequestSnapshotJson` in the canonical
   LLM user prompt (`SdxlSceneImagePromptBuilder.BuildCanonicalUserPrompt`,
   `PonySceneImagePromptBuilder` equivalent). It is a duplicate payload; the DB copy stays for
   provenance. Alternative if the second block must stay: patch its `semanticInput` in lockstep with
   the semantic snapshot.
2. **RC-2 (complete character removal).** Build a `profileKey → characterId/name` map from the
   original `frozenState.characters` *before* removal and use it to remove the matching
   `participantSummary` entries, `continuity.start|end.characterStates` / `wardrobeStates`, and
   `typedReferences` entries whose `subjectKey` matches. Also strip the returned
   `RemovedCharacters` set back to profile keys for UI matching.
3. **RC-3 (inspector matches the real payload).** Replace `moment.visualDescription` with
   `frozenState.visualDescription`; add `moment.compositionRationale`, `moment.frozenState`,
   `continuity.start.stateSummary` (or the whole `continuity` block) as removable elements; drop
   rows whose key does not exist in the payload.
4. **RC-4 (authority signal).** Emit an explicit line in the compiler user prompt when overrides are
   present: the listed elements are user-removed and must not be reintroduced, inferred, or
   re-derived; render only the remaining elements.
5. Update/extend tests: `ScenePromptOverridesApplierTests` (participant/continuity/typed-reference
   removal), `SceneImageStudioUiContractTests` (new element keys), plus a builder test asserting the
   provider-request duplicate is not sent.

**Blast radius:** LLM prompt text for the canonical Still path only (SDXL + Pony builders), the
overrides applier, and the composer's inspector rows. No change to the deterministic compilers, the
brief contract, DB schema, or the provider request records. Tests in
`DreamGenClone.Tests/RolePlay/**`.

## Resolution

Implemented 2026-09-10 (user said "go").

1. **Single semantic source** — `SdxlSceneImagePromptBuilder.BuildCanonicalUserPrompt` and
   `PonySceneImagePromptBuilder.BuildCanonicalUserPrompt` no longer emit
   `ProviderRequestSnapshotJson`; the brief is labelled `CANONICAL STILL BRIEF (the complete semantic
   source for this request)`. The DB record keeps the provider snapshot for provenance.
2. **Complete character removal** — `ScenePromptOverridesApplier.RemoveCharacters` now resolves the
   removed cast entries to their `profileKey`s and names *before* mutating the cast, then removes the
   matching `moment.participantSummary`, `continuity.start|end.characterStates`, `continuity.start|end.wardrobeStates`,
   `typedReferences` (by `subjectKey`/`profileKey`/`characterId`/`name`/`key`) and
   `visibleCharacterNames`.
3. **Inspector matches the real payload** — element scopes are now generic (`scene.*`, `moment.*`,
   `frozenState.*`, `continuity.<block>.*`, `character:{key}.*`); an unrecognized scope throws instead
   of being ignored. Composer rows: `moment.visualDescription` (a key that never existed) replaced by
   `frozenState.visualDescription`, plus new rows for `moment.temporalAnchor`, `moment.frozenState`,
   `moment.compositionRationale`, `continuity.start.stateSummary`, `continuity.end.stateSummary`.
   Group headers now show the character name instead of the raw id.
4. **Authority signal** — new `ScenePromptRemovalNotice` emits a `USER REMOVALS — AUTHORITATIVE`
   block (removed elements + removed character names, with an explicit do-not-re-derive instruction)
   for both builders, and both canonical system prompts gained a matching rule.
5. **Tests** — `ScenePromptOverridesApplierTests` (+4): id-addressed removal strips profile-keyed
   participants/continuity/typed references; unknown element key fails fast; `continuity.<block>.<prop>`
   removal; `frozenState.visualDescription` + `moment.compositionRationale` removal.
   `SdxlSceneImagePromptBuilderTests` (+2, -1): provider snapshot absent from the user prompt;
   removal notice present with removals and absent without.

**Verification:** `DreamGenClone.Tests` — **1858 passed, 0 failed** (`artifacts/testout-composer8`);
Web build 0 errors; Web rebuilt into `DreamGenClone.Web/bin/Debug/net9.0`.

**Regression basis for the reported case** (overrides of record `09caea87`: remove `scene.mood`,
`moment.visibleAction`, `frozenState.continuityState`, both characters): the semantic snapshot loses
those keys and the cast; the participants, continuity state and typed references no longer name the
removed people; the duplicate provider snapshot no longer reaches the model; and the notice tells the
pre-processor the removals are deliberate. The still-present prose carriers
(`frozenState.visualDescription`, `moment.frozenState`, `moment.compositionRationale`) are now
removable rows in the inspector — they are not auto-stripped by a character removal because they are
prose, not per-character records.

## Validated

- [ ] pending — awaiting a user regeneration from the Composition Composer.
- Note 2026-09-10 09:02: the running app instance (PID 2340, started 09:01:49) has
  `DreamGenClone.Web/data/dreamgenclone.db` open (Production env), not `dreamgenclone.dev.db`;
  it must be restarted with `ASPNETCORE_ENVIRONMENT=Development` to see session `8bc36efb`.
