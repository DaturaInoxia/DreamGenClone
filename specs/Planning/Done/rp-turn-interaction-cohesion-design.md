# RP Engine: Turn & Interaction Cohesion — Design Specification

**Date:** 2026-06-10
**Status:** Decisions applied — ready for implementation planning
**Session analyzed:** `48f42c62-e1e1-4de4-8465-60a4ddd3b60b` ("The Party")

---

## 1. Core Concepts

### 1.1 Turn vs Interaction — The Key Distinction

```
┌─────────────────────────────────────────────────────────┐
│ TURN (one user Continue click or auto-continue)          │
│                                                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │ Interaction 1: Character A (POV of the act)      │    │
│  ├─────────────────────────────────────────────────┤    │
│  │ Interaction 2: Character B (POV of the same act) │    │
│  ├─────────────────────────────────────────────────┤    │
│  │ Interaction 3: Persona/POV (observer or oblivious)│   │
│  ├─────────────────────────────────────────────────┤    │
│  │ Interaction 4: Narrative (omniscient, setting)   │    │
│  └─────────────────────────────────────────────────┘    │
│                                                         │
│  → Scene advances AFTER the full turn completes         │
│  → All interactions in one turn describe the SAME beat  │
└─────────────────────────────────────────────────────────┘
```

| Term | Definition |
|---|---|
| **Turn** | One unit of scene progression. Triggered by user clicking Continue or auto-continue. Produces N interactions. The scene beat changes between turns, not within. |
| **Interaction** | One character's perspective on the current turn's beat. Multiple interactions in a turn describe the **same moment** from different POVs. |
| **Beat** | A discrete story moment or physical act. One turn = one beat in Climax. |
| **Scene** | A continuous sequence of turns in the same location/context. |

### 1.2 Character Categories Per Turn

| Category | Who | Role in Turn |
|---|---|---|
| **Active participants** | Characters directly engaged in the act (e.g., Wife + OtherMan during Climax) | First active participant advances the beat; subsequent active participants describe same moment from their POV. |
| **Persona** | The POV character (Husband) | Always last before Narrative. Describes the same moment from their perspective — what they observe, think, or their parallel oblivious moment. Does NOT advance the sex act. |
| **Narrative** | Omniscient voice | Always last. Synthesizes all character perspectives into a rich account of the moment. Closes the turn. Does NOT advance. |
| **Excluded** | Characters not in the scene at all | Should NOT produce an interaction this turn. |

### 1.3 Actor Order Per Turn (All Phases Except Initial)

```
┌─────────────────────────────────────────────────────────┐
│ TURN                                                     │
│                                                         │
│  1. Active Participant 1  (Wife)     ← advances beat    │
│  2. Active Participant 2  (OtherMan) ← same moment, POV │
│  3. Persona               (Husband)  ← same moment, POV │
│  4. Narrative             (omniscient) ← close turn     │
│                                                         │
│  → Scene advances AFTER the full turn completes         │
│  → All interactions in one turn describe the SAME beat  │
└─────────────────────────────────────────────────────────┘
```

**Exception — Initial RP interactions:** The persona leads the first few turns to establish the scenario setup and husband-wife dynamic before other characters enter rotation. The current implementation (persona first for first 6 interactions via OtherMan exclusion) handles this correctly.

---

## 2. Current Implementation

### 2.1 Turn Orchestration (Working As Designed)

**File:** `RolePlayEngineService.cs` lines 1362-1420

The overflow continue path already encodes the Turn/Interaction distinction in the prompt text:

```csharp
// FIRST actor (i==0) in Climax:
"Advance the scene naturally from where it left off — escalate physical intimacy, 
 deepen the act, or progress to the next beat as continuity allows. 
 Describe the moment from your character's perspective with explicit physical and 
 sensory detail. Establish this turn's scene clearly so other participants can 
 react to the same moment."

// SUBSEQUENT actors (i>0) in Climax:
"Describe the same scene moment your turn-partner just established, from your own 
 perspective. Match and deepen the physical moment they set — give your character's 
 sensations, reactions, and dialogue for that exact beat. Do not advance to a new 
 act or jump ahead of what has already been established this turn."
```

The Narrative prompt (line 2545) also respects this:
```
"Write an omniscient narrative description of the full scene as it stands this turn... 
 All participants have already described this same moment from their own perspectives 
 — your role is to close the turn with a rich, omniscient account... 
 Do not advance the scene beyond what the characters have already established."
```

