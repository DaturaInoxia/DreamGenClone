# Theme Engine Gap Analysis & Backlog

Analysis of the RP engine's ability to explore the themes listed in `suggested-themes.md`, with backlog items for each gap and suggested new themes.

---

## Current Architecture Summary

The theme pipeline works as follows:

1. **Seeding** — At session create, themes are seeded into the `ThemeTracker` from either per-session selections or the scenario's default RPThemeProfile. Tier-based `ChoiceSignal` values are applied.
2. **Per-interaction scoring** — Each AI output is scored against all tracked themes using 4 signals: `ChoiceSignal` (tier preferences), `CharacterStateSignal` (character stats), `InteractionEvidenceSignal` (keyword hits in AI output), `ScenarioPhaseSignal` (scenario text keywords at session start).
3. **Selection** — `RecalculateSelectedThemes` picks `PrimaryThemeId` and optionally `SecondaryThemeId` using the `Top1` or `Top2Blend` rule.
4. **Commitment** — `ScenarioSelectionService` evaluates candidates with two-stage gating (Stage A: willingness tier, Stage B: character-state fit). Hysteresis requires consecutive-lead before committing.
5. **Guidance injection** — The active theme's phase guidance and AI notes are injected into the continuation prompt. Influence strength is configurable per session (`ThemeAIGuidanceInfluencePercent`).
6. **Phase progression** — The narrative advances through BuildUp → Committed → Approaching → Climax → Reset, with explicit transition conditions.
7. **Semi-reset** — On completion, the theme's score is penalized and the engine can select a new theme.

---

## Gap 1: One Active Scenario at a Time (Mutual Exclusion)

**Current behavior:** The engine commits to exactly one active scenario/theme per narrative cycle. `Top2Blend` tracks a secondary theme but does not inject its guidance.

**Why this hurts:** Many suggested themes are inherently layered:
- Seduction + Corruption — seduction is the vehicle, moral decline is the arc
- Growing Apart + Infidelity — the first creates vulnerability, the second exploits it
- Denial/Edging + Dominance — denial is a tactic of dominance
- Regret/Guilt + Reconciliation — the first is aftermath, the second is resolution

**Suggested fix:** Allow the secondary theme to also inject phase guidance and AI notes at reduced influence (e.g., 50% of primary influence). The `Top2Blend` rule already identifies the secondary — it just needs to feed into `AppendThemeAIGuidance`.

**Complexity:** Medium. The `Top2Blend` rule already tracks the secondary theme. The change is in `BuildPromptAsync` and `AppendThemeAIGuidance` to also call `GetThemePhaseGuidanceLines` and `GetPhaseRelevantThemeAIGuidanceNotes` for the secondary theme at reduced `influencePercent`.

---

## Gap 2: Linear Phase Progression for All Themes

**Current behavior:** Every theme follows BuildUp → Committed → Approaching → Climax → Reset.

**Why this hurts:** Some themes don't fit this arc:
- **Growing Apart** — no climax, no reset. It's a slow erosion. The "Reset" phase contradicts the theme.
- **Corruption/Moral Decline** — each step is permanent. There's no going back. A "Reset" phase contradicts the theme.
- **Regret/Guilt** — this is inherently an aftermath state. It belongs after a climax, not as its own 5-phase arc.
- **Negotiation/Consent** — this is a setup phase, not a standalone arc. It should happen before BuildUp.
- **Denial/Edging** — the climax is the release, but the theme's whole point is delaying it. The phase model should emphasize the middle, not rush to the end.

**Suggested fix:** Add a `PhaseModel` field to `RPTheme` with values like `Linear` (current default), `Cyclical` (repeats without reset), `Static` (no phase progression — just a persistent state), `AftermathOnly` (only Reset phase applies), `SetupOnly` (only BuildUp applies). Themes declare which model they use; the engine respects it.

**Complexity:** Medium. The phase enum and transition logic already exist. The change is allowing themes to opt out of phases that don't apply and adjusting the transition conditions accordingly.

---

## Gap 3: Keyword-Driven Scoring Under-Serves Psychological Themes

**Current behavior:** `UpdateTheme` scores themes primarily by counting keyword hits in the AI's output text. `CharacterStateSignal` exists but is uniformly weighted across all themes.

**Why this hurts:** Psychological themes live in the gaps between actions:
- **Seduction** — key moments are subtle signals (a lingering glance, a loaded pause). These rarely produce keyword hits. The engine would score seduction low even when it's the dominant narrative force.
- **Internal Conflict** — the target's inner war is narrated in internal monologue, not in observable keywords.
- **Corruption** — each small step ("just this once") is narratively significant but keyword-weak. The engine wouldn't accumulate score fast enough.

