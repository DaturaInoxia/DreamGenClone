# Phase 0 Research: Final Writing Instruction Consolidation

**Feature**: `001-final-writing-instruction`
**Date**: 2026-07-19
**Status**: Complete

---

## Research Tasks

### R1: Scene Direction ↔ Writing Instruction Ordering (FR-013)

**Decision**: Scene Direction BEFORE Writing Instruction (Scene Direction first, Writing Instruction last as absolute end content).

**Rationale**:
- **Primacy-recency (U-shaped attention)**: Models attend strongly to content at the start AND end of the prompt, with weaker attention in the middle (Liu et al. 2023, "Lost in the Middle").
- **Two distinct directive types**: Scene Direction is *contextual* ("what this scene should accomplish") — it sets the frame. Writing Instruction is *operational* ("how to write") — it sets the execution rules. Context should precede execution rules so the model understands *why* before *how*.
- **Current implementation evidence**: The existing `FinalInstructionSlot.cs` already places Phase Directive BEFORE Writing Instruction for the Character variant (lines 47-56 emit Phase Directive, then lines 58-63 emit Writing Instruction). This was the proven-effective pattern from debug#012. The plan's original "Phase Directive after Writing Instruction" was a later proposal that was never validated.
- **Instruction saturation risk**: Placing Scene Direction after Writing Instruction risks the model treating it as a "trailing addendum" to the writing rules rather than as scene-level context. Placing it before gives it standalone weight as scene framing.
- **Validation plan**: Per FR-013, the chosen ordering must pass all three validation methods (manual checklist, automated scoring, subjective review) against the pre-consolidation baseline. The baseline is the current implementation (Phase Directive before Writing Instruction), so this ordering is expected to pass.

**Alternatives considered**:
- Writing Instruction first, Scene Direction last (absolute end): rejected — demotes Writing Instruction from the authoritative end position, and risks Scene Direction being treated as a trailing addendum.
- Scene Direction in a separate slot (e.g., new Slot 16.5): rejected — would require amending the frozen 17-slot architecture; consolidation into Slot 17 is the spec's intent.

---

### R2: Model-Generated Cleanup Spec for Sensual & Emotional ToneProfiles (FR-010)

**Decision**: Apply the following rewrites to remove prose-style language and retain only heat-level language.

#### Sensual (current)
> Emphasize sensory details and the progression of physical intimacy. Describe touch, taste, scent, and the rhythm of interaction with mature, evocative language. Focus on build-up, anticipation, and responsive reactions. Include passionate kissing, caressing, and sensual exploration—avoid graphic anatomical descriptions. Convey tension and release through visible physical responsiveness and deliberate pacing.

**Prose-style phrases to remove**:
- "mature, evocative language" → prose-craft (register/voice), not heat level
- "deliberate pacing" → prose-craft (pacing), not heat level

**Rewritten Sensual (heat-level only)**:
> Emphasize sensory details and the progression of physical intimacy. Describe touch, taste, scent, and the rhythm of interaction. Focus on build-up, anticipation, and responsive reactions. Include passionate kissing, caressing, and sensual exploration—avoid graphic anatomical descriptions. Convey tension and release through visible physical responsiveness.

**Rationale**: Removed the two prose-craft phrases. Retained all heat-level content: sensory focus, physical intimacy progression, build-up/anticipation, kissing/caressing/sensual exploration, the explicit boundary ("avoid graphic anatomical descriptions"), and tension/release through physical responsiveness.

#### Emotional (current)
> Prioritize emotional intimacy expressed through meaningful dialogue, tender gestures, lingering eye contact, and moments of vulnerability. Physical expressions remain chaste but meaningful—touches on the arm, hand-holding, closeness. Let connection build through conversation, shared experiences, and emotional disclosure. Keep physical escalation minimal; focus on the emotional bond and relational depth.

**Prose-style phrases to remove**:
- "meaningful dialogue" → prose-craft (dialogue is a prose element, not a heat level)
- "conversation" → prose-craft (dialogue)
- "emotional disclosure" → prose-craft (dialogue content)

**Rewritten Emotional (heat-level only)**:
> Prioritize emotional intimacy expressed through tender gestures, lingering eye contact, and moments of vulnerability. Physical expressions remain chaste but meaningful—touches on the arm, hand-holding, closeness. Let connection build through shared experiences. Keep physical escalation minimal; focus on the emotional bond and relational depth.

**Rationale**: Removed the three dialogue/conversation phrases. Retained all heat-level content: emotional intimacy priority, tender gestures, eye contact, vulnerability, chaste physical expressions, minimal escalation, emotional bond focus.