### 2.2 Actor Selection — Change Required

**File:** `RolePlayEngineService.cs` lines 2060-2068

**Current:** Persona is always inserted at position 0:
```csharp
// Persona is always first in the batch
actors.Insert(0, new OverflowActorCandidate(ContinueAsActor.You, personaName, personaReason));
```

**Required change:** Persona should be appended last (before Narrative), not inserted first. During initial RP interactions (first 6), keep current behavior.

```csharp
// DECISION: Persona is last before Narrative (all phases except initial)
if (totalInteractions >= 6)
{
    actors.Add(new OverflowActorCandidate(ContinueAsActor.You, personaName, 
        "Persona auto candidate (last before narrative)."));
}
else
{
    // Initial interactions: persona leads to establish husband-wife setup
    actors.Insert(0, new OverflowActorCandidate(ContinueAsActor.You, personaName, 
        "Persona auto candidate (initial lead for scenario setup)."));
}
```

### 2.3 Escalation Guidance — Must Become Turn-Aware

**File:** `RolePlayContinuationService.cs` lines 1700-1752

**Current:** `AppendEscalationGuidance` fires identically for ALL characters during Approaching/Climax.

**Problem:** The guidance says "Every turn must advance to a new beat" — but this fires for characters who are told to describe the SAME moment. The LLM gets contradictory instructions.

**Required change:** Escalation Guidance stays, but its content varies by the character's position in the turn. See Section 4 for exact prompt text.

### 2.4 Scene Presence Contract — Keep As-Is, With Turn Context

**File:** `RolePlayContinuationService.cs` lines 1168-1177

**Decision:** Keep the Scene Presence Contract. Its role (prevent fade-to-black, enforce "stay in the moment") remains correct. The contradiction with Escalation Guidance is resolved by making Escalation Guidance turn-aware (Section 2.3).

---

## 3. Resolved Gaps

### Gap 1: ✅ DECIDED — Persona Order
**Resolution:** Persona is last before Narrative in all phases except initial RP interactions (first 6). Initial interactions keep current persona-first ordering to establish husband-wife scene setup.

### Gap 2: ✅ DECIDED — Escalation Guidance Turn-Awareness
**Resolution:** Both Escalation Guidance and Scene Presence Contract are kept. The Escalation Guidance content varies by the actor's position in the turn (see Section 4). The turn-level prompt text already encodes the correct per-position direction. The gap is that `BuildPromptAsync` needs to know the actor's turn position to select the right guidance variant.

### Gap 3: ✅ DECIDED — Turn Position Awareness in Prompt
**Resolution:** A new "Turn Context" block is injected at the top of every prompt (see Section 4.2). This explicitly tells the model: how many interactions are in this turn, which position this interaction is, and what its role is.

### Gap 4: ⏸️ DEFERRED — Location Services
Not in scope for this design. When location services are off, all characters are treated as potentially in-scene (current behavior). The persona's observation state (watching vs not watching) is handled adequately by the current implementation.

### Gap 5: ⏸️ DEFERRED — Observer vs Oblivious Distinction
**User decision:** "Current implementation seems to be ok when the persona is watching or not watching." No change needed for this gap. The model handles this distinction naturally based on the interaction history context.

---

## 4. Dynamic Turn-Aware Prompt Design

### 4.1 Design Principle: Position + Count, Not Hardcoded Roles

The number of characters in a turn varies by scenario (2, 3, 6, 10 — no limit). The prompt must adapt dynamically. Instead of an enum (`First`, `Middle`, `LastCharacter`), we use two integers:

| Field | Source | Description |
|---|---|---|
| `TurnIndex` | `persistedTurn.TurnIndex` (already auto-increments in `StartTurnAsync`) | Groups interactions into the same turn. Already exists in `RolePlayTurn`. |
| `PositionInTurn` | Loop index `i` (0-based) in the overflow continue loop | Which character response this is (1..N) |
| `TurnActorCount` | `batchSize` (after clamping) | How many character responses are in this turn |

### 4.2 Turn Context Block (Injected at Top of Every Prompt)

Placed right after `"Behavior mode: {mode}"` in `BuildPromptAsync`. Generated dynamically:

```
Turn Context: turn {TurnIndex}, response {positionInTurn} of {turnActorCount}
- {turnActorCount} character responses this turn, in sequence, then a narrative close.
- {positionGuidance}
```

Where `positionGuidance` has exactly **two variants** (scales to any N):

