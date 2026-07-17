# Debug 010 — Slot 15 Intensity & Pacing Empty

**Created:** 2026-07-17

## Report

Slot 15 (Intensity & Pacing) produces an empty header with no content in all 3 prompt variants. The spec mandates this slot (FR-021, never trimmed) but no intensity profile data flows into the prompt builder context.

## Analysis

`BuildPromptViaBuilderAsync` creates `Intensity = new ResolvedIntensityData()` with all null properties. The session has `SelectedIntensityProfileId` set (e.g., "Emotional" profile) and `_intensityProfileService` is already injected — but never called to resolve the profile.

The `IntensityProfile` domain model has `Intensity` (base level), `Description`, and `GetPhaseOffset(NarrativePhase)` which computes the phase-adjusted intensity level (e.g., Opening→Emotional, Climax→Hardcore).

## Plan

Add `ResolveIntensityAsync` method that:
1. Looks up `session.SelectedIntensityProfileId` via `_intensityProfileService.GetAsync`
2. Computes phase-adjusted intensity using `GetPhaseOffset(NarrativePhase)`
3. Populates `ResolvedIntensityData` with resolved label, description, base level, adaptive level

Replace `new ResolvedIntensityData()` in context with the resolved result.

**Files:** `RolePlayContinuationService.cs` only

## Resolution

Added `ResolveIntensityAsync` method. Context now uses resolved intensity data.

## Validated

[ ] Pending user confirmation
