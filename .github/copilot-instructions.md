# DreamGenClone Non-Negotiable Rules

These rules are mandatory for all coding tasks in this repository.

## Hard Rule: No Fallbacks For Gate Values
- For roleplay narrative gate thresholds, always use configured source values.
- Do not introduce fallback/default/backup threshold logic.
- Do not add alternate hidden code paths that change threshold source selection.
- If required values are missing, fail fast with explicit diagnostics instead of substituting defaults.

## Hard Rule: No Fallbacks Across RP Engine
- For all RP engine behavior (selection, phase transitions, adaptive stats, continuation, prompts, safety checks, assistant, and diagnostics), use configured values only.
- Do not introduce hardcoded runtime defaults, guessed substitute values, or hidden backup branches.
- Missing/invalid required RP configuration must fail fast with explicit diagnostics; do not silently continue.
- Any RP behavior control must be configurable in UI-backed persisted data, not hidden in code-only defaults.

## Hard Rule: Obey Explicit User Constraints
- Treat explicit user constraints as requirements, not suggestions.
- Do not re-introduce behavior the user explicitly removed.
- If a requested behavior conflicts with existing code patterns, follow the user requirement and surface the conflict in plain language.

## Required Verification Before Declaring A Fix
- Show where the value source is resolved.
- Confirm there is exactly one active decision path for gate threshold source.
- Confirm no fallback branch remains for this behavior.
- Validate with build/tests when available and summarize concrete evidence.

## Required Verification For RP Engine Changes
- Show the exact configuration source for every RP behavior changed.
- Confirm exactly one active decision path exists for each changed behavior.
- Confirm no fallback/default branch remains for each changed behavior.
- Confirm missing required configuration now fails explicitly instead of silently continuing.
- Confirm UI/config surface exists for newly introduced RP behavior settings.

## Forbidden Patterns In This Repo For Gate Threshold Source
- "if missing then default profile" for roleplay gate thresholds.
- silent fallback to global defaults.
- duplicate source selection logic in multiple services.

## Forbidden Patterns In This Repo For RP Engine
- hardcoded runtime behavior defaults that bypass configured RP data.
- "best effort" or guessed RP values when configuration is missing.
- duplicated configuration-source resolution logic across services.
- hidden recovery paths that alter RP behavior without explicit configured data.

## Project Backlog

The project backlog is at `specs/Planning/backlog.md`.
When the user refers to "the backlog", "backlog item", "add to the backlog", or "update the backlog", they mean this file.
- Each item has a number (B-###), title, state, and notes.
- Valid states: `new`, `designed`, `planned`, `implemented`, `debugging`, `done`, `done done`.
- New ideas are added as `new`. Items progress through states as work advances.
- Do not remove items from the backlog — change their state to `done done` when fully closed.
