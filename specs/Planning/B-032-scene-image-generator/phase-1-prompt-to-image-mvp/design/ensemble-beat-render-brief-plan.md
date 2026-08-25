# Ensemble Beat Extraction and Render Brief Plan

**Date:** 2026-08-21  
**Status:** Approved for implementation  
**Scope:** Scene Image Studio beat extraction, POV selection, and preprocessor input

## Purpose

The beat analysis is the Studio's primary narrative interpretation stage. It converts one authoritative role-play turn into persisted, image-ready visual events. The image-prompt preprocessor must not rediscover the scene from raw prose; it receives a complete selected-beat render brief and converts that brief into one straightforward provider-ready image prompt.

## Authoritative Inputs

- One `RolePlayV2Turn` is one scene timeline and may contain one or many visual beats.
- The turn's `Narrative` interaction is the canonical synthesis of chronology, shared action, spatial relationships, and scene progression.
- Character interactions enrich that synthesis with concurrent actions, reactions, perceptions, knowledge, and sightlines.
- A change of source interaction or viewpoint is not a beat boundary.
- Missing Narrative is an explicit beat-analysis failure. This workflow does not guess a synthesis from one character interaction.

## Generic Beat Semantics

A beat is one temporally coherent still-image event. It may contain:

- one active character;
- two or more mutually active characters;
- zero or more observers;
- observers in adjacent or remote locations with distinct sightlines;
- a clothing, location, arrangement, action, or time state different from another beat in the same turn.

A new beat is created only when the visually depictable state materially changes. Viewpoint repetition across character interactions must be merged into the same beat.

## Persisted Beat Contract

`SceneImageBeatAnalysisRecord.BeatsJson` remains the turn-level source of truth. It uses schema version 2 and stores a list of rich beats:

```json
{
  "schemaVersion": 2,
  "beatId": "b1",
  "order": 1,
  "label": "short selector label",
  "visualDescription": "multi-sentence image-ready description of one frozen moment",
  "location": "primary event location",
  "timeOfDay": "supported time",
  "lighting": "supported visible lighting",
  "environment": "supported spatial and environmental context",
  "mood": "visually depictable atmosphere",
  "interactionIds": ["supporting interaction ids"],
  "characters": [
    {
      "name": "character name",
      "profileId": "profile id or null",
      "involvement": "active or observer",
      "position": "physical position",
      "actionOrObservation": "depictable action, reaction, or observation",
      "sightline": "view geometry or not applicable",
      "visibleCharacterNames": ["characters visible from this position"],
      "clothing": "supported clothing or not established"
    }
  ]
}
```

Rules:

- Every beat has at least one `active` character; observers are optional.
- A character appears once per beat.
- `visibleCharacterNames` may reference only characters associated with the beat.
- Visibility is directional; it never implies reciprocal awareness.
- Clothing uses turn evidence first, then supplied profile clothing, then `not established`.
- Unsupported garments, positions, actions, visibility, lighting, or awareness are forbidden.
- Every beat includes all turn interaction IDs that materially support it, including Narrative.

## POV Contract

The available POVs are:

- the reserved `Omniscient` option; and
- every character associated with the selected beat.

`Omniscient` uses the complete Narrative-derived spatial model. It does not require every associated character to appear in frame.

A character POV uses that character's persisted position, sightline, and visible-character set. The POV character is not automatically visible. An observer POV may place the camera behind an obstruction, through a doorway, across a window, or at another supported vantage.

## Persistence and Variant Generation

No schema migration is required:

1. `SceneImageBeatAnalysisRecord.BeatsJson` stores the complete versioned beat analysis once per turn.
2. Each `SceneImagePromptRecord` stores `BeatAnalysisId`, the complete selected `BeatSnapshotJson`, and `Pov` (`Omniscient` or canonical character name).
3. Each prompt record also has an exact `SceneImagePromptSent` audit containing the deterministic render brief sent to the preprocessor.
4. Each `SceneImageRecord` stores `PromptRecordId`, `BeatId`, `Pov`, exact `PromptSnapshot`, and settings.
5. Every beat/POV generation creates a new prompt and image record. Selecting another POV never mutates a prior variant.

Existing schema-version-1 beat analyses are not interpreted with guessed values. Studio requires explicit regeneration before they can create new prompts.

## Deterministic Render Brief

After the user selects a beat and POV, application code builds a complete long-form brief containing:

- frozen visual moment;
- location, time, lighting, environment, and mood;
- active characters with positions, actions, identity, and clothing;
- observers with positions, observations, sightlines, visibility, identity, and clothing;
- selected POV type and concrete camera position;
- visible subjects and off-camera/occluded characters;
- image style, size, aspect, and content policy.

The brief is authoritative. The preprocessor may improve image-model phrasing and composition, but may not add, remove, or reinterpret story facts.

## Preprocessor Responsibility

The preprocessor converts one render brief into one direct image prompt. It must:

- describe one still image;
- preserve identities, clothing, positions, actions, visibility, and selected POV;
- convert abstract prose into concrete visual language;
- omit off-camera people from the depicted subject list while preserving their relevance where composition requires it;
- avoid inventing garments, anatomy, actions, locations, awareness, or camera geometry.

It must not perform beat discovery, chronology reconstruction, participant selection, or POV inference.

## Implementation Steps

1. Extend `SceneImageBeat` and `SceneImageBeatCharacter` with the versioned rich visual contract.
2. Rewrite `SceneImageBeatAnalysisService.BuildMessages` around Narrative authority, synchronized evidence, generic active/observer roles, material visual transitions, and strict no-invention rules.
3. Extend strict parsing and reject legacy/incomplete output rather than filling defaults.
4. Change `SceneImagePovFramer` to consume the selected beat and selected character's persisted viewing geometry.
5. Build the authoritative long-form render brief in `SceneImagePromptGenerationJobHandler` and append it to the preprocessor request.
6. Update Studio cards to expose involvement, action/observation, position, and sightline, and require regeneration for legacy analyses.
7. Add regression coverage for solo action, mutual action, observers, multiple sightlines, duplicate perspectives, material transitions, unsupported details, omniscient, character POV, persistence snapshots, and legacy rejection.
8. Validate focused scene-image tests, RolePlay tests, full solution build, and a live regeneration of the cited turn.

## Acceptance Criteria

- Parallel character accounts of one Narrative event produce one shared beat.
- Mutually active characters can share a beat without a required singular driver.
- Beats can contain no observers.
- Observers retain independent positions, sightlines, and visibility.
- Narrative defines chronology and spatial truth; character interactions enrich it.
- Selecting any beat with any associated character POV or Omniscient creates an independent persisted variant.
- The preprocessor receives everything needed to create a specific image prompt without rereading or reinterpreting the turn.
- Legacy analyses fail explicitly and request regeneration.
