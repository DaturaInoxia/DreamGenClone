# Implementation Plan: Attractiveness Tier Catalog

**Branch**: `079-attractiveness-tier-catalog` | **Date**: 2026-08-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/079-attractiveness-tier-catalog/spec.md`

## Summary

The `PhysicalAttributes.AttractivenessRating` (1–10) currently renders as a bare number (`Attractiveness: 10/10`) in the prompt appearance block — a dead signal the model cannot turn into narrative (observed in session `44d9af9f`, where a 10/10 character was written as passively "present" with no magnetism). This feature attaches effect-based tier prose (physical descriptors + behavioral cues describing how others react) to each rating band, so the number becomes a narrative engine.

**Approach (from research)**: a static, code-defined `AttractivenessTierCatalog` in `DreamGenClone.Domain/Templates/` — five non-overlapping bands covering 1–10 (Striking 9–10 / Attractive 7–8 / Average 5–6 / Plain 3–4 / Repelling 1–2), each with gender-neutral prose containing ≥1 physical descriptor + ≥1 behavioral-cue sentence. `AttractivenessTierCatalog.Resolve(int?)` maps a rating to exactly one tier, returning `null` for null/out-of-range. `PhysicalAttributesFormatter.FormatBlock` appends `— <Label>: <prose>` after the `n/10`; when `Resolve` returns no tier the line is omitted (no fallback prose — repo no-fallback rule). Because the appearance block is injected into every scene actor's prompt, the prose is injected state only and other characters' reactions flow through the narrative — zero cross-character stat coupling (FR-009). No DB change (FR-011), applies uniformly to all roles/genders (FR-007/008). P2 (deferred): `IntimateBehavioralTextBuilder.BuildSelfAwarenessText` adds attractiveness framing gated behind the existing `awarenessLevel` mechanism (FR-010) — additive, no rework to P1.

**Verification**: new catalog tests (band coverage 1–10, prose cue contract, Resolve null/out-of-range/boundaries) + formatter tests (renders prose for a set rating, omits when null/out-of-range); full regression suite must pass (tier prose is strictly additive after `n/10`).

## Technical Context

<!-- Technical context resolved during Phase 0 research (see research.md). -->

**Language/Version**: C# 12 / .NET 9 (net9.0 across Domain, Web, Tests)  
**Primary Dependencies**: None new — pure Domain-layer C# for the catalog (no packages, no DI registration). Existing: xUnit 2.9.2 (tests), Serilog 9.0.0 (logging), Microsoft.Data.Sqlite 9.0.0. Requires adding `<InternalsVisibleTo Include="DreamGenClone.Tests" />` to the Web csproj so the internal formatter is testable  
**Storage**: None — no DB migration. `AttractivenessRating` already persists as `int?` inside the `PhysicalAttributes` JSON payload. FR-011 documents the explicit SQLite exception: static, code-defined catalog with no runtime-persisted data  
**Testing**: xUnit (existing). New: `AttractivenessTierCatalogTests` (bands cover 1–10, prose has physical + behavioral cues, Resolve null/out-of-range/boundaries) + `PhysicalAttributesFormatterTests` (renders prose for set rating, omits when null/out-of-range); full suite must pass (zero regressions — prose is additive)  
**Target Platform**: Local Windows desktop web app (ASP.NET Core/Blazor, .NET 9); local-first, no cloud dependency
**Project Type**: Layered .NET 9 web application (Web / Application / Infrastructure / Domain projects with enforced dependency direction)  
**Performance Goals**: N/A — pure string formatting; no measurable performance requirement. Prose adds ~1 line per rated character's appearance block (prompt length already bounded by MaxPromptChars)  
**Constraints**: Repo hard rules — no hardcoded/fallback prose in the formatter (FR-006); attractiveness is optional character data so omit-on-absent is intended, not a forbidden fallback; fail-fast applies to required RP config only. Gender-neutral prose (FR-008); zero stat coupling (FR-009); Serilog structured logging for any new diagnostics (FR-012); log levels configurable via settings (FR-013, existing)  
**Scale/Scope**: Small. P1: 1 new Domain file + 1 formatter edit + 1 csproj edit + 2 new test files. P2 deferred: 1 additive edit to `IntimateBehavioralTextBuilder`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] Local-first runtime preserved (no mandatory cloud dependency for core flow) — pure local C# string formatting; no cloud dependency introduced
- [x] Module boundaries and adapter seams are explicit and swappable — catalog in Domain/Templates, formatting in existing Web/Application/RolePlay formatter, tests in Tests; no new projects, no new seams
- [x] .NET layered architecture uses separate projects with enforced dependency direction — Domain owns the catalog; Web already references Domain (formatter already uses `DreamGenClone.Domain.Templates`); dependency direction unchanged
- [x] Deterministic state transitions and JSON contract validation are test-covered — the catalog is pure/deterministic (same input → same tier) and fully unit-tested; no JSON contract changes (text injection only)
- [x] Persistence uses SQLite by default, or spec explicitly documents exception scope and rationale — FR-011 explicitly documents the exception: static, code-defined catalog, no runtime-persisted data, no migration
- [x] Serilog is the primary logging framework with .NET 9 structured logging best practices — no new diagnostics required for P1 (unresolvable → silent omit per FR-005/FR-006); any future diagnostic would be structured and Verbose-level at the call site
- [x] Logging coverage exists across layers/components/services with Information logs for major call paths — formatter is a pure utility on the existing injection path already logged by the calling services; no new major call path added
- [x] Log levels are externally configurable, including Verbose diagnostics without code changes — existing Serilog appsettings configuration unchanged

**Gate Result**: PASS — no violations. No Complexity Tracking entries needed.

**Post-Design Re-check (Phase 1 complete)**: PASS — design adds a static Domain catalog (`AttractivenessTierCatalog`), edits the existing `PhysicalAttributesFormatter.FormatBlock` (additive prose after `n/10`), adds `<InternalsVisibleTo Include="DreamGenClone.Tests" />` to the Web csproj (mirrors the Infrastructure precedent), and adds two new test files. No new projects, no cloud dependency, no non-SQLite storage, no schema change, Serilog preserved, deterministic resolve logic is unit-testable. P2 (self-awareness integration) is explicitly deferred and additive. No new violations introduced.

## Project Structure

### Documentation (this feature)

```text
specs/079-attractiveness-tier-catalog/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── contracts.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
DreamGenClone.Domain/
└── Templates/
    └── AttractivenessTierCatalog.cs   # NEW — AttractivenessTier record + static catalog + Resolve(int?)

