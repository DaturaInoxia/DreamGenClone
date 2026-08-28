# Debug 001 — Opening-Period Direction Not Injected Into Prompts

**Feature**: `001-opening-period` | **Date**: 2026-07-31
**Status**: Plan drafted — awaiting confirmation. NO code changed.

---

## Report

**Symptom**: The Opening phase does not carry its special "focus on the husband and wife" prompt direction. The user expected a `HARD CONSTRAINT — Opening Period Direction` block during the first 3 turns (as specified by `001-opening-period` FR-003 / FR-016) and reported it missing.

**Verification (session `42b79db3-050f-4cd4-b62a-75c9f1b113a2`)**
- 131 interactions; currently in Climax.
- Opening-phase interactions (turns 1–3) prompts contain **zero** occurrences of `Opening Period Direction` or the seeded seed text ("couple's relationship").
- Opening prompts show only the generic `Phase: Opening.` header + the couple-only character list (Becky/Ken). No husband–wife focus direction.
- Codebase-wide: no slot emits the opening-period direction; `OpeningGuidanceText` appears only in the DB migration + domain model.

---

## Analysis — Root Cause (two independent breaks)

### Break 1 — Prompt injection never wired into the 17-slot pipeline
- `001-opening-period` designed the injection for the **old inline prompt builder** in `RolePlayContinuationService.cs`.
- `001-rp-prompt-redesign` replaced it with `RolePlayPromptBuilder` (17 slots) and deleted the old injectors; the opening-period direction was **never re-homed** into a slot.
- `Prompts/Slots/` has 19 slots; none reference opening-period guidance. The only Opening-specific text is the generic phase line in `ScenarioGuidanceSlot.cs:89` ("Establish the scene, introduce characters, set the tone…") — **not** the husband/wife direction.

### Break 2 — `OpeningGuidanceText` never reaches the prompt builder
- `Scenario.OpeningGuidanceText` (`DreamGenClone.Web/Domain/Scenarios/Scenario.cs:18-23`) deserializes from **PayloadJson**.
- The migration (`SqlitePersistence.cs:2008-2025`) writes to the separate **`Scenarios.OpeningGuidanceText` column** — which nothing ever reads (`ScenarioService.EnsureLoadedAsync` / `LoadScenarioAsync` read only `Id, Name, PayloadJson, UpdatedUtc`).
- The migration block sits **behind the legacy-migration gate**: `ShouldRunLegacyMigrationsAsync` returns false when `AppMetadata[LegacyMigrationVersionKey] == CurrentLegacyMigrationVersion`, and the code does `goto AfterLegacyMigrations` (line 1415) → the block at 2008–2025 is skipped.
- Result on the dev DB: querying `Scenarios.OpeningGuidanceText` fails with `no such column` — the migration never ran.

### Confirmed working (no change needed)
- Opening couple-only character filter (`ResolveOpeningCoupleIds`, `RolePlayContinuationService.cs:543-555`) — prompt sees only Husband+Wife.
- OtherMan overflow exclusion + Opening→BuildUp transition at turn 3 (`RolePlayEngineService.cs`, `OpeningPeriodTurnCount = 3`).
- Leftover dead constant: `OpeningPeripheralTurnCount = 6` (`RolePlayContinuationService.cs:36`) — unused anywhere.

---

## Plan — Proposed Fix (design; NOT implemented)

### Design decisions
- **D1 — Source of truth = PayloadJson.** Keep `Scenario.OpeningGuidanceText` (JSON property). Seed existing scenarios' payload JSON with the default text; stop depending on the `OpeningGuidanceText` column (drop the column approach; remove the column DDL or keep it as harmless but unused — recommend removing column seeding and moving the payload-seed out of the legacy gate).
- **D2 — Injection point = reuse `ScenarioGuidanceSlot` (Slot 14, Zone C).** Respects the frozen 17-slot architecture; the slot is already the phase-steering slot. When `Phase == Opening` AND `session.AdaptiveState.ObservedTurnCount <= 3`, emit `HARD CONSTRAINT — Opening Period Direction: {guidance}` instead of the generic phase line.
  - Alternative (rejected for now): new dedicated `OpeningGuidanceSlot` — requires `PromptSlotId` enum entry + Program.cs registration + violates the "frozen 17-slot" contract.
- **D3 — Context plumbing:** add `OpeningGuidanceText` (string?) to `ResolvedScenarioData`; populate from `scenario.OpeningGuidanceText` in `BuildPromptViaBuilderAsync`.
- **D4 — Default text (REVISED 2026-07-31, user-approved new global default):** define `DefaultOpeningGuidanceText` constant used only when `OpeningGuidanceText` is null. **Replaces FR-016 seed text** for all scenarios. Per user intent:
  - Introduces the characters and scenario.
  - States the couple's sexual **status quo** — NOT an emotional-connection arc, NOT "trying to connect."
  - Sex-life framing **grounded in character stats**: high Desire / low Restraint → active, recently intimate; muted stats → routine/subdued.
  - On-screen sex allowed in opening **only when profiles/current state support it**.
  - Generic foreshadowing via the Potential Arcs (already injected by `ThemeContractSlot`); no hardcoded arc names.

  **Final text:**
  > Introduce the characters and the scenario — who they are, how they fit into their world, and the situation they are in now — grounded in the character profiles and descriptions. State the marriage as it currently is: a settled, long-established couple with a sex life that matches their stats. When their Desire is high and their Restraint is low, they are sexually active and recently intimate — comfortable with each other's bodies, past courtship and discovery. When their stats are muted, show that instead: a physical life that is routine or subdued. On-screen intimacy is allowed in the opening only when their profiles and current state support it. This is not about them reconnecting or reaching for emotional closeness; their dynamic is already fixed. Sketch their routines, the rhythm of their days, and the setting. Let the potential arcs foreshadow quietly in the background. Keep the focus on the husband and wife; other characters remain in the background.

