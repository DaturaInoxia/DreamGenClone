# 027 - Scene image POV combines locations and renders camera holder

**Report**

- Date: 2026-08-21
- Session: `6e836089-0505-4b7b-b7d0-53e1ee81f15b`
- Interaction: `b1766b0e-4d8e-48be-9a48-42d027917c9c`
- Beat analysis: `9927d629-2790-459a-a28e-9b52e9ffa462`
- Prompt record: `d67a98a4-6dd4-4dc3-b07e-e55320357f5c`
- POV: Dean
- Symptom: Seedream placed Becky outside the trailer and rendered Dean's head between her legs even though the prompt requested a first-person view with Dean outside the frame.

**Analysis**

- The deterministic prompt faithfully projected a malformed schema-v2 beat whose primary location combined two spaces: `trailer bedroom and outside maintenance shed`. Its environment also mixed bedroom and exterior/shed objects.
- Schema-v2 required non-empty location strings but did not structurally separate each character's physical location from their position within that location. The parser therefore could not enforce one active-event location.
- `SceneImagePovFramer` converted Dean's complete narrative position (`kneeling between Becky's thighs, body poised above her`) into the camera origin. That body-pose wording made Dean's anatomy compositionally salient.
- `ImageGenerationClient` sends only one positive `prompt` field. The `EXCLUDE` section was not a provider-native negative prompt, so repeating camera-holder/head/body language could reinforce those concepts instead of suppressing them.
- This was a prompt-contract defect, not random renderer behavior or missing identity data.

**Plan**

1. Introduce schema v3 with a required `physicalLocation` for every beat character.
2. Require one atomic beat location and require every active character's `physicalLocation` to match it exactly; remote observers may retain different locations.
3. Restrict beat environment instructions to the active-event space.
4. Make character POV framing anonymous and affirmative: active location, sightline, exact visible-person count, and visible names only.
5. Omit camera-holder name, location, position, pose, anatomy, and the pseudo-negative `EXCLUDE` section from character POV prompts.
6. Add focused regressions and run Scene Image plus full-solution tests.

**Resolution**

- `SceneImageBeat` schema is now version 3. `SceneImageBeatCharacter` includes required `PhysicalLocation`.
- Beat analysis asks for exactly one active-event location, active-character location equality, remote-observer locations separately, and environment details only from the active space.
- Parsing fails explicitly when an active character does not match the primary location or when the primary location contains compound-space separators.
- Character POV framing no longer emits the camera holder's identity or body pose. It emits an anonymous strict first-person camera, active setting, sightline, and exact visible-person count.
- Character POV prompts omit the `EXCLUDE` section because the configured image endpoint has no negative-prompt channel.
- Continuity wording is affirmative and keeps each depicted person distinct with natural anatomy.
- Existing schema-v2 beats are unsupported and must be regenerated; stored prompts and images remain viewable.

**Validated**

- [x] Persisted beat and prompt inspected through the canonical read-only DB tooling.
- [x] Focused schema and projection tests: 50 passed, 0 failed.
- [x] All Scene Image tests: 103 passed, 0 failed.
- [x] Full solution tests: 1209 passed, 0 failed.
- [x] No editor diagnostics in touched implementation or test files.
- [ ] Fresh schema-v3 beat and Dean POV render confirm Becky remains inside the trailer and no camera-holder anatomy is rendered.
