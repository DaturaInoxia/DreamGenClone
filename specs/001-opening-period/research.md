# Research: RP Session Opening Period

**Feature**: `001-opening-period`  
**Date**: 2026-06-22

## 1. Opening Period Gate Location

### Decision
Place the opening period gate in `RolePlayContinuationService.cs` at the prompt-building method, wrapping the theme-guidance injection block (lines ~920-1000). Also place the OtherMan exclusion gate in `RolePlayEngineService.cs` at the overflow actor resolution method (lines ~2198-2218).

### Rationale
The prompt-building method is the single point where all guidance (theme contract, framing guards, hard constraints, AI notes) assembles into the LLM prompt. Wrapping this block with an opening-period gate ensures all theme guidance is suppressed uniformly. The actor-resolution method is the single point where overflow candidates are filtered, so OtherMan exclusion naturally sits there.

### Alternatives Considered
- **Gate at individual Append* methods**: Rejected — would require adding the check to 4+ separate methods, error-prone and harder to audit.
- **Gate in the theme tracker state**: Rejected — tracker state is about theme selection, not prompt injection. Separating the gate from the tracker keeps the two mechanisms independent.

## 2. Turn Count Source (`ObservedTurnCount`)

### Decision
Use `session.AdaptiveState.ObservedTurnCount` as the turn counter for the opening period gate.

### Rationale
`ObservedTurnCount` is incremented immediately after every `StartTurnAsync` call (4 call sites: `AddInteraction`, `Continue`, `SubmitPrompt`, `ContinueAs`) — before any overflow actor resolution or prompt building within that turn. This means when the prompt is built for turn N, `ObservedTurnCount` already equals N. Using `<= 3` correctly covers turns 1-3.

### Alternatives Considered
- **`session.Interactions.Count`**: Rejected — this counts individual interactions (Narrative, Message, etc.) within turns, not turns themselves. A single turn can produce 3-4 interactions, making the threshold ambiguous.
- **`RolePlayV2Turns.TurnIndex` max query**: Rejected — requires a DB round-trip; `ObservedTurnCount` is already in memory.

## 3. Guidance Suppression Mechanism

### Decision
During the opening period (`ObservedTurnCount <= 3`):
1. **Skip** the entire theme-guidance block (`AppendActiveThemeContract`, `AppendThemeHardConstraints`, `AppendThemeAIGuidance`, and secondary theme blend)
2. **Skip** phase-specific framing guards (pass empty via `BuildFramingGuards` or gate the call)
3. **Inject** opening-period guidance text (loaded from scenario definition)
4. **Skip** the observer candidate menu (if applicable)

After the opening period:
1. Resume normal theme guidance injection
2. If the observation window is still active (`ObservedTurnCount <= SelectionMinimumTurns` and `SelectionMinimumTurns > 0`), inject the observer candidate menu
3. If the observation window is done, inject full theme guidance (contract + guards + hard constraints + AI notes)

### Rationale
The opening period must suppress ALL theme-related prompt content to avoid contradictions. Leaving any theme guidance (even partial) would recreate the original OPF contradiction problem.

### Alternatives Considered
- **Suppress only the phase guidance text**: Rejected — hard constraints and AI notes also embed theme-specific direction that contradicts the opening focus.
- **Add opening guidance downstream (end of prompt)**: Rejected — the old OPF hack was at the end of the prompt to overpower early theme guidance. Placing opening guidance in the theme slot (early) means there's nothing to overpower.

## 4. Scenario-Level Guidance Storage

### Decision
Store the opening-period guidance text in the existing `Scenarios` table. Add a new column `OpeningGuidanceText TEXT` (nullable). When null, use the seeded default text.

### Rationale
The `Scenarios` table already holds scenario-specific configuration via `PayloadJson`. A dedicated column is cleaner than embedding in the JSON blob — it's directly queryable, schema-visible, and simple to update. The column is nullable so existing scenarios can be seeded with a single UPDATE statement.

### Alternatives Considered
- **Embed in `PayloadJson`**: Rejected — requires JSON parsing to read/update, less schema-visible, harder to query.
- **New table**: Rejected — overkill for a single text field; adding a column to the existing table is simpler.
- **Store on `RPThemeProfiles`**: Rejected — scenarios vary more than profiles; a campground needs different baseline establishment than an office.

## 5. Observation Window Interaction (Independent Counters)

### Decision
The opening period and the theme observation window use independent counters. The opening period does NOT consume observation turns. `ObservedTurnCount` increments normally through both periods.

### Rationale
Decoupled counters keep the two mechanisms independent. The observation window needs its full configured window to gather evidence before committing a theme. If the opening period consumed observation turns, the effective observation window would be shorter in multi-theme scenarios, potentially leading to premature theme selection.

### Flow example (4 themes, `ThemeSelectionTurnsPerTheme=2`):
- Turns 1-3: Opening period active → opening guidance only, observer silent
- Turns 4-6: Opening period lifted, observer window active → candidate menu only
- Turn 7+: Observer window lifts, theme committed → full theme guidance

### Alternatives Considered
- **Opening period consumes observation turns**: Rejected — would shorten the observation window, potentially causing themes to be selected before enough evidence is gathered.
- **Opening period pauses observation counter**: Rejected — adds coupling between the two mechanisms; `ObservedTurnCount` should reflect actual turns started.

## 6. Single-Theme Scenario Optimization

### Decision
When `activeThemeCount == 1`, `SelectionMinimumTurns = 0` (existing behavior). The observer never engages. After the opening period lifts at turn 4, theme guidance begins immediately with no observer candidate menu in between.

### Rationale
With a single theme, there's nothing to observe or select between — the theme is already known. The opening period still runs to establish the baseline, then theme guidance kicks in directly.

## 7. RP Engine Strict Configuration Contract Compliance

### Decision
The opening period threshold (`3`) is a `private const int` in `RolePlayContinuationService.cs`. This is a simple fixed setting, not a configurable value, so it does NOT violate the "no hardcoded defaults" rule. The opening-period guidance text is stored as configurable data in the `Scenarios` table, satisfying the "UI-backed configuration" principle (seeded via DB migration, editable in future).

### Rationale
The constitution prohibits hardcoded runtime defaults for RP engine behavior *that varies by configuration*. The opening period threshold is a fixed architectural constant (like the observation window formula), not a user-tunable setting. The guidance text — which IS user-tunable — is stored as configurable data.

## 8. Seeding Existing Scenarios

### Decision
Run a one-time SQL migration to set `OpeningGuidanceText` on all existing scenarios to the seed default: *"Focus on the couple's relationship and their current life together. Include a brief sense of their intimate life from her point of view — the rhythm of it, what she feels about it, what she wants or doesn't get — grounding these details in the character profiles and their descriptions. Describe their routines, interactions, and daily rhythms. Establish the setting, mood, and any relevant history. Other characters remain in the background."*

### Rationale
All existing scenarios get the same seed text. New scenarios created later will also get this default if no specific text is provided. Users can update individual scenarios manually or via a future UI.