**Suggested fix:** Add a per-theme `CharacterStateSignalWeight` override (default 1.0). Psychological themes set this higher (e.g., 2.0–3.0) so character state changes drive their scoring more than keyword hits. This leverages the existing `CharacterStateSignal` mechanism — just makes it theme-aware.

**Complexity:** Low. The `CharacterStateSignal` is already computed per-interaction. The change is multiplying it by a per-theme weight before adding to the tracker score.

---

## Gap 4: No Theme-to-Theme Causality

**Current behavior:** Themes are independent candidates. When a theme completes, `ApplyThemeSemiReset` reduces its score. There's no mechanism to boost causally-related next themes.

**Why this hurts:** Narrative arcs span multiple themes:
- Growing Apart → Infidelity → Regret/Guilt → Reconciliation
- Seduction → Corruption → Regret/Guilt
- Denial/Edging → Dominance → Climax

Without causality, the engine can't model these arcs. Each theme is an island.

**Suggested fix:** Add a `SucceedingThemeIds` field to `RPTheme` (a list of theme IDs that should be boosted when this theme completes). When `ApplyThemeSemiReset` fires, it reduces the completed theme's score AND boosts each successor's score by a configurable amount (e.g., +15). This creates narrative momentum across themes.

**Complexity:** Medium. Requires schema change (`RPThemes` table + seeding), plus a few lines in `ApplyThemeSemiReset`.

---

## Gap 5: Soft Guidance Can't Enforce Restraint Themes

**Current behavior:** Theme AI guidance notes are injected as "soft hints" or "strong guidance" depending on `ThemeAIGuidanceInfluencePercent`. The LLM can and does ignore them, especially when they conflict with the user's immediate input or the AI's natural tendency to escalate.

**Why this hurts:** Themes that require restraint are systematically under-served:
- **Denial/Edging** — the AI naturally wants to resolve tension. Without hard constraints, it will escalate past the denial.
- **Blackmail/Coercion** — the AI may soften the coercion into consent, undermining the theme.
- **Growing Apart** — the AI may introduce reconciliation prematurely, undermining the erosion.

The husband-awareness HARD CONSTRAINT mechanism already proves this pattern works — it's just limited to one specific use case.

**Suggested fix:** Generalize the HARD CONSTRAINT mechanism. Allow any theme to declare hard constraints in its AI guidance notes (using a new section, e.g., `HardConstraints`). These are injected with the same authoritative framing as the husband-awareness constraint: "HARD CONSTRAINT — enforce in this response: ..."

**Complexity:** Low. The mechanism already exists in `AppendScenarioGuidance`. The change is allowing themes to contribute hard constraint text, and injecting it alongside the existing husband-awareness constraint.

---

## Engine Improvement Backlog

| ID | Title | State | Notes |
|---|---|---|---|
| TE-001 | Allow primary + secondary themes to both inject guidance | `new` | The `Top2Blend` selection rule already tracks a secondary theme, but only the primary gets phase guidance and AI notes injected into the prompt. Allow the secondary theme to also inject guidance at reduced influence. Enables layered narratives (e.g. Seduction primary + Corruption secondary). See Gap 1 above. |
| TE-002 | Support per-theme phase models (linear, cyclical, static, aftermath-only) | `new` | All themes currently follow the same 5-phase ladder (BuildUp→Committed→Approaching→Climax→Reset). Some themes are poorly served: Growing Apart has no climax, Corruption has no reset, Regret/Guilt is aftermath-only, Negotiation/Consent is setup-only. Allow RPTheme to declare a phase model and skip phases that don't apply. See Gap 2 above. |
| TE-003 | Boost character-state-driven scoring for psychological themes | `new` | Keyword-driven scoring works for observable themes (infidelity, voyeurism) but under-scores psychological themes where the action is internal (Seduction, Internal Conflict, Corruption). The `CharacterStateSignal` already exists but is underweighted for these themes. Add a per-theme `CharacterStateSignalWeight` override so psychological themes can rely more on character stats than keyword hits. See Gap 3 above. |
| TE-004 | Add theme-to-theme causality chains | `new` | Themes are currently independent candidates with no causal relationships. Add a `SucceedingThemeIds` field to RPTheme so that when a theme completes, its successors get a score boost instead of just a generic penalty on the completed theme. Enables narrative arcs: Growing Apart → Infidelity → Regret/Guilt → Reconciliation. See Gap 4 above. |
| TE-005 | Allow themes to declare hard constraints (not just soft hints) | `new` | Theme AI guidance notes are currently injected as soft hints or strong guidance, but the LLM can ignore them. For themes requiring restraint or pacing (Denial/Edging, Blackmail/Coercion), the AI's natural tendency to escalate works against the theme. Generalize the existing husband-awareness HARD CONSTRAINT mechanism so any theme can declare hard constraints. See Gap 5 above. |