---

### R3: Atmospheric Profile Migration (FR-009)

**Decision**: Move Atmospheric from ToneProfiles to StyleProfiles. No existing-session migration (per clarification Q3 — only new RP sessions are supported after the feature goes live).

**Current Atmospheric ToneProfile**:
- Id: `96b9e19cd16048a49e6460d0c115e658`
- Name: Atmospheric
- Intensity: Intro
- Description: "Prioritize environmental details, lighting, sounds, and atmosphere over action or dialogue. Establish the mood through sensory imagery—what characters see, hear, smell, feel. Keep physical interaction subtle or absent. Let tension emerge from setting, body language, and subtext rather than explicit activity. Slow, patient pacing with rich descriptive language."

**Migration plan**:
1. INSERT a new row into `StyleProfiles` with:
   - Id: new GUID
   - Name: "Atmospheric"
   - Description: the existing ToneProfile description (above)
   - Example: "" (none exists)
   - RuleOfThumb: "Favor environmental immersion, sensory imagery, and slow-burn atmosphere over action or explicit activity." (derived from the description)
   - ThemeAffinities / EscalatingThemeIds / StatBias: empty JSON defaults
   - **NEW required fields** (per FR-005/FR-006): ImmersionDirective, ActionDirective, WordTargetMin, WordTargetMax, NarrativeWordTargetMin, NarrativeWordTargetMax — must be populated (fail-fast at runtime if null)
2. DELETE the Atmospheric row from `ToneProfiles`.

**Overlap with Sultry StyleProfile**: The existing Sultry StyleProfile description mentions "Atmospheric settings that hint at danger." This is a prose-style description that *references* atmosphere as a stylistic flavor — it is NOT the same as the Atmospheric profile, which is *entirely* about environmental immersion. Both can coexist: Sultry is a moody/seductive style; Atmospheric is a pure environmental-immersion style. No deduplication needed.

---

### R4: SteeringProfile New Fields — Default Values for Existing Sultry Profile (FR-015)

**Decision**: Populate the existing Sultry StyleProfile with the following values before the code change goes live. These are the values currently hardcoded in `FinalInstructionSlot.cs` (lines 60-62), migrated to config.

**Sultry StyleProfile new fields**:
- `ImmersionDirective`: "Stay inside this character's perceptions, thoughts, feelings, and physical sensations. Show, don't tell."
- `ActionDirective`: "Respond to the scene naturally."
- `WordTargetMin`: 200 (Character variant — matches current hardcoded "Target 200-400 words")
- `WordTargetMax`: 400 (Character variant)
- `NarrativeWordTargetMin`: 300 (Narrative variant — intentionally longer; current narrative target is "200-400 words of scene synthesis" but spec clarification Q4 mandates Narrative > Character, so 300-500)
- `NarrativeWordTargetMax`: 500 (Narrative variant)

**Rationale**: These match the current hardcoded values (Character 200-400) and extend to Narrative (300-500) per the per-variant clarification. "Show, don't tell" is added to the immersion directive per the source plan's decision (LLMs respond well to this fundamental writing directive).

---

### R5: NarrativeSettings Decomposition for Scenario 135a9237 (FR-008)

**Decision**: Populate the new Tone, Register, and Focus fields for scenario `135a9237` (Campground Intimacy) by decomposing its existing NarrativeTone string.

**Current NarrativeTone**:
> "Erotic, conversational, playful, and focused on physical pleasure. First-person limited perspective with fast pacing and low to moderate language complexity."

