# Design & Analysis: Preprocessor Refinement — Beats, Participants, Full Turn, POV, Transparency

**Feature**: 001-scene-image-generator (B-032)  
**Date**: 2026-08-20  
**Status**: **DESIGN FINALIZED** — all open questions resolved; ready for implementation (P1→P6)  
**Tracks**: `change-requests.md` → CR-006

---

## 0. Executive summary

The current preprocessor selects characters by **naive name-substring matching** against a single interaction and **always includes the persona**, producing wrong subjects (e.g. Ken's profile injected when Becky + Dean are in bed). Two structural gaps cause this:

1. **No authoritative participant resolution** — the app already has a presence model (`RolePlayScenePresenceHelper.IsActorInScene`, `CharacterEncounterStates`, `CharacterLocations`, `CharacterSnapshots`) that the RP engine uses, but the preprocessor ignores it.
2. **No beat/scene awareness** — one interaction (or one **turn**) can span many beats (undressing → oral → sex → climax → recovering → dressing → leaving). The preprocessor has no idea which beat to depict.

This document is the **full design & analysis plan** for:

- **Option 2** — a **pre-analysis stage** that determines beats and participants before the prompt is built.
- **Option 3** — **user-driven beat + POV selection** in the studio.
- **Option 4** — a **transparency/debugging surface** showing what the preprocessor chose and why.

It also answers the user's specific question: **"is generating the same beat from multiple POVs too complex?"** — with an expert answer: *the beat + identity stay constant; only the framing line changes.* That keeps each image simple while enabling a coherent POV set.

---

## 1. Current behavior (verified root cause)

### 1.1 Character selection today (`SceneImagePromptPreprocessor.BuildCharacterAppearanceBlock`)

| Path | Logic | Failure |
|------|-------|---------|
| Actor | `interaction.ActorName` matched by name to scenario characters | Narrative/omniscient interactions have `ActorName = "Narrative"` → no actor match |
| Persona | **Always** added when it differs from the actor and has appearance data | Ken always injected even when not in the scene |
| Others | Any scenario character whose **exact name** is a substring of `interaction.Content` | "she"/"he", nicknames, or characters not in the scenario list → missed |
| "Characters present" line | `scenarioState.CharacterRoles.Values` | That is a list of **roles** ("Wife"), not names |

### 1.2 What is available but unused

- `RolePlayV2Turns` — **first-class turn records** with `OutputInteractionIds` (all interactions generated in that turn), `InputInteractionId`, `InitiatedByActorName`, `TurnIndex`. **This is the "Full Turn" source.**
- `RolePlaySession.Interactions` — the timeline; each has `ActorName`, `Content`, `InteractionType`, `NarrativePhaseAtCreation`, `WasInSexScene`, `EncounterNumberAtCreation`, `InteractionIndexInEncounter`.
- `AdaptiveScenarioState` — `CharacterEncounterStates` (who `IsHavingSex`/`IsHavingSexConfirmed`), `CharacterLocations` (+ truth state), `CharacterLocationPerceptions` (line-of-sight/proximity), `CharacterSnapshots`, `CharacterRoles`, `CurrentSceneLocation`, `CurrentPhase`, `CurrentEncounterNumber`, `IsEncounterActive`, `EncounterSummaries` (with `StartInteractionIndex`/`EndInteractionIndex`).
- `RolePlayScenePresenceHelper.IsActorInScene(session, name)` — tri-state presence resolver already used by the engine.
- `SceneImagePromptSent` debug event — already captures the full system+user prompt; `SceneImageResponseReceived` captures output.

---

## 2. The Full Turn concept (foundation)

### 2.1 Definition

A **Turn** = one user submission cycle (Message / Narrative / Continue / ContinueAs / AddInteraction) that produces **one or more interactions** (e.g. Becky's action, Dean's reaction, Ken's hidden observation, Narrative's omniscient synthesis). The engine already records this in `RolePlayV2Turns.OutputInteractionIds`.

### 2.2 Why expand from a single interaction to the full turn

- A single interaction is one actor's slice. The **Narrative** interaction is the omniscient synthesis with all environmental detail — exactly what an image prompt needs for setting.
- The beat may be *partially* in each actor's slice (Becky's POV shows the act from her angle; Narrative describes the full physical layout).
- **Resolution**: given a selected interaction id, find its turn via `RolePlayV2Turns` (match `InputInteractionId` or membership in `OutputInteractionIds`), then load all sibling interactions of that turn. Fallback: if no turn row exists (legacy), fall back to the single interaction + surrounding `Narrative` interactions in a small window.