### File-by-file changes

| # | File | Change |
|---|------|--------|
| 1 | `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | Move the opening-guidance migration **out of the legacy-gated block** into the `AfterLegacyMigrations:` section (runs unconditionally). Rewrite it to seed **PayloadJson** via `json_set(PayloadJson,'$.OpeningGuidanceText',…)` where the JSON field is NULL (idempotent) using the new default text (D4). Remove/keep the column DDL — recommend removing the column approach to avoid a dead second source. |
| 2 | `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs` | Add `public string? OpeningGuidanceText { get; init; }` to `ResolvedScenarioData`. |
| 3 | `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Populate `OpeningGuidanceText = scenario?.OpeningGuidanceText` in the `PromptBuildContext` construction. Remove dead `OpeningPeripheralTurnCount = 6`. |
| 4 | `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ScenarioGuidanceSlot.cs` | Add opening-period branch: when `Phase == Opening` and `Session.AdaptiveState.ObservedTurnCount <= 3`, emit `HARD CONSTRAINT — Opening Period Direction: {OpeningGuidanceText ?? DefaultOpeningGuidanceText}` (skip generic phase line). `DefaultOpeningGuidanceText` = D4 text. Information-level log when injected (per T015). |
| 5 | Dev DB (`dreamgenclone.dev.db`) | Run the (now unconditional) migration via app startup OR `dbq exec` SQL to seed payload JSON on existing scenarios. Verify via `dbq sql`. |
| 6 | `DreamGenClone.Tests/RolePlay/` (new/extended slot test) | Add test: Opening phase + turn ≤ 3 → prompt contains opening direction + no theme guidance; turn ≥ 4 → theme guidance present, opening direction absent. |

### Blast radius
- Prompt pipeline: `ScenarioGuidanceSlot` output changes only during Opening+turns 1–3 (previously generic "Establish the scene…" line). Other phases unchanged.
- Scenario loading: payload JSON gains one optional field; backward compatible.
- Migration: now runs unconditionally; idempotent (`WHERE json_extract(...) IS NULL`).
- No change to `RolePlayEngineService.cs` (OtherMan exclusion/phase transition already correct).
- Existing sessions unaffected retroactively (prompts are stored; only new prompts change).

### Validation protocol
1. Build: `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore` + `dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore`.
2. Slot tests: `dotnet test DreamGenClone.Tests --no-build --filter "FullyQualifiedName~SlotContractTests"`.
3. DB: confirm `json_extract(PayloadJson,'$.OpeningGuidanceText')` populated on scenarios (via `dbq sql`).
4. Fresh session: extract turns 1–3 prompts → opening direction present, theme guidance absent (FR-002/FR-003); turn 4+ → theme guidance present, opening direction absent (FR-006).

---

## Resolution

[x] Implemented 2026-07-31 (user-approved):
- `SqlitePersistence.cs`: removed the legacy-gated `OpeningGuidanceText` **column** migration; added an unconditional, idempotent seed that writes the direction into `Scenarios.PayloadJson` (`$.OpeningGuidanceText`) in the `AfterLegacyMigrations:` section.
- `PromptBuildContext.cs`: added `ResolvedScenarioData.OpeningGuidanceText` (nullable).
- `RolePlayContinuationService.cs`: populated `OpeningGuidanceText = scenario?.OpeningGuidanceText` in the prompt context; removed dead `OpeningPeripheralTurnCount = 6`.
- `ScenarioGuidanceSlot.cs`: added `DefaultOpeningGuidanceText` (new D4 text) + `OpeningPeriodTurnCount = 3`; when `Phase == Opening` and `ObservedTurnCount <= 3`, emits `HARD CONSTRAINT — Opening Period Direction: {text}` instead of the generic phase line, with an `OpeningPeriodDirectionInjected` Information log.
- Dev DB: seeded all 3 scenarios' payload JSON (verified `IN-JSON`).
- Tests: added 3 `ScenarioGuidanceSlot` opening-period tests (injection, scenario-override, post-opening absence) to `SlotContractTests.cs`.

## Validated

[ ] pending — confirmed fixed by user? Date/time.

Build evidence: Web + Tests build with 0 errors; 5/5 `ScenarioGuidanceSlot` tests pass (2 existing + 3 new). 7 other `SlotContractTests` failures are pre-existing in untouched slots (see `/memories/repo/pre-existing-test-failures.md`).