**Position 1 (establishes the beat):**
```
- You are first this turn. Establish the scene beat — advance from where the 
  previous turn left off.
- The other {turnActorCount - 1} character(s) will describe this same moment 
  from their perspectives after you.
- Do not leave the beat unresolved — give it clear shape so others can react to it.
```

**Position > 1 (describes the established beat):**
```
- Describe the same scene beat established this turn, from your character's 
  perspective.
- Give your sensations, reactions, dialogue, and internal experience of this 
  exact moment.
- Do NOT advance to a new act, position, or story beat.
- {narrativeNote}
```

Where `{narrativeNote}` for the **last** position (persona, position == turnActorCount):
```
- The narrative closes the turn after your response.
```

For all other positions > 1, `{narrativeNote}` is empty.

**For Narrative (separate prompt, not in the loop):**
```
Turn Context: turn {TurnIndex}, narrative close
- All {turnActorCount} character responses for this turn are complete.
- Write an omniscient account: setting, character positions, sensations, atmosphere.
- Synthesize character perspectives into a rich, unified picture.
- Do NOT advance the scene beyond what the characters established this turn.
```

### 4.3 Escalation Guidance (Two Variants, Position-Triggered)

The `AppendEscalationGuidance` method checks position:

**Position 1 — full escalation (as current):**
```
Escalation Guidance:
- Advance the scene with clear forward momentum.
- Show concrete progression in physical intimacy this turn.
- Every turn must advance to a new beat.
- Vary who is the focus, the position, the tempo, or the specific sensation.
- Write explicit physical description — name body parts, movements, sensations.
```

**Position > 1 — deepen-only (modified):**
```
Scene Deepening Guidance:
- Deepen the physical moment already established this turn — give richer 
  sensory detail, internal reaction, and physical sensation for the same act.
- Do NOT advance to a new beat or position. Stay on the exact act established.
- Vary your character's specific sensation, thought, or reaction to add depth.
- Write explicit physical description matching the established act.
```

**Position == turnActorCount (persona, last before narrative):**
Escalation guidance is suppressed entirely. The Turn Context block provides sufficient direction.

### 4.4 Scene Presence Contract (Unchanged)

Kept as-is for all characters. Its role (prevent fade-to-black, "ONE RESPONSE = ONE SCENE MOMENT") is orthogonal to turn position.

### 4.5 Turn-Level Prompt Text (Engine Loop, Simplified)

The prompt text in `RolePlayEngineService.cs` lines 1395-1408 is simplified since the Turn Context block now carries the primary signal. Only a short direction remains:

```csharp
// Position 1 (i==0):
"Advance the scene naturally from where it left off."

// Position > 1 (i>0):
"Describe this same moment from your character's perspective."
```

---

## 5. Code Changes

### 5.1 Actor Order: Persona Last (After First 6 Interactions)

**File:** `RolePlayEngineService.cs` ~line 2060-2068

```csharp
// BEFORE:
actors.Insert(0, new OverflowActorCandidate(ContinueAsActor.You, personaName, personaReason));

// AFTER:
if (totalInteractions < 6)
{
    // Initial setup: persona leads to establish husband-wife dynamic
    actors.Insert(0, new OverflowActorCandidate(ContinueAsActor.You, personaName, 
        "Persona auto candidate (initial lead for scenario setup)."));
}
else
{
    // Persona is last character response before narrative
    actors.Add(new OverflowActorCandidate(ContinueAsActor.You, personaName, 
        "Persona auto candidate (last before narrative)."));
}
```

### 5.2 Pass Turn Metadata Through ContinueAsync

**File:** `RolePlayEngineService.cs` loop ~line 1388

```csharp
for (var i = 0; i < batchSize; i++)
{
    var candidate = sceneActors[i];
    var positionInTurn = i + 1;  // 1-based
    
    // ... existing promptText logic, simplified ...
    
    await _continuationService.ContinueAsync(
        session, actor, actorName, PromptIntent.Message, promptText,
        turnIndex: persistedTurn.TurnIndex,       // NEW
        positionInTurn: positionInTurn,             // NEW
        turnActorCount: batchSize,                  // NEW
        onChunk, cancellationToken);
}
```

### 5.3 ContinueAsync and BuildPromptAsync Accept Turn Metadata

**File:** `RolePlayContinuationService.cs`

Add parameters to `ContinueAsync`, `ContinueNarrativeAsync`, and `BuildPromptAsync`:
```csharp
int? turnIndex = null,
int? positionInTurn = null, 
int? turnActorCount = null
```

