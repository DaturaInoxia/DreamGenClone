# Research Report: Full Prompt Injection Refactor

**Phase**: 0 — Outline & Research
**Date**: 2026-06-29
**Feature**: [spec.md](spec.md)

## Methodology

Codebase exploration via file reading, grep search, and static analysis of `DreamGenClone.Web/Application/RolePlay/`, `DreamGenClone.Domain/RolePlay/`, and `DreamGenClone.Tests/RolePlay/` directories.

---

## 1. Current Injector Catalog

**Source**: `RolePlayContinuationService.BuildPromptAsync` (~1100 lines, lines 380–1500+)

### Assembly Order (34 steps):

| # | Inject | Lines | Conditions | Category |
|---|--------|-------|------------|----------|
| 1 | System header | ~386–387 | Always | Data |
| 2 | Turn Context | ~390–430 | `turnIndex.HasValue` | **Behavioral** |
| 3 | POV Persona | ~434–460 | Persona exists | Data |
| 4 | Behavioral Rules | ~466 | Intimate attributes exist | Data |
| 5 | Scene Location Lock | ~470–482 | Always | **Behavioral** |
| 6 | Scenario Data | ~484–770 | Scenario bound | Data |
| 7 | Style Profile | ~772–800 | Profile set | Data |
| 8 | Interaction History | ~802–810 | Always | Data |
| 9 | Session Memory | ~812–815 | Memory enabled | Data |
| 10 | Scene Continuity Anchor | ~817–856 | Location services | Data |
| 11 | Adaptive Stats | ~858–867 | Stats exist | Data |
| 12 | Theme Tracker | ~869–902 | Scores exist | Data |
| 13 | Scenario Guidance Context | ~904–956 | Scenario bound | **Behavioral** |
| 14 | Framing Guards | ~958–985 | Not opening period | **Behavioral** |
| 15 | Scenario Guidance Block | ~987 | Always | **Behavioral** |
| 16 | Opening Period | ~989–1002 | Is opening | **Behavioral** |
| 17 | Active Theme Contract | ~1004–1056 | Theme service exists | **Behavioral** |
| 18 | Secondary Theme AI | ~1027–1054 | Top2Blend | **Behavioral** |
| 19 | Candidate Menu | ~1058–1062 | Observing | Data |
| 20 | Steer Guidance | ~1063–1075 | Steer present | Data |
| 21 | Time Skip Guidance | ~1077–1082 | Timeskip present | Data |
| 22 | Profile Theme Tiers | ~1088–1196 | Profile set | Data |
| 23 | Intensity Contract | ~1198–1230 | Always | **Behavioral** |
| 24 | Escalation Guidance | ~1231 | Not Instruction + stats | **Behavioral** |
| 25 | Position List | ~1232 | Approaching/Climax | **Behavioral** |
| 26 | Scene Pacing Contract | ~1234–1250 | BuildUp/Reset, not Instruction | **Behavioral** |
| 27 | Scene Writing Directive | ~1252–1284 | Directive or marker exists | **Behavioral** |
| 28 | Beat Stage Context | ~1286–1354 | Climax + episodic | **Behavioral** |
| 29 | Active Instruction | ~1356–1370 | Not Instruction + recent | Data |
| 30 | Prompt Text | ~1372–1386 | Non-empty | Data |
| 31 | Behavioral Frame HCs | ~1388–1395 | Frames exist | Data |
| 32 | Theme HC re-injection | ~1397–1401 | HCs exist | Data |
| 33 | World Rules re-injection | ~1403–1408 | Rules exist | Data |
| 34 | Final Writing Directive | ~1410–1500+ | Based on intent | **Behavioral** |

### Categorization:

- **~11 behavioral injects** (marked **Behavioral** above): TurnContext, Location/Time, ScenarioGuidance, FramingGuards→ThemeContract, ThemeAI, Intensity, Escalation, PositionList, SceneTimeDirection, BeatStage, FinalDirective
- **~23 data assembly blocks**: Everything else (scenario fetch, characters, locations, memory, stats, styles, personas, etc.) — stay inline

---

## 2. BuildFramingGuards Content

**Source**: `RolePlayAssistantPrompts.cs`, lines ~200–345

### Phase → Text Mapping:

| Phase | Texts |
|-------|-------|
| **BuildUp** | 1. "tension and anticipation only — do not write explicit sexual acts" |
| | 2. "characters may flirt, exchange glances, build emotional tension" |
| **Committed/Approaching/Climax** (no special markers) | 1. "keep all major beats aligned to '{activeScenarioId}'" |
| | 2. "do not pivot to a competing scenario" |
| **Committed/Approaching** (with time-shift marker) | 1. "each turn should advance to a different time and/or location" |
| | 2. "if previous turn was in X setting, next must be in different setting" |
| **Climax** (standard) | 1. "deliver high-intensity culmination" |
| | 2. "write with explicit positional and sensory detail" |
| | 3. "narrative urgency must increase writing intensity, not truncate" |
| | 4. "every turn must advance the scene to a new beat" |
| | 5. "vary position, tempo, focus each turn" |
| **Climax** (quick-finish) | 7 unique texts — quick-release, concealment-focused, single-position |

**Migration plan**: All 20+ strings move to theme phase guidance prose fields (`RPThemePhaseGuidance.GuidanceText`). BuildFramingGuards retired.

---

## 3. SceneDirectionResolver (62-line scaffold)

**File**: `SceneDirectionResolver.cs`
**Status**: Refers to 5 non-existent helper methods:

