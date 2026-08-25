# Phase 3 - Location And Multi-POV

**Status:** Planned
**Epic:** B-032 Scene Image Generator

## Goal

Keep a detailed location, important object placement, and one frozen beat consistent while rendering multiple camera viewpoints.

## Delivery

- Add persisted `LocationVisualProfile` records for reusable scenario locations.
- Store approved location references, canonical objects, spatial anchors, lighting variants, layout/depth references, provenance, and checksums.
- Introduce the camera-independent `SceneVisualPlan` as the source of truth for cast, wardrobe, blocking, relationships, objects, lighting, mood, and content boundary.
- Add `SceneShotPlan` records compiled from one frozen visual plan.
- Generate camera-specific pose, depth, edge/segmentation, and character-region mask controls.
- Derive omniscient and character POV shots by changing the camera/framing, not by reinterpreting the beat.
- Evaluate whether environment LoRAs are useful after reference/location conditioning is measured.

## Exit Gate

Multiple approved POV shots preserve the same cast assignments, wardrobe, required relationships, object anchors, and location identity unless the visual plan is explicitly versioned.
