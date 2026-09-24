# 068 — Moment enrichment `scene_moment_enrichment_output_invalid` (SoundEventAnchor impossible in the image tier)

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59` · Interaction `6946a8a6-092c-4336-87d8-bedcaaa9749a`
- **Error**: `scene_moment_enrichment_output_invalid` — "Moment enrichment references unknown sound cue keys: screen-door-sigh."
- **Failing job**: `9fa8e710-7d91-402c-8ef9-d35e495395e9` (`SceneMomentEnrichment`, Lane `TextAnalysis`)
- **Failing attempt**: `2bdae43a-8bfb-4a04-8d74-be8c19fd26c1`
- **Enrichment record**: `bb94fb16-fdb1-4040-9dde-831ba2e15ef7` (beat `b1`, moment `m2`, plan `ad22bb03` v4, moment set `0e786249`)
- **Date**: 2026-09-23 21:56–21:57 local (2026-09-24 01:56Z)
- **Follows**: debug `067` (this failure only became reachable once beat production stopped failing on model identity)

## Report

Moment enrichment failed strict validation. The attempt showed `FinishReason=stop` and a well-formed 2048-character
response — the model completed normally and its output was rejected. `ValidationDetailsJson`:
`{"message":"Moment enrichment references unknown sound cue keys: screen-door-sigh."}`

The model returned `instantaneousSoundCueKeys: ["screen-door-sigh"]` for moment `m2`.

## Analysis

### The three-way contradiction

**The plan contains no instantaneous sound events.** Image tier sets
`SceneBeatProductionContract.ProviderPassCount = 3` (soundscape pass removed), and
`SceneBeatProductionAssembler.BuildDefaultSoundscape` emits exactly one cue — authored silence:

```json
{ "id": "ad22bb03-…:ambience", "kind": 1, "eventKey": null,
  "description": "Authored silence", "intensityEnvelope": "Silence" }
```

Every complete plan in the DB has exactly **1** sound cue. There are no instantaneous cues, and no key named
`screen-door-sigh`.

**Moment discovery still mandates the role.** `SceneMomentDiscoveryContract.SystemPrompt` said: identify Moments that
satisfy "…every requested video key-state role, **and sound key-state coverage**. **Assign the SoundEventAnchor
production role to at least one Moment where a distinct instantaneous sound event occurs** (for example a door creak,
a glass setting on the rail, or water running)…". The prompt supplied the exact examples the model then used. Discovery
complied: `m2` = `["StillCandidate","SoundEventAnchor"]`, label *"Screen door sigh"*, rationale *"supplies the key sound
event — the screen door sigh"*.

**Enrichment is then required to reference a cue that cannot exist.** `SceneMomentEnrichmentParser:103-114` requires
every referenced cue key to exist in the plan, and requires a `SoundEventAnchor` moment to list at least one cue.

### No valid output exists

| Model behaviour | Result |
|---|---|
| Invent a plausible key | `references unknown sound cue keys` — **what happened** |
| Return `[]` | `A SoundEventAnchor Moment requires at least one instantaneous sound cue` |
| Reference the ambience cue | passes validation but semantically wrong — authored silence is not an instantaneous event |

A guaranteed failure for any moment carrying `SoundEventAnchor`. This is a contract defect, not model misbehaviour.

The requirement was also not covered by tests: `SceneBeatPipelineEndToEndTests` rewrites
`instantaneousSoundCueKeys: ["s1"]` → `[]` to make the e2e pass, and its discovery fixture never emits
`SoundEventAnchor`. Production had no equivalent workaround.

### Historical context

Debug `050` and `051` record the soundscape drop being **reverted twice** because moment discovery/enrichment reference
`SoundEventAnchor` sound events. The image-tier decision then removed the soundscape pass permanently, but left the
discovery requirement behind — this record is that leftover finally surfacing.

`RequiredMomentRoles` is not a source: `SceneBeatProductionAssembler` assembles `['start','end']` deterministically, and
`SceneMomentDiscoveryParser.ResolveRequiredRoles` maps only start/end/internal.

## Plan

Remove the role from the producer, keep the consumer intact:

1. `SceneMomentDiscoveryContract.cs` — drop "and sound key-state coverage" from the objective; delete the
   `SoundEventAnchor` mandate sentence; remove `"SoundEventAnchor"` from the `productionRoles` enum.
2. Leave `SceneMomentEnrichmentContract` and `SceneMomentEnrichmentParser` **unchanged** — already correct and
   tier-agnostic; they remain the hook for a future audio/AVN tier.

## Resolution

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/SceneMomentDiscoveryContract.cs` | objective no longer demands sound coverage; mandate sentence replaced with "This Plan tier carries no instantaneous sound events, so no Moment carries a sound-event role."; `productionRoles` enum reduced to `StillCandidate`, `VideoStart`, `VideoEnd`, `VideoInternalKeyframe` |

Verified that both edits reach the provider: in `JsonObject` mode `BuildJsonObjectSystemMessage` **prepends** the
contract system prompt and appends the raw schema text, so the reduced enum is visible to the model.

Blast radius confirmed clean: `SceneMomentProductionRole.SoundEventAnchor` remains in
`DreamGenClone.Domain/RolePlay/SceneMomentSet.cs`; the enrichment parser's anchor branches become unreachable rather
than removed; no code requires a `SoundEventAnchor` moment to exist; `SceneMomentDiscoveryContractTests` asserts neither
the role enum nor the sound sentence.

Tests: solution build 0 errors; `~SceneMoment|~SceneBeat` 136/136; wider
`~SceneMoment|~SceneBeat|~MultimodalMediaCompiler|~StoryPresentationImport|~StructuredText` 168/168.

## Residual risk (not fixed — needs a decision)

`StructuredOutputMode` for the DeepSeek analyzer row is `JsonObject`, **not** `StrictJsonSchema`, so the provider does
not enforce the enum — the schema is only included as text in the system message. And
`SceneMomentDiscoveryParser` does **not** validate `ProductionRolesJson` against the closed role set. A model that
emits `SoundEventAnchor` anyway would be persisted and would reproduce this exact failure.

Recommended hardening (one small change, not made): validate `productionRoles` against the allowed set in
`SceneMomentDiscoveryParser` and fail fast with a precise message, instead of persisting an unsatisfiable moment.

## Validated

- [ ] Pending — re-run moment discovery for beat `b1`, then re-run enrichment and confirm it completes.
- [ ] Note: this fix prevents recurrence but does **not** repair already-persisted moment sets. Moment set
      `0e786249` still carries the saturated `m2` role and needs discovery re-run.
