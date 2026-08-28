# B-082: Continuation Settings Popup — Pacing, Phase-Guidance Markers & Word Count Override

**State**: `designed` (plan persisted, pending implementation confirmation)
**Priority**: high
**Scope**: medium
**Date**: 2026-08-13
**Backlog**: `specs/Planning/backlog.md` (B-082)

---

## TL;DR

Add a **Settings** button on the Continue As line (alongside You / NPC / Custom / Next Phase / Steer / Finish) that opens a popup where the user can override continuation settings **persistently for the session — they stay in effect until the user changes or clears them**:

1. **Pacing** — Slow / Medium / Fast.
2. **The other phase-guidance markers** — Beat Style (BeatScope), Time Shift, Granularity, Deepening, Scene Presence, Climax Mode, Aftermath.
3. **Target word count** — presets (small/medium/large) and/or a custom min–max.

The chosen values are stored as a `ContinuationOverride` on the `RolePlaySession` (persisted automatically with the session JSON, exactly like the existing `IntensityFloorOverride` / `IntensityCeilingOverride`), read by the engine and the continuation service, folded into the resolved `SceneDirection` / `WritingStyle`, and injected into the prompt. Every option shows a human-readable description; selections that conflict with the current phase or with each other are disabled in the popup.

**All markers are treated the same** — `[ClimaxMode:*]` and `[Aftermath:husband-contrast]` are included. They differ from the others only in *what consumes them* (see "Engine markers" below): the prompt-only markers are read while building one prompt, whereas Climax Mode / Aftermath are read by the engine *between* prompts and change multi-turn behavior (encounter splitting + the time-skip state machine). Because the override is now sticky and persisted, the engine can consult the same override at those decision points, so including them is consistent and safe.

**Mental model:** this is a UI for the same `[Marker:value]` controls that today live only inside the theme's phase guidance. The override substitutes the chosen value at the exact same resolution points those markers are read from (scene direction + word target + engine markers) — so the user can change pacing, beat style, time shift, etc. for this session without leaving the workspace to edit the Theme. Applied to **every** prompt in a continuation (whole-batch), because the override sits at the same decision point the theme marker would have occupied.

---

## Background (verified findings)

### 1. The 17-slot prompt pipeline (current, post-redesign)

Prompts are built by `RolePlayPromptBuilder` (`DreamGenClone.Web/Application/RolePlay/Prompts/`), which runs 17+ slots sorted by `PromptZone` then `Order`. The `PromptSlotId` enum (`DreamGenClone.Domain/RolePlay/PromptSlotId.cs`) is the frozen normative contract; `RolePlayPromptBuilder.GetExpectedZone/GetExpectedOrder` validate each registered slot at startup. Slots are registered explicitly in `DreamGenClone.Web/Program.cs` (lines ~137–167).

Relevant slots for this feature:

| Slot | Order | Zone | Owns |
|---|---|---|---|
| `TurnContextSlot` | 3 | A | Turn number, position, **Deepening** (position 2+ deepening directive) |
| `FinalInstructionSlot` | 17 | C | Theme contract, scene guidance/direction, **Pacing HARD CONSTRAINT**, narrative Action block |
| `WritingStyleSlot` | 18 | C | Style guide incl. **Word Target** line |

### 2. Scene direction resolution

`SceneDirectionResolver.Resolve(phase, activeTheme, climaxSubPhase, intent)` (`DreamGenClone.Web/Application/RolePlay/SceneDirectionResolver.cs`) produces an immutable `SceneDirection` record (`DreamGenClone.Domain/RolePlay/SceneDirection.cs`) with:

- `Pacing` (Slow/Medium/Fast) — **consumed** by `FinalInstructionSlot`.
- `Deepening` (None/SubsequentActors) — **consumed** by `TurnContextSlot`.
- `BeatScope` (Single/Short/Extended), `TimeShift` (None/Small/Medium/Large), `Granularity` (Micro/Meso/Macro/Montage) — **parsed but currently UNCONSUMED** by any slot (dead dimensions; see repo memory `theme-guidance-marker-hygiene.md`).
- `RequireScenePresence` (bool) — parsed from `[ScenePresence]`; **no slot consumes it in the current pipeline**.

