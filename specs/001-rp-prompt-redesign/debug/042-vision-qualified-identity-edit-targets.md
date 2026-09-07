# 042 - Vision-qualified identity targets for multi-person image edits

**Date:** 2026-09-07
**Area:** Scene Image Editor / Qwen Image Edit 2511 vision compiler
**Related:** 041 (Qwen identity edit does not transfer the approved face identity)

## Report

The scene image edit form could compile a vision-grounded prompt, but its reference UI exposed
only one generic Identity row. In a scene with multiple people, that did not let the user bind an
approved face reference to the correct visible person, such as Becky on image-left and Ken on
image-right. The compiled prompt was editable, but the identity references did not carry the
compiler's target location into the execution payload.

## Analysis

The vision compiler already returns a target key, visible locator, and normalized region. The
execution handler already supports multiple ordered native references. The missing boundary was
the editor UI and request construction between those two contracts: target-specific reference
selection was absent, and the generic identity row could not express which person a reference was
for. Qwen workflow/settings changes are not required.

## Plan

1. Require the vision compiler to use distinct image-relative location-qualified target keys and
   locators for multiple visible people, and repeat that locator in the executable prompt.
2. Show one approved face-reference selector per ready compiler target, including its locator and
   region.
3. Submit those selections as ordered Identity bindings with the target key, locator, and region
   in the binding snapshot.
4. Preserve the existing editable compiled prompt and accepted prompt-revision flow.

## Resolution

- Updated `QwenSceneImageEditPromptCompiler` system instructions for image-relative multi-person
  target qualification.
- Replaced the ambiguous generic Identity row in `SceneImageEditor` with target-specific identity
  rows generated from the ready vision result.
- Each target binding defaults to `NativeMultiReference` and persists its target key, visible
  locator, and normalized region in `BindingSnapshotJson`.
- Run execution combines non-identity reference applications with the target-specific identity
  applications; the user-edited compiled prompt is still appended as a prompt revision before
  dispatch.
- Added compiler regression assertions for location-qualified target language.

## Validated

- [x] Web project build succeeded: `DreamGenClone.Web/DreamGenClone.csproj`.
- [x] Focused compiler tests passed: 24 passed, 0 failed.
- [x] Editor/compiler diagnostics report no errors.
- [ ] Live multi-person edit pending: select Becky for the left target and Ken for the right target,
  review/edit the compiled prompt, then run the edit.