---

## New Theme Backlog

| ID | Title | State | Notes |
|---|---|---|---|
| TT-001 | Create Corruption / Moral Decline theme | `new` | High-priority suggested theme. Taboo category. Step-by-step moral erosion — each small transgression is permanent. Requires TE-002 (non-linear phase model) and TE-003 (character-state scoring) to work well. See `suggested-themes.md`. |
| TT-002 | Create Hotwife / Cuckold theme | `new` | High-priority suggested theme. Power category. Already referenced in `Husband Awareness.md` spec but has no theme entry. Husband's complicity ranges from enthusiastic (hotwife) to humiliated (cuckold). See `suggested-themes.md`. |
| TT-003 | Create Exhibitionism theme | `new` | High-priority suggested theme. Taboo category. Natural pair with existing Voyeurism (performer side vs observer side). See `suggested-themes.md`. |
| TT-004 | Create First Time / Awakening theme | `new` | High-priority suggested theme. Emotional category. Sexual or emotional awakening — high vulnerability/tension. Fills the Emotional category gap. See `suggested-themes.md`. |
| TT-005 | Create Denial & Edging theme | `new` | High-priority suggested theme. Taboo category. Prolonged restraint as deliberate power tool. Unique tension mechanic not covered by any existing theme. Requires TE-005 (hard constraints) to prevent AI from escalating past the denial. See `suggested-themes.md`. |
| TT-006 | Create Blackmail & Coercion theme | `new` | High-priority suggested theme. Taboo category. Leverage-based power, distinct from Dominance (claimed power) — this is extorted compliance. Requires TE-005 (hard constraints) recommended. See `suggested-themes.md`. |
| TT-007 | Create Competition & Rivalry theme | `new` | High-priority suggested theme. Relational category. Two characters actively competing for the same person. Action-oriented tension, distinct from Jealousy Triangle (the feeling). See `suggested-themes.md`. |
| TT-008 | Create Reunion theme | `new` | High-priority suggested theme. Emotional category. Reconnecting with someone from the past — weight of history and unresolved feelings. See `suggested-themes.md`. |
| TT-009 | Create Secret Voyeur Discovery theme | `new` | High-priority suggested theme. Power category. Already named in `infidelity-public-discovery.md` spec as a separate theme. The act of secretly watching and the consequences of being discovered. See `suggested-themes.md`. |

---

## Priority Order

### Engine Improvements

| Priority | ID | Title | Why | Depends On |
|---|---|---|---|---|
| 1 | TE-003 | Character-state scoring weight | Lowest complexity, highest impact for psychological themes | Nothing |
| 2 | TE-005 | Hard constraints for themes | Low complexity, enables restraint-based themes | Nothing |
| 3 | TE-001 | Primary + secondary guidance | Enables layered narratives | Nothing |
| 4 | TE-004 | Theme causality chains | Enables multi-theme narrative arcs | Nothing |
| 5 | TE-002 | Per-theme phase models | Medium complexity, needed for non-linear themes | Design first |

### New Themes

| Priority | ID | Title | Why | Depends On |
|---|---|---|---|---|
| 1 | TT-002 | Hotwife/Cuckold | Already referenced in specs, major gap | Nothing |
| 2 | TT-001 | Corruption/Moral Decline | Strong narrative engine, fills Taboo | TE-002, TE-003 recommended |
| 3 | TT-003 | Exhibitionism | Natural pair with existing Voyeurism | Nothing |
| 4 | TT-004 | First Time/Awakening | Fills Emotional gap | TE-003 recommended |
| 5 | TT-005 | Denial/Edging | Unique tension mechanic | TE-005 recommended |
| 6 | TT-006 | Blackmail/Coercion | Distinct power dynamic | TE-005 recommended |
| 7 | TT-007 | Competition/Rivalry | Opens Relational category | Nothing |
| 8 | TT-008 | Reunion | Strong emotional setup | Nothing |
| 9 | TT-009 | Secret Voyeur Discovery | Already named in specs | Nothing |