`ResolveIntensityAsync` (`RolePlayContinuationService.cs` ~1071) calls the resolver once per prompt and stores the result in `ResolvedIntensityData.SceneDirection`.

**Critical consequence for this feature**: overriding `BeatScope` / `TimeShift` / `Granularity` / `ScenePresence` today would be a **no-op** because no slot renders them. The plan below wires those dimensions into a dedicated override slot so an explicit user choice actually reaches the prompt — but it deliberately does **not** start rendering these dimensions in *every* prompt (that would add 4+ noise lines to all prompts and regress prompt size for zero benefit).

### 3. Word target resolution

`ResolveWritingStyleAsync` (`RolePlayContinuationService.cs` ~903) builds `ResolvedWritingStyleData`:

- Base values from the session's `SteeringProfile` (`WordTargetMin/Max`, `NarrativeWordTargetMin/Max`), fail-fast when invalid (FR-006).
- `RolePlayAssistantPrompts.GetWordTargetMarker` reads `[targetwords:small/medium/large]` from the active theme's phase guidance; the marker range then **overrides** the profile range: `small=(200,400)`, `medium=(300,700)`, `large=(500,1000)`.
- `WritingStyleSlot` emits `Word Target: Target {min}-{max} words.` (Narrative uses `min*2` / `min(max*2,1500)`).

So the word count is already marker-driven — the popup reuses the same three sizes (and can add a free numeric range) as a sticky override on top.

### 4. Continue As flow (where the override must travel)

All Continue As actions funnel through `RolePlayEngineService.ContinueAsAsync(ContinueAsRequest, …)` (`RolePlayEngineService.cs` ~1394):

- `ContinueAsRequest` (`DreamGenClone.Web/Domain/RolePlay/ContinueAsRequest.cs`) currently carries `SelectedIdentityIds`, `SelectedParticipants`, `IncludeNarrative`, `CustomIdentityName`, `TriggeredBy`, `IsClearAction`.
- The UI builds it via `BuildContinueRequest(...)` in `RolePlayWorkspace.razor` (~7156) and dispatches via `ExecuteContinueAsync` → `RolePlayEngine.ContinueAsAsync`.
- Inside the engine, one submission can generate **several** prompts: opening narrative, per-identity sequential continuations, overflow multi-actor batch, and the closing auto-narrative. All go through `IRolePlayContinuationService.ContinueAsync` / `ContinueNarrativeAsync` / `ContinueBatchAsync`.
- The main overflow **Continue** button (`SubmissionSource.MainOverflowContinue`) and the You/NPC/Custom quick buttons all use this same path — so attaching the override to `ContinueAsRequest` covers the whole Continue As line.

The `…` (SubmitPromptAsync / staged-direction) path is a separate flow and is **out of scope** for this item (noted as an extension below).

### 5. Pacing position facts (from `pacing-directive-findings.instructions.md`, corrected)

- Phase-default pacing is **all Medium** (`SceneDirectionResolver.PhaseDefaultPacingMap`).
- `FinalInstructionSlot` emits the pacing HC for **all** character positions: position 1 gets the raw Slow/Medium/Fast line; positions 2+ get a fixed "subsequent actor — build on the beat" containment line (this replaced the older position-1-only behavior).
- `SystemPrimerSlot` contains a *glossary* pacing line ("Fast: advance through multiple beats") that is definitional and must not be mistaken for an active directive.

---

## Design

### Decision summary

| # | Decision | Choice |
|---|---|---|
| D1 | Where the override state lives | `ContinuationOverride` property on `RolePlaySession`, persisted via session JSON. **Sticky** until changed/cleared. |
| D2 | Which markers are overridable | **All** markers: `Pacing`, `BeatScope`, `TimeShift`, `Granularity`, `Deepening`, `RequireScenePresence`, `ClimaxMode`, `Aftermath` + word count. |
| D3 | How the override reaches the prompt | Fold into resolved `SceneDirection`/`WritingStyle` (so existing slots work unchanged) + one new slot that renders only the otherwise-dead dimensions. |
| D4 | Which continuation prompts receive the override | **All** prompts generated by a continuation (every actor + narrative) — every time, while the override is set. |
| D5 | Word-count override form | Presets small/medium/large **plus** optional custom min–max. |
| D6 | Validity gating | UI disables phase-incompatible and pairwise-conflicting selections; server applies as-is (dimensions are independent). |
| D7 | Marker catalog | New static `ContinuationMarkerCatalog` (Web/Application/RolePlay) — single source of descriptions + validity rules. |

