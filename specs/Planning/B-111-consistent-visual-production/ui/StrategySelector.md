# StrategySelector

## 1. Job (U1)
Choose a qualified identity strategy.

## 2. Reused by
- Studio: Identity.
- Strategy Qualification (P3).

## 3. Input contract
All types below are proposed design types, not final APIs.

- `IReadOnlyList<StrategyCell> Cells` - strategy/model/endpoint/strength combinations.
- `StrategySelection? Selected` - current selectable combination.
- `StrategySelectorContext Context` - target reference, composition, and requested change.
- `QualificationQuery QualificationQuery` - bounded filter and page state.
- `IReadOnlyList<QualificationEvidence> Evidence` - scores and reasons for each cell.
- `Uri QualificationUri` - link to the qualification screen for unqualified cells.

## 4. Output/events
- `EventCallback<StrategySelectedEventArgs> OnSelected` - selects a qualified combination.
- `EventCallback<QualificationRequestedEventArgs> OnQualify` - follows the qualify link for an unqualified cell.
- `EventCallback<StrategyDetailsRequestedEventArgs> OnDetails` - opens evidence and constraints.
- `EventCallback<StrategyPageRequestedEventArgs> OnPageRequested` - requests another bounded cell page.

## 5. States
- `empty`: no strategies apply to the current context.
- `loading`: qualification matrix or evidence is loading.
- `error`: matrix resolution failed with explicit diagnostics.
- `populated`: selectable cells are shown.
- Cell states: `qualified`, `unqualified`, `structurally-impossible`.
- `selected`: one qualified cell is selected.
- `blocked`: submission is prevented because the selected cell is unqualified or structurally impossible.

## 6. Data-volume behaviour (U4)
Render the strategy matrix in a virtualized viewport or bounded pages, never as an unbounded grid. Load qualification evidence and long reasons on demand for the focused cell. Keep the visible cell summary small; do not inline large qualification payloads or score histories.

## 7. Progressive disclosure (U3)
By default show strategy, model, endpoint, strength, state, and the short reason for unavailable cells. Behind an expander show qualification scorecard, constraints, evidence, timestamps, and full backend diagnostics. The qualification action remains a direct link for unqualified cells.

## 8. Honest-state / no-black-box notes (U5/U6)
Structurally-impossible cells MUST be greyed out and show the reason. Unqualified cells MUST be blocked and expose a `Qualify` link; they are not selectable. The component MUST NEVER silently substitute a different strategy, model, endpoint, or strength. When a selected strategy leads to generation or editing, the host must provide `View what will be submitted`, including exact strategy and all resolved settings, before Run.

## 9. Acceptance
- Structurally-impossible cells are visibly greyed out with a specific reason and cannot be selected (FR-C3-04).
- Unqualified cells are blocked and provide a `Qualify` link; no alternate value is silently substituted (FR-C3-04, U5).
- Only a qualified cell can become the selected strategy.
- The matrix is virtualized or paged and evidence is on demand (U3/U4).
- Any downstream generate/edit action exposes the exact strategy and resolved inputs before submission (U6).
