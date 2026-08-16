# Continuation Settings — Marker Implementation Audit

**Created:** 2026-08-13
**Feature:** B-082 — continuation settings popup (pacing, phase-guidance markers, word-count override).
**Purpose:** For each setting the popup exposes, document (a) what it is **supposed** to do, (b) what is **actually implemented**, and (c) the gap. Sources are verified against current code and the spec/debug artifacts below.

**Method:** For each dimension, traced the marker → `SceneDirectionResolver`/engine → slot consumer. Consumers verified by searching `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/**` for the resolved field names.

**Authoritative references consulted:**

- `DreamGenClone.Domain/RolePlay/SceneDirection.cs` — enum doc comments (intended semantics).
- `.github/instructions/rp-prompt-injection-reference.instructions.md` — historical marker→injector map + prompt text (pre-redesign; describes intended behavior, architecture is outdated).
- `specs/001-rp-prompt-redesign/research.md` (§R5) — injector→slot absorption map for the 17-slot rebuild.
- `specs/001-rp-prompt-redesign/debug/015-scene-direction-not-wired.md` — "Scene Direction Not Wired", **Resolution [Pending]**.
- `specs/001-rp-prompt-redesign/debug/021-pacing-position1-only-full-scene.md`, `debug/022-pacing-other-actors.md` — pacing wiring history.
- `specs/001-final-writing-instruction/contracts/slot-17-output-contract.md` — pacing text mapping.
- `.github/instructions/pacing-directive-findings.instructions.md` — corrected pacing findings.

---

## Summary table

| # | Setting | Marker(s) | Resolved field | Consumer (current) | Status |
|---|---|---|---|---|---|
| 1 | Pacing | `[Pacing:slow\|medium\|fast]` | `SceneDirection.Pacing` | `FinalInstructionSlot` (Slot 17) | ✅ wired |
| 2 | Deepening | `[Deepening:subsequent-actors]` | `SceneDirection.Deepening` | `TurnContextSlot` (Slot 3) | ✅ wired |
| 3 | Time Shift | `[TimeShift:none\|small\|medium\|large\|within-timeframe]` | `SceneDirection.TimeShift` | **none** | ❌ not wired |
| 4 | Beat Style | `[BeatStyle:single\|short\|episodic]` | `SceneDirection.BeatScope` | **none** | ❌ not wired |
| 5 | Scene Presence | `[ScenePresence]` | `SceneDirection.RequireScenePresence` | **none** | ❌ not wired |
| 6 | Granularity | `[Granularity:micro\|meso\|macro\|montage]` | `SceneDirection.Granularity` | **none** | ❌ never wired |
| 7 | Climax Mode | `[ClimaxMode:multi-encounter\|quick-finish]` | engine check | `RolePlayEngineService` + semantic job | ✅ wired (engine-side) |
| 8 | Aftermath | `[Aftermath:husband-contrast]` | engine check | `RolePlayEngineService` | ✅ wired (engine-side) |
| 9 | Word Count | `[targetwords:small\|medium\|large]` | word-target range | `WritingStyleSlot` (Slot 18) | ✅ wired (with quirk) |

**Key finding:** three scene-direction dimensions (`TimeShift`, `BeatScope`, `ScenePresence`) were parsed but never wired to any prompt slot, and `Granularity` was added later with no consumer at all. Debug 015 ("Scene Direction Not Wired") remains **Resolution [Pending]**. The B-082 override slot renders these four **only when the user explicitly sets an override** — so an explicit user choice works, but a *theme* declaring `[TimeShift:*]`, `[BeatStyle:*]`, `[ScenePresence]`, or `[Granularity:*]` still produces **no prompt effect**.

---

## 1. Pacing — ✅ wired

- **Supposed to do** (`ScenePacing` enum): control beat advancement per turn. Slow = "advance within the current beat — deepen, do not leap"; Medium = "advance one beat with forward momentum"; Fast = "compress multiple beats into one response".
- **Implemented**: `FinalInstructionSlot` (Slot 17, Zone C) emits a HARD CONSTRAINT for **all** Character positions:
  - Position 1: raw Slow/Medium/Fast text (e.g. `HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat. Move the story forward.`).
  - Positions 2+: fixed containment line (`…subsequent actor — build on the beat already established this turn. Do not restart or jump past it.`).
  - Narrative variant: no pacing line (Action block instead).
