# Debug 040 — Beat Catalogue All-Observer Classification

## Report

- Reported from Scene Image Studio on 2026-09-08.
- Beat Catalogue generation failed with `scene_beat_output_invalid`.
- Error: `Beat Catalogue beat 'b5' must contain at least one active participant.`
- Failed catalogue attempt: `e67a0834-9a91-44e8-9546-1b4b93698ef5`.
- The rejected `b5` output classified Dean, Ken, and Becky as `observer`.

## Analysis

- `SceneBeatCatalogueContract.ParseBeat` correctly enforces the invariant that every Beat has an active participant.
- The authoritative `b5` evidence includes meaningful action and interaction: Ken speaks and touches Becky, Becky responds, and Dean joins the fire conversation and deliberately watches Becky.
- `BuildSystemPrompt` only said that people who "only watch or notice" are observers. It did not define active participation broadly enough for restrained, observational, reactive, or indirect action.
- The model therefore classified all three participants as observers even though the Beat contained meaningful physical, conversational, and reactive participation.
- No fallback or automatic participant promotion is appropriate: involvement is a model-authored semantic classification and the parser must continue failing invalid output explicitly.

## Plan

- Update `SceneBeatCatalogueContract.BuildSystemPrompt` with a behavioral definition of `active` and a strict observer-only definition.
- Bump the catalogue prompt contract version so new attempts cannot reuse stale prompt snapshots.
- Add contract-test assertions protecting the classification guidance.

## Resolution

- Updated `SceneBeatCatalogueContract.BuildSystemPrompt` with explicit behavioral definitions for `active` and `observer` involvement, including restrained, indirect, emotional, and observational participation.
- Added an explicit instruction never to mark every participant observer when the evidence contains meaningful action, dialogue, response, contact, or deliberate reaction.
- Bumped `ContractVersion` from `scene-beat-catalogue-v1` to `scene-beat-catalogue-v2`.
- Added prompt guidance assertions and a b5-shaped multi-participant parse regression test while preserving observer-only rejection.

## Validated

- [x] Focused `SceneBeatCatalogueContractTests`: 11 passed, 0 failed.
- [x] Full solution build: succeeded with 0 errors.
- [ ] Pending fresh catalogue generation in Scene Image Studio to validate the live provider behavior.