# Research: Wife Resistance & Cheating Motivation Gap

**Feature**: `001-wife-resistance-motivation` | **Date**: 2026-06-07

## Research Questions & Resolutions

### RQ-1: Can we add new canonical stats (Attentiveness, IntimacyEngagement) to model motivation drivers?

**Decision**: NO. Use behavioral dimensions instead.

**Rationale**: `RolePlaySessionCompatibilityService.RequiredStats = AdaptiveStatCatalog.CanonicalStatNames` — adding any stat to `CanonicalStats` automatically adds it to `RequiredStats`, which rejects ALL existing sessions with `UnsupportedSessionError`. `CharacterStatProfileV2` uses fixed C# properties (not a dictionary), so new canonical stats also require domain property additions + accessor reflection updates + snapshot schema changes + `CharacterStatTextCatalog` entries (3 roles × 4 bands = 12 new entries). Total blast radius: ~20 files.

**Alternatives considered**:
- New canonical stats: rejected for the cascade cost above.
- Behavioral dimensions in `BehavioralDimensionCatalog` + `EncounterStatsJson`: chosen. Values are per-CharacterProfile, flow through `CharacterBehavioralFrameGenerator` automatically, zero schema changes. Only cost: 4 new `BehavioralDimension` entries + validation whitelist extension.

### RQ-2: How should the ResistanceProfile be persisted and wired end-to-end?

**Decision**: Mirror the existing `StatWillingnessProfile` pattern exactly.

**Rationale**: The willingness profile has a proven, tested end-to-end pattern:
- Domain: `StatWillingnessProfile` + `WillingnessThreshold` in `DreamGenClone.Domain/StoryAnalysis/`
- Interface: `IStatWillingnessProfileService` in `DreamGenClone.Application/StoryAnalysis/`
- Implementation: `StatWillingnessProfileService` with `EnsureDefaultsAsync` seeding, `SaveAsync` validation (contiguous 0-100, single default, unique name)
- Persistence: `CREATE TABLE StatWillingnessProfiles` + UPSERT + 5 load/delete methods in `ISqlitePersistence`/`SqlitePersistence`
- DI: `AddScoped<IStatWillingnessProfileService, StatWillingnessProfileService>` in `Program.cs`
- Prompt: `ScenarioGuidanceGenerator.BuildWillingnessInterpretationAsync` loads profile, resolves band, appends to guidanceText
- Session: `AdaptiveScenarioState.SelectedWillingnessProfileId`, persisted on `RolePlayV2AdaptiveStates`
- UI: Tab in `ThemeProfiles.razor` with list + JSON threshold editor + Start/Select/Save/Delete methods
- Facade: `StoryAnalysisFacade` passthrough methods

The ResistanceProfile copies this at every layer. Differences: (a) Resistance uses HARD CONSTRAINT injection (not guidanceText append), (b) resistance band selection uses effective stat = min(targetStat + motivationScore, 100) instead of raw stat lookup, (c) adaptive panel must display active profile + band.

### RQ-3: How should the motivation score be computed?

**Decision**: Simple equal-weight average of four profile-level inputs.

Formula: `motivationScore = ((100 - Husband.Attentiveness) + (100 - Husband.IntimacyAvailability) + (100 - Wife.SelfRespect) + OtherMan.PersistencePastLimits) / 4`

All inputs normalized so higher = more motivation (Husband neglect and Wife low self-respect are inverted; OtherMan persistence is direct). Missing inputs default to 50 (neutral). Score clamped to [0, 100].

**Rationale**: Fixed in code (not configurable per profile) per Q1 clarification. Equal weighting avoids premature optimization — four equally-weighted drivers are simple to understand, test, and reason about. The ResistanceProfile bands themselves handle the non-linear mapping from score to directive.

**Alternatives considered**:
- Weighted average with per-profile multipliers: more flexible but adds UI complexity for weight configuration. Rejected per user decision.
- Threshold-based count: each driver independently crosses threshold, count active drivers. Rejected — loses nuance of degree (Attentiveness=0 vs 24 both cross same threshold).
- Configurable formula per profile: maximum flexibility but violates simplicity. Rejected per user decision.

### RQ-4: How does the motivation score select a resistance band?

