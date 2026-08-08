# B-075: Per-Character Steering with Directional Options

**State**: `designed` (analysis + plan, pending confirmation)
**Priority**: high
**Scope**: large

---

## TL;DR

Expand the steering system from a single global free-text instruction to a **per-character steering model** with four directional options (away / neutral / towards / hard) that:

1. Targets a specific character (not the whole scene),
2. Generates **context-aware option text** for each of the four directions — shaped by the target character's **current stats, behavioral frame, encounter dimensions, active theme phase guidance, scene location, and recent interactions** (so a Desire 10 character produces different "towards" text than a Desire 90 one),
3. Injects the chosen **per-character steering directive** into the next continuation prompt via the 17-slot architecture,
4. Surfaces the **built prompt** and the **LLM response** in the UI so the user can see what was sent and what came back.

**This feature does NOT mutate character stats directly.** The current stats affect the *option text* (the four choices), not the post-apply stat math. When the user picks a direction and presses Continue, the existing semantic pipeline updates stats naturally from the generated narrative — exactly like any normal continuation. **B-020 stays `new`/unimplemented** and is out of scope here.

**Related items**: B-020 (steer commands — apply stat changes, `new` → deliberately NOT subsumed; deferred), B-049 (adaptive panel data visibility, `new` — prompt/response surface is a sibling concern), B-066 (character selection data in panel).

---

## Discovery Summary — What Already Exists

The existing steering system was verified in code. It is a **single global action** with two flow modes and **no stat mutation, no per-character targeting, and no injected-prompt surface in the UI**.

### Current Steer Flow (verified)

| Component | Location | Current Behavior |
|---|---|---|
| **Steer popup trigger** | `RolePlayWorkspace.razor` L8728–8738 — "Steer" chip in the `Continue As` row | `OpenSteerPopupAsync` (L5002); eligible when phase != Reset (`IsSteerEligible` L4508). |
| **Popup UI** | `RolePlayWorkspace.razor` L8932–8995 | Two flow toggles: **Direction** (free-text 4 options) and **Position** (sexual-position options from matrix). Generate / Regenerate paginated sets of 4 options. |
| **Option generation** | `GenerateSteerOptionsAsync` (L5073) → `BuildSteerOptionsPromptAsync` (L5119) | Calls `RolePlayAssistant.GenerateSuggestionAsync` with an LLM prompt. Returns a JSON array of 4 strings — **plain directives, no character targeting and no stat metadata**. |
| **Option generation context** | `AppendSteerPositionMatrixContextAsync` (L5323) | Only the Position flow enriches the prompt with `RPSteerPositionMatrixRow` bands and detected position. Direction flow gets no stat/matrix context. |
| **Apply** | `ApplySteerOptionAsync` (L5959) | Loads the selected option into `_promptText`, sets `_selectedIntent = PromptIntent.Instruction`, **closes the popup**. The user must then press Continue. No stat mutation occurs at apply. |
| **Submit path** | Engine `RolePlayEngineService.SubmitPromptAsync` L1224–1257 | **Dead code / legacy.** `ContainsSteerCommand` and `TryExtractSteerDirective` detect `/steer` and early-return. This path is superseded by B-074 staged flow (`+` stages without continuation; `…` runs the next continuation). |
| **Steer detection** | `ContainsSteerCommand` / `TryExtractSteerDirective` (engine L6534/L6559; continuation service L1443/L1454) | **Dead code.** `AppendSteerGuidance` (L1365) and `ResolveSteerDirective` (L1443) are defined but never called anywhere in the codebase. The enriched "Steer Flow Guidance" injection was never wired up. On the next continuation, the steer text only appears as a plain "Instruction: /steer blah" history entry via `InteractionHistorySlot`. |
| **Prompt injection** | None (dead code) | There is no working steer prompt injection. The `AppendSteerGuidance` method that would provide it is unreachable. |
| **Prompt slot surface** | None | There is no dedicated `SteeringSlot`. The steer directive is injected as a builder-level text append, not through the 17-slot architecture. |
| **Stat mutation** | None | B-020 is `new`/unimplemented. `/steer` produces no `CharacterStatProfileV2` deltas. No `ApplyTrackedDelta`, no `StatToDimensionMappings.ApplyDelta`. |
| **Per-character targeting** | None | The steer directive is a global scene instruction; it is injected for all actors equally in the continuation prompt. |
| **UI prompt/response surface** | None | The steer popup only *generates options*. The user never sees the actual continuation prompt that the steer directive ends up inside, nor the raw LLM response with reasoning. B-053 (prompt viewer tab) is `new`/unscoped here. |

### Stat Model (verified surface for delta mapping)

