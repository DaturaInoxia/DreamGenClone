# Research: Attractiveness Tier Catalog

**Branch**: `079-attractiveness-tier-catalog` | **Date**: 2026-08-12
**Status**: Complete — all open questions resolved for P1; P2 scope locked.

## 1. Catalog placement and shape

- **Decision**: Static `AttractivenessTierCatalog` in `DreamGenClone.Domain/Templates/AttractivenessTierCatalog.cs`, defined as `public sealed record AttractivenessTier(int Min, int Max, string Label, string Prose)` plus `public static class AttractivenessTierCatalog` exposing `All` (`IReadOnlyList<AttractivenessTier>`, ordered low→high) and `Resolve(int? rating)`.
- **Rationale**: Matches the user's explicit decision #1 (static code-defined catalog). The `Templates` folder is the correct home because attractiveness is persona/template data — it lives alongside `PhysicalAttributesCatalog` and the `PhysicalAttributes` model it describes. This mirrors how `StoryAnalysis` catalogs (`BehavioralDimensionCatalog`, `SeductionArchetypeCatalog`) sit next to the types they describe.
- **Alternatives considered**:
  - *Per-scenario / DB-backed catalog* — rejected: user decision #1 is explicitly static; adds persistence surface (violates FR-011's no-new-store intent) and a config lookup path with no UI.
  - *`Domain/StoryAnalysis/` placement* (alongside `BehavioralDimensionCatalog`) — rejected: attractiveness is not story analysis; `Templates` is the semantically correct namespace and keeps the physical-attributes family together.

## 2. Resolve() semantics and band boundaries

- **Decision**: `Resolve(int? rating)` returns `null` for `null` or any value outside 1–10; otherwise returns the single band whose `Min <= rating <= Max`. Bands (non-overlapping, union covers 1–10): Striking 9–10, Attractive 7–8, Average 5–6, Plain 3–4, Repelling 1–2.
- **Rationale**: Boundary ratings must map deterministically (1→Repelling, 3→Plain, 5→Average, 7→Attractive, 9→Striking — FR-003, edge cases). Non-overlapping ranges prevent 6/7 and 8/9 bleed. Returning `null` for null/out-of-range satisfies US3 and FR-005.
- **Alternatives considered**:
  - *Switch expression on rating* — rejected: duplicates the band ranges in two places and risks drift; a single `All` list plus a `Min/Max` containment check is the single source of truth.
  - *Throwing on out-of-range* — rejected: the rating is optional character data; out-of-range is a data anomaly that must degrade to omit (US3), not crash prompt assembly.

## 3. Formatter integration — no fallback prose

- **Decision**: In `PhysicalAttributesFormatter.FormatBlock`, the attractiveness line is emitted **only when `Resolve` returns a tier**:
  ```csharp
  var tier = AttractivenessTierCatalog.Resolve(attrs.AttractivenessRating);
  if (tier is not null)
      Append(sb, "Attractiveness", $"{attrs.AttractivenessRating!.Value}/10 — {tier.Label}: {tier.Prose}");
  ```
  Output form: `Attractiveness: 10/10 — Striking: <prose>` (FR-004).
- **Rationale**: The single `Resolve` call is the only prose source (FR-006). Gating on `tier is not null` covers null (no line), out-of-range (no line), and valid (line with prose) in one decision path — there is no branch that injects substitute/hardcoded text. Omitting the line when absent is *intended* behavior (attractiveness is optional character data), so it does not conflict with the repo's "fail fast on required RP config" rule (spec assumption + FR-006).
- **Behavior note**: A stored out-of-range-but-set rating (e.g. `11`) previously rendered `Attractiveness: 11/10`; under the new gate it is omitted (US3, edge cases). No existing test asserts the old rendering (verified by repo search — no test references `Attractiveness: n/10`), so this is a zero-regression tightening.
- **Alternatives considered**:
  - *Keep `HasValue` branch and append prose only when resolved* — rejected: leaves an `11/10`-style rendering path for out-of-range values, contradicting US3.
  - *Log-and-substitute* — rejected outright: any substitute prose violates the repo no-fallback hard rule.

## 4. Testing the internal formatter (InternalsVisibleTo)