### 5.4 Inject Turn Context Block in BuildPromptAsync

**File:** `RolePlayContinuationService.cs` ~line 428

After `sb.AppendLine($"Behavior mode: {session.BehaviorMode}");`:

```csharp
if (turnIndex.HasValue && positionInTurn.HasValue && turnActorCount.HasValue)
{
    sb.AppendLine();
    sb.AppendLine($"Turn Context: turn {turnIndex.Value}, response {positionInTurn.Value} of {turnActorCount.Value}");
    sb.AppendLine($"- {turnActorCount.Value} character responses this turn, in sequence, then a narrative close.");
    
    if (positionInTurn.Value == 1)
    {
        sb.AppendLine($"- You are first this turn. Establish the scene beat — advance from where the previous turn left off.");
        sb.AppendLine($"- The other {turnActorCount.Value - 1} character(s) will describe this same moment from their perspectives after you.");
        sb.AppendLine($"- Do not leave the beat unresolved — give it clear shape so others can react to it.");
    }
    else
    {
        sb.AppendLine($"- Describe the same scene beat established this turn, from your character's perspective.");
        sb.AppendLine($"- Give your sensations, reactions, dialogue, and internal experience of this exact moment.");
        sb.AppendLine($"- Do NOT advance to a new act, position, or story beat.");
        if (positionInTurn.Value == turnActorCount.Value)
        {
            sb.AppendLine($"- The narrative closes the turn after your response.");
        }
    }
}
```

### 5.5 Narrative Prompt Uses Turn Metadata

**File:** `RolePlayEngineService.cs` ~line 1471

When calling `ContinueNarrativeAsync`, pass `turnIndex` and `turnActorCount` (no `positionInTurn` — narrative is separate):

```csharp
await _continuationService.ContinueNarrativeAsync(
    session, "Narrative", narrativePrompt,
    turnIndex: persistedTurn.TurnIndex,
    turnActorCount: batchSize,
    cancellationToken);
```

The Narrative Turn Context block (injected in `BuildPromptAsync` when `turnIndex` is set but `positionInTurn` is null) uses the narrative-specific text from Section 4.2.

### 5.6 AppendEscalationGuidance Becomes Position-Aware

**File:** `RolePlayContinuationService.cs` ~line 1700

Add `int? positionInTurn, int? turnActorCount` parameters. Logic:

```csharp
if (!positionInTurn.HasValue || !turnActorCount.HasValue)
{
    // No turn metadata — use current (full) guidance (backward compatible)
    // ... existing full escalation guidance ...
    return;
}

if (positionInTurn.Value == 1)
{
    // Position 1: full escalation guidance (current behavior)
    // ... existing code ...
}
else if (positionInTurn.Value < turnActorCount.Value)
{
    // Middle positions: deepen-only variant
    sb.AppendLine("Scene Deepening Guidance:");
    sb.AppendLine("- Deepen the physical moment already established this turn...");
    // ...
}
else
{
    // Last position (persona): suppressed
    // (no guidance injected)
}
```

### 5.7 Summary of Files Changed

| File | Change |
|---|---|
| `RolePlayEngineService.cs` | Actor order (persona last after 6 interactions), pass `turnIndex`/`positionInTurn`/`turnActorCount` to ContinueAsync and ContinueNarrativeAsync |
| `RolePlayContinuationService.cs` | Accept new params in `ContinueAsync`/`ContinueNarrativeAsync`/`BuildPromptAsync`, inject Turn Context block, make `AppendEscalationGuidance` position-aware |
| `InteractionRetryService.cs` | (No changes — retry uses its own prompt path, out of scope) |

No new types. No enum. Three integers threaded through existing method signatures.

---

## 6. Example Prompt Structure (3-Character Climax Turn, Turn 12)