| Component | Location | Detail |
|---|---|---|
| Canonical stats | `AdaptiveStatCatalog` (`DreamGenClone.Application/StoryAnalysis/AdaptiveStatCatalog.cs`) | 5 stats: `Desire`, `Restraint`, `Dominance`, `Loyalty`, `SelfRespect`. Range 0–100, default 50. Legacy names normalized (`Arousal→Desire`, `Inhibition→Restraint`, etc.). |
| Per-character stats | `AdaptiveScenarioState.CharacterStats` (`Dictionary<string, CharacterStatProfileV2>`) | Keyed by character name. Each `CharacterStatProfileV2` has `Desire/Restraint/Dominance/Loyalty/SelfRespect` props + `BaselineStats` + `LastStatDeltas` + `RuntimeEncounterStats` + `CharacterRole`. |
| Stat accessor | `CharacterStatProfileV2Accessor` (`DreamGenClone.Application/RolePlay/`) | `GetStatOrDefault`, `SetStat`, `ApplyDelta`, and behavioral-dimension resolution from `RuntimeEncounterStats`. |
| Stat mutation paths | (1) `RolePlayAdaptiveStateService.ApplyTrackedDelta` (semantic scoring), (2) `DecisionPointService.ApplyDeltas` (decision points), (3) `RolePlayWorkspace.razor` `SetStat` (manual UI). All three must call `StatToDimensionMappings.ApplyDelta` per FR-011 of `001-stat-char-text-drift`. | A new steer path is a fourth mutation site and MUST route through the same `ApplyTrackedDelta`-equivalent path to satisfy FR-011. |
| Stat-to-dimension drift | `StatToDimensionMappings.ApplyDelta(stats, role, stat, delta)` (`DreamGenClone.Domain/StoryAnalysis/`) | Wife and Husband have rules; OtherMan has none. Steering deltas on Wife/Husband will drift encounter dimensions; OtherMan deltas will not. This is an explicit constraint on the design. |

### Theme "Push" Direction (verified)

| Component | Location | Detail |
|---|---|---|
| Theme description | `RPTheme.Description`, `RPTheme.Label` | The active theme's Description is the only "where the theme is going" signal. There is no `ThemeDirection`/`NarrativePush`/`PushVector` field on `RPTheme`. |
| Phase guidance | `RPTheme.GuidancePoints` (per-phase `Direction`/`Emphasis` text via `GetThemePhaseGuidanceLines`) | Phase-specific prose that describes the intended trajectory. This is what "towards the theme" currently means in practice. |
| Steer flow guidance text | `AppendSteerGuidance` L1372 | Currently says: `Requested steer direction: {directive}` + `Active theme anchor: {label}` + phase guidance. There is **no directional enum** (away/neutral/towards/hard) computed or injected today. |

The four B-075 directions must therefore be resolved **relative to the active theme's phase guidance text** at submit time — there is no pre-existing directional vector to read.

### Prompt Build Context (verified injection surface)

`PromptBuildContext` (`DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs`) is an immutable record built once per prompt by `RolePlayPromptBuilder`. It already carries per-character surfaces:

- `CharacterBehavioralFrames` — keyed by character ID/name → used by `BehavioralFramesSlot` (Slot 13).
- `CharacterStatStateTexts` — keyed by character label → used by `BehavioralFramesSlot`.
- `PinnedInteractions` — used by `PinnedContextSlot` (Slot 8).

There is **no `SteeringDirective` / `PerCharacterSteering` field** on `PromptBuildContext`. The current steer directive is injected at the builder level, bypassing the slot architecture. **B-075 should add a per-character steering field to `PromptBuildContext` and emit it through a dedicated slot** (see Design Decisions).

---

## Design Decisions

### D1 — Backend per-character steer model (new domain type)

Introduce a domain type carrying the resolved steering intent for one continuation:

```
RolePlaySteeringDirective (new, in DreamGenClone.Web/Domain/RolePlay/)
  string TargetCharacterId            // resolved character ID (matches CharacterStats key / CharacterPerspectives)
  string? TargetCharacterLabel       // display name for prompt + UI
  SteerDirection Direction            // enum: Away | Neutral | Towards | Hard
  string FreeTextDirective            // user/LLM-authored prose (from the option or manual entry)
```

- `SteerDirection` enum: `Away=0`, `Neutral=1`, `Towards=2`, `Hard=3` (`DreamGenClone.Web/Domain/RolePlay/SteerDirection.cs`).
- The directive is **one-shot**: the user picks a direction card → `+` stages it as a `PromptIntent.Instruction` interaction with `IsStagedDirection = true` → the next `…` continuation injects it via `StagedDirectionsSlot` and graduates it to history. No special persistence on `RolePlaySession` needed — the staged interaction row IS the directive.
- **No `StatDeltas` field.** This feature does not mutate stats directly — the current stats only shape the option text (D11), and the existing semantic pipeline updates stats naturally on the next continuation from the generated narrative, same as any other turn. B-020 (direct stat mutation on steer) is deliberately out of scope.

### D2 — REMOVED (was: Direction → stat-delta mapping)

**Removed per user clarification.** No `RPSteerDirectionProfile` table, no per-direction stat-delta configuration, no `Steer Directions` editor grid on `ThemeProfiles.razor`. Direct stat mutation is B-020, not B-075. The four directions are a **prompt-injection concern**, not a stat-mutation concern. See D11 for how the current stats shape the option text instead.

### D3 — Steering directive as a staged instruction (uses B-074/B-076 flow)

**No new session-level persistence needed.** The steering directive is carried as a standard staged `RolePlayInteraction` row with structured metadata:

```
RolePlayInteraction (existing, with new metadata field):
  InteractionType = System
  ActorName = "Instruction"
  Content = free-text directive prose  (visible in timeline)
  IsStagedDirection = true              (B-076: injects on next …, then graduates)
  + new: SteeringMetadataJson           // serialized RolePlaySteeringDirective (target, direction)
```