---

### 1. `ContinuationOverride` model

New file `DreamGenClone.Web/Domain/RolePlay/ContinuationOverride.cs`:

```csharp
public sealed class ContinuationOverride
{
    // Scene-direction dimensions (read at prompt build).
    public ScenePacing? Pacing { get; set; }
    public BeatScope? BeatScope { get; set; }
    public TimeShiftPolicy? TimeShift { get; set; }
    public NarrativeGranularity? Granularity { get; set; }
    public DeepeningPolicy? Deepening { get; set; }
    public bool? RequireScenePresence { get; set; }

    // Engine markers (read between prompts by RolePlayEngineService).
    // Null = theme marker decides; true/false = force on/off regardless of theme.
    public bool? ForceMultiEncounterClimax { get; set; }    // [ClimaxMode:multi-encounter]
    public bool? ForceAftermathHusbandContrast { get; set; } // [Aftermath:husband-contrast]

    // Word count.
    public int? WordTargetMin { get; set; }
    public int? WordTargetMax { get; set; }

    public bool HasAny => Pacing.HasValue || BeatScope.HasValue || TimeShift.HasValue
        || Granularity.HasValue || Deepening.HasValue || RequireScenePresence.HasValue
        || ForceMultiEncounterClimax.HasValue || ForceAftermathHusbandContrast.HasValue
        || WordTargetMin.HasValue || WordTargetMax.HasValue;

    public bool HasSceneDirectionOverride => Pacing.HasValue || BeatScope.HasValue
        || TimeShift.HasValue || Granularity.HasValue || Deepening.HasValue
        || RequireScenePresence.HasValue;
}
```

- Nullable fields = "no override" (fall back to theme marker → phase default); a set value wins.
- `DeepeningPolicy.None` is a *valid explicit choice* (force no deepening even if the theme declares `[Deepening:subsequent-actors]`); the null sentinel distinguishes "not set".
- `ForceMultiEncounterClimax` / `ForceAftermathHusbandContrast` are tri-state bools: `null` = theme decides, `true` = force on, `false` = force off (overrides a theme that has the marker).
- Word-count override is applied after the profile/marker resolution (D5).

### 2. Marker catalog (descriptions + validity rules)

New static `DreamGenClone.Web/Application/RolePlay/ContinuationMarkerCatalog.cs`. Each dimension exposes: label, description, values with per-value descriptions, and phase/pairwise validity rules. Descriptions mirror the enum doc comments in `SceneDirection.cs` so UI prose and injected prose stay consistent.

| Dimension | Values (description) |
|---|---|
| **Pacing** | Slow — "stay within the current beat; deepen, do not leap". Medium — "advance one beat with forward momentum". Fast — "compress multiple beats into one response; push forward rapidly". |
| **Beat Style** | Single — "resolve this moment in one turn". Short — "build the moment across 2–3 turns". Episodic (Extended) — "linger in this moment for 4+ turns; deepen sensory/emotional detail". |
| **Time Shift** | None — "no time skip; continue from the exact moment". Small — "minutes to a few hours". Medium — "hours to half a day". Large — "a day or more". |
| **Granularity** | Micro — "one response = one moment". Meso — "one response = one scene/beat (default)". Macro — "one response = a day/significant span". Montage — "one response = multiple days to weeks, selected highlights". |
| **Deepening** | None — "standard pacing applies to all actors". Subsequent Actors — "positions 2+ deepen the current beat from their POV; never advance". |
| **Scene Presence** | Off / On — "opt in to a stay-present, no-time-skip contract". |
| **Climax Mode** | No override / Normal / Multi-encounter — "split the Climax into several discrete encounters; the engine watches for an encounter ending, then injects a time-skip and starts a new encounter". (The legacy `quick-finish` value is retired — its prose lives in the theme guidance.) |
| **Aftermath** | No override / Off / Husband Contrast — "after an encounter ends, the wife returns to the normal setting and acts normal to her husband — the secret-vs-ordinary contrast is the point" (any phase except Reset). |