```
┌─ POSITION 1: Becky (Wife) ──────────────────────────────┐
│ ...                                                      │
│ Turn Context: turn 12, response 1 of 3        ← NEW      │
│ - 3 character responses this turn, then a narrative close│
│ - You are first this turn. Establish the scene beat.     │
│ - The other 2 character(s) will describe this same moment│
│                                                          │
│ Escalation Guidance:                          ← FULL     │
│ Scene Presence Contract:                      ← KEPT     │
│ Message: Advance the scene naturally...                  │
└──────────────────────────────────────────────────────────┘

┌─ POSITION 2: Dean (OtherMan) ───────────────────────────┐
│ ...                                                      │
│ Turn Context: turn 12, response 2 of 3        ← NEW      │
│ - Describe the same scene beat established this turn.    │
│ - Do NOT advance to a new act, position, or story beat.  │
│                                                          │
│ Scene Deepening Guidance:                     ← MODIFIED │
│ Scene Presence Contract:                      ← KEPT     │
│ Message: Describe this same moment from your perspective.│
└──────────────────────────────────────────────────────────┘

┌─ POSITION 3: Ken (Persona) ─────────────────────────────┐
│ ...                                                      │
│ Turn Context: turn 12, response 3 of 3        ← NEW      │
│ - Describe the same scene beat established this turn.    │
│ - The narrative closes the turn after your response.     │
│                                                          │
│ (no Escalation Guidance)                      ← OMITTED  │
│ Scene Presence Contract:                      ← KEPT     │
│ Message: Describe this same moment from your perspective.│
└──────────────────────────────────────────────────────────┘

┌─ NARRATIVE ─────────────────────────────────────────────┐
│ ...                                                      │
│ Turn Context: turn 12, narrative close        ← NEW      │
│ - All 3 character responses are complete.               │
│ - Synthesize into an omniscient account.                │
│ - Do NOT advance the scene.                             │
└──────────────────────────────────────────────────────────┘
```

### 6.1 Scaling to N Characters (Same Two-Variant Logic)

| Actor Count | Position 1 | Positions 2..N-1 | Position N (Persona) | Narrative |
|---|---|---|---|---|
| 2 (Wife + Persona) | Wife: establish beat | — | Persona: describe same | Close turn |
| 3 (Wife + OM + Persona) | Wife: establish beat | OM: describe same | Persona: describe same | Close turn |
| 6 | Char A: establish beat | B,C,D,E: describe same | Persona: describe same | Close turn |
| 10 | Char A: establish beat | B..I: describe same | Persona: describe same | Close turn |

The Turn Context block text is generated from the three integers. **No hardcoded text per count.**

---

## 7. Implementation Phases

### Phase 1: Thread Turn Metadata (No Prompt Changes)
- Add `int? turnIndex = null`, `int? positionInTurn = null`, `int? turnActorCount = null` params to:
  - `ContinueAsync`, `ContinueNarrativeAsync` (in `RolePlayContinuationService`)
  - `BuildPromptAsync` (private)
- Pass values from engine loop; all other callers use defaults
- **Verification:** Build succeeds, all existing tests pass, zero functional change

### Phase 2: Actor Order Change
- In `ResolveSceneContinueActorsAsync`: persona `Add()` instead of `Insert(0)` when `totalInteractions >= 6`
- First 6 interactions: persona stays first (current, establishes husband-wife)
- **Verification:** Debug log `OverflowActorSelection` shows correct persona position

### Phase 3: Inject Turn Context Block
- In `BuildPromptAsync`, after Behavior mode line: inject Turn Context when metadata present
- Two text variants: position 1 vs position > 1 (Section 4.2)
- Narrative variant: when `turnIndex` set but `positionInTurn` is null
- **Verification:** `PromptBuilt` debug events show "Turn Context:" block with correct values

### Phase 4: Escalation Guidance Position-Aware
- `positionInTurn == 1`: full escalation guidance (unchanged)
- `1 < positionInTurn < turnActorCount`: "Scene Deepening Guidance"
- `positionInTurn == turnActorCount`: suppressed
- `null` (no metadata): current behavior (backward compatible)
- **Verification:** `PromptBuilt` events show correct guidance per position

### Phase 5: End-to-End Validation
- Create new session with "The Party" (3 characters)
- Run through full phase progression
- Verify: one turn = one beat, all characters describe same moment
- Verify: scene advances between turns, not within
- Create test with different character count (2, 4) to verify dynamic scaling

---

## 8. Open Questions

1. **Narrative during BuildUp/Reset:** The Turn Context says "all responses complete." During BuildUp/Reset where time pacing is encouraged, is this still correct?

2. **Explicit actor selection (Continue As popup):** Turn Context still applies — selected actors follow same position logic.

3. **Batch size vs total candidates:** Turn Context uses `batchSize` (actual), not `sceneActors.Count`. Correct — model only needs to know about responders.

4. **InteractionRetryService:** Retry uses `BuildPromptAsync`. If turn metadata passed, retry gets same Turn Context — correct since it's same position, same turn.

5. **TurnIndex scope:** Auto-increments per session (1, 2, 3...). Model just needs "same turn" vs "different turn" distinction.