### 2.3 Data contract (proposed)

```
FullTurnContext
├── Turn (RolePlayTurn metadata: TurnIndex, TurnKind, TriggerSource)
├── Interactions (all output interactions of the turn, ordered)
│     ├── ActorName, ActorType (Narrative|Npc|You|Custom), Content, Phase, Encounter#
├── ScenarioCharacters (id, name, role, gender, physical attributes, description)
├── SessionState (current scene location, phase, encounter#, IsEncounterActive)
└── PresenceMap (per character: in-scene? in-encounter? having-sex? location)
```

---

## 3. Option 2 — Pre-analysis stage: beats + participants

### 3.1 Goal

Before the preprocessor builds the image prompt, a dedicated analysis produces a **structured decision**:

```
SceneImageAnalysisResult
├── Beats[]                    # segmented beats of the turn
│     ├── BeatId               # "b1", "b2", ...
│     ├── Label                # "oral-sex", "climax", "undressing", ...
│     ├── Description          # 1-line depiction target ("Becky on her knees, giving oral to Dean")
│     ├── InteractionIds       # which interactions in the turn evidence this beat
│     ├── SubjectCharacterIds  # who is physically in this beat
│     └── DefaultSetting       # where it happens
├── Participants               # authoritative in-scene/in-encounter characters
│     └── (characterId, role, presence: in-scene|in-encounter|observer, reason)
└── SuggestedBeat              # the most depictable beat (or null)
```

### 3.2 Two implementation shapes

#### Shape 2A — LLM-structured pre-analysis (recommended)

A new background job stage (or a synchronous pre-call from the studio) that runs the text LLM over the **Full Turn** + presence map and returns the structured result above (JSON envelope, mirroring the existing `SceneImagePreprocessor.ParseOutput` tolerant-JSON pattern).

- **Input**: Full Turn context + presence map (so the LLM does NOT guess who is present — it is told).
- **Output**: beats + subjects + suggested beat, as structured JSON.
- **Deterministic post-pass**: intersect LLM `SubjectCharacterIds` with the **presence map** (Option 1 grounding). A subject not present/encountering is demoted unless the actor explicitly names them.
- **Cost**: one extra LLM call per prompt generation. Acceptable at single-user scale (image gen is already slow).
- **Failure mode**: on LLM failure or malformed JSON → fall back to current behavior (single interaction + persona) and mark `analysis: failed` in the debug event. **No silent wrong subjects.**

#### Shape 2B — Deterministic beat splitting (lighter)

Split the turn into beats using heuristics (encounter state, `WasInSexScene`, phase, position keywords via `SexualActivityKeywords`, `CharacterEncounterStates`), then for each beat assign subjects from the presence map. No extra LLM call.

- Cheaper, deterministic, testable.
- Weaker: cannot recognize *narrative* beats like "she was about to" vs "she finished" without LLM reading.

**Recommendation**: implement **2B first as the deterministic baseline** (drives participant resolution + beat boundaries from existing signals), then layer **2A** on top for beat *labeling + suggestion*. The presence grounding (Option 1) is the mandatory core for both.

### 3.3 Participant resolution rules (deterministic, used by both shapes)

| Rank | Rule | Source |
|------|------|--------|
| 1 | Actor of the beat's primary interaction | interaction.ActorName |
| 2 | Present at current scene location | `RolePlayScenePresenceHelper.IsActorInScene` == true |
| 3 | Active in current encounter | `CharacterEncounterStates.IsHavingSexConfirmed` \|\| `IsHavingSex` |
| 4 | Named in the beat/narrative text | content name match (only after 1–3) |
| Exclude | Persona **unless** it matches 1–4 | removes the "Ken always included" bug |

Persona is only injected when it actually participates (e.g. Ken hiding in the closet *is* an observer — he gets an "observer/not in-frame" presence flag rather than a subject entry).

### 3.4 Beat boundary heuristics (Shape 2B baseline)