**Validity rules (D6)** — enforced by disabling options in the popup (with a tooltip/label explaining why):

1. **Opening phase**: disable Deepening (no established beat yet).
2. **Reset phase**: disable Granularity=Micro (Reset advances time; a one-moment response contradicts it).
3. **Pairwise — presence vs time-shift**: if Time Shift override is non-None, disable Scene Presence=On (and vice versa). Presence = "no time skip".
4. **Pairwise — pacing vs beat scope**: Pacing=Fast disables Beat Style=Episodic (Extended); Pacing=Slow disables Beat Style=Single.
5. **Pairwise — deepening vs granularity**: Deepening=Subsequent Actors disables Granularity=Macro/Montage (deepen-the-beat contradicts time-spanning density).
6. **Phase — Climax Mode**: only enabled during the Climax phase (multi-encounter splitting is a Climax concept).
7. **Phase — Aftermath**: disabled during the Reset phase (out of scope per B-056) — otherwise enabled.

The catalog is the **single** source for both UI rendering and (optionally) a defensive server-side `Validate` helper. No hardcoded strings in the Razor file.

### 3. Override application

**Engine markers (Climax Mode / Aftermath) are applied in `RolePlayEngineService`, not here** — see "Engine markers" below. Everything else is applied in `RolePlayContinuationService.BuildPromptViaBuilderAsync`:

1. After `ResolveIntensityAsync` returns, apply the scene-direction override to the resolved `SceneDirection` (clone with `with { … }` for each set field). This makes `FinalInstructionSlot` (pacing) and `TurnContextSlot` (deepening) pick up the override with **no change to those slots**.
2. After `ResolveWritingStyleAsync` returns, apply the word-count override to `WordTargetMin/Max` (and set `NarrativeWordTargetMin/Max` consistently, e.g. `min*2` / `min(max*2,1500)` as `WritingStyleSlot` already does, or store the raw override and let the slot derive). This makes `WritingStyleSlot` emit the override with **no change**.
3. Store the override on `PromptBuildContext` as `ContinuationOverride? Override` so the new override slot knows which dimensions were explicitly chosen.

Precedence (single active path, no fallback): **override > theme marker > phase default** for scene direction; **override > marker range > SteeringProfile range** for word count; **override > theme marker** for Climax Mode / Aftermath. This mirrors the existing resolver tiers and does not introduce any new "default when missing" branch — a null override field simply means the theme/phase value is used (nullable).

### 4. New slot: `ContinuationOverrideSlot`

New file `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ContinuationOverrideSlot.cs`:

- `PromptSlotId.ContinuationOverride` (new enum value `21`), Zone C, Order **19** (after `WritingStyleSlot` 18, at the very end for recency). `IsTrimEligible=false`.
- `ShouldWrite` → `context.Override is not null && context.Override.HasSceneDirectionOverride` (word-count-only overrides do not need this slot — they already render in `WritingStyleSlot`).
- `WriteAsync` emits a compact, clearly-labelled block only for the dimensions the user actually set that are otherwise unrendered: **Beat Style, Time Shift, Granularity, Scene Presence**. Pacing/Deepening/word-count are already covered by slots 17/3/18, so the override slot does **not** duplicate them (keeps prompts lean; avoids the "contradictory duplicate directive" class of bug called out in B-052).

Example (user set Time Shift=Large + Granularity=Macro):

```
Scene Direction Override (user-selected for this turn):
  HARD CONSTRAINT — Time Shift: Large — time may advance a day or more.
  HARD CONSTRAINT — Granularity: Macro — cover a day's arc in this response.
```

Register the slot in `Program.cs` alongside the other `IPromptSlot` registrations and add its Zone/Order to `RolePlayPromptBuilder.GetExpectedZone/GetExpectedOrder`.

### 4b. Engine markers (Climax Mode / Aftermath) — researched

These two markers are the only ones that reach *outside* prompt text into engine state: they gate the encounter-boundary / time-skip state machine. Verified behavior and exact integration points:

**State machine** (`TimeSkipPhase` in `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`):
`None → CloseScene → AftermathCoupleInteraction → AdvanceTime → None`