- **Stage**: `ApplySteerOptionAsync` builds the directive, creates the instruction interaction with `IsStagedDirection = true` + `SteeringMetadataJson`, and submits via `SubmissionSource.PlusButton` — the existing `+` / `AddPromptEntryAsync` flow.
- **Inject**: `StagedDirectionsSlot` (Order 9, already implemented per B-076) injects the staged row as `[Staged Scene Directions — Execute This Turn]` on the next `…` continuation. The slot already handles both Instruction and Character Message rows.
- **Graduate**: After the continuation, `GraduateStagedDirections` flips `IsStagedDirection` to false — the row becomes normal history.
- **One-shot**: Because `IsStagedDirection` graduates after one continuation, the directive is consumed automatically. No explicit clear step needed.
- **Pin for persistence**: The user can pin the staged row via the existing kebab menu → `PinnedContextSlot` injects it into every future prompt.

This avoids:
- A new `PendingSteeringDirectiveJson` column on `Sessions`.
- A separate consumption/clear step in the engine.
- Duplicated injection logic (the existing `StagedDirectionsSlot` already does this).
- The legacy `/steer` early-return path in `RolePlayEngineService`.

### D4 — REMOVED (was: Stat mutation on steer execution)

**Removed per user clarification.** Steer execution does not mutate stats directly. The engine only **injects the per-character steering directive** into the next continuation's prompt (D5). The existing semantic pipeline then updates the character stats naturally from the generated narrative on the continuation, exactly like any other turn — no `ApplyTrackedDelta` is called by the steer action itself, no `RolePlaySteeringDirective` deltas are produced, and `CharacterStatProfileV2.LastStatDeltas` is not touched by the steer. This explicitly leaves B-020 (`new`) out of scope.

### D5 — Per-character prompt injection (extends existing `StagedDirectionsSlot`)

Steering directives are injected through the **existing `StagedDirectionsSlot`** (B-076, Order 9) — no new slot needed. The slot already handles `IsStagedDirection = true` rows and emits them as `[Staged Scene Directions — Execute This Turn]`.

To make the injection per-character-aware, `StagedDirectionsSlot.WriteAsync` is extended to read `SteeringMetadataJson` from staged instruction rows:

- When `SteeringMetadataJson` is present (i.e. this staged row is a B-075 steering directive), emit a per-character block:
  ```
  [Staged Scene Directions — Execute This Turn]
  Steering Directive (Target: {TargetCharacterLabel}, Direction: {Direction}):
  {FreeTextDirective}
  - This directive applies to {TargetCharacterLabel}'s next actions and choices. Other characters are unaffected.
  ```
- When `SteeringMetadataJson` is absent (i.e. ordinary staged instruction/message), emit the existing generic format.

`StagedDirectionsSlot.ShouldWrite` already fires for both Character and Narrative variants when staged rows exist — no change needed. The per-character block's `"Other characters are unaffected"` line is sufficient to scope the directive; the Narrative variant gets it too (the LLM understands scoping).

**No new slot, no new `PromptBuildContext` field, no new `PromptSlotId` enum member.** The steering metadata travels on the interaction row itself, which the slot already reads.

The legacy dead code — `AppendSteerGuidance`, `ResolveSteerDirective`, `TryExtractSteerDirective` (continuation service copies) — is removed as part of this change since it was never called (verified: no call sites exist). The engine-side `ContainsSteerCommand` and `TryExtractSteerDirective` are also removed (the `/steer` early-return path is superseded by staged flow).

### D6 — Four-direction option generation (extends existing popup)

Extend `BuildSteerOptionsPromptAsync` (L5119) so the generated options are **tagged with one of the four directions**. Two options (kept simple, no new model call):

- **Option A — schema change**: ask the LLM to return `[{ "direction":"away", "text":"..." }, ...]` for 4 options, one per direction, ordered Away → Neutral → Towards → Hard. Parse into the new `RolePlaySteeringDirective` model on Apply.
- **Option B — pre-labeled cards**: keep the JSON-array-of-strings response, but generate exactly 4 options and label them in fixed order Away/Neutral/Towards/Hard in the popup UI (no per-option LLM schema change). Lower risk, matches existing `ParseFinishOptions` path.

**Recommended: Option B** for the first slice (smaller change, reuses the existing parser), with the direction fixed to the card position in the popup. The user picks a card ⇒ the direction is implied by the card slot ⇒ the directive is built with that direction + the card text. This avoids any LLM-schema risk and keeps the four-direction guarantee intact. (Confirm at design time.)

The popup UI (L8932–8995) changes the option rendering:

```
Direction flow — pick a direction for {TargetCharacter}:
 [Away]     option text...      (select)
 [Neutral]  option text...      (select)
 [Towards]  option text...      (select)
 [Hard]     option text...      (select)
```

### D7 — Per-character target selector in the popup

Add a character selector at the top of the steer popup (`RolePlayWorkspace.razor` L8932 block):

- `<select>` of session characters from `_session.CharacterPerspectives` (or `AdaptiveState.CharacterStats` keys). Default to the POV character or the first NPC.
- `TargetCharacterId` + `TargetCharacterLabel` captured on Apply.
- The selected target is also injected into `BuildSteerOptionsPromptAsync` so the generated options are grounded in that character's current stats/role (e.g. "Wife rejects other man's advances" vs "Husband pushes harder").

This is the per-character expansion the backlog item requires.

### D8 — Surface the built prompt and LLM response in the UI

The user's explicit ask: "the prompt and response needs to be surfaced in the UI." This is done in two places:

