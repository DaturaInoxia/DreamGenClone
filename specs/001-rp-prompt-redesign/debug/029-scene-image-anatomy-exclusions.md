# Debug 029 - Scene image anatomy exclusions missing from character POV

**Report**

- Date: 2026-08-22
- Session: `6e836089-0505-4b7b-b7d0-53e1ee81f15b`
- Symptom: Character-POV image prompts no longer included explicit exclusions for extra body parts and malformed anatomy.

**Analysis**

- `SceneImagePromptPreprocessor.BuildDeterministicBeatPrompt` emitted the `EXCLUDE` section only for Omniscient POV.
- Debug 027 removed the section from character POV because its old text could make an off-camera holder compositionally salient when combined with camera-holder anatomy and pose language.
- Current character-POV projection already omits the camera holder's name and anatomy. Generic anatomy exclusions therefore do not reintroduce that defect.
- Anatomy defects apply to every render, including single-subject and character-POV images, so conditioning the block on Omniscient POV left those renders unprotected.
- Consulted the final-writing and RP prompt redesign debug instructions and debug record 027.

**Plan**

1. Emit a generic `EXCLUDE` section for every POV.
2. Cover extra and missing body parts, limbs, hands, fingers, heads, malformed anatomy, merged bodies, duplicates, identity/wardrobe exchange, incorrect hand ownership, text, and watermark.
3. Keep the section free of camera-holder names, poses, locations, and anatomy.
4. Update focused character-POV regression coverage.
5. Run focused Scene Image prompt tests, all Scene Image tests, and the full test suite.

**Resolution**

- `SceneImagePromptPreprocessor` now emits the same generic anatomy and artifact exclusions for Omniscient and character POV prompts.
- The exclusion text does not name or describe the off-camera viewpoint holder.
- `SceneImagePromptPreprocessorTests` now verifies that a character-POV prompt contains the anatomy exclusions while still omitting camera-holder identity.

**Validated**

- [x] Focused `SceneImagePromptPreprocessorTests`: 35 passed, 0 failed.
- [x] All Scene Image tests: 106 passed, 0 failed.
- [x] Full test suite: 1213 passed, 0 failed.
- [ ] Fresh character-POV render confirms no extra body parts and no camera-holder anatomy.