- Start a new beat at: `WasEncounterStart`, `WasEncounterBoundaryDetected`, `InteractionIndexInEncounter == 0`, phase transition (`NarrativePhaseAtCreation` change), or a content signal (position/location noun change, "she finished", "he pulled away").
- Reuse `SexualActivityKeywords` (already exists for encounter detection) + `ContinuationMarkerCatalog` beat-stage concepts (open/escalate/resolve).
- Cap beats per turn (e.g. 6) to bound prompt size.

---

## 4. Option 3 — Studio: beat + POV selection

> **Confirmed decisions (2026-08-20):**
> 1. Beat selector is **always shown and user-selectable** (no auto-pick-with-override; the user explicitly picks the beat).
> 2. **POV options are derived from who is in the beat** (not a fixed four-way set).
> 3. **One POV at a time** — render the selected beat from one selected POV; re-render with a different POV to get another.
> 4. Turn boundary confirmed: **one user submission = one turn = one scene** (may contain 1, 2, or several/many beats depending on continuation settings).

### 4.1 Flow (user-facing)

1. User clicks **Generate image** on an interaction in the workspace → studio opens **scoped to the full turn** (not just that interaction).
2. Studio runs (or has cached) the **pre-analysis** → shows a **Beat selector** (always visible, user-selectable):
   - `[Beat 1: Undressing] [Beat 2: Oral] [Beat 3: Climax] [Beat 4: Recovering]`
   - Each beat card shows its label + subject characters + a mini-excerpt.
   - Selecting a beat re-runs POV resolution for that beat.
3. User picks a beat → studio shows a **POV selector derived from that beat's participants**:
   - `Omniscient (wide) | Becky (her view) | Dean (his view) | Ken (observer)`
   - Only POVs whose character is present in the beat appear. A solo beat (e.g. Becky alone dressing) offers only `Omniscient | Becky`.
4. User sets style/size/explicit (existing) and clicks **Render** (renders **one** image from the selected beat + POV).
5. The studio passes `{ turnId, beatId, pov }` to the pipeline; the preprocessor builds the prompt from the chosen beat + POV framing.
6. To get another POV of the same beat, the user keeps the beat selected, changes POV, and renders again (new `SceneImageRecord`; `RegenerateOfId`/`BeatId`+`Pov` keep them linked).

### 4.2 POV as a framing dimension (the expert answer)

**Question: "is generating the same beat from multiple POVs too complex?"**

**Answer: No — if POV is modeled as a *framing line*, not as narrative perspective.** Image models do not reliably understand "first-person POV" as an abstract concept; they *do* respond to concrete camera/framing language. The trick:

- **Keep constant** across the POV set: identity anchors (hair/eyes/body), beat description, setting, clothing state.
- **Vary only** the framing/composition line per POV.

| POV | Framing line (example, for the oral beat) |
|-----|-------------------------------------------|
| Omniscient | "wide shot, both figures fully visible, neutral third-person, full scene in frame" |
| Becky | "low angle from her perspective, Dean's torso and face above her, her hands and knees in the foreground, she looks up" |
| Dean | "high angle looking down at her, she is on her knees before him, his hands on her head, his view" |
| Ken (observer) | "hidden voyeur view from a gap in the doorway across the room, both figures at a distance, slightly blurred foreground edge (door frame)" |

Each single prompt is **simple** (identity + beat + one framing line + style/size). The POV *set* is coherent because identity + beat are shared. This is well within image-model capability and directly answers the user's example: **the same one snapshot of the beat from different POVs.**

**POV derivation rule (confirmed):** the available POV set = `Omniscient` + every participant present in the beat (per §3.3 presence rules). POVs whose character is not in the beat are **not offered**.

**When POV genuinely complicates things:** asking for "her inner emotional POV" or "what she's feeling" — that is *not* image-depictable and should be rejected by the beat analyzer (only physical/compositional POV is valid).

### 4.3 Storage / linking

- `SceneImageRecord` already snapshots `PromptSnapshot` + `SettingsJson` (CR-003). Add `BeatId` + `Pov` fields so the "Continue from this image" (CR-003) can restore the same beat + POV.
- `RegenerateOfId` already links POV variants to a parent image if desired.

---

## 5. Option 4 — Transparency / debugging

### 5.1 What exists

- `SceneImagePromptSent` / `SceneImageResponseReceived` debug events (full system+user prompt captured).
- Debug View surfaces them (T065).