**Decision**: Add to effective stat value before ResistanceProfile band lookup.

Formula: `effectiveStat = min(targetStatValue + motivationScore, 100)`

The ResistanceProfile's existing contiguous bands then resolve `effectiveStat` to the appropriate resistance directive. No separate band-shift concept.

**Rationale**: This is the simplest architecture — it reuses the ResistanceProfile's own band structure. A Wife with Loyalty=70 and motivation=25 resolves as if her Loyalty were 95. The profile bands for 95 produce the resistance directive. No new domain entities, no separate shift tables.

**Alternatives considered**:
- Fixed tiers per 25 motivation points: creates a parallel band system disconnected from the ResistanceProfile. Rejected — duplication.
- Per-profile shift mapping: adds profile complexity. Rejected per user decision.

### RQ-5: How should the resistance directive be injected into the prompt?

**Decision**: HARD CONSTRAINT line positioned before escalation guidance.

Format: `HARD CONSTRAINT — {WifeLabel} resistance directive (authoritative, overrides escalation guidance): {resistanceBandText}`

Placed in the prompt section order immediately after per-character current-state HARD CONSTRAINT lines, before the escalation guidance section.

**Rationale**: A soft guidanceText append (like WillingnessProfile) would be drowned out by the louder, more numerous escalation lines. HARD CONSTRAINT is the only format that can credibly counter "advance the scene / progress intimacy". Per user decision.

### RQ-6: How should escalation guidance be made target-aware?

**Decision**: In `AppendEscalationGuidance`, before emitting push-forward lines, check the Wife's resolved resistance band. If the band indicates firm resistance, skip the "advance the scene", "progress intimacy", and "don't stay clothed" lines. Also drop the legacy "Tension" stat reference (not a canonical stat — always resolves to 50 fallback).

**Rationale**: The current method checks only the actor's stats (not the target Wife's) and references a non-canonical Tension stat. Making it target-aware requires: (a) resolving the Wife's character stats specifically, (b) checking her resolved resistance band, (c) conditionally suppressing escalation lines when the band is firm. The bands themselves define what "firm" means via the ResistanceProfile, so no hardcoded thresholds.

### RQ-7: How should sessions be handled at cutover?

**Decision**: Purge all existing roleplay sessions at cutover. Follow the B-038 pattern.

**Rationale**: New `SelectedResistanceProfileId` column on `RolePlayV2AdaptiveStates` is additive. Seeded default ResistanceProfile ensures all new sessions auto-select sensible gating. No backfill needed for old sessions — they are purged. This matches the established B-038 cutover approach (V1 → V2 migration).

### RQ-8: Where should the active ResistanceProfile be displayed to the user?

**Decision**: RP workspace adaptive panel, alongside the existing WillingnessProfile readout.

**Rationale**: Per Q4 clarification. The adaptive panel already shows willingness profile status. Adding the resistance profile name + current resolved band gives the user visibility into which profile is governing the Wife's resistance without navigating away from the session. Required by FR-016.

## Technology Best Practices Confirmed

- **BehavioralDimensionCatalog pattern**: Static class with `BehavioralDimension` record entries, `GetDimensions(role)`, `ResolveTierText(role, name, value)`. Proven pattern used for 14 existing dimensions across 3 roles. Adding 4 more follows precedent exactly.
- **StatToDimensionMappings pattern**: Static `ApplyDelta(encounterStats, targetRole, statName, statDelta)` with slope×delta drift. New dimensions need corresponding drift rules (e.g., Restraint drops → BoundaryFirmness increases).
- **CharacterProfile validation**: `ValidateStats` enforces EncounterStats keys must be in `BehavioralDimensionCatalog.GetDimensions(TargetRole)`. Adding new dimensions to the catalog automatically gates them through validation — but the role-specific whitelist must be confirmed (Wife currently allows 4 dims; adding 2 more is safe).
- **ISqlitePersistence pattern**: Each profile type has 5 methods (Save, Load, LoadDefault, LoadAll, Delete). Adding 5 more for StatResistanceProfile follows the exact naming convention.
- **ThemeProfiles.razor tab pattern**: Each tab has a button in the nav, a content block with list + edit form, and @code methods (StartCreate, Select, Save, Delete). Cloning the willingness tab is the established approach.