| Marker | Read by | Effect when on |
|---|---|---|
| `[ClimaxMode:multi-encounter]` | `RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax")` | (a) turns on the `minIxns = 4` premature-advance guard in `TryDetectEncounterBoundaryAsync`; (b) after a boundary fires, sets `CurrentTimeSkipPhase = CloseScene` so the next overflow turn injects a time-skip. |
| `[Aftermath:husband-contrast]` | `RolePlayAssistantPrompts.IsAftermathHusbandContrast(theme, phase)` (returns false for Reset) | after a boundary fires, routes the time-skip through `AftermathCoupleInteraction` so the next turns inject the "act normal to your husband" closure before advancing time. |

**Exact decision points that must consult the sticky override** (all three have `session` in scope):

1. `RolePlayEngineService.ContinueAsAsync` (~1549–1550) — computes `hasMulti` / `hasAftermath` for the overflow time-skip injection block. These two booleans drive the CloseScene → (Aftermath | AdvanceTime) → None transitions and the directive text chosen for each stage.
2. `RolePlayEngineService.TryDetectEncounterBoundaryAsync` (~5632–5634) — computes `isMulti` / `isAftermath` after a successful `encounter-completed` detection. `isMulti` gates the `minIxns = 4` guard; `isMulti || isAftermath` decides whether `CurrentTimeSkipPhase` is set to `CloseScene` (vs. the encounter just ending with no time-skip).
3. `SemanticInteractionAnalysisJobHandler.IsMultiEncounterClimaxActiveAsync` (~400) — decides whether to **exclude `encounter-completed`** from the async semantic job (it is owned by the sync detection path). Must stay consistent with the engine's decision so the async job doesn't double-detect when the override is on, and doesn't suppress the event when it's off.

**Effective resolution (single decision path, no fallback):**

```csharp
// Applied at each site: override first, then theme marker.
var isMulti = session.ContinuationOverride?.ForceMultiEncounterClimax
    ?? RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax");
var isAftermath = session.ContinuationOverride?.ForceAftermathHusbandContrast
    ?? RolePlayAssistantPrompts.IsAftermathHusbandContrast(theme, phase);
```

- `null` = theme marker decides (existing behavior unchanged).
- `true` = force the behavior on even if the theme lacks the marker.
- `false` = force the behavior off even if the theme has the marker (e.g. disable multi-encounter splitting on a theme that declares it, or suppress the aftermath turn).

**Phase gates are preserved** — the override only replaces the *marker* value, never the phase logic:
- `IsMultiEncounterClimaxActiveAsync` still returns false outside the Climax phase (multi-encounter is a Climax concept; forcing it elsewhere has no effect because the sync detection path is Climax-bound in practice).
- `IsAftermathHusbandContrast`'s Reset exclusion still applies (the override does not re-enable aftermath in Reset — matches B-056's documented scope).

**Transition semantics with the override** (mirrors the existing `hasMulti`/`hasAftermath` logic):
- Force both on → CloseScene → AftermathCoupleInteraction → AdvanceTime → None.
- Force multi on, aftermath off → CloseScene → AdvanceTime → None (no aftermath turn).
- Force aftermath on, multi off → CloseScene → AftermathCoupleInteraction → None (aftermath-only closure, then resume).
- Force both off → no time-skip phase; the encounter ends and the story continues in place.

**Implementation note:** the theme load in the detection path uses `state.ActiveScenarioId` as the theme id (`_rpThemeService.GetThemeAsync(state.ActiveScenarioId)`); the override is consulted *after* that load, so no change to theme resolution is needed — the override is orthogonal to which theme is loaded.

**Risk note:** this is the riskiest part of the feature (encounter-boundary detection + the time-skip state machine). `MultiEncounterClimaxTests` / `MultiEncounterTimeSkipTests` already cover the theme-marker path; new tests must cover the override at all three decision points (see test plan).

### 5. Plumbing (implemented — sticky direct-read)