**Decomposition**:
- `Tone`: "Erotic, conversational, playful"
- `Register`: "Low to moderate language complexity"
- `Focus`: "Physical pleasure"
- (POV "First-person limited perspective" and pacing "fast pacing" are NOT narrative tone — they are handled by the character's `PerspectiveMode` and the `SceneDirection.Pacing` respectively, per the source plan section 3.1.)

**Rationale**: The decomposition follows the semantic boundaries defined in the source plan: Tone = mood/attitude, Register = language complexity, Focus = subject emphasis. POV and pacing are removed because they are owned by other systems (PerspectiveMode and SceneDirection).

---

### R6: Slot 17 Component Ordering Within the Writing Instruction Block

**Decision**: Order the 9 components within the Writing Instruction block as follows (Character variant):

1. Prose Style: `{ProfileName} — {ProfileDescription}`
2. Voice: `{SteeringProfile.RuleOfThumb}`
3. Tone: `{NarrativeSettings.Tone}` (+ Register appended if present)
4. Heat Level: `{IntensityProfile.ResolvedLabel} — {IntensityProfile.Description}`
5. Pacing: `{SceneDirection.Pacing text}`
6. POV: `Write in {first|third}-person from {actorName}'s point of view.`
7. Immersion: `{SteeringProfile.ImmersionDirective}`
8. Word Target: `Target {WordTargetMin}-{WordTargetMax} words.`
9. Action: `{SteeringProfile.ActionDirective}`

**Rationale**:
- Prose Style first (sets the authorial voice baseline)
- Voice second (the narrator's timeless style baseline — closely related to Prose Style)
- Tone third (scenario-specific mood, building on the voice)
- Heat Level fourth (the content intensity boundary — a hard constraint that should appear before operational directives)
- Pacing fifth (temporal constraint, paired with Heat Level as scene-level controls)
- POV sixth (the first operational directive — tells the model *whose* perspective to write from)
- Immersion seventh (the depth-of-field directive — builds on POV)
- Word Target eighth (the length constraint — a formatting directive)
- Action ninth (the final operational directive — "what to do now," positioned last for maximum recency within the block)

**Narrative variant differences**:
- POV: always "Write in third-person omniscient point of view."
- No Immersion directive (Narrative synthesizes, doesn't inhabit a character)
- Word Target uses NarrativeWordTargetMin/Max: "Target {NarrativeWordTargetMin}-{NarrativeWordTargetMax} words of scene synthesis."
- Action: replaced with narrative-specific constraints (zero dialogue, no new events, synthesis-only, physical detail checklist)

---

### R7: Backward Compatibility Path for NarrativeSettings (FR-008)

**Decision**: Three-tier resolution with explicit precedence.

**Resolution order** (first non-empty wins):
1. New `Tone` field (if populated)
2. Legacy `NarrativeTone` field (if new fields all empty)
3. Silent omit (if all empty)

**Register and Focus**: These have no legacy equivalent. If empty, they are silently omitted (they are enhancements, not required).

**Implementation**: `ResolvedWritingStyleData` (or a new `ResolvedNarrativeToneData` sub-record on `PromptBuildContext`) carries the resolved Tone/Register/Focus. The builder resolves at build time using the three-tier logic. Slot 17 reads the resolved values.

---

### R8: Fail-Fast Diagnostics for Missing SteeringProfile Fields (FR-006)

**Decision**: Each missing required field produces a distinct, actionable error.

**Error format**:
```
MissingPromptConfig: SteeringProfile '{profileName}' (Id={profileId}) is missing required field '{fieldName}'. FR-006 requires this field to be populated. No hardcoded fallback is permitted. Populate the field via the Style Profile management UI or a DB update.
```

**Required fields** (all fail-fast):
- `ImmersionDirective`
- `ActionDirective`
- `WordTargetMin`
- `WordTargetMax`
- `NarrativeWordTargetMin`
- `NarrativeWordTargetMax`

**Rationale**: Distinct errors per field let the user identify exactly what to populate. The error names the profile and field, and points to the UI or DB as the fix path. This complies with the repo Hard Rule (no fallbacks for RP engine values).

---

## Summary of Decisions

| ID | Decision | Alternatives Rejected |
|----|----------|----------------------|
| R1 | Scene Direction before Writing Instruction (Writing Instruction at absolute end) | Writing Instruction first; separate slot |
| R2 | Sensual: remove "mature, evocative language" + "deliberate pacing"; Emotional: remove "meaningful dialogue" + "conversation" + "emotional disclosure" | Keep unchanged; defer cleanup |
| R3 | Move Atmospheric to StyleProfiles with derived RuleOfThumb + populated new fields; delete from ToneProfiles; no session migration | Auto-migrate sessions; block migration; leave dangling |
| R4 | Sultry: ImmersionDirective="Stay inside... Show, don't tell.", ActionDirective="Respond to the scene naturally.", Character 200-400, Narrative 300-500 | Single word target range; per-scenario targets |
| R5 | 135a9237: Tone="Erotic, conversational, playful", Register="Low to moderate language complexity", Focus="Physical pleasure" | Keep as single NarrativeTone string |
| R6 | Slot 17 order: Prose Style → Voice → Tone → Heat Level → Pacing → POV → Immersion → Word Target → Action | Alphabetical; by source entity |
| R7 | Three-tier Tone resolution: new Tone → legacy NarrativeTone → silent omit | New fields only (breaks backward compat); legacy only (no new fields) |
| R8 | Distinct fail-fast error per missing SteeringProfile field, naming profile + field + fix path | Generic "missing config" error |