- **Override path**: `ContinuationOverrideResolver.ApplySceneDirection` sets `SceneDirection.Pacing`, so the existing slot picks it up unchanged.
- **Notes**: `SystemPrimerSlot` contains a glossary pacing line that is definitional, not a directive.

## 2. Deepening — ✅ wired

- **Supposed to do** (`DeepeningPolicy`): positions 2+ deepen the current beat from their POV, never advance, orthogonal to pacing.
- **Implemented**: `TurnContextSlot` (Slot 3, Zone A) emits `- You are a subsequent actor this turn. Deepen the moment… Do not advance to a new beat or position.` for position 2+ when `Deepening == SubsequentActors`.
- **Override path**: applied via `SceneDirection.Deepening`.

## 3. Time Shift — ❌ not wired

- **Supposed to do** (`TimeShiftPolicy`): control whether/how far the story may jump forward in time (None = stay; Small = minutes–hours; Medium = hours–half day; Large = a day+). Historical `SceneTimeDirectionInjector` (pri 70) emitted "Stay in the current moment…" / "Time must advance significantly…"; `TimeLocationInjector` (pri 10) allowed position 2+ to shift time/location. Debug 015 proposed: *"Small time shifts allowed (minutes to hours). Use transitions like 'later that evening', 'after supper'…"*.
- **Implemented**: `SceneDirectionResolver` parses the marker and sets `TimeShift`, but **no slot reads `TimeShift`**. Redesign §R5 said `SceneTimeDirectionInjector → IntensityPacingSlot`, but `IntensityPacingSlot` was later reduced to "available positions only" (001-final-writing-instruction consolidation), leaving TimeShift with no consumer.
- **Artifact**: `specs/001-rp-prompt-redesign/debug/015-scene-direction-not-wired.md` — **Resolution [Pending]**.
- **B-082 override**: rendered by `ContinuationOverrideSlot` only when `TimeShift` is explicitly overridden.

## 4. Beat Style / Beat Scope — ❌ not wired

- **Supposed to do** (`BeatScope`): Single = resolve in one turn; Short = build across 2–3 turns; Extended (episodic) = linger 4+ turns. Historical note: `[BeatStyle:single]` / `[BeatStyle:short]` were **metadata-only** (no injector); `[BeatStyle:episodic]` fired `BeatStageInjector` (pri 90): "stay present in the moment — deepen sensory/emotional detail." Redesign §R5 said `BeatStageInjector → ScenarioGuidanceSlot`.
- **Implemented**: parsed by `SceneDirectionResolver`; **no slot reads `BeatScope`** (verified `ScenarioGuidanceSlot` does not render it).
- **B-082 override**: rendered by `ContinuationOverrideSlot` only when `BeatScope` is explicitly overridden.

## 5. Scene Presence — ❌ not wired

- **Supposed to do** (`RequireScenePresence`): opt-in "stay present, no time-skip" contract (`ScenePresenceInjector`, pri 75).
- **Implemented**: parsed (`[ScenePresence]` → `RequireScenePresence = true`); **no slot reads it**. Redesign §R5 deliberately routed `ScenePresenceInjector → SceneContinuityAnchorSlot` as "cross-perceptions only", i.e. the stay-present contract was **dropped** in the 17-slot rebuild.
- **B-082 override**: rendered by `ContinuationOverrideSlot` only when `RequireScenePresence` is explicitly overridden.

## 6. Granularity — ❌ never wired

- **Supposed to do** (`NarrativeGranularity`): narrative density per response — Micro = one moment; Meso = one scene/beat; Macro = a day/significant span; Montage = multiple days–weeks. Defined in `SceneDirection.cs` with `[Granularity:micro|meso|macro|montage]` markers.
- **Implemented**: parsed by `SceneDirectionResolver`; **never had any consumer** — not in the historical 13-injector map, not in the 17-slot rebuild. This dimension was added to the resolver but never surfaced.
- **B-082 override**: rendered by `ContinuationOverrideSlot` only when `Granularity` is explicitly overridden.