1. `RolePlaySession` gets `public ContinuationOverride? ContinuationOverride { get; set; }` — persisted automatically with the session JSON (like `IntensityFloorOverride`); **sticky** until changed/cleared. Survives page reload and app restart.
2. `RolePlayContinuationService.BuildPromptViaBuilderAsync` reads `session.ContinuationOverride` directly via `ContinuationOverrideResolver` — no request-object snapshot or method-parameter threading. This covers every continuation path (Continue As, `…`/SubmitPromptAsync, opening narrative, auto-narrative) uniformly.
3. `RolePlayEngineService` + `SemanticInteractionAnalysisJobHandler` marker decision points consult `ContinuationOverrideResolver.ResolveMultiEncounterClimax` / `ResolveAftermathHusbandContrast` (which read `session.ContinuationOverride` first).
4. The UI writes `session.ContinuationOverride` and persists it immediately via `RolePlayEngine.SaveSessionAsync` (the same pattern used for other session settings).
5. `RolePlayEngineService` marker decision points — the `IsMultiEncounterClimax` / `IsAftermathHusbandContrast` call sites and the multi-encounter guard consult `session.ContinuationOverride` first (see "Engine markers").

### 6. UI — popup + entry point

- New **Settings** button on the Continue As line, styled like the existing `rw-continue-chip` buttons, opening `_continuationSettingsPopupOpen`. Close the other popups on open (same pattern as the existing `OpenNextPhasePopupAsync`).
- Popup content (a new `ContinuationSettingsPopup.razor` component with `[Parameter]` for the current phase + the active theme + bindable override + close callback, keeping the 9,600-line `RolePlayWorkspace.razor` from growing further):
  - Each dimension as a row: label + description + **the effective current value** ("current") + radio-style buttons (with per-value description), starting with a "No override" (null) choice.
  - Word count row: No override / Small (200–400) / Medium (300–700) / Large (500–1000) / Custom (min + max numeric inputs).
  - Options disabled per the catalog validity rules, with a short "why disabled" hint.
  - Footer: **Clear all** (reset override to null — back to theme/phase defaults) and **Done** (close; the override stays active for all subsequent continuations).
- **Effective current values** are computed with the same resolvers the engine uses (single source, no divergence):
  - Scene-direction dimensions → `SceneDirectionResolver.Resolve(phase, activeTheme, ClimaxSubPhase.None, Message)`.
  - Climax Mode → `RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax")`; Aftermath → `RolePlayAssistantPrompts.IsAftermathHusbandContrast(theme, phase)`.
  - Word count → `RolePlayAssistantPrompts.GetWordTargetMarker` + the `[targetwords:*]` range (falling back to the session SteeringProfile range when no marker).
  - So the popup shows the theme marker value when the theme declares one, and the phase default (or profile default) when it does not — exactly matching what would be injected with no override.