### 5.2 What to add

1. **Structured selection event** `SceneImageAnalysisCompleted`:
   ```
   { turnId, beatId, pov,
     beats: [{label, subjects, evidenceInteractionIds}],
     participants: [{name, presence, reason}],
     excluded: [{name, reason}],      # e.g. "Ken — not present in beat"
     promptSources: { actor, persona, coPresent },
     settingsJson, rawAnalysis }
   ```
2. **In-studio panel** "Why this prompt?" — collapsible, shows:
   - the chosen beat + subjects + reason
   - the POV framing line
   - the exact system + user prompt sent (reuse the debug event content)
   - which characters were excluded and why
3. **Prompt preview before render** — the studio shows the composed prompt (already editable in the textarea; the panel makes the *context* visible).

This is the "I need to see what is happening" requirement, delivered structurally rather than as a log-dive.

---

## 6. End-to-end pipeline (target)

```
[Workspace] click Generate on interaction
   → Studio opens scoped to FULL TURN (resolve via RolePlayV2Turns)
   → [Option 2] Pre-analysis runs: beats + participants (2B baseline, 2A labeling)
        → deterministic presence pass (Option 1 grounding)
        → SceneImageAnalysisCompleted event
   → [Option 3] User picks Beat + POV
   → Preprocessor BuildMessages({ fullTurn, beat, pov, presenceMap, settings })
        → identity anchors (visual block, per participant)
        → beat description (chosen beat)
        → POV framing line
        → style/size/explicit (existing)
   → Render (existing) → SceneImageRecord stores { BeatId, Pov, SettingsJson, PromptSnapshot }
   → Debug View + studio panel show selection + prompt
```

---

## 7. Phasing & effort

| Phase | Scope | Effort | Risk |
|-------|-------|--------|------|
| **P1** | Option 1: presence-grounded participant resolution + persona fix + `Characters present` names | S | Low |
| **P2** | Full Turn context (turn resolver) + pass turn into preprocessor | S | Low |
| **P3** | Option 4: `SceneImageAnalysisCompleted` event + studio "Why this prompt?" panel | M | Low |
| **P4** | Option 2B: deterministic beat segmentation + beat list in studio | M | Med |
| **P5** | Option 3: POV framing dimension + POV selector + `BeatId`/`Pov` persistence | M | Med |
| **P6** | Option 2A: LLM beat-label/suggestion pass (structured) | M–L | Med |

**Recommended order**: P1 → P2 → P3 (fixes correctness + gives visibility fast), then P4 → P5 (beats + POV), then P6 (LLM labeling).

---

## 8. Open questions — RESOLVED (2026-08-20)

1. **Beat granularity** → **Always show the beat selector; the user explicitly selects the beat.** No auto-pick-with-override; the beat is a user-chosen input. (Confirms §4.1.)
2. **POV set** → **Derived from who is in the beat.** POV options = `Omniscient` + each participant present in the beat (§3.3). A solo beat offers only `Omniscient` + that character. No fixed four-way set.
3. **Scope of "full turn"** → **Confirmed: one user submission = one turn = one scene.** Depending on continuation settings a turn may contain 1, 2, or several/many beats. `RolePlayV2Turns.OutputInteractionIds` is the source.
4. **Per-POV images** → **One POV at a time, user-selectable.** Each render produces one image from the selected beat + POV; changing POV and rendering again creates a new linked record.

---

## 9. Files likely touched (design only, not yet implemented)

- `DreamGenClone.Web/Application/RolePlay/SceneImagePromptPreprocessor.cs` (presence grounding, turn input, POV framing)
- `DreamGenClone.Web/Application/RolePlay/SceneImageBeatAnalyzer.cs` (new — 2B, later 2A)
- `DreamGenClone.Web/Application/RolePlay/SceneImageTurnResolver.cs` (new — full-turn context)
- `DreamGenClone.Web/Application/RolePlay/SceneImagePromptGenerationJobHandler.cs` (turn load, analysis call)
- `DreamGenClone.Domain/RolePlay/SceneImageRecord.cs` (BeatId, Pov)
- `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor` (beat + POV selectors, transparency panel)
- `DreamGenClone.Web/Application/RolePlay/Models/*` (analysis DTOs)
- Tests under `DreamGenClone.Tests/RolePlay/`