## 7. Climax Mode — ✅ wired (engine-side)

- **Supposed to do**: `[ClimaxMode:multi-encounter]` splits the Climax into discrete encounters (encounter-boundary detection + time-skip between them); `[ClimaxMode:quick-finish]` is retired (prose moved to guidance).
- **Implemented**: `RolePlayAssistantPrompts.IsMultiEncounterClimax` gates the `minIxns=4` guard in `TryDetectEncounterBoundaryAsync` and the `CloseScene` transition; `SemanticInteractionAnalysisJobHandler.IsMultiEncounterClimaxActiveAsync` excludes `encounter-completed` from the async job.
- **B-082 override**: `ContinuationOverrideResolver.ResolveMultiEncounterClimax` = `override ?? theme-marker`, consulted at both decision points (Climax phase gate preserved in the async job).

## 8. Aftermath — ✅ wired (engine-side)

- **Supposed to do**: `[Aftermath:husband-contrast]` adds an `AftermathCoupleInteraction` time-skip phase after an encounter — wife acts normal to her husband (secret-vs-ordinary contrast). Reset excluded (B-056).
- **Implemented**: `RolePlayAssistantPrompts.IsAftermathHusbandContrast` drives the `CloseScene → AftermathCoupleInteraction → AdvanceTime` transitions in `ContinueAsAsync` / `TryDetectEncounterBoundaryAsync`.
- **B-082 override**: `ContinuationOverrideResolver.ResolveAftermathHusbandContrast` = `override ?? theme-marker`, with the Reset exclusion preserved.

## 9. Word Count — ✅ wired (with quirk)

- **Supposed to do**: `[targetwords:small|medium|large]` → target range 200–400 / 300–700 / 500–1000.
- **Implemented**: `RolePlayAssistantPrompts.GetWordTargetMarker` + `RolePlayContinuationService.ResolveWritingStyleAsync`; `WritingStyleSlot` (Slot 18) emits `Word Target: Target {min}-{max} words.` (Narrative derives `min*2` / `min(max*2,1500)`).
- **Quirk (pre-existing)**: `ResolveWritingStyleAsync` always applies the marker range, defaulting to `"small"` when no marker is present — so the `SteeringProfile.WordTargetMin/Max` values are **effectively inert** for the Character variant (only used for FR-006 validation). `NarrativeWordTargetMin/Max` are computed but `WritingStyleSlot` ignores them (it derives from `WordTargetMin/Max`).
- **B-082 override**: `ContinuationOverrideResolver.ApplyWordCount` sets `WordTargetMin/Max` directly, so the slot picks it up unchanged.

---

## Gaps to close (candidates for follow-up)

1. **Wire TimeShift / BeatScope / ScenePresence / Granularity into the prompt** so theme markers work, not just explicit overrides. Debug 015 ("Scene Direction Not Wired") is the authoritative pending artifact. Minimal options: (a) extend `FinalInstructionSlot`/a dedicated slot to render the resolved values when non-default, or (b) confirm these dimensions were intentionally dropped and deprecate the markers.
2. **Word-count profile-values quirk** — decide whether `SteeringProfile.WordTargetMin/Max` should actually be used (current code always overrides with the marker range, default "small").
3. **Consistency** — the B-082 override currently *does* render the four dead dimensions (only when explicitly set), which is more than the theme-marker path does for those same dimensions; decide whether the theme-marker path should match.

## References

- `specs/001-rp-prompt-redesign/research.md` §R5 (injector→slot map)
- `specs/001-rp-prompt-redesign/debug/015-scene-direction-not-wired.md` (Resolution [Pending])
- `specs/001-rp-prompt-redesign/debug/021-pacing-position1-only-full-scene.md`
- `specs/001-rp-prompt-redesign/debug/022-pacing-other-actors.md`
- `specs/001-final-writing-instruction/contracts/slot-17-output-contract.md`
- `.github/instructions/rp-prompt-injection-reference.instructions.md` (historical marker→injector text)
- `.github/instructions/pacing-directive-findings.instructions.md`
