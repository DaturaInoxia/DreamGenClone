# Tasks: Attractiveness Tier Catalog

**Input**: Design documents from `/specs/079-attractiveness-tier-catalog/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**Tests**: Included per the spec — catalog tests (band coverage 1–10, prose cue contract, Resolve boundaries/null/out-of-range) and formatter tests (renders prose for set rating, omits when null/out-of-range) are explicitly required.

**Organization**: Grouped by priority tier (P1 catalog + formatter, P2 deferred self-awareness) to enable clean, independently testable slices.

## Format: `[ID] [P?] [Priority] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Priority]**: P1 = core (catalog + formatter), P2 = deferred (self-awareness integration)
- Exact file paths in descriptions

---

## Phase 1: Setup

**Goal**: Verify project state and confirm zero regression risk before starting.

- [X] T001 Run `dotnet build DreamGenClone.sln` from repo root and confirm 0 errors before starting
- [X] T002 [P] Verify no existing test asserts the bare `Attractiveness: n/10` text (grep `DreamGenClone.Tests` for `Attractiveness`) — confirms the formatter change is strictly additive with zero regression risk

---

## Phase 2: Foundational — AttractivenessTierCatalog (P1)

**Goal**: Create the `AttractivenessTier` record and `AttractivenessTierCatalog` static class in the Domain layer. Single source of truth for all 5 band definitions and prose.

**Independent test**: Call `AttractivenessTierCatalog.Resolve(10)` and verify it returns the Striking tier with correct Min/Max/Label/Prose.

- [X] T003 Create `AttractivenessTier` record with Min, Max, Label, Prose fields in `DreamGenClone.Domain/Templates/AttractivenessTierCatalog.cs`
- [X] T004 Create `AttractivenessTierCatalog` static class with all 5 bands (Striking 9-10 / Attractive 7-8 / Average 5-6 / Plain 3-4 / Repelling 1-2) as `IReadOnlyList<AttractivenessTier> All`, ordered low→high in `DreamGenClone.Domain/Templates/AttractivenessTierCatalog.cs`
- [X] T005 Implement `AttractivenessTierCatalog.Resolve(int? rating)` — maps 1-10 to exactly one tier, returns null for null/0/11/out-of-range in `DreamGenClone.Domain/Templates/AttractivenessTierCatalog.cs`
- [X] T006 [P] Create unit tests: verify All has exactly 5 non-overlapping bands covering 1-10, Resolve maps each boundary correctly (10,9→Striking; 8,7→Attractive; 6,5→Average; 4,3→Plain; 2,1→Repelling), Resolve returns null for null/0/11/out-of-range, Resolve is deterministic in `DreamGenClone.Tests/Templates/AttractivenessTierCatalogTests.cs`
- [X] T007 [P] Create prose-contract tests: verify each band's Prose contains ≥1 physical-descriptor token AND ≥1 behavioral-cue token (use the cue lexicon from `contracts/contracts.md` Contract 2 — physical: features/symmetry/face/body/well-kept/ordinary/unremarkable/forgettable/neglected/unappealing; behavioral: turn to look/flustered/nervous/attention follows/presence/lingering looks/smiles/warmer/pull/interest/avoid eye contact/distance/avoidance) in `DreamGenClone.Tests/Templates/AttractivenessTierCatalogTests.cs`
- [X] T008 Build Domain project and run catalog tests: `dotnet build DreamGenClone.Domain/DreamGenClone.Domain.csproj && dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~AttractivenessTierCatalog"` — all must pass

---

## Phase 3: User Story 1 + 2 — Formatter Integration (P1)

**Goal**: Change `PhysicalAttributesFormatter.FormatBlock` so the `Attractiveness: n/10` line renders as `Attractiveness: n/10 — <Label>: <prose>`, using the catalog's Resolve as the sole source. Add `InternalsVisibleTo` to the Web csproj so the internal formatter is unit-testable.

**Independent test**: Call `PhysicalAttributesFormatter.FormatBlock(attrs, "Male")` with a `PhysicalAttributes` that has `AttractivenessRating = 10` and verify the output contains `Attractiveness: 10/10 — Striking:` followed by the Striking prose. Call with `AttractivenessRating = null` and verify the line is omitted.