- The popup also initializes from the current `session.ContinuationOverride` so an already-set override is shown as selected (with "No override" = fall through to the effective value).
- The Settings chip shows an active indicator (like the Custom chip's `is-active`) whenever `session.ContinuationOverride is not null && session.ContinuationOverride.HasAny`.

---

## Files touched (blast radius)

| File | Change |
|---|---|
| `DreamGenClone.Web/Domain/RolePlay/ContinuationOverride.cs` | **New** — override model. |
| `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs` | Add `ContinuationOverride` property (persisted with session JSON). |
| `DreamGenClone.Domain/RolePlay/PromptSlotId.cs` | Add `ContinuationOverride = 21`. |
| `DreamGenClone.Web/Application/RolePlay/ContinuationMarkerCatalog.cs` | **New** — descriptions + word presets. |
| `DreamGenClone.Web/Application/RolePlay/ContinuationOverrideResolver.cs` | **New** — applies the override to `SceneDirection`/`WritingStyle` + resolves engine markers. |
| `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs` | Add `ContinuationOverride? Override`. |
| `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ContinuationOverrideSlot.cs` | **New** — renders dead-dimension overrides. |
| `DreamGenClone.Web/Application/RolePlay/Prompts/RolePlayPromptBuilder.cs` | Zone/Order map for the new slot. |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | Read `session.ContinuationOverride`; apply to `SceneDirection` + `WritingStyle`; set `context.Override`. |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | Consult `session.ContinuationOverride` at the ClimaxMode/Aftermath decision points. |
| `DreamGenClone.Web/Application/RolePlay/SemanticInteractionAnalysisJobHandler.cs` | Consult `session.ContinuationOverride` in `IsMultiEncounterClimaxActiveAsync`. |
| `DreamGenClone.Web/Program.cs` | Register `ContinuationOverrideSlot`. |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | Settings button, popup, session override persistence. |
| `DreamGenClone.Web/Components/Pages/ContinuationSettingsPopup.razor` | **New** — popup component. |
| `DreamGenClone.Tests/RolePlay/**` | Unit tests (below). |

**No changes** to `SceneDirectionResolver` (pure resolver untouched — override applied by the caller via record cloning) or to `FinalInstructionSlot` / `TurnContextSlot` / `WritingStyleSlot` (they consume the already-overridden resolved data).

**Compliance**: exactly one active decision path per dimension — the override is applied once (scene direction + word count in `BuildPromptViaBuilderAsync`; engine markers at the `RolePlayEngineService` decision points), folded into the single `SceneDirection`/`WritingStyle` the slots read. No fallback/default branch is added; a null override field simply falls through to the existing theme-marker/phase-default path. The override is user-facing persisted data (on the session, editable only via the popup), so the no-fallback/no-hidden-default rules are unaffected.

---

## Test plan

1. **Override application** (`RolePlayContinuationService` or a small extracted helper):
   - Each set field wins over the theme marker and the phase default; unset fields leave the resolved value untouched.
   - Word-count override wins over both profile range and `[targetwords:*]` marker; narrative range derives consistently.
   - Effective-value resolver returns the theme marker value when present, else the phase default (or profile range for word count) — the popup's "current" display source.
2. **`ContinuationOverrideSlot`**:
   - `ShouldWrite` false when override null / word-count-only; true when a scene-direction dimension is set.
   - Renders Beat Style / Time Shift / Granularity / Scene Presence only when those exact fields are set; no duplication of pacing/deepening/word-count.
3. **Catalog validity gating**:
   - Opening disables Deepening; Reset disables Granularity=Micro; presence/time-shift and pacing/beat-scope and deepening/granularity conflicts are flagged.
4. **Plumbing**:
   - `RolePlayEngineService.ContinueAsAsync` passes `request.Override` to every continuation call (assert via a fake `IRolePlayContinuationService`).
   - `RolePlaySession.ContinuationOverride` round-trips through `SaveRolePlaySessionAsync` / `LoadRolePlaySessionAsync` (persistence).
5. **Engine markers** (override at all three decision points — true forces on, false forces off, null defers to the theme):
   - `RolePlayEngineService.ContinueAsAsync` — `hasMulti` / `hasAftermath` reflect the override (assert the injected time-skip directive and the CloseScene → Aftermath → AdvanceTime transition chosen).
   - `RolePlayEngineService.TryDetectEncounterBoundaryAsync` — `isMulti` gates the `minIxns=4` guard and `isMulti || isAftermath` decides `CurrentTimeSkipPhase = CloseScene`.
   - `SemanticInteractionAnalysisJobHandler.IsMultiEncounterClimaxActiveAsync` — excludes `encounter-completed` from the async job consistent with the override.
   - Phase gates preserved: multi-encounter override has no effect outside Climax; aftermath override has no effect in Reset.
6. **Existing tests** remain green — especially `SceneDirectionResolverTests` and the prompt-slot tests (the resolver and existing slots are untouched).

---

## Decisions

**Resolved:**
1. **Word-count form** — presets (200–400 / 300–700 / 500–1000) **plus** a custom min–max. ✅
2. **Engine markers** — Climax Mode / Aftermath are included now; see the researched "Engine markers" section for the exact integration points. ✅
3. **Popup implementation** — new `ContinuationSettingsPopup.razor` component (not inline in `RolePlayWorkspace.razor`). ✅
4. **Override scope** — whole-batch: the override substitutes the theme-guidance marker at the same resolution points, so it applies to every prompt in a continuation (all actors + narrative). ✅

All decisions resolved — ready to move to implementation.

## Implementation phases (suggested order)

1. `ContinuationOverride` model + `RolePlaySession.ContinuationOverride` + `ContinueAsRequest.Override` + catalog (no behavior).
2. Plumbing through continuation service + engine + `PromptBuildContext`.
3. Override application into `SceneDirection`/`WritingStyle` + new `ContinuationOverrideSlot` + registration.
4. UI popup + Settings button + session persistence wiring.
5. Tests + build + manual session verification (inspect the injected `promptText` in a debug session to confirm the override block, per the pacing-directive-findings checklist).