1. **Per-interaction prompt/response viewer** (existing `RolePlayInteraction` storage; mirrors B-053 but scoped to steering):
   - Extend `RolePlayInteraction` with `PromptText` (the full built continuation prompt for that interaction) and `RawResponseText` (the raw LLM response including any reasoning). Persisted in new columns `PromptText` / `RawResponseText` on the `RolePlayInteractions` table (additive). Pure text — not charms-dependent.
   - These fields are populated at continuation time in `RolePlayContinuationService.ContinueAsync` (after `RolePlayPromptBuilder.BuildAsync` returns the built prompt, store it on the generated interaction before persistence).
   - Render in the existing "Interaction Info" modal on `RolePlayWorkspace.razor` (the modal that already exists for adaptive state) via a new **"Steering" sub-tab** or a two-pane **"Prompt" / "Response"** tab. The tab is scrollable for long prompts. Includes a **"Resolved Steering Directive"** header at the top showing the consumed `RolePlaySteeringDirective` (target + direction + free-text directive) for that interaction.

2. **Live preview in the steer popup** (immediate feedback before submit):
   - After the user selects a direction card but before pressing Apply, the popup footer shows a **read-only preview block** of what will be applied:
     - `Target: {character}` · `Direction: {direction}`
     - `Directive text: "{option text}"`
   - On Apply, a toast confirms: `Steering applied → {character} | {direction}`. **No stat deltas are shown because no stat deltas are applied by the steer itself** — stats move naturally from the generated narrative via the existing semantic pipeline, not from the steer action.

3. **Engine → UI callback** for the response: the existing `onChunk` streaming callback (used by `SubmitPromptWithContinuationAsync` / `ContinuePromptAsync`) already delivers the response text progressively. The full response is captured for persistence via D8.1. No new event sink required.

### D9 — Interaction with theme direction & phase escalation

- Steering does **not** advance the phase. The directive is staged via `+` (no continuation, no analytics, no V2 pipeline). The next `…` fires a normal continuation — phase escalation (BuildUp→Committed→…) runs through the V2 pipeline on that continuation, with stat changes produced by the **existing semantic pipeline** reading the generated narrative.
- `Towards`/`Hard` direct the narrative toward the theme's trajectory in the option text; `Away`/`Neutral` resist it.
- A steer directive is **suppressed during Reset phase** (`IsSteerEligible` already excludes Reset L4508). No change needed.

### D10 — Open decisions (to confirm at design time)

- **OD1 Slot architecture** (RESOLVED): No new slot. Steering directives ride on the existing `StagedDirectionsSlot` (B-076, Order 9) via `SteeringMetadataJson` on the staged interaction row. This keeps the 17-slot contract frozen and avoids adding a `PromptBuildContext` field.
- **OD2 Multi-character batch steering**: allow one Apply to queue multiple per-character directives (e.g. steer Wife towards + Husband away simultaneously). Recommended: defer — single target per directive in this slice; multi-target is a follow-up.
- **OD3 Role resolution**: `TargetCharacterRole` is read from `CharacterStatProfileV2.CharacterRole`. Role affects only the option-text generation (which `CharacterStatTextCatalog` band text and which `StatToDimensionMappings` dimension set applies). If `CharacterRole` is null (e.g. persona), role-specific band text is unavailable — option generation falls back to canonical stat values only (no per-role band text, no encounter dimensions). This is a data-availability fallback for option-text richness, **not** a config-resolution fallback — there is no profile table to resolve anymore (D2 removed).
- **OD4 Position flow**: the existing Position flow (sexual-position steering) does not map cleanly onto the 4-direction model. Recommended: keep the two flows separate in the popup — the Direction flow gains the 4-direction + per-character model; the Position flow remains as today, with optional per-character targeting added later.
- **OD5 Stat-conditioning scope**: limit stat-conditioned option text to canonical stats only (Desire/Restraint/Dominance/Loyalty/SelfRespect), or also include `RuntimeEncounterStats` behavioral dimensions (Wife/Husband only)? Recommended: include both — behavioral dimensions already flow into the prompt's behavioral frame, so option text should match. Wife/Husband get dimensions; OtherMan does not (matches `StatToDimensionMappings` constraint).
- **OD6 (REMOVED)** — previously about band-scaled vs flat stat deltas. No longer applies since this feature does not mutate stats. B-020 (direct stat mutation on steer) is the right home for any delta-math question and stays `new`/out of scope.

### D11 — Context-aware direction selection (the four choices reflect the scene)

The four direction cards (Away / Neutral / Towards / Hard) are **not generic**. Each card's text is generated from the **active scene context and the target character's current stats + behavioral frame text**, so the option the user picks is grounded in what that character would actually do *right now*.

#### What feeds the option-generation prompt

`BuildSteerOptionsPromptAsync` (`RolePlayWorkspace.razor` L5119) is extended to inject the following **per-target-character context block** before the "Generate exactly 4 options…" instruction:

