# 084 — Beat production spoken `speakerKey` must be a participant profile key

**Report**

- Session: `8bc36efb-b235-485b-8675-b98ad7754e59`
- Studio interaction: `5d5ce679-94c2-46d2-9eee-d2bf50985193`
- Catalogue: `b58aba18-c370-40ff-9cae-c13a6f85ba22`
- Plan: `653e022f-24fe-49af-a9e1-9483af832dea`, beat `b2` "Dean Finishes on Becky's Face"
- Attempt: `933dcc49-f40c-45f0-bf36-78823f260e72` (`scene-beat-production-v8`, `deepseek-flash` / DeepSeek)
- Error: `scene_beat_production_output_invalid` — "Unknown Beat Production profile key 'dean'."
- Timestamp: 2026-10-26 23:43 local (`2026-10-27T03:43Z`), structure/spoken/continuity all completed, failure at parse validation.

**Analysis**

The plan snapshot contained profiles `p0` = Becky and `p1` = Dean, and every structure-pass
participant reference used those keys (`actionArc`, `characterStates`, `wardrobeStates`,
`typedReferences` built by the assembler). The spoken pass instead emitted
`"speakerKey": "dean"` for the one dialogue cue.

The response schema still exposed several participant references as unrestricted strings even
after `014` constrained `actionArc`. `SpokenCue.speakerKey`, `DialogueCue.speakerKey`,
`DialogueCue.addresseeKeys`, `Performance.speakerKey`, `SoundCue.subjectKey`,
`Continuity.characterStates`/`wardrobeStates` keys, and `TypedReference.subjectKey` had no
schema vocabulary, and only the continuity prompt stated the profile-key rule.

The assembler copied the spoken cue's `speakerKey` into the final `dialogue[].speakerKey` and
also synthesized the `voice-dean` typed reference from it, so a single name in the spoken pass
produced both offending values and the parser failed on the dialogue cue first. The persisted
raw response confirms only those two fields contained `dean`; every other key was `p0`/`p1`.

**Plan**

Constrain every participant-reference field in every generated response schema to the selected
snapshot's profile keys, and state the rule explicitly in the shared and spoken prompts. Keep
the strict parser as the final validator; no name-to-key fallback is appropriate (same decision
as `014`).

**Resolution**

- `SceneBeatProductionContract` now derives participant enums from the snapshot profile keys for
  `SpokenCue`, `DialogueCue`, `Performance`, `SoundCue`, `Continuity`, `TypedReference`, and
  `ActionStep`; `addresseeKeys` is a unique enum array; `characterStates`/`wardrobeStates` use
  enum keys while `objectStates` keys remain free-form.
- `BuildSpokenPass`, `BuildSoundscapePass`, and `BuildContinuityPass` pass
  `snapshot.Profiles` keys into their schemas; `CreateResponseSchema` normalizes and requires a
  non-empty key set.
- The shared system prompt and `SpokenSystemPrompt` now name every participant-reference field
  and require profile keys instead of character names.
- Contract tests assert the enums for the combined schema and each pass schema; a parser test
  asserts a spoken-style `speakerKey` name is still rejected.

**Validated**

- [x] Focused Beat Production test set passed: 48 passed, 0 failed on 2026-10-27.
- [x] New regression tests passed: 3 passed, 0 failed.
- [ ] Live re-run of the affected beat not yet done.
