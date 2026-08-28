Read memory [](file:///c%3A/Users/kenac/AppData/Roaming/Code/User/workspaceStorage/0708362e45bd5c986b7244a07737d690/GitHub.copilot-chat/memory-tool/memories/YjA0Y2Y4YTktNzhiZi00YWNjLWFmOTQtMjkyYzgxNWM1OTJk/plan.md)

Here is the complete plan document:

---

## Plan: Wife Resistance & Cheating Motivation Gap

**Status:** Analysis complete — ready for design phase  
**Tags:** `rp-engine`, `character-behavior`, `prompt-framing`, `cheating-motivation`, `resistance-gate`

---

### 1. Root Cause (5 Layers)

The Wife says "we shouldn't" but never genuinely resists because the engine has **zero counterweight** against escalation pressure:

| Layer | File | Problem |
|---|---|---|
| **1. Escalation Guidance** | RolePlayContinuationService.cs ~L1680 | Always pushes forward ("advance the scene", "progress intimacy") — checks *actor's* stats, not *target's* |
| **2. Behavioral Dimensions** | BehavioralDimensionCatalog.cs | Wife has 4 dimensions (DiscoveryCaution, Exhibitionism, EmotionalEngagement, PostEncounterGuilt) — **none control resistance to advances** |
| **3. Scene Writing Directive** | RolePlayContinuationService.cs ~L1740 | Hard progression ladder (Stage 1→8), assumes escalation inevitable |
| **4. Theme Guidance** | seduction.md | Frames resistance as "visible in body language even as they participate" — flavor on compliance |
| **5. Cheating Formula** | ScenarioGuidanceGenerator.cs ~L200 | `Loyalty - (Desire/2) + (Restraint/2)` — only measures resistance, never asks *why* she'd cheat |

---

### 2. Why A Wife Would Cheat — Motivational Driver Catalog

| # | Driver | Core Mechanism | Modeled? | Required |
|---|---|---|---|---|
| 1 | **Emotional Neglect** | Husband distant/inattentive | ❌ | New stat: `Attentiveness` |
| 2 | **Sexual Frustration** | Dead bedroom, mismatched libido | ❌ | New stat: `IntimacyEngagement` |
| 3 | **Revenge** | Husband cheated first | ❌ | Session-level flag |
| 4 | **Emotional Connection** | Bond with OtherMan over time | ⚠️ Partial | Wife `Connection` exists, needs trigger |
| 5 | **Validation Seeking** | OtherMan fills void husband leaves | ⚠️ Partial | Wife `SelfRespect` + `Attentiveness` gap |
| 6 | **Persistent Temptation** | OtherMan aggressively pursues | ✅ Yes | `OtherMan.PersistencePastLimits` exists |
| 7 | **Midlife Crisis** | Wants to feel alive | ❌ | Scenario metadata |
| 8 | **Substance/Lowered Inhibitions** | Moment-of-weakness trigger | ❌ | Event-level trigger |
| 9 | **Marital Breakdown** | Fighting, resentment, separate lives | ❌ | Marriage-quality score |
| 10 | **Financial/Power** | Coercion or obligation | ❌ | Low priority edge case |

**Coverage:** 1/10 modeled, 2/10 partial, 7/10 not modeled.  
**Core fix:** 2 new Husband stats (`Attentiveness`, `IntimacyEngagement`) cover drivers #1, #2, #5 — the most common RP scenarios.

---

### 3. BehavioralDimensionCatalog — Complete Reference

**Current Wife dimensions (4):**
- `DiscoveryCaution` — how cautious about being caught
- `Exhibitionism` — how comfortable being seen/heard
- `EmotionalEngagement` — how focused on his pleasure
- `PostEncounterGuilt` — how she behaves afterward

**Current Husband dimensions (6):** Awareness, Acceptance, Voyeurism, Participation, Encouragement, RiskTolerance

**Current OtherMan dimensions (4):** HusbandAwareness, MarriageContextUse, DiscoveryRisk, PersistencePastLimits

**Proposed new Wife dimensions:**
- `BoundaryFirmness` — she says no and means it (resistance gate)
- `ResistanceToEscalation` — physically resists escalation
- `MotivationalTrigger` — what justifies a boundary crossing (cheating justification)
- `SeductionReceptivity` — vulnerability to persistent pursuit

---

### 4. Options

| Option | Cost | Risk | Effectiveness |
|---|---|---|---|
| **A:** Prompt-level motivator gate (code-defined, no new stats) | Small | Medium (violates no-fallback rule) | Medium |
| **B:** Add Husband stats (`Attentiveness`, `IntimacyEngagement`) + gate | Medium | Low (default 50 backward compat) | **High** |
| **C:** Add Wife `BoundaryFirmness` + `ResistanceToEscalation` dimensions | Small | None | Medium |
| **D:** New `BoundaryFirmness` canonical stat + gate | Medium | Low | **High** |
| **E:** Replace static scene directive with configured data | Large | High | Very High |

---

### 5. Files to Modify

| File | Change |
|---|---|
| RolePlayContinuationService.cs | Add resistance contract + motivator gate in `BuildPromptAsync`; fix `AppendEscalationGuidance` to check target stats |
| AdaptiveStatCatalog.cs | Add `Attentiveness`, `IntimacyEngagement` canonical stats |
| CharacterStatTextCatalog.cs | Add stat × role text bands for new stats |
| BehavioralDimensionCatalog.cs | Add Wife resistance/motivation dimensions |
| ScenarioGuidanceGenerator.cs | Fix `BuildStatInterpretation` to use per-character (not average) stats |
| ScenarioGuidanceContextFactory.cs | Route new stat/behavior data into prompt pipeline |
| CharacterBehavioralFrameGenerator.cs | New Wife dimensions will flow through automatically |

---

### 6. Decision Log

| Question | Status |
|---|---|
| LLM "we shouldn't" = token resistance? | ✅ Confirmed |
| Cheating formula considers marriage health? | ❌ No |
| Existing Wife dimensions cover resistance? | ❌ No |
| New canonical stats break existing data? | 🔄 Needs audit (~20 refs) |
| Static scene directive violates no-fallback rule? | ⚠️ Yes |
| Existing formulas close to "motivation score"? | 🔄 Candidate: Vulnerability, SubmissivenessCapacity |

---

### 7. Verification

1. Audit all ~20 call sites referencing `AdaptiveStatCatalog.CanonicalStatNames` to confirm backward compatibility when adding new stats
2. Verify CharacterProfileService.cs stat validation accepts new stats
3. Confirm RolePlaySessionCompatibilityService.cs required-stats check allows optional stats
4. Run existing test suite — all tests must pass unchanged (new stats default to 50)
5. Integration test: create session with Husband Attentiveness=25, Wife Loyalty=75, Restraint=70; prompt must contain resistance HARD CONSTRAINT
6. Integration test: same scenario but Husband Attentiveness=75; prompt must NOT contain resistance HARD CONSTRAINT (no motivational condition met)