| Context input | Source (verified) | Why it matters |
|---|---|---|
| **Target character label + role** | Selected in the popup character `<select>` (D7); role from `CharacterStatProfileV2.CharacterRole` | A Wife choice reads differently from a Husband or OtherMan choice at the same scene moment. |
| **Target character current canonical stats** | `CharacterStatProfileV2.Desire/Restraint/Dominance/Loyalty/SelfRespect` via `CharacterStatProfileV2Accessor.GetStatOrDefault` | A Wife with Desire 100 vs Desire 10 produces different "towards" text (urgency vs reluctance). |
| **Target character stat state text** | `CharacterStatTextCatalog.GetBandText(stat, role, value)` (Domain/StoryAnalysis) — the same 4-band text already injected into continuation prompts by `BehavioralFramesSlot` (Slot 13) | Reuses the authoritative per-stat×role phrasing so the option text matches the narrative voice the model already gets on the next continuation. |
| **Target character behavioral frame** | `context.CharacterBehavioralFrames[targetLabel]` from `ScenarioGuidanceContextFactory` → `CharacterBehavioralFrameGenerator` (Infrastructure/StoryAnalysis) | The frame text encodes role-specific disposition (Wife boundary firmness, Husband awareness, etc.) — drives what "towards" means for that specific character. |
| **Runtime encounter dimensions** | `CharacterStatProfileV2.RuntimeEncounterStats` (Wife/Husband only — `StatToDimensionMappings` rules) | Behavioral dimensions (e.g. `BoundaryFirmness`, `SeductionReceptivity`) already flavor the frame; carrying them into option generation keeps the popup coherent with the prompt. |
| **Active theme + phase guidance** | `_v2State.ActiveTheme` + `GetThemePhaseGuidanceLines(theme, currentPhase)` (already used in `AppendThemeReferenceContextAsync`) | "Towards" is defined *relative to* the active theme's phase guidance — these lines are the steering vector. |
| **Current narrative phase** | `_v2State.CurrentPhase` | The four options must stay phase-consistent (BuildUp options differ from Climax options). Already in today's prompt. |
| **Recent interaction context (scene + last ~6 interactions)** | `BuildRecentSteerContextText()` (L5468) — already used by the Position flow | Anchors the option text to what just happened. |
| **Current scene location** | `_v2State.CurrentSceneLocation` | Grounds options in the surroundings. |

This is **the same surface the continuation prompt already uses** (Slot 13 `BehavioralFramesSlot` + `CharacterStatTextCatalog`), so option generation and the next continuation stay coherent. No new data source; the change is *feeding the existing per-character data into the option-generation LLM call that today only gets scene/position context*.

#### Why stat values change the options (worked examples)

The four direction cards are labeled **fixed** (Away / Neutral / Towards / Hard — see D6 Option B), but the **text on each card** is generated to reflect the target character's current state:

- **Wife, Desire 10, Restraint 85, Loyalty 80** (cool, guarded, committed):
  - **Away**: "She stiffens and changes the subject; redirects the conversation away from any intimate undertone."
  - **Neutral**: "She maintains polite distance; acknowledges the moment without engaging it."
  - **Towards**: "She lets a small, deliberate glance linger a beat longer than necessary — a crack in the wall."
  - **Hard**: "Against her own resistance, she reaches out and initiates contact — crossing her own stated line."

- **Wife, Desire 90, Restraint 15, Loyalty 20** (already lost the wall):
  - **Away**: "She catches herself mid-approach and pulls back — a flicker of returning guilt briefly overrules urge."
  - **Neutral**: "She holds her current intimacy level without deepening or retreating."
  - **Towards**: "She presses closer, her hand sliding with deliberate intent."
  - **Hard**: "She takes control, straddles him, and sets the pace without waiting for him to lead."

The **direction labels are fixed**; the **option text is stat-conditioned**. The LLM is asked to produce 4 options, one per direction, each tailored to the target's current stats/role/scene.

#### LLM generation contract (D6 Option B refined)

`BuildSteerOptionsPromptAsync` is rewritten so the system prompt instructs the model to return **exactly 4 options in a fixed order: Away, Neutral, Towards, Hard**. Parsing still uses `ParseFinishOptions` (L6384 — flat JSON array of strings, no schema change → low risk). The popup renders the four strings in fixed labeled slots (Away/Neutral/Towards/Hard), so the user knows what each card means regardless of the model's prose.

The generation prompt structure becomes:

```
You are generating steering options for ONE character in the current scene.

Target character: {label} (role: {role})
Current phase: {phase}
Active theme: {themeLabel} — {themeDescription}
Theme phase guidance for {phase}:
  - {phase guidance lines}

Target character's current state:
  Stats: Desire={n}, Restraint={n}, Dominance={n}, Loyalty={n}, SelfRespect={n}
  State text:
    Desire: {Desire band text}
    Restraint: {Restraint band text}
    Dominance: {Dominance band text}
    Loyalty: {Loyalty band text}
    SelfRespect: {SelfRespect band text}
  Behavioral frame: {frame text}
  {if Wife/Husband} Encounter dimensions: {key=value, ...}

Recent scene context (last ~6 interactions, abbreviated):
  {BuildRecentSteerContextText}
Current location: {sceneLocation}

Generate exactly 4 steering directives for {label}, one per direction, in this fixed order:
  1. AWAY   — steer this character against the active theme's direction (e.g. pull back, resist, refuse)
  2. NEUTRAL — hold the current state; do not escalate or retreat
  3. TOWARDS — steer this character toward where the theme is going in the {phase} phase
  4. HARD   — push extreme; jump fully into the theme's escalation

Constraints:
- Each option MOUSE one sentence, concrete and actionable, grounded in the recent scene.
- Each option MUST be consistent with the target character's CURRENT stats and behavioral frame (a Desire 10 character will not "hard" by initiating; the option must account for that tension).
- Stay in the {phase} phase; do not advance the phase.
- Apply only to {label}; do not narrate other characters' reactions.

Return ONLY a JSON array of 4 strings, in the exact order [Away, Neutral, Towards, Hard]. No markdown, no labels, no extra text.
```

