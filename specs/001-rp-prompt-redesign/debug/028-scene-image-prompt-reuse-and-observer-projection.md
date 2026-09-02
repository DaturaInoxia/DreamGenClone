# Debug 028: Scene Image Prompt Reuse and Observer Projection

## Report

- Date: 2026-08-21
- Session: `6e836089-0505-4b7b-b7d0-53e1ee81f15b`
- Interaction: `b1766b0e-4d8e-48be-9a48-42d027917c9c`
- Beat analysis: `0e36f3f4-d707-4ccb-8e69-879e040c548d`
- Symptoms:
  - Selected Beat and POV appeared below Generate Prompt.
  - Selecting a previously generated POV did not load its persisted prompt.
  - Dean POV made Becky appear alone without a grounded eye-position camera origin.
  - Omniscient rendering moved Ken from the maintenance-shed exterior into the trailer bedroom and depicted him in boxer shorts.
  - Eye-color wording caused the image model to color more than the iris.

## Analysis

Authoritative references consulted:

- `specs/001-final-writing-instruction/spec.md`
- `specs/001-rp-prompt-redesign/spec.md`
- `.github/instructions/razor-editing.instructions.md`
- `.github/razor-style-reference.md`

Persisted prompt records already contained `BeatAnalysisId`, `BeatSnapshotJson`, `Pov`, and `OutputPrompt`, but the repository exposed only the latest prompt for the whole interaction. `SceneImageStudio` changed `_selectedPov` without replacing `_activePrompt`. The latest Dean render was consequently tagged `Pov=Dean` while referencing the latest Omniscient prompt record.

The selected schema-v3 beat correctly placed Becky and Dean in `trailer bedroom` and Ken at `maintenance shed exterior`, with Ken classified as an observer. Deterministic Omniscient projection included every character as full-detail visible cast but omitted each character's `PhysicalLocation`, collapsing Ken into the active bedroom setting. Ken's source wardrobe was `shirtless, wearing shorts`, which the image model rendered as boxer shorts.

Character POV framing had been made anonymous to prevent camera-holder anatomy, but it also discarded the camera holder's stored position. Visible-character facts containing the camera holder's name were removed wholesale, so Dean-relative interaction facts disappeared from Dean POV.

Visual identity used `Eyes: Blue`, which did not scope the color specifically to the iris.

## Plan

1. Add an exact persisted prompt lookup by session, interaction, current beat analysis, beat ID, and POV.
2. Move Selected Beat and POV above Image Prompt and load the exact saved prompt on selection without regeneration.
3. Use the POV character's stored physical location, position, and sightline as an anonymous eye-position camera origin.
4. Preserve facts involving the camera holder by replacing the holder's name with `the unseen viewpoint`.
5. Keep remote observers out of Omniscient full-detail cast and project them as anonymous, distant, heavily occluded silhouette cues without identity or wardrobe.
6. Change image-only eye wording to `Iris color`.
7. Add focused regression tests and run Scene Image plus full-solution tests.

## Resolution

- Added `GetLatestCompletedPromptAsync` through repository and service boundaries.
- Added a SQLite lookup using the current analysis ID, JSON beat ID, case-insensitive POV, and `Complete` status.
- Moved the Studio selection card above prompt generation.
- Beat selection now clears mismatched prompt state; POV selection loads the exact completed persisted prompt and settings remain associated with that record.
- Polling preserves the active pending prompt instead of replacing it with an unrelated latest interaction prompt.
- Omniscient detailed cast now excludes observers whose physical location differs from the active beat location.
- Remote observers now become anonymous, small, distant, heavily occluded silhouette cues; names, appearance, and wardrobe are omitted.
- Character POV now uses the stored physical location, position, sightline, and viewpoint eye position, with close-range or distant framing derived from location equality.
- Camera-holder references in visible-subject facts are anonymized instead of discarded.
- Image visual identity now emits `Iris color` instead of `Eyes`.

## Validated

- [x] Repository tests: 9 passed, 0 failed.
- [x] Focused projection and formatter tests: 52 passed, 0 failed.
- [x] All Scene Image tests: 106 passed, 0 failed.
- [x] Web project build succeeded.
- [x] Full solution tests: 1,213 passed, 0 failed.
- [x] Full solution build succeeded.
- [ ] Fresh user render confirmation pending.