- **Decision**: Add `<InternalsVisibleTo Include="DreamGenClone.Tests" />` to `DreamGenClone.Web/DreamGenClone.csproj`.
- **Rationale**: `PhysicalAttributesFormatter` is `internal static` (repo convention, decision #10) and lives in the Web project. The Tests project references the Web project (verified in `DreamGenClone.Tests.csproj`) but the Web csproj currently exposes **no** internals — only `DreamGenClone.Infrastructure.csproj` declares `InternalsVisibleTo` for `DreamGenClone.Tests`. Adding the same one-line item to the Web csproj mirrors the established precedent and makes the formatter unit-testable without changing its visibility.
- **Alternatives considered**:
  - *Make `PhysicalAttributesFormatter` public* — rejected: widens the public API surface and breaks the repo's internal-utility convention.
  - *Test through a public seam (prompt builder)* — rejected: heavy integration test; the formatter is a pure function and deserves a focused unit test, consistent with how `SceneDirectionResolver` (public static in the same folder) is tested directly.

## 5. Test placement

- **Decision**: `DreamGenClone.Tests/Templates/AttractivenessTierCatalogTests.cs` (new folder) and `DreamGenClone.Tests/RolePlay/PhysicalAttributesFormatterTests.cs`.
- **Rationale**: Mirrors source locations — `BehavioralDimensionCatalogTests`/`SeductionArchetypeCatalogTests` sit in `DreamGenClone.Tests/StoryAnalysis/` because their catalogs live in `Domain/StoryAnalysis/`; the `Templates` catalog therefore gets `DreamGenClone.Tests/Templates/`. The formatter test sits in `DreamGenClone.Tests/RolePlay/` alongside `SceneDirectionResolverTests` (which also tests a `Web/Application/RolePlay` type).
- **Alternatives considered**: `DreamGenClone.Tests/RolePlay/Prompts/` for the formatter (as the B-079 draft suggested) — rejected as it groups a non-slot utility with slot tests; `DreamGenClone.Tests/StoryAnalysis/` for the catalog — rejected as semantically mismatched.

## 6. Prose authorship contract

- **Decision**: Five gender-neutral prose blocks authored once in code. Contract per band: **≥1 physical descriptor** (how the character looks) **and ≥1 behavioral-cue sentence** (how others react — turns heads, flusters, draws attention, presence felt, avoids eye contact, etc.). No gender or body-type assumptions (FR-008). Draft prose per band is defined in `contracts/contracts.md` (Contract 2) and enforced by automated tests (SC-002).
- **Rationale**: The behavioral-cue requirement is what converts the dead number into a narrative engine (the `44d9af9f` defect). Gender-neutral wording is mandatory because the identical prose renders for male, female, and other presentations across all roles (US2, FR-007/FR-008).
- **Alternatives considered**: Role- or gender-specific prose variants — rejected by the user's explicit decision that all roles/genders share one scale and one rendering path.

## 7. Logging for unresolvable ratings

- **Decision**: No new diagnostic for P1. An unresolvable rating (null/out-of-range) silently omits the line per FR-005/FR-006 — this is the defined behavior, not an error condition. `PhysicalAttributesFormatter` stays a pure static utility (no logger dependency).
- **Rationale**: FR-012 conditions logging on *new diagnostics*; P1 introduces none. Adding a logger to a pure formatter would break its static, dependency-free shape. The calling services (`RolePlayContinuationService`, `InteractionRetryService`, `CharacterDataSlot`) already emit structured logs on the prompt-injection path; if a future diagnostic for "rating present but unresolvable" is wanted, it should be a structured Verbose-level log at those call sites (configurable via existing Serilog settings — FR-013).
- **Alternatives considered**: Inline structured log in the formatter — rejected (dependency + FR-013 configuration still trivial but adds surface to a pure function).

## 8. P2 scope lock — self-awareness integration (deferred)

- **Decision**: P2 modifies `IntimateBehavioralTextBuilder.BuildSelfAwarenessText` to append attractiveness framing **only when `awarenessLevel` is present** (FR-010), reusing the existing thresholds already in the method (≥70 aware / ≤30 does not dwell / else quiet awareness). When `awarenessLevel` is null the output is byte-for-byte unchanged (existing behavior preserved). P1 ships without touching this file.
- **Rationale**: The spec marks P2 optional/deferred and requires P1 stories to not depend on it. Because the change is purely additive prose inside the existing awareness-gated block, it can be layered on later without rework. The persona path already passes a resolved awareness level (`CharacterDataSlot.ResolvePersonaAwarenessLevel`, currently `2`), and scenario characters pass `null` — so the "no awareness → unchanged" acceptance scenario maps directly onto existing call sites.
- **Alternatives considered**: Shipping P2 in P1 — rejected: user priorities explicitly sequence P2 after P1 and forbid P1 depending on it.

## Consolidated decisions

| # | Question | Decision |
|---|----------|----------|
| D1 | Catalog location/shape | `Domain/Templates/AttractivenessTierCatalog.cs` — `AttractivenessTier` record + static catalog + `Resolve(int?)` |
| D2 | Resolve semantics | `null` for null/out-of-range; single band per rating 1–10; non-overlapping ranges |
| D3 | Formatter change | `Resolve`-gated append: `n/10 — Label: prose`; no fallback branch; omit when unresolvable |
| D4 | Test access to internal formatter | Add `<InternalsVisibleTo Include="DreamGenClone.Tests" />` to Web csproj |
| D5 | Test placement | `Tests/Templates/` (catalog) + `Tests/RolePlay/` (formatter) |
| D6 | Prose contract | ≥1 physical descriptor + ≥1 behavioral-cue sentence; gender-neutral |
| D7 | Logging | No new diagnostic in P1 (silent omit is the defined behavior); any future one is structured Verbose at call sites |
| D8 | P2 | Deferred, additive, awarenessLevel-gated; no rework to P1 |