This makes the four-card output context-aware: the cards' *labels* are fixed, the cards' *text* is shaped by stats + frame + theme + scene.

#### Direction assignment is chosen by the user; stats don't auto-select

Important: **the user picks the direction.** Stat values do not auto-select Away vs Towards. Stats only **shape the candidate option text** (the four cards) — they do not produce stat deltas, do not auto-resolve a direction, and do not run any math on Apply.

When the user picks a card and presses Continue, the chosen per-character directive is injected into the next continuation's prompt (D5). The continuation generates narrative. **Stats then move naturally** through the existing semantic analysis pipeline reading that narrative — exactly the same path that fires for any regular turn. There is no separate stat-mutation code path triggered by the steer action itself. B-020 (direct stat mutation on steer) is the place to introduce explicit deltas later, and it remains `new`/unimplemented.

#### Validate direction is plausible (optional hard guard)

The engine **does not** refuse a direction because it feels implausible (e.g. "Hard" on a Desire 10 Wife) — that's a narrative tension the user may want to play. But the **option text** surfaces the tension ("Against her own resistance…") so the user makes an informed choice. Engine enforcement is limited to: (1) target character exists, (2) phase != Reset, (3) one-shot consumption. No new behavioral gate.

### D12 — REMOVED (was: "Flat per (role, direction)" explained)

**Removed per user clarification.** The question of "flat vs band-scaled stat deltas" no longer applies — this feature does not apply stat deltas at all. The D12 worked example, the `RPSteerDirectionProfile(Wife, Hard)` numbers, and the band-scaled-vs-flat tradeoff discussion have all been deleted. That conversation belongs in B-020 if/when it is picked up.

---

## Implementation Plan (high-level — tasks.md generated after confirmation)

### Phase 0 — Dead code removal (prep)
1. Remove `AppendSteerGuidance`, `ResolveSteerDirective`, `TryExtractSteerDirective` from `RolePlayContinuationService.cs` (all dead — verified zero call sites).
2. Remove `ContainsSteerCommand`, `TryExtractSteerDirective`, and the `/steer` early-return block (L1224–1257) from `RolePlayEngineService.cs`. The `SteerCommandApplied` debug event is replaced by a normal `PromptSubmitted` event on the staged instruction.

### Phase 1 — Domain & persistence (no UI)
3. Add `SteerDirection` enum (`DreamGenClone.Web/Domain/RolePlay/`).
4. Add `RolePlaySteeringDirective` record (`DreamGenClone.Web/Domain/RolePlay/`) — `TargetCharacterId`, `TargetCharacterLabel`, `Direction`, `FreeTextDirective`. **No `StatDeltas` field.**
5. Add `RolePlayInteraction.SteeringMetadataJson` column (nullable text, additive migration in `SqlitePersistence`). Stores the serialized `RolePlaySteeringDirective` when the interaction is a steering directive.
6. Add `RolePlayInteraction.PromptText` + `RawResponseText` + column migration (`RolePlayInteractions.PromptText`/`RawResponseText`). Additive.

### Phase 2 — Engine: no changes needed for steering flow

The existing B-074/B-076 staged flow already handles:
- `+` stages an instruction without continuation (`SubmissionSource.PlusButton` + `IsStagedDirection = true`).
- `…` runs continuation with `StagedDirectionsSlot` injecting all staged rows.
- `GraduateStagedDirections` flips the flag after one continuation.

