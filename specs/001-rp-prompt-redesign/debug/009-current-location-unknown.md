# Debug 009 — Slot 7 Current Location Shows "Unknown"

**Created:** 2026-07-17

## Report

Slot 7 (Current Location) displays "Scene: Unknown" in all prompts. The session's default starting location ("Husband and Wife Trailer — Shared Private Space") appears correctly in Slots 1 and 4, but not in Slot 7.

## Analysis

`CurrentLocationSlot.WriteAsync()` only reads `session.AdaptiveState.CurrentSceneLocation`. When null (opening/new sessions), it shows "Unknown" with no fallback. `SceneAnchorSlot` (Slot 1) and `SceneLocationLockSlot` (Slot 4) already fall back to `context.Scenario.DefaultStartingLocationName` — Slot 7 was missing this fallback, making it inconsistent.

## Plan

Add `DefaultStartingLocationName` fallback in `CurrentLocationSlot.WriteAsync()`. One method, one file.

## Resolution

Changed fallback from hardcoded "Unknown" to `context.Scenario.DefaultStartingLocationName`, then "Unknown" only if both are null.

## Validated

[ ] Pending user confirmation
