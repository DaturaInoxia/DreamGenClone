# Quickstart: Attractiveness Tier Catalog

**Branch**: `079-attractiveness-tier-catalog` | **Date**: 2026-08-12

## What this feature does (30 seconds)

Maps `PhysicalAttributes.AttractivenessRating` (1–10) to one of five effect-based tiers (Striking / Attractive / Average / Plain / Repelling). The tier's prose (physical descriptor + how others react) is appended to the `Attractiveness: n/10` line in the appearance block, so a 10/10 character reads as magnetic instead of a dead number. Applies to all roles and genders via the existing formatter path. No DB change, no stat coupling.

## Build

From the repository root:

```powershell
dotnet build DreamGenClone.sln
```

**Note**: if the running webapp holds `DreamGenClone.Web/bin` locks, stop it first (per repo workflow), build, then restart it.

## Test

Run the new feature tests:

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~AttractivenessTierCatalog|FullyQualifiedName~PhysicalAttributesFormatter"
```

Run the full suite to confirm zero regressions (prose is additive after `n/10`; no existing test asserts the old bare `n/10` rendering):

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj
```

Expected new coverage:

- `AttractivenessTierCatalogTests` — exactly 5 bands; bands cover 1–10 with no overlap/gaps; boundary ratings (1, 3, 5, 7, 9) map to the correct band; 6/7 and 8/9 don't bleed; `Resolve` returns `null` for `null`, 0, 11, negatives; each `Prose` has ≥1 physical descriptor + ≥1 behavioral-cue sentence; labels canonical/unique.
- `PhysicalAttributesFormatterTests` — set rating renders `Attractiveness: n/10 — <Label>: <prose>`; rating 10 and 9 both render Striking; rating 5 renders Average; `null` and out-of-range ratings omit the line; a block with attractiveness-only still renders; empty block still returns `string.Empty`; prose present is exactly the catalog prose (no fallback text).

## Manual verification (prompt-level)

1. Start the app from `DreamGenClone.Web` with `ASPNETCORE_ENVIRONMENT=Development` (use `helpers/start-webapp-dev-clean.ps1`; the user starts the app themselves).
2. Create/open a scenario with a present character whose `AttractivenessRating` is 10 (and one with 5, and one with no rating).
3. Run a roleplay session turn involving that character.
4. Inspect the prompt/debug log (RolePlayDebugEvents) for the appearance block and confirm:
   - `Attractiveness: 10/10 — Striking: <prose>` (physical + behavioral cue).
   - The same block (with prose) appears in **every** actor's prompt that includes that character (reactions can flow through narrative).
   - A character with no rating or an out-of-range value shows **no** attractiveness line.

## Rollback / revert

Forward-only repo policy (no `git restore`/`checkout --`). To disable the prose:

- Option A (safe): remove/comment the single `if (tier is not null) Append(...)` block in `PhysicalAttributesFormatter.FormatBlock` → falls back to no attractiveness line at all (not the old `n/10`, because the old rendering was also in that line).
- Option B: delete the `Resolve` gate and restore the previous two-line `HasValue` branch that emitted `n/10` only.

Both are small forward edits; the catalog file can stay harmlessly or be deleted with the tests.

## Scope notes

- **P1 (this slice)**: `AttractivenessTierCatalog.cs` (new), `PhysicalAttributesFormatter.cs` (edit), `DreamGenClone.csproj` (add `InternalsVisibleTo`), two new test files.
- **P2 (deferred)**: `IntimateBehavioralTextBuilder.BuildSelfAwarenessText` attractiveness framing gated behind `awarenessLevel` — additive, no rework. Not in this slice.