B-075 only needs to:
7. In `RolePlayEngineService.SubmitPromptAsync`, ensure the `Instruction` path sets `IsStagedDirection = true` when the submission carries steering metadata (so instructions created by the steer popup behave like `+`-staged rows, injected once then graduated). Currently the `Instruction` path does NOT set `IsStagedDirection`; it adds the interaction directly and then hits the dead `/steer` early-return. After Phase 0 removes that early-return, the instruction would flow through to `UpdateStateAndDetectEncounterAsync` — which should be skipped for staged instructions (they don't generate narrative).

### Phase 3 — Prompt slot: extend StagedDirectionsSlot
8. Extend `StagedDirectionsSlot.WriteAsync` to read `SteeringMetadataJson` from staged rows. When present, emit the per-character steering block (D5). When absent, emit the existing generic format.
9. No new slot, no new `PromptBuildContext` field, no new `PromptSlotId`.
10. Persist `PromptText` + `RawResponseText` on the generated `RolePlayInteraction` (new columns from Phase 1 step 6). Populate in `ContinueAsync` after build and after model response.

### Phase 4 — UI: per-character steer popup with context-aware options
11. Add character `<select>` to the steer popup (`RolePlayWorkspace.razor` L8932). Default to POV or first NPC. Capture `TargetCharacterId` + `TargetCharacterLabel` + resolve `TargetCharacterRole` from `CharacterStatProfileV2.CharacterRole` (may be null for persona — D11/OD3 fallback applies to option-text richness only).
12. **Rewrite `BuildSteerOptionsPromptAsync` (L5119) per D11**: inject the per-target-character context block (stats, `CharacterStatTextCatalog` band text, behavioral frame, runtime encounter dimensions for Wife/Husband, theme + phase guidance, recent scene context, location). Request exactly 4 options in fixed order Away/Neutral/Towards/Hard. Keep `ParseFinishOptions` parsing unchanged.
13. Change option rendering to 4 labeled cards (Away / Neutral / Towards / Hard) per D6 Option B — fixed labels from the array index, generated text from the LLM.
14. Add live preview footer (D8.2) showing `Target · Direction · Directive text` before Apply; toast on Apply. **No `StatDeltas` line** — this feature does not show or apply deltas.
15. `ApplySteerOptionAsync` (L5959) builds a `RolePlaySteeringDirective` (target, direction by card index, free text from the card — no deltas), serializes it to `SteeringMetadataJson`, creates a staged instruction interaction with `IsStagedDirection = true`, and submits via `AddPromptEntryAsync` / `SubmissionSource.PlusButton`. The interaction appears in the timeline as a pending staged row. Keep the existing Position flow unchanged (separate path).

### Phase 5 — UI: prompt/response surface
16. New "Prompt" / "Response" tab in the existing Interaction Info modal (`RolePlayWorkspace.razor`). Scrollable. Renders the stored `PromptText` / `RawResponseText`.
17. Show "Resolved Steering Directive" header on the tab when the interaction's prompt was a steer consumption (target + direction + free text — no deltas).

### Phase 6 — Tests
18. `RolePlaySessionLifecycleTests` — applying a per-character steering directive creates a staged instruction interaction with `IsStagedDirection = true` + `SteeringMetadataJson`; the next continuation injects it via `StagedDirectionsSlot` and graduates it; the engine does NOT mutate stats directly (assert `LastStatDeltas` is empty post-steer).
19. `StagedDirectionsSlotTests` (extend existing or new) — `WriteAsync` emits per-character block when `SteeringMetadataJson` is present; emits generic format when absent.
20. `PromptBuilderTests` — built prompt stored on `RolePlayInteraction.PromptText` after build.
21. **`SteerOptionContextTests` (new)** — `BuildSteerOptionsPromptAsync` (extracted to a testable builder) produces a prompt that includes: (a) target character label + role, (b) all 5 canonical stat values for the target, (c) `CharacterStatTextCatalog` band text for each stat×role, (d) behavioral frame text when present, (e) `RuntimeEncounterStats` for Wife/Husband roles, (f) active theme + phase guidance lines, (g) recent scene context + location. Assert each context block is present by substring. Verify the generated *prompt* differs for Desire 10 vs 90 (different band text → different prompt, per D11).
22. Fail-fast test: steering directive referencing a target character not in `session.AdaptiveState.CharacterStats` throws, does not silently stage a no-op directive.

---

## Verification (per repo Hard Rules — RP engine changes)

- **Value source resolved**: the four option cards' text is sourced from `CharacterStatTextCatalog` band text + `CharacterBehavioralFrames` + `RuntimeEncounterStats` + active theme phase guidance + recent scene context + location — all existing authoritative sources, no new canonical data fabricated for option generation. The per-character directive injected into the next continuation is the user-selected free-text option, carried as `SteeringMetadataJson` on a staged interaction row.
- **Single active decision path**: one path to stage (the steer popup → `AddPromptEntryAsync` → `SubmissionSource.PlusButton`); one injection path (`StagedDirectionsSlot` reading `SteeringMetadataJson`). Legacy dead code (`AppendSteerGuidance`, `ResolveSteerDirective`, `ContainsSteerCommand`, `TryExtractSteerDirective`, `/steer` early-return) removed. The four option cards are generated by a single LLM call with the D11 context block; the user, not the engine, selects the direction.
- **No fallback branch**: missing target character ⇒ fail fast with explicit diagnostic; null `CharacterRole` ⇒ option-text richness fallback (canonical stats only, no per-role band text) — this is a documented data-availability fallback for option prose, **not** a config-resolution fallback, since no config table is involved.
- **No direct stat mutation**: this feature does NOT call `ApplyTrackedDelta`, does NOT set `LastStatDeltas`, does NOT introduce a `StatDeltas` field on `RolePlaySteeringDirective`, does NOT add any `RPSteerDirectionProfile` table. Stats move only through the existing semantic pipeline on the continuation. This is the explicit B-020 boundary.
- **Missing config fails explicitly**: this feature introduces no new required RP config (no profile table), so there is no "missing config" surface to fail on. The only fail-fast is the missing-target-character case above.
- **UI/config surface exists**: per-character selector + live preview in `RolePlayWorkspace.razor`; prompt/response tab in the Interaction Info modal. No new `ThemeProfiles.razor` section (D2/Steer Directions editor was removed).
- **Context-aware sources verified**:
  - Stats → `CharacterStatProfileV2Accessor.GetStatOrDefault(profile, statName)` (canonical, always present after session seed).
  - Stat state text → `CharacterStatTextCatalog.GetBandText(stat, role, value)` (verified Domain/StoryAnalysis).
  - Behavioral frame → `ScenarioGuidanceContextFactory` → `CharacterBehavioralFrameGenerator` (verified Infrastructure/StoryAnalysis); null-tolerant.
  - Runtime encounter dimensions → `CharacterStatProfileV2.RuntimeEncounterStats` (Wife/Husband only).
  - Theme phase guidance → `RolePlayAssistantPrompts.GetThemePhaseGuidanceLines(theme, currentPhase)` (verified).
  - Recent scene context → `BuildRecentSteerContextText()` (verified L5468).
  - These are the **same sources** the continuation prompt (Slot 13 `BehavioralFramesSlot`) uses, so option text and the next-turn injection stay coherent.
- **Build + tests**: `dotnet build DreamGenClone.sln`, then `dotnet test DreamGenClone.Tests --no-build --filter "FullyQualifiedName~RolePlay|FullyQualifiedName~StagedDirections"`.

---

## Files Touched (estimate)

| File | Change |
|---|---|
| `DreamGenClone.Web/Domain/RolePlay/SteerDirection.cs` | new enum |
| `DreamGenClone.Web/Domain/RolePlay/RolePlaySteeringDirective.cs` | new record |
| `DreamGenClone.Web/Domain/RolePlay/RolePlayInteraction.cs` | add `SteeringMetadataJson`, `PromptText`, `RawResponseText` |
| `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` | **Remove** dead `/steer` early-return (L1224–1257), `ContainsSteerCommand`, `TryExtractSteerDirective`; ensure Instruction path sets `IsStagedDirection = true` for steering submissions |
| `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` | **Remove** dead `AppendSteerGuidance` (L1365), `ResolveSteerDirective` (L1443), `TryExtractSteerDirective` (L1454); persist `PromptText`/`RawResponseText` on interaction |
| `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/StagedDirectionsSlot.cs` | extend `WriteAsync` to read `SteeringMetadataJson` and emit per-character block |
| `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs` | additive migrations: `RolePlayInteractions.SteeringMetadataJson`, `RolePlayInteractions.PromptText`/`RawResponseText`. **No new table, no `Sessions` column.** |
| `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor` | per-character popup, 4-direction cards (Away/Neutral/Towards/Hard), context-aware option generation per D11, live preview (Target·Direction·Directive text — no deltas), `ApplySteerOptionAsync` builds staged instruction, Interaction Info modal Prompt/Response tab |
| `DreamGenClone.Tests/RolePlay/**` | new tests: staged directive injection, prompt/response storage, D11 context-aware option-generation prompt, missing-target fail-fast |

---

## Blast Radius

- **Dead code removal**: `AppendSteerGuidance`, `ResolveSteerDirective`, `TryExtractSteerDirective` (continuation service), `ContainsSteerCommand`, `TryExtractSteerDirective`, `/steer` early-return block (engine). All verified dead — zero call sites for the continuation service copies; the engine copies only fire on the now-removed early-return path.
- **Prompt pipeline**: `StagedDirectionsSlot.WriteAsync` extended with a conditional branch for `SteeringMetadataJson`. No new slots, no new `PromptBuildContext` fields, no changes to the 17 existing slots. Existing slot/prompt tests must stay green.
- **Stat mutation**: **NONE.** This feature does not call `ApplyTrackedDelta`, does not set `LastStatDeltas`, does not add a `StatDeltas` field, and does not add a `RPSteerDirectionProfile` table. Stats move only through the existing semantic pipeline on the next continuation. B-020 (`new`) remains untouched.
- **DB**: three additive columns on `RolePlayInteractions` only (`SteeringMetadataJson`, `PromptText`, `RawResponseText`). **No new table, no `Sessions` column.** No destructive changes.
- **UI**: Steer popup is rewritten in form (per-character selector + 4 labeled cards + context-aware option text + live preview with no deltas line) but stays in the same location. `ApplySteerOptionAsync` now stages via `+` instead of loading into `_promptText`. Interaction Info modal gains a Prompt/Response tab. **No `ThemeProfiles.razor` change (Steer Directions editor removed).** No nav changes.
- **Backward compat**: The `/steer` token no longer works as a special command (the detection and early-return are removed). Users who previously typed `/steer blah` manually will need to use the 4-direction popup. The popup provides strictly more functionality (per-character targeting, context-aware options, direction labels) and the manual text-input fallback path (typing an instruction directly into the prompt box and clicking `+`) still works for free-form steering without the `/steer` prefix.

---

## Architectural Decision (2026-08-08): Staged Flow replaces `/steer`

After inspecting the B-074/B-076 staged scene directions implementation and all `/steer` code paths:

- **`AppendSteerGuidance` and `ResolveSteerDirective` in `RolePlayContinuationService` are dead code** — defined but never called (verified: zero call sites).
- **The `/steer` early-return in `RolePlayEngineService` (L1224–1257) is the only active `/steer` behavior** — it detects the token and returns without running a continuation.
- **B-074/B-076 staged flow already provides the equivalent**: `+` stages without continuation, `StagedDirectionsSlot` injects on next `…`, `GraduateStagedDirections` makes it one-shot.
- **Decision**: B-075 uses staged flow exclusively. The `/steer` command, all detection methods, and all dead code are removed. Steering directives are `PromptIntent.Instruction` interactions with `IsStagedDirection = true` + `SteeringMetadataJson`.

---

## Status

**Plan revised for staged-flow architecture.** OD1 resolved (no new slot — use existing `StagedDirectionsSlot`). No code changed.

Per repo Hard Rule (No RP Engine Code Changes Without Plan + Confirmation), I'm presenting this plan and waiting for explicit "go ahead" before any `RolePlayEngineService.cs`, `RolePlayContinuationService.cs`, `StagedDirectionsSlot.cs`, or persistence code is touched.

**Open decisions needing confirmation before tasks.md generation**: OD2 (multi-character batch — recommended defer), OD3 (null `CharacterRole` option-text fallback — recommended canonical-stats-only), OD4 (position flow coexistence — recommended keep separate), D6 Option A vs B (LLM schema vs fixed-label cards — recommended B), OD5 (include `RuntimeEncounterStats` in option context — recommended yes).