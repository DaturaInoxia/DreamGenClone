# DreamGenClone Non-Negotiable Rules

These rules are mandatory for all coding tasks in this repository.

## Hard Rule: Never Use Git Restore
- Do NOT use `git restore`, `git checkout --`, `git reset --hard`, or any equivalent to revert files.
- All fixes are forward-only code changes. If a change was wrong, fix it with another forward code edit.

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

## Hard Rule: No RP Engine Code Changes Without Plan + Confirmation
- Before ANY code change to RP engine files (`RolePlayEngineService.cs`, `RolePlayContinuationService.cs`, prompt slots, etc.), present: root cause, proposed fix with file list, and blast radius.
- Wait for explicit "go ahead" or "yes" before touching any code.
- This applies even when the fix seems obvious.
- If the root cause is a config/data issue (not a code bug), state that and do not change code.

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

## Razor Editing Rules

When editing or creating `.razor`, `.razor.cs`, or `.razor.css` files, follow the rules in [`.github/instructions/razor-editing.instructions.md`](instructions/razor-editing.instructions.md). These rules enforce full-context reads, anti-hallucination constraints, Razor grammar reminders, a self-validation checklist, and diff-only / micro-step workflows. They are mandatory for all models editing Razor files in this repository.

For this project's Razor style conventions and patterns, see [`.github/razor-style-reference.md`](razor-style-reference.md).

## DB Query Tool

A permanent .NET 9 console project lives at `artifacts/tmp/dbquery/dbquery.csproj` (part of the solution under `artifacts > tmp`).
- **Use it for all SQLite database queries**, inspections, and data seeding tasks against `DreamGenClone.Web/data/dreamgenclone.dev.db`.
- Run with: `dotnet run --project artifacts/tmp/dbquery -- <command> [args...]`
- **Program.cs is a permanent named-command dispatcher — do NOT rewrite it per task.**
- For ad-hoc SQL: write a `.sql` file and use the `sql` command: `dotnet run --project artifacts/tmp/dbquery -- sql myquery.sql [id]`
- Full schema, all commands, and usage examples are in `.github/instructions/dbquery-reference.instructions.md`.
- **Do not recreate this project.** It already exists in the solution and is ready to use.

## Project Backlog

The project backlog is at `specs/Planning/backlog.md`.
When the user refers to "the backlog", "backlog item", "add to the backlog", or "update the backlog", they mean this file.
- Each item has a number (B-###), title, state, and notes.
- Valid states: `new`, `designed`, `planned`, `implemented`, `debugging`, `done`, `done done`.
- New ideas are added as `new`. Items progress through states as work advances.
- Do not remove items from the backlog — change their state to `done done` when fully closed.