DreamGenClone.Web/
├── Application/
│   └── RolePlay/
│       └── PhysicalAttributesFormatter.cs   # EDIT — FormatBlock appends " — <Label>: <prose>" to the Attractiveness line (Resolve-gated; no fallback)
└── DreamGenClone.csproj                     # EDIT — add <InternalsVisibleTo Include="DreamGenClone.Tests" /> (mirrors Infrastructure precedent)

DreamGenClone.Tests/
├── Templates/
│   └── AttractivenessTierCatalogTests.cs    # NEW — catalog tests (bands cover 1–10, prose physical+behavioral cues, Resolve boundaries/null/out-of-range)
└── RolePlay/
    └── PhysicalAttributesFormatterTests.cs  # NEW — formatter renders prose for set rating; omits when null/out-of-range

# P2 (deferred — NOT part of P1):
DreamGenClone.Web/Application/RolePlay/
└── IntimateBehavioralTextBuilder.cs         # LATER — attractiveness framing in BuildSelfAwarenessText, gated by awarenessLevel (FR-010)
```

**Structure Decision**: Existing 4-project layered .NET 9 architecture (Domain, Application, Infrastructure, Web) with enforced dependency direction. No new projects, no new DI registrations. The catalog lives in the Domain `Templates` layer (same namespace as `PhysicalAttributesCatalog`); prompt formatting stays in the existing `internal static` formatter in `Web/Application/RolePlay`; tests mirror their source locations (`DreamGenClone.Tests/Templates/` and `DreamGenClone.Tests/RolePlay/`). One build-level change: `<InternalsVisibleTo Include="DreamGenClone.Tests" />` added to `DreamGenClone.Web/DreamGenClone.csproj` (matches the Infrastructure csproj precedent) so the internal formatter is unit-testable. No SQLite/schema change, no UI change, no slot change.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. No complexity tracking entries needed.