| Helper | Purpose |
|--------|---------|
| `NormalizePhase(string?)` | Normalize phase string to canonical form |
| `ResolvePacing(...)` | 3-tier: profile directive > theme marker > phase default |
| `ResolveBeatScope(...)` | Same 3-tier pattern |
| `ResolveTimeShift(...)` | Same 3-tier pattern |
| `SanitizeNote(string?)` | Null-guard + whitespace trim |

Also references undefined constants: `PhaseDefaultPacing`, `PhaseDefaultBeatScope`, `PhaseDefaultTimeShift`.

**Decision**: Complete all 5 helpers. Define per-phase default constants. Resolver never called in production — implement + wire through coordinator.

---

## 4. SceneDirection Record (Domain)

**File**: `DreamGenClone.Domain/RolePlay/SceneDirection.cs`
**Current state**: Has `Pacing`, `BeatScope`, `TimeShift`, `ClimaxSubPhase`, `DirectorNote`, `HasProfileDirective`. Missing `Deepening` field.

**Enums defined**: `ScenePacing` (Slow/Medium/Fast), `BeatScope` (Single/Short/Extended), `TimeShiftPolicy` (None/Small/Medium/Large), `ClimaxSubPhase` (None/Early/Mid/Late)

**Decision**: Add `DeepeningPolicy` enum (`None`, `SubsequentActors`) and `Deepening` field to `SceneDirection`.

---

## 5. SceneDirectionCoordinator (Empty stub)

**File**: `SceneDirectionCoordinator.cs`
**Status**: Contains only `using System.Text;` — no class, no members.

**Decision**: Implement full coordinator — injector list, registration, ordered loop. Wire into `BuildPromptAsync`.

---

## 6. Theme Data Model

**Key entities**:
- `RPTheme` — theme entity with collections for PhaseGuidance, AI notes, keywords, etc.
- `RPThemePhaseGuidance` — `GuidanceText` (free-text + markers), `DirectiveText` (hard directives)
- `RPThemeAIGuidanceNote` — per-section AI notes, includes `HardConstraint` section (enum value 8)
- `GetThemePhaseGuidanceLines` — filters by phase, deduplicates, trims

**Key insight**: Markers like `[Pacing:slow]` are already parsed by `GetPacingMode()`, `AllowsWithinTimeframeTimeShift()`, `IsEpisodicBeatStyle()`, `IsQuickFinishClimaxMode()`. These utilities exist in `RolePlayAssistantPrompts.cs` — they need to be migrated into `SceneDirectionResolver` rather than duplicated.

---

## 7. Seed Data

**ThemeCatalogService.SeedDefaultsAsync()** seeds 10 catalog entries (keywords, stat affinities, fit rules) but NOT full `RPTheme` records with phase guidance. Full `RPTheme` records are created through the UI.

**Decision**: No existing seed data to migrate for phase guidance prose. The `BuildFramingGuards` text was never stored in the DB — it was always hardcoded in C#. Migration means creating this data in the theme records for the first time, not updating existing. This changes the migration scope: instead of "update existing themes," it's "populate phase guidance for themes that lack it."

---

## 8. Existing Test Patterns

### SceneWritingDirectivePromptTests
- Direct static calls to `RolePlayAssistantPrompts.BuildFramingGuards(phase, scenarioId, theme)`
- Inline `RPTheme` construction with `PhaseGuidance` list
- `Assert.Contains` / `Assert.DoesNotContain` for string presence
- Key tests: Climax guards (7), quick-finish (7), time-shift (2), intensity (4), climax fallback (4), position spanning (5), multi-perspective (3)

### SessionMemoryInjectionTests
- Reflection-based invocation of private `InjectSessionMemoryBlock`
- Factory methods for `EncounterSummaryRecord`
- Assertions on output string presence/absence
- Tests arc completion vs milestone rendering, cycle filtering, count limits

**Key insight**: Existing prompt tests are fragile (rely on exact string matching against `BuildFramingGuards`). After migration, these tests will need updating to match the new theme-driven prose. The spec's FR-013 (structural parity, not text equivalence) means we need NEW `PromptInjectorCaptureTests` rather than modifying existing string-match tests.

---

## Decisions

| Item | Decision | Rationale |
|------|----------|-----------|
| Injector catalog | 12 behavioral injectors (11 from current code + 1 new DirectorNoteInjector); 23 data blocks stay inline | Matches spec's priority-sorted loop design |
| BuildFramingGuards migration | Retire the method; prose goes to theme phase guidance | Spec FR-009; zero themes currently have this prose in DB |
| SceneDirectionResolver | Complete 5 helpers, define phase defaults, add Deepening resolution | Spec FR-010; currently never called |
| DeepeningPolicy | Add enum and field to SceneDirection | Spec Phase 3; needed for EscalationInjector |
| Coordinator | Full injector loop implementation | Spec FR-001 through FR-003 |
| Existing tests | Preserve as-is; new PromptInjectorCaptureTests added | Structural parity, not text equivalence |
| Theme seed migration | Create phase guidance prose in theme records for first time | No existing DB data to migrate; was always C#-only |
| Marker parsing utilities | Move from RolePlayAssistantPrompts to SceneDirectionResolver | Avoid duplication; resolver is the new single source of truth |

## Alternatives Considered

| Alternative | Rejected Because |
|-------------|------------------|
| Convert all 37 injects to injector pattern | ~23 data blocks have no behavioral logic; converting them adds ceremony without value |
| Keep BuildFramingGuards as fallback | Violates FR-009 (no hardcoded phase text) and repo's "no silent fallback" rule |
| Per-turn SceneDirection caching | Adds state complexity; per-prompt is simpler and matches current flow |
| Log-and-skip on injector failure | Violates repo's "fail fast" rule; hides configuration bugs |