- [X] T009 Add `<InternalsVisibleTo Include="DreamGenClone.Tests" />` to `DreamGenClone.Web/DreamGenClone.csproj` (mirrors the Infrastructure csproj precedent — verify with a grep of `DreamGenClone.Infrastructure/DreamGenClone.Infrastructure.csproj` for the exact pattern)
- [X] T010 Edit `PhysicalAttributesFormatter.FormatBlock` to append ` — <Label>: <prose>` after the `n/10` for the Attractiveness line, gated by `AttractivenessTierCatalog.Resolve(attrs.AttractivenessRating)`. When Resolve returns null, omit the line entirely (current behavior — no fallback prose, repo no-fallback rule) in `DreamGenClone.Web/Application/RolePlay/PhysicalAttributesFormatter.cs`
- [X] T011 [P] Create formatter tests: renders `Attractiveness: 10/10 — Striking: <prose>` for rating 10, same Striking band for rating 9, `Attractiveness: 5/10 — Average: <prose>` for rating 5, omits the line when rating is null or out-of-range (0/11) in `DreamGenClone.Tests/RolePlay/PhysicalAttributesFormatterTests.cs`
- [X] T012 Build Web project and run formatter tests: `dotnet build DreamGenClone.Web/DreamGenClone.csproj && dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~PhysicalAttributesFormatter"` — all must pass

---

## Phase 4: Polish & Cross-Cutting

**Goal**: Full build, all tests pass, final validation.

- [X] T013 Run full solution build: `dotnet build DreamGenClone.sln` — 0 errors, 0 new warnings
- [X] T014 Run full regression for affected areas: `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~Attractiveness|FullyQualifiedName~PhysicalAttributes|FullyQualifiedName~CharacterDataSlot|FullyQualifiedName~Seduction"` — all pass
- [X] T015 Verify no cross-character stat coupling: confirm the formatter change is purely presentational (no stat writes, no willingness formula change, no gate change). Read `PhysicalAttributesFormatter.FormatBlock` diff against the plan FR-009 constraint.
- [X] T016 Run quickstart validation per `specs/079-attractiveness-tier-catalog/quickstart.md`: build, test, and (if the webapp is run) confirm a character with a rating renders the prose in the appearance block

---

## Phase 5: P2 (Deferred — NOT part of this implementation)

**Goal**: Optional self-awareness integration in `IntimateBehavioralTextBuilder.BuildSelfAwarenessText` — adds attractiveness framing to the "BEHAVIORAL CONSTRAINT — X's attributes:" prose, gated behind the existing `awarenessLevel` mechanism (FR-010). Additive, no P1 rework.

- [ ] T017 (Deferred) Extend `IntimateBehavioralTextBuilder.BuildSelfAwarenessText` to include attractiveness framing when `AttractivenessTierCatalog.Resolve` returns a tier, gated by the existing `awarenessLevel` thresholds (>=70 aware / <=30 does not dwell / else quiet awareness) in `DreamGenClone.Web/Application/RolePlay/IntimateBehavioralTextBuilder.cs`
- [ ] T018 (Deferred) Extend the self-awareness tests to cover attractiveness framing per awareness level

---

## Dependencies

```
Phase 1 (Setup)
    ↓
Phase 2 (Catalog)  ←── blocks Phase 3
    ↓
Phase 3 (Formatter integration)  ←── depends on Phase 2
    ↓
Phase 4 (Polish)
    ↓
Phase 5 (P2 deferred — not started in this cycle)
```

- Phase 2 is the critical blocking phase — Phase 3 formatter integration needs the catalog's `Resolve` to exist.
- Phase 5 (P2) is explicitly deferred — the P1 slice (Phase 1-4) is complete and mergeable without it.

## Parallel Opportunities

| Phase | Parallel tasks |
|-------|---------------|
| Phase 1 | T001, T002 |
| Phase 2 | T006, T007 (both test files — can run with T003-T005) |
| Phase 3 | T011 (formatter tests) parallel with T009-T010 |
| Phase 4 | T013, T014, T015, T016 all parallel |

## Implementation Strategy

**P1 (this cycle)**: Complete Phase 1 → Phase 2 → Phase 3 → Phase 4. This delivers the core feature: the catalog exists, the formatter renders tier prose for all roles/genders, no cross-character stat coupling. Fully testable and mergeable.

**P2 (deferred)**: Add Phase 5 later when the self-awareness framing is wanted — it's additive with no P1 rework.
