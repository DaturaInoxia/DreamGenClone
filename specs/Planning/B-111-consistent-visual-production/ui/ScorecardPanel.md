# ScorecardPanel

## 1. Job (U1)
Show the three-metric render scorecard.

## 2. Reused by
- Phase gates.
- Studio screens.
- Strategy Qualification.
- Validation Review.
- Studio: Lineage.

## 3. Input contract
All types below are proposed design types, not final APIs.

- `ScorecardSubject Subject` - render, candidate, attempt, or phase being scored.
- `ScorecardTriple Scores` - identity, adherence, and diversity together.
- `ScorecardThresholds Thresholds` - configured gate thresholds and comparison operators.
- `IReadOnlyList<MetricEvidence> Evidence` - per-metric explanation loaded for detail.
- `ScorecardDisplayMode DisplayMode` - compact, review, or gate context.
- `bool ShowDecision` - whether the host needs pass/fail/blocked outcome.

## 4. Output/events
- `EventCallback<ScorecardDetailsRequestedEventArgs> OnDetails` - expands metric evidence.
- `EventCallback<ScorecardSubjectOpenedEventArgs> OnOpenSubject` - opens the scored item.
- `EventCallback<ScorecardRefreshRequestedEventArgs> OnRefresh` - requests a current score.

## 5. States
- `empty`: no score has been produced.
- `loading`: score or evidence is being calculated or loaded.
- `error`: scoring failed with diagnostic and retry.
- `populated`: identity, adherence, and diversity are all present.
- `partial`: one or more triple values are unavailable; never imply a complete decision.
- `passed`, `failed`, `blocked`, or `stale`: configured decision state shown with its reason.

## 6. Data-volume behaviour (U4)
Show only the three metric summaries in the main panel. Load detailed evidence, score histories, embeddings, and raw evaluator output only after expansion or subject selection. Paginate long evidence lists and open large payloads in a bounded panel with a size indicator.

## 7. Progressive disclosure (U3)
By default show the triple `identity + adherence + diversity`, the aggregate decision, and threshold summary. Behind an expander show per-metric detail, evaluator evidence, threshold calculations, timestamps, and raw output. The expander is collapsed by default.

## 8. Honest-state / no-black-box notes (U5/U6)
This panel does not initiate generation, but a parent generate/edit surface must expose `View what will be submitted`. Score availability and decision state are explicit; `partial`, `stale`, and `blocked` are not rendered as a passing score. The panel does not hide a missing metric behind a single aggregate number.

## 9. Acceptance
- Identity, adherence, and diversity are shown together every time; identity alone is never presented (FR-C6-01).
- Per-metric evidence is behind a collapsed expander and large evidence is loaded on demand (U3/U4).
- Partial, stale, failed, and blocked states are distinguishable from a passing score (U5).
- Configured thresholds and the resulting decision are visible without exposing an unbounded payload.
