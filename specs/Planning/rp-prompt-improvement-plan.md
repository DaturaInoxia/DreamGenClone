# RP Prompt Improvement Plan

**Session analyzed:** `1f8112fb-a557-4bd3-a07e-935b4cfbcce8`  
**Interactions analyzed:**  
- `db6412b1` — Becky, Turn 30, Climax (character prompt)  
- `c8667fef` — Narrative, Turn 35, Climax (narrative prompt)  

**Prompt sizes:** 50,485 chars (character) / 50,093 chars (narrative)  
**Date:** 2026-07-17  
**Goal:** Open-world, immersive, feel-alive RP prompts

---

# PART 1: TARGET ARCHITECTURE

*What we're building toward. Defines the target state before any tactical fixes.*

---

## GAP-1: Target Prompt Structure

### Principle

A quality open-world RP prompt follows a **three-zone architecture** aligned with LLM attention mechanics:

- **Zone A (Primacy)** — First ~2,000 chars. Highest attention weight. Use for: scene grounding, actor assignment, turn structure. The model's first reads set the frame for everything that follows.
- **Zone B (Context)** — Middle ~25,000 chars. Lower attention weight but high capacity. Use for: character data, interaction history, session memory, location truth state. This is the "library" the model draws from.
- **Zone C (Recency)** — Last ~3,000 chars. Second-highest attention weight. Use for: theme contract, behavioral frames, intensity/pacing directives, final writing instruction. The model's last reads directly shape output.

### Two Prompt Variants

The system generates two fundamentally different prompt types. Both use the same 17-slot architecture, but each slot filters its content by prompt variant:

| Aspect | Character Prompt | Narrative Prompt |
|--------|-----------------|-----------------|
| **Actor** | One character (Becky, Dean, Ken) | Omniscient narrator |
| **POV** | 1st person ("I felt...") | 3rd person ("She felt...") |
| **Purpose** | Write one character's experience | Synthesize all characters into unified scene |
| **History emphasis** | This character's recent interactions | All characters' recent interactions |
| **Character data** | Self (full) + partners (full) + non-present (comparison) | All characters (lighter format) |
| **Behavioral frames** | Self + partners | All frames (for accurate portrayal) |
| **Persona** | Player persona (if actor == You) | NONE — Narrative has no POV persona |
| **Final instruction** | "Write as {character} in FIRST PERSON" | "Write omniscient narrative in THIRD PERSON" |
| **Word target** | 100-300 words (pacing-aware) | 300-500 words (synthesis needs more space) |
| **Response size** | ~1,500 chars | ~5,000 chars (3.5x larger) |
| **Memory role** | Source material for encounter enrichment | Primary source for encounter enrichment (S-027) |

The Narrative prompt is NOT a character prompt with a different final instruction. It's a distinct variant with different needs throughout. The slot architecture handles this via `ActorProfile` filtering (GAP-4).

### Target Structure (Slot-Based)

```
═══════════════════════════════════════════════════════════════
ZONE A — PRIMACY (Scene Grounding)                    ~1,500 chars
═══════════════════════════════════════════════════════════════

SLOT 1: Scene Anchor
  "Current scene: {location} — {phase} phase."
  One line. Replaces the dead "You are continuing..." header.
  Grounds the model in WHERE and WHEN immediately.

SLOT 2: Actor Assignment
  Character: "Continue as: {actorName} ({actorRole})."
  Narrative: "Write as omniscient narrator — synthesize all character perspectives."
  One line. Tells the model WHO it is writing and from what perspective.

SLOT 3: Turn Context
  "Turn {N}, response {pos} of {total}."
  Character: + 2-3 lines of position-specific guidance (pacing-aware).
  Narrative: + "All {N} character responses complete. Synthesize into unified scene."
  Tells the model its structural role this turn.

SLOT 4: Scene Location Lock
  "HARD CONSTRAINT — Scene Location: {currentLocation}."
  + continuity rule.
  Same for both variants — spatial grounding is universal.

SLOT 4a: World State
  "Day {N} of {total} — {dayOfWeek}. {timePhase} ({time})."
  Weather: {condition}, {temperature}°C. {humidity note}.
  World rhythm: {what's happening at this time of day}.
  Temporal pressure: {any active time constraints}.
  Same for both variants — world state affects all perspectives.

═══════════════════════════════════════════════════════════════
ZONE B — CONTEXT (World + History)                   ~25,000 chars
═══════════════════════════════════════════════════════════════

SLOT 5: Actor-Relevant Character Data
  Character variant:
  - For the writing actor: full character sheet (description + 
    appearance + intimate attributes, MERGED — no duplication)
  - For scene partners: full character sheet
  - For non-present characters: comparison reference only
    (endowment, stamina, skill — 1 line)
  Narrative variant:
  - All characters in lighter format (description + key attributes)
  - No POV persona (S-025 — Narrative has no persona)
  - No intimate behavioral self-awareness text

SLOT 6: Scenario Context (progressive compression)
  - Turns 1-10: full scenario block (name, plot, setting, goals, 
    conflicts, world rules, environmental details)
  - Turns 10+: compressed "World Context" summary
    (2-3 lines capturing the essential world state)

SLOT 7: Current Scene Location (full description)
  - Only the current scene location — full detail
  - Other locations with tracked characters: one-line summary
  - All other locations: omitted

SLOT 8: Writing Style Profile
  - Description (timeless — always include)
  - Example (timeless — always include)
  - Rule of Thumb (phase-aware — see GAP-6)

SLOT 9: Interaction History
  Character variant: Last 2-3 turns, all interactions (character + narrative)
  Narrative variant: Last 2-3 turns, all interactions (same — Narrative needs 
    to see what it's synthesizing)
  Both variants use tiered compression for older turns (S-026):
  - Turns 4-6: Narrative-only (the synthesis already captures what happened)
  - Turns 7+: Session Memory (encounter summaries)
  Formatted as [Type] Actor: Content. Excluded interactions filtered out.

SLOT 10: Session Memory (3-tier — see GAP-8)
  - Long-term: character backstory (static, compressed)
  - Medium-term: encounter summaries (sexual memory enriched)
  - Short-term: phase transition milestones (current cycle)

SLOT 11: Scene Continuity Anchor
  - Character Locations truth state (3-6 lines)
  - Cross-perceptions only (drop self-perceptions)
  - No confidence/LOS/Near annotations

═══════════════════════════════════════════════════════════════
ZONE C — RECENCY (Directives + Instruction)           ~3,000 chars
═══════════════════════════════════════════════════════════════

SLOT 12: Theme Contract (SINGLE INSTANCE)
  - Active theme name + description
  - Current phase guidance prose
  - Theme directives
  - STEERING RANK statement
  Appears ONCE. Not in Zone B, not at end-of-prompt. Only here.

SLOT 13: Behavioral Frames (SINGLE INSTANCE, actor-relevant)
  Character variant: Only frames for characters the actor will interact with
    - For Becky's prompt: Becky's frame + Dean's frame + Ken's (comparison only)
  Narrative variant: All frames (Narrative must portray all characters accurately)
  Appears ONCE. Not in Zone B, not at end-of-prompt.

SLOT 14: Scenario Guidance (narrative-aware)
  - Phase + active scenario + guidance line
  - Resistance band ONLY if threshold not yet crossed
  - No raw engine numbers

SLOT 15: Intensity + Pacing (MERGED, phase-aware)
  - Resolved intensity label + description
  - Intensity writing contract (style vs content distinction)
  - Pacing directive (single block — merged Escalation + 
    SceneTimeDirection)
  - Available positions (if Approaching/Climax)

SLOT 16: User Direction (if provided)
  - Only if user provided actual direction
  - Omit if generic "continue naturally" default

SLOT 17: Final Writing Instruction
  Character variant:
    - POV: "Write as {character} in FIRST PERSON. Use 'I' throughout."
    - Word target: 100-300 words (pacing-aware — S-021)
    - Style hint
  Narrative variant:
    - POV: "Write omniscient narrative in THIRD PERSON. Refer to characters by name."
    - Synthesis instruction: "Synthesize all character perspectives into unified scene."
    - Physical detail checklist: positions, contact, sensations, sounds, rhythm
    - HARD CONSTRAINT: Zero quoted speech (no dialogue in narrative)
    - HARD CONSTRAINT: Do not advance beyond what characters established
    - Word target: 300-500 words (synthesis needs more space)
  The LAST thing the model reads before generating.
  NOTE: S-024 (duplicate directive) is fixed by having exactly ONE instruction here.
```

### Why This Structure Works

1. **Primacy zone is lean** — scene grounding in ~1,500 chars instead of ~5,000 chars of dead headers + wrong-character POV + irrelevant scenario metadata
2. **Context zone holds the bulk** — history, memory, and character data sit where capacity matters, not where attention peaks
3. **Recency zone is authoritative** — every directive appears exactly once, in the high-attention position where the model weights them most
4. **No duplication** — each piece of content writes to exactly one slot
5. **Actor-aware throughout** — every slot filters by what the current actor needs
6. **Two variants, one architecture** — Character and Narrative prompts share the 17-slot structure but each slot produces variant-specific content. The Narrative prompt is a first-class participant in the architecture, not a character prompt with a different ending.

---

## GAP-2: Prompt Ordering Strategy

### LLM Attention Mechanics

LLMs exhibit two well-documented attention biases:

- **Primacy bias** — The first ~500-2,000 tokens receive disproportionate attention. The model uses these to establish the "frame" for interpreting everything that follows.
- **Recency bias** — The last ~500-1,500 tokens receive the second-highest attention weight. These directly shape the generated output.
- **Middle sag** — Content in the middle of a long prompt receives the lowest relative attention. This is where detail-heavy context belongs — the model retrieves from here as needed, but doesn't weight it as heavily for framing.

### Current Ordering Problems

```
[PRIMACY — high attention]
System header (wasted)                    ← dead tokens
Turn context (useful)                      ← good but buried under dead header
POV Persona: Ken (wrong character)        ← confusing frame
Scenario data (huge, static)              ← 3,000 chars of world-building
Locations (huge, irrelevant)              ← 8,000 chars, 80% irrelevant

[MIDDLE — low attention]
Interaction history (MOST IMPORTANT)     ← worst possible position
Session memory (important)               ← also buried
Theme contract (authoritative)           ← buried
Behavioral frames (authoritative)         ← buried

[RECENCY — high attention]
Duplicated constraints (noise)            ← 3,000 chars of repetition
Final directive (correct)                ← good but preceded by noise
```

### Proposed Ordering

```
[PRIMACY — high attention]
Scene anchor (1 line)                    ← immediate world grounding
Actor assignment (1 line)                ← immediate identity
Turn context (3 lines)                   ← structural role
Scene location lock (2 lines)            ← spatial constraint

[MIDDLE — low attention, high capacity]
Character data (actor-filtered)          ← who's in this scene
Scenario context (compressed)            ← world state
Current location (full)                  ← where we are
Writing style (phase-aware)              ← how to write
Interaction history (widened)            ← what just happened
Session memory (3-tier)                  ← what happened before
Scene continuity anchor                   ← where everyone is

[RECENCY — high attention]
Theme contract (single)                  ← what the scene is about
Behavioral frames (single, filtered)     ← how characters behave
Scenario guidance (narrative-aware)      ← steering direction
Intensity + pacing (merged)              ← how explicit, how fast
User direction (if provided)             ← specific instruction
Final writing instruction                ← POV + word target + style
```

### Key Principle

**The most authoritative directives go last. The most informative context goes in the middle. The most grounding frame goes first.** Nothing important appears only in the middle — if it's critical, it either opens the prompt or closes it.

---

## GAP-3: Token Budget Framework

### The Problem Without a Budget

The plan removes ~5,000 chars of duplication (good) but S-014b proposes widening history to ~24 interactions (~10,000+ chars added). Net result: the prompt grows from 50K to 55K chars. Without a budget, every fix creates pressure for the next fix, and the prompt regresses.

### Proposed Budget

| Component | Current | Target | Notes |
|-----------|---------|--------|-------|
| Zone A (Primacy) | ~5,000 | ~1,800 | Add world state slot |
| Zone B (Context) | ~35,000 | ~25,000 | Filter locations, compress scenario after turn 10, remove stat numbers |
| Zone C (Recency) | ~10,000 | ~3,000 | Remove all duplication, merge injectors |
| **Total** | **~50,000** | **~30,000** | **40% reduction** |

### Budget Mechanism

1. **Hard cap**: 35,000 chars (≈8,750 tokens at 4 chars/token). If the prompt exceeds this, the engine logs a warning and the oldest history interactions are trimmed first.

2. **Slot budgets**: Each slot has a max char allocation. If a slot exceeds its budget, the engine compresses (e.g., history trims older turns, scenario compresses to summary).

3. **Priority order for trimming**: When over budget, trim in this order:
   - Oldest interaction history (keep most recent)
   - Non-present character data (keep comparison reference only)
   - Scenario metadata (compress to summary)
   - Session memory (keep most recent encounters)
   - Never trim: Zone A, theme contract, final instruction

4. **Configurable**: The budget is a session-level setting (`MaxPromptChars`, default 35000) so users with larger context windows (128K models) can raise it.

### Why 35K?

- Most RP models have 8K-32K context windows. At 4 chars/token, 35K chars ≈ 8,750 tokens — leaves room for the model's output (1,000-2,000 tokens) within an 8K window.
- For 32K+ context models, the budget can be raised to 60K-80K, allowing wider history.
- The budget forces discipline: every section must earn its place.

---

## GAP-4: Actor-Awareness Principle

### The Principle

**Every section of the prompt must be filtered by what the current actor needs.** The actor being generated determines:
- Which character sheets are full vs. comparison-only
- Which behavioral frames are included
- Which history interactions are emphasized
- Which directives are relevant
- What POV instruction is given

### Actor Profiles

| Actor Type | Character Data | Behavioral Frames | History Emphasis | POV | Directives |
|-----------|---------------|-------------------|-----------------|-----|-----------|
| **Player (You)** | Full self + full scene partners + comparison for non-present | Self + partners | All interactions | 1st person | All |
| **NPC (scene partner)** | Full self + full player (if present) + comparison for non-present | Self + player + other present | Recent interactions with player | 1st person (character voice) | All except resistance band |
| **NPC (non-present)** | Full self + comparison for present chars | Self only | Recent interactions involving self | 1st person | Pacing + intensity only |
| **Narrative** | All characters (lighter format, no intimate self-awareness) | All frames | All interactions (needs to see what it's synthesizing) | 3rd person omniscient | All except POV-specific; includes physical detail checklist + zero-dialogue constraint |
| **Custom** | Full self + full scene partners | Self + partners | All interactions | Per configuration | All |

### Implementation Pattern

Instead of the current `BuildPromptAsync` which builds one prompt for all actors, the builder should:

```csharp
public async Task<string> BuildPromptAsync(
    RolePlaySession session,
    ContinueAsActor actor,
    PromptIntent intent,
    ...)
{
    var profile = ResolveActorProfile(actor, session);
    
    var prompt = new PromptBuilder()
        .WriteSlot(SceneAnchorSlot, session, profile)
        .WriteSlot(ActorAssignmentSlot, session, profile)
        .WriteSlot(TurnContextSlot, session, profile)
        .WriteSlot(LocationLockSlot, session, profile)
        .WriteSlot(CharacterDataSlot, session, profile)  // filtered by profile
        .WriteSlot(ScenarioContextSlot, session, profile) // compressed by turn count
        .WriteSlot(LocationSlot, session, profile)        // filtered by current scene
        .WriteSlot(WritingStyleSlot, session, profile)     // phase-aware
        .WriteSlot(HistorySlot, session, profile)         // widened, turn-based
        .WriteSlot(MemorySlot, session, profile)          // 3-tier
        .WriteSlot(ContinuitySlot, session, profile)      // cross-perceptions only
        .WriteSlot(ThemeContractSlot, session, profile)   // single instance
        .WriteSlot(BehavioralFramesSlot, session, profile) // filtered by profile
        .WriteSlot(ScenarioGuidanceSlot, session, profile) // narrative-aware
        .WriteSlot(IntensityPacingSlot, session, profile)  // merged
        .WriteSlot(UserDirectionSlot, session, profile)   // if provided
        .WriteSlot(FinalInstructionSlot, session, profile) // POV + word target
        .Build();
    
    return prompt;
}
```

Each slot receives the `ActorProfile` and filters its output accordingly. No slot writes content the actor doesn't need.

---

## GAP-5: World State — Time, Season, Weather, Environment

Open-world RP requires the world to feel like it exists independently. Time of day, weather, season, and day tracking shouldn't just be flavor text — they should actively steer what can happen, what makes sense, and what's likely.

> **Engine implementation reference**: The Weather & Environmental System is backlog item **B-062** (`specs/Planning/B-062-weather-environmental-system.md`), which covers the full engine layer: domain model, weather detection, transition engine, narrative gate integration, theme affinity, UI widget, and persistence. **This GAP defines the prompt-facing side only** — the slot, positioning, and content format that B-062 will populate.

### The Current Problem

| World dimension | Current state | What's missing |
|----------------|--------------|----------------|
| **Time of day** | Static scenario text: "Mornings, afternoons, evenings, and late nights each offer distinct moods" | No actual tracking of what time it is right now. The model can't reference "it's 2am and the campground is silent" unless the history happens to mention it. |
| **Day tracking** | None. No concept of "day 3 of the two-week vacation." | Temporal progression. The model can't write "on the fourth morning..." because the engine doesn't track what day it is. |
| **Season / weather** | Static scenario text: "Mid-summer, sweltering heat" | No dynamic weather. A thunderstorm, a cool evening breeze, a heatwave — all would steer outcomes differently but don't exist. |
| **Environmental effects** | None | The world shouldn't just be backdrop. Rain makes outdoor encounters impossible. Heat drives characters to the beach. Night makes certain locations risky. Dawn brings the campground to life. |
| **Temporal pressure** | None | The vacation has an end date. The weekend ends Sunday night. The husband returns from his hike in an hour. These pressures should feel real. |

### What "World State" Should Capture

```
World State:
- Day {N} of {vacationLength} — {dayOfWeek}
- Time: {timeOfDay} ({specificTime}) — {timePhase}
  e.g., "Day 4 of 14 — Wednesday. Time: Late morning (10:30am) — the heat is building."
- Weather: {currentWeather}, {temperature}
  e.g., "Clear sky, 33°C. The air is heavy with humidity. Heat shimmers off the gravel."
- World rhythm: {what's happening at this time}
  e.g., "Morning: campers are showering, the hiking trail is busy. The campground is quiet by 2pm 
  when the heat peaks. Evening: the fire pit area fills up. Night: trailers glow with interior lights."
- Temporal pressure: {any active time constraints}
  e.g., "Ken is hiking the north loop — typically returns by 4pm. It's 3:45pm."
  e.g., "The weekend ends Sunday — residents pack up and leave by 5pm."
```

### Proposed Prompt Design

**SLOT 4a: World State** — positioned in Zone A (primacy), between Scene Location Lock (Slot 4) and Actor-Relevant Character Data (Slot 5):

```
ZONE A:
  SLOT 1: Scene Anchor
  SLOT 2: Actor Assignment
  SLOT 3: Turn Context
  SLOT 4: Scene Location Lock
  SLOT 4a: World State ← NEW — dynamic time/weather/environment
ZONE B:
  SLOT 5: Actor-Relevant Character Data
  ...
```

**Slot content** (populated by B-062 engine at generation time):

```
World State:
- Day {N} of {total} — {dayOfWeek}. {timePhase} ({time}).
- Weather: {condition}, {temperature}°C. {humidity/description}.
- World rhythm: {ambient activity appropriate to time/location}.
- Temporal pressure: {any active time constraints}.
```

**Token cost**: ~300 chars. High-impact positioning (Zone A primacy) for relatively low cost.

### Why World State Must Be in Zone A

The model reads this immediately after scene grounding. It sets the frame for everything that follows:
- "It's 2am and raining" → every directive reads differently than "it's noon and sunny"
- The interaction history is reinterpreted through the lens of the current world state
- Character behavior takes environmental context into account before any directive is applied

### Follow-Up

After the prompt design is implemented, **B-062** provides the engine layer that populates this slot with live data. The slot is designed to consume whatever B-062 produces — condition, temperature, time phase, day tracking, temporal pressures — without the prompt builder needing to know how they were computed.

---

## GAP-5b: Immersion Features — NPC Agency, Time Pressure, Consequences

*Previously GAP-5. World state moved to GAP-5, immersion features renumbered to GAP-5b.*

### 1. NPC Agency

**Problem**: Currently, NPCs (Dean, Ken) only act when the turn system activates them. They have no independent desires, plans, or reactions that surface unprompted.

**Fix**: Add an **NPC Agency Directive** to the prompt for NPC actors:

```
NPC Agency Directive:
- You are not a passive responder. You have your own desires, plans, and emotional state.
- If the scene offers an opportunity that aligns with your character, take initiative — 
  don't wait for the other character to act first.
- Your reactions should reflect your internal state, not just respond to what happened.
- You remember past encounters and they color your current behavior.
- You have a life outside this scene — reference it when natural (other plans, 
  other people, your own concerns).
```

**Source**: New injector at priority 15 (after TurnContext, before TimeLocation).

### 2. Time Pressure

**Problem**: The world only moves when the player advances it. Other characters don't have schedules, the environment doesn't change on its own.

**Fix**: Add a **World Time Context** slot:

```
World Time Context:
- Current time of day: {timeOfDay} (morning/afternoon/evening/night)
- Time pressure: {anyPendingEvents}
  - e.g., "Ken usually returns from his hike by 4pm — it's currently 3:30pm."
  - e.g., "The campground social starts at 7pm — Dean mentioned he'd be there."
- The world moves whether the player acts or not. Other characters have schedules.
```

**Source**: Requires engine tracking of world time + NPC schedules. New slot in Zone B.

### 3. Consequence Accumulation

**Problem**: Past actions don't echo forward. Becky's exhibitionism at the clothesline doesn't affect how Dean looks at her later — unless the model happens to remember it from history.

**Fix**: The 3-tier memory (GAP-8) handles this. Encounter summaries should explicitly capture **consequences**:

```
Encounter Memory should capture:
- What happened (plot)
- What she felt (emotion)
- What she learned (sexual/self-knowledge)
- What changed (relationship dynamic, power balance, guilt level)
- What risk was taken (near-miss, discovery risk)
- What the other character now knows/suspects
```

**Source**: Enhance `EncounterSummaryJobHandler` enrichment prompt to capture these dimensions.

## GAP-6: Phase-Specific Writing Style Content

### The Problem

S-013 says "make Rule of Thumb phase-aware" but doesn't define what it should say in each phase. Implementation will guess. Here's the concrete content:

### Phase-Specific Rule of Thumb

| Phase | Rule of Thumb Text |
|-------|-------------------|
| **Opening** | "Favor atmosphere and sensory grounding. Establish the world, the characters, and the mood before any tension begins. Let the reader settle into the setting." |
| **BuildUp** | "Favor atmosphere, tension, and sensory detail over speed. Let desire accumulate before anything explicit happens. Build anticipation through what characters notice, feel, and almost-do." |
| **Committed** | "Balance atmosphere with forward momentum. The tension is established — now let it simmer. Characters are aware of the dynamic; let that awareness color their interactions without resolution." |
| **Approaching** | "Tighten the pace. The tension is escalating — let proximity, accidental contact, and charged glances carry the scene. Sensory detail should heighten, not linger. The characters are drawn toward the threshold." |
| **Climax** | "The culmination is here. Maintain the evocative, sensory-rich style but with urgency and compression. Every sentence should advance the encounter. Do not slow for atmosphere — the atmosphere IS the encounter now." |
| **Reset** | "Let the emotional aftermath breathe. Sensory detail over action. The intensity has passed — now write the quiet, the guilt, the replay, the return to ordinary texture. The character is alone with what they did." |

### Implementation

```csharp
// RolePlayContinuationService.cs:838-840
private static string GetPhaseAwareRuleOfThumb(string phase)
{
    return phase.ToLowerInvariant() switch
    {
        "opening" => "Favor atmosphere and sensory grounding...",
        "buildup" => "Favor atmosphere, tension, and sensory detail over speed...",
        "committed" => "Balance atmosphere with forward momentum...",
        "approaching" => "Tighten the pace...",
        "climax" => "The culmination is here...",
        "reset" => "Let the emotional aftermath breathe...",
        _ => styleProfile.RuleOfThumb // fallback to configured
    };
}
```

### Why This Works

- Each phase's Rule of Thumb **aligns** with the phase-synced Intensity and Pacing systems instead of fighting them
- The progression is **continuous** — each phase naturally flows into the next
- The style Description and Example remain timeless (they describe the aesthetic, not the tempo)
- The fallback preserves the configured Rule of Thumb for unknown phases

---

## GAP-7: Structural Refactor — Template-Based Prompt Builder

### The Root Cause

`BuildPromptAsync` is a 900-line procedural method that:
- Writes inline blocks (Turn Context, POV Persona, Scenario, Characters, Locations, Style, History, Memory, Continuity, Stats, Theme, Guidance, Frames, Constraints, Priorities, Intensity, Positions, Beat Stage, Instruction, Message, Frames-again, Constraints-again, World-Rules-again, Final Directive)
- Calls the coordinator (13 injectors)
- Writes more inline blocks after the coordinator

The result: content appears 2-3x because there's no single authority for where each piece goes. The inline pipeline and the coordinator both write the same kinds of content.

### The Refactor

Replace the procedural method with a **slot-based template builder**:

```csharp
public sealed class RolePlayPromptBuilder
{
    private readonly List<IPromptSlot> _slots;
    private readonly ILogger<RolePlayPromptBuilder> _logger;

    public RolePlayPromptBuilder(IEnumerable<IPromptSlot> slots, ILogger<RolePlayPromptBuilder> logger)
    {
        // Slots are ordered by Zone A → B → C
        _slots = slots.OrderBy(s => s.Zone).ThenBy(s => s.Order).ToList();
        _logger = logger;
    }

    public async Task<string> BuildAsync(PromptBuildContext context, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var charCount = 0;
        var maxChars = context.Session.MaxPromptChars ?? 35000;

        foreach (var slot in _slots)
        {
            if (!slot.ShouldWrite(context)) continue;

            var text = await slot.WriteAsync(context, ct);
            
            // Budget enforcement
            if (charCount + text.Length > maxChars && slot.IsTrimEligible)
            {
                text = slot.Trim(text, maxChars - charCount);
                _logger.LogWarning("Prompt slot {SlotId} trimmed to fit budget", slot.Id);
            }

            sb.Append(text);
            charCount += text.Length;
        }

        _logger.LogInformation(
            "Prompt built: SessionId={SessionId} Actor={Actor} Phase={Phase} " +
            "Chars={Chars} Slots={SlotsFired}",
            context.Session.Id, context.ActorName, context.Phase,
            charCount, _slots.Count(s => s.ShouldWrite(context)));

        return sb.ToString();
    }
}

public interface IPromptSlot
{
    string Id { get; }
    PromptZone Zone { get; }     // A, B, or C
    int Order { get; }           // within zone
    bool ShouldWrite(PromptBuildContext context);
    Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct);
    bool IsTrimEligible { get; } // can be trimmed if over budget
    string Trim(string text, int maxChars);
}

public enum PromptZone { A, B, C }
```

### Slot Inventory

| Zone | Order | Slot ID | Replaces | Trim Eligible |
|------|-------|---------|----------|---------------|
| A | 1 | scene-anchor | System header (S-001) | No |
| A | 2 | actor-assignment | "Continue as:" line | No |
| A | 3 | turn-context | Inline Turn Context + TurnContextInjector (S-003) | No |
| A | 4 | location-lock | Scene Location Lock (S-007) | No |
| A | 4a | world-state | NEW — dynamic time/weather/environment (GAP-5) | No |
| B | 5 | character-data | Character sheets + BEHAVIORAL CONSTRAINT (S-008, S-009) | Yes |
| B | 6 | scenario-context | Scenario metadata (S-011) | Yes |
| B | 7 | current-location | Locations filtered (S-012) | Yes |
| B | 8 | writing-style | Writing Style Profile (S-013) | No |
| B | 9 | interaction-history | History window (S-014b) | Yes |
| B | 10 | session-memory | Session Memory (S-022) | Yes |
| B | 11 | continuity-anchor | Scene Continuity (S-015) | No |
| C | 12 | theme-contract | Theme Contract + ThemeAIGuidanceInjector (single) | No |
| C | 13 | behavioral-frames | Behavioral Frames (single, filtered) | No |
| C | 14 | scenario-guidance | Scenario Guidance (narrative-aware, S-020) | No |
| C | 15 | intensity-pacing | Intensity + Pacing merged (S-019) + Positions | No |
| C | 16 | user-direction | Message (S-023) | No |
| C | 17 | final-instruction | Final Writing Directive (S-021) | No |

### Migration Strategy

1. **Phase 1**: Create `IPromptSlot` interface and `RolePlayPromptBuilder`
2. **Phase 2**: Implement each slot by extracting from `BuildPromptAsync` (move existing code into slots)
3. **Phase 3**: Replace `BuildPromptAsync` call with `RolePlayPromptBuilder.BuildAsync`
4. **Phase 4**: Delete the 900-line method and the coordinator's inline duplicates
5. **Phase 5**: Update tests to verify slot output instead of full-prompt string matching

### Why This Works

- **No duplication possible** — each piece of content has exactly one slot
- **Actor-awareness** — each slot receives the `ActorProfile` and filters
- **Budget enforcement** — the builder enforces the token budget (GAP-3)
- **Testable** — each slot is unit-testable in isolation
- **Extensible** — new features add new slots, don't modify a 900-line method
- **Auditable** — logging shows which slots fired and the char count per slot

---

## GAP-8: 3-Tier Memory Architecture

### Current State

| Tier | Implemented As | Status |
|------|---------------|--------|
| Long-term | Scenario Data (character descriptions, backstory) | ✅ Present but static |
| Medium-term | `EncounterSummaries` (EncounterCompletion type) | ⚠️ Only 1 encounter detected; captures plot not sexual memory |
| Short-term | Interaction History (last 12 interactions) | ⚠️ Only 4 turns (S-014b) |

### Target Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ TIER 1: LONG-TERM MEMORY (Background)                       │
│ Injected: Once at session start, compressed after turn 10    │
│ Source: Scenario Data + Character backstory                  │
│ Persistence: Session-level, static                           │
│                                                             │
│ Contents:                                                   │
│ - Character backstory (Becky: married 20 yrs, unfulfilled)  │
│ - Relationship history (Ken: complacent, no passion)        │
│ - Key personality traits (prone to guilt, easily swayed)   │
│ - Physical self-knowledge (what she knows about her body)  │
│ - Pre-session significant events (if any)                   │
│                                                             │
│ Format: 3-5 lines per character, compressed                 │
│ Token cost: ~500 chars                                      │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ TIER 2: MEDIUM-TERM MEMORY (Encounter Memory)               │
│ Injected: Every prompt, current cycle encounters             │
│ Source: EncounterSummaryRecord (EncounterCompletion)        │
│ Persistence: DB (RolePlayV2EncounterSummaries)               │
│                                                             │
│ Enrichment input (S-027):                                    │
│ - Narrative response (omniscient physical account)          │
│ - Character responses (emotional/POV detail)                │
│ → LLM produces sexual memory                                │
│                                                             │
│ Contents per encounter:                                     │
│ - What happened (plot summary)                              │
│ - What she felt (emotional texture)                         │
│ - What she learned (sexual self-knowledge)                  │
│ - What changed (relationship dynamic, guilt level)          │
│ - What risk was taken (near-miss, discovery risk)          │
│ - What the other character now knows/suspects              │
│ - Sexual comparison anchors (how this compared to before) │
│                                                             │
│ Format: [Encounter N — Character] + 3-5 sentence prose      │
│ Token cost: ~300 chars per encounter × 5 encounters = 1,500 │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ TIER 3: SHORT-TERM MEMORY (Scene Window) — TIERED (S-026)   │
│ Injected: Every prompt, tiered compression                   │
│ Source: Interaction History (GetContextView)                │
│ Persistence: Session payload (Interactions)                 │
│                                                             │
│ Layer 1 — LAST 2-3 TURNS: All interactions                  │
│   Character + Narrative responses                           │
│   Full detail for immediate continuity                      │
│   ~9,500 chars/turn × 3 = ~28,500 chars (trimmed to budget) │
│                                                             │
│ Layer 2 — TURNS 4-6: Narrative only                        │
│   Only the Narrative response per turn                       │
│   Synthesized omniscient view — "what happened"             │
│   ~5,000 chars/turn × 3 = ~15,000 chars                     │
│                                                             │
│ Layer 3 — TURNS 7+: Session Memory (Tier 2)                │
│   Compressed encounter summaries                            │
│   ~300 chars/encounter × 5 = ~1,500 chars                  │
│                                                             │
│ Total budget: ~20,000 chars (within 35K prompt budget)      │
└─────────────────────────────────────────────────────────────┘
```

### Why Tiered History Compression Works

1. **Recent turns need full detail** — the model needs per-character voices, dialogue, and internal thoughts for immediate continuity
2. **Older turns only need the synthesis** — the Narrative response already captures what happened at a higher level than individual character responses
3. **Narrative responses are memory-ready** — they're already 3rd person, omniscient, LLM-generated prose describing the full scene
4. **Token budget respected** — instead of 8 turns × 9,500 chars = 76,000 chars (way over budget), tiered compression fits in ~20,000 chars
5. **No information loss** — the Narrative response IS the synthesis of the character responses. Keeping it preserves the essential information while dropping the redundant per-character detail.

### Changes Required

#### 1. Encounter Summary Enrichment Prompt (Tier 2)

**Current** enrichment prompt captures plot. **New** enrichment prompt must capture sexual memory:

```
You are writing a sexual encounter memory for {characterName} in an ongoing role-play.

Encounter {encounterNumber} at {sceneLocation}.

Write a 3-5 sentence first-person memory from {characterName}'s perspective that captures:
1. What happened — the key physical and emotional beats of this encounter
2. What she/he felt — the dominant emotional texture (guilt, thrill, shame, desire, satisfaction)
3. What she/he learned — any sexual self-knowledge gained (what felt good, what surprised them, what they want again)
4. What changed — how this encounter shifted the relationship dynamic or their self-image
5. What risk was taken — any near-miss, discovery risk, or boundary crossed
6. Sexual comparison — if this is not the first encounter, how it compared to previous ones (more confident? more guilty? more physically intense?)

Write in {characterName}'s voice. Be specific and sensory. This memory will be injected into future prompts to maintain continuity across encounters.
```

**Source**: `EncounterSummaryJobHandler.cs` — update the enrichment prompt.

#### 2. Encounter Detection Reliability (Tier 2)

**Current**: Detection requires male orgasm/ejaculation in narrative. If the model doesn't write Dean ejaculating, no new encounter is detected.

**Fix**: Add **secondary detection signals**:
- Scene change (location transition after intimacy)
- Significant time passage ("later that evening", "the next morning")
- Explicit encounter boundary language ("when it was over", "after they dressed")
- Phase transition (Climax → Reset always creates an encounter summary)

**Source**: `RolePlayEngineService.cs:TryDetectEncounterBoundaryAsync` — add secondary signal detection.

#### 3. History Window Widening (Tier 3)

**Current**: `TakeLast(12)` = 4 turns.

**Fix**: Turn-based window:

```csharp
// Instead of:
var windowSize = Math.Max(12, session.ContextWindowSize);
foreach (var interaction in contextView.TakeLast(windowSize))

// Use:
var turnWindowSize = Math.Max(8, session.ContextWindowTurns); // default 8 turns
var interactionsByTurn = contextView
    .GroupBy(i => i.TurnIndex)
    .OrderByDescending(g => g.Key)
    .Take(turnWindowSize)
    .SelectMany(g => g.OrderBy(i => i.SequenceInTurn))
    .Reverse()
    .ToList();
```

**Source**: `RolePlayContinuationService.cs:846-847` — change to turn-based window.

#### 4. Long-Term Memory Compression (Tier 1)

**Current**: Full scenario data injected every turn (~3,000 chars).

**Fix**: After turn 10, compress to summary:

```csharp
if (session.Interactions.Count(i => !i.IsExcluded) > 10)
{
    // Compressed world context
    sb.AppendLine($"World: {scenario.Name} — {scenario.Setting.WorldDescription.Substring(0, 200)}...");
    sb.AppendLine($"Core conflict: {scenario.Plot.Conflicts.FirstOrDefault()}");
    sb.AppendLine($"Active goals: {string.Join(", ", scenario.Plot.Goals.Take(2))}");
}
else
{
    // Full scenario block (current behavior)
}
```

**Source**: `RolePlayContinuationService.cs:578-680` — add compression after turn 10.

### Why This Architecture Works

1. **Long-term** grounds the character and world — stable, compressed, always present
2. **Medium-term** carries the sexual/emotional arc — each encounter is distinct, comparable, cumulative
3. **Short-term** maintains scene continuity — enough turns to follow the immediate narrative
4. **The model can write callbacks** — "This time was different from the shower. Then she'd been nervous. Now she pushed him against the tile and took what she wanted." This requires medium-term memory to carry the emotional texture of each encounter.
5. **Token budget balanced** — Tier 1 (~500) + Tier 2 (~1,500) + Tier 3 (~10,000) = ~12,000 chars for memory, leaving ~23,000 for everything else within the 35K budget.

---

# PART 2: TACTICAL FINDINGS

*What's broken in the current prompt. Each finding maps to a slot in Part 1.*

---

## P0 — Critical (4 items)

### S-003: Delete duplicate Turn Context from TurnContextInjector
- **Source:** `TurnContextInjector.cs` + `RolePlayContinuationService.cs:509-541`
- **Problem:** Word-for-word duplicate of inline Turn Context block. Appears ~5,000 chars later mid-prompt. Zero new information.
- **Fix:** Remove TurnContextInjector or suppress via ShouldFire. Keep only the inline version that runs before the coordinator.
- **Maps to:** Slot 3 (turn-context)

### S-006: Gate POV Persona injection to player actor only
- **Source:** `RolePlayContinuationService.cs:549-575`
- **Problem:** "POV Persona: Ken" unconditionally injected into every prompt including NPC actors. ~500+ chars of Ken's description + physical attributes + intimate behavior text in Becky's prompt. Creates POV confusion ("POV Persona: Ken" vs "Continue as: Becky").
- **Fix:** Gate on `actor == ContinueAsActor.You`. For NPCs, inject nothing or at most `Player character: Ken (Husband)`.
- **Maps to:** Slot 5 (character-data) + GAP-4 (actor-awareness)

### S-008: Merge Appearance + BEHAVIORAL CONSTRAINT — remove per-character duplication
- **Source:** `RolePlayContinuationService.cs:578-820`
- **Problem:** Each character's intimate attributes appear twice within ~3 lines: once in the Appearance block and again in the BEHAVIORAL CONSTRAINT block. Zero new information. ~200 chars duplicated per character × 3 characters = ~600 wasted chars per prompt.
- **Fix:** Merge into a single block. Remove the BEHAVIORAL CONSTRAINT duplicate.
- **Maps to:** Slot 5 (character-data)

### S-013: Make Writing Style Rule of Thumb phase-aware
- **Source:** `RolePlayContinuationService.cs:838-840`
- **Problem:** "Favor atmosphere over speed. Let desire accumulate before anything explicit happens" is a slow-burn directive that conflicts with BOTH the phase-synced Intensity Profile ("Maximum explicitness. Raw.") AND the phase-synced pacing injectors ("Fast. Compress beats."). This is a three-way conflict where the static Writing Style is the lone dissenter.
- **Fix:** Make Rule of Thumb phase-aware or suppress during Climax. Description and Example are timeless — keep those.
- **Maps to:** Slot 8 (writing-style) + GAP-6 (phase-specific content)

---

## P1 — High (8 items)

### S-001: Remove "You are continuing an interactive role-play scene."
- **Source:** `RolePlayContinuationService.cs:505`
- **Problem:** Dead tokens by turn 30. Wastes highest-attention first-line position on generic meta-instruction.
- **Fix:** Delete. Optionally replace with dynamic scene-anchoring sentence.
- **Maps to:** Slot 1 (scene-anchor)

### S-002: Remove "Behavior mode: TakeTurns"
- **Source:** `RolePlayContinuationService.cs:506`
- **Problem:** C# enum label, not natural language. Turn Context block 4 lines later already explains this.
- **Fix:** Delete.
- **Maps to:** Slot 1 (scene-anchor)

### S-009: Reformat non-present characters as comparison reference
- **Source:** `RolePlayContinuationService.cs:578-820`
- **Problem:** Ken's intimate attributes ARE needed for comparison (Becky compares Dean to Ken), but the current format injects his FULL character sheet as an equal peer. Only 3-4 attributes are comparison-relevant.
- **Fix:** Replace with concise comparison block: `Comparison: Ken (Husband) — endowment: below-average; stamina: quick; skill: below average.`
- **Maps to:** Slot 5 (character-data) + GAP-4 (actor-awareness)

### S-012: Filter locations to current scene + character-occupied only
- **Source:** `RolePlayContinuationService.cs:810-820`
- **Problem:** All 5 scenario locations (~8,000 chars, ~16% of prompt) injected unconditionally. Only 1 is the current scene. Engine already has runtime location state — use it for filtering.
- **Fix:** Show full description for current scene location, one-line summary for locations with tracked characters, omit rest. Saves ~6,200 chars/prompt.
- **Maps to:** Slot 7 (current-location)

### S-014b: Widen history window — 12 interactions = ~4 turns
- **Source:** `RolePlayContinuationService.cs:846-847`
- **Problem:** `TakeLast(12)` with 3 interactions per turn = only 4 turns of context. Narrative passages consume 1/3 of the window. No positional weighting.
- **Fix:** Consider window based on turns (last 8 turns = 24 interactions), or weight recent interactions higher.
- **Maps to:** Slot 9 (interaction-history) + GAP-8 (Tier 3)
- **⚠️ REVISED after Narrative analysis (S-026):** Simple widening to 8 turns would consume the ENTIRE 35K budget on history alone (Narrative responses are ~5K chars each). Must use tiered compression instead — see S-026.

### S-017: Theme config misaligned with scene reality
- **Source:** RPTheme config (data issue, not code)
- **Problem:** Exhibitionism theme's Climax guidance describes exposure/flashing scenarios while actual scene is full sexual contact. Theme constraints scold behavior already established across 30 turns.
- **Fix:** Update theme's Climax phase guidance for post-exposure transition, or select different theme when narrative crosses threshold.
- **Maps to:** Slot 12 (theme-contract)

### S-020: Resistance band ignores narrative state
- **Source:** `ScenarioGuidanceContextFactory` (engine issue)
- **Problem:** "Resistance band 'Unbreakable Vow': transgression is unthinkable" injected while Becky is mid-encounter with Dean. Computed from stats (Loyalty=56) but ignores that narrative crossed threshold 20 turns ago.
- **Fix:** Suppress or weaken resistance band when narrative state shows threshold already crossed.
- **Maps to:** Slot 14 (scenario-guidance)

### S-023: "Message: Continue naturally" is dead tokens
- **Source:** `RolePlayContinuationService.cs:1340`
- **Problem:** "Continue the current encounter naturally from where it left off" says nothing the Turn Context + injectors haven't already said. Occupies high-authority near-end position with zero-information placeholder.
- **Fix:** When promptText is the generic default, omit the line. Only inject when user provides actual direction.
- **Maps to:** Slot 16 (user-direction)

---

## P2 — Medium (5 items)

### S-004: Tune inline Turn Context to be pacing-aware
- **Source:** `RolePlayContinuationService.cs:509-541`
- **Problem:** "Establish the scene beat" implies one beat, but fast pacing injectors demand "full arc."
- **Fix:** Make position-1 guidance phase/pacing aware.
- **Maps to:** Slot 3 (turn-context)

### S-016: Remove raw Adaptive Character Stats
- **Source:** `RolePlayContinuationService.cs:912-920`
- **Problem:** Raw numeric stats (Desire=100, Restraint=55) are engine data the model cannot compute with. Behavioral frames already translate these into actionable prose.
- **Fix:** Remove the numeric stats block.
- **Maps to:** Removed (no slot — stats are engine internals)

### S-018: Remove BehavioralFrameInjector generic stub
- **Source:** `BehavioralFrameInjector.cs:16-18`
- **Problem:** "Stay in character according to your behavioral frame" — 100% generic filler with zero actionable information. Burns ~40 tokens.
- **Fix:** Remove or make context-specific.
- **Maps to:** Slot 13 (behavioral-frames)

### S-019: Merge EscalationInjector + SceneTimeDirectionInjector
- **Source:** `EscalationInjector.cs` + `SceneTimeDirectionInjector.cs`
- **Problem:** Both injectors emit pacing directives with ~80% overlapping content.
- **Fix:** Merge into single "Scene Pacing" injector at priority 65.
- **Maps to:** Slot 15 (intensity-pacing)

### S-021: Word target conflicts with fast pacing
- **Source:** `RolePlayContinuationService.cs:1400-1430`
- **Problem:** "Output 100-300 words" while pacing demands "cover the full arc." 300 words can't fit a full arc.
- **Fix:** Pacing-aware word target. Fast pacing → "Output 300-500 words."
- **Maps to:** Slot 17 (final-instruction)

---

## P3 — Low (3 items)

### S-005: Fix "1 character(s)" plural awkwardness
- **Source:** `RolePlayContinuationService.cs:523`
- **Fix:** Singular/plural conditional.
- **Maps to:** Slot 3 (turn-context)

### S-010: Replace Intensity Profile GUID with label
- **Source:** `RolePlayContinuationService.cs:680`
- **Problem:** `a441720bf98d49d5b599aa460114a8f6` is a raw GUID — 32 meaningless hex chars.
- **Fix:** Resolve profile name from GUID.
- **Maps to:** Slot 15 (intensity-pacing)

### S-015: Trim Scene Continuity Anchor — drop self-perceptions
- **Source:** `RolePlayContinuationService.cs:872-910`
- **Problem:** 3 self-perception lines (Becky→Becky, etc.) are pure redundancy. Truth state table already says where everyone is.
- **Fix:** Drop self-perceptions, keep cross-perceptions.
- **Maps to:** Slot 11 (continuity-anchor)

---

## No Change — Keep As-Is (3 items)

### S-007: Scene Location Lock
- Essential for spatial grounding. Well-placed, well-worded, earns its HARD CONSTRAINT label.
- **Maps to:** Slot 4 (location-lock)

### S-014: Interaction History + Session Memory
- Essential. Interaction history IS the narrative. Session Memory bridges beyond the rolling window.
- **Maps to:** Slots 9 + 10

### S-011: Scenario metadata (future consideration)
- Keep for now. Consider progressive summarization after turn ~10.
- **Maps to:** Slot 6 (scenario-context) + GAP-8 (Tier 1 compression)

---

## Future / Spec Needed (2 items)

### S-009b: Filter intimate attributes by semantic role
- Tag intimate attributes with categories (comparison-relevant, self-awareness, encounter-mechanics) and filter by scene context.
- **Maps to:** Slot 5 (character-data)

### S-022: 3-Tier Memory Architecture
- **Finding:** Infrastructure exists for all 3 tiers. Long-term = Scenario Data. Medium-term = EncounterSummaries. Short-term = Interaction History.
- **Gaps:** Only 1 encounter detected (depends on male orgasm in narrative). LLM summaries capture plot, not "sexual memory." Interaction window too narrow.
- **Recommendation:** Enhance encounter summary enrichment prompt to capture sexual comparison/learning. Widen short-term window or make encounter memory richer.
- **Maps to:** Slot 10 (session-memory) + GAP-8 (full spec)

---

## Narrative-Specific Findings (from `c8667fef` analysis)

### S-024: Narrative directive appears twice at end of prompt
- **Source:** `RolePlayContinuationService.cs:246` + `:265` (in Narrative prompt)
- **Problem:** Two nearly identical directives at the end: "Write an omniscient narrative description of the full scene..." (line 246) and "Write a detailed omniscient narrative of the physical scene..." (line 265). Both say almost the same thing. The first is shorter, the second has the 6-point checklist.
- **Fix:** Resolved by Slot 17 (final-instruction) — the Narrative variant produces exactly ONE instruction with the physical detail checklist. No duplicate.
- **Maps to:** Slot 17 (final-instruction) — Narrative variant

### S-025: POV Persona injected for Narrative makes no sense
- **Source:** `RolePlayContinuationService.cs:549-575` (same as S-006)
- **Problem:** "POV Persona: Ken" in a Narrative prompt is even more wrong than in character prompts. Narrative is omniscient — it has no POV persona.
- **Fix:** Resolved by Slot 5 (character-data) — the Narrative variant produces NO persona and NO intimate self-awareness text. S-006 is extended: for `PromptIntent.Narrative`, inject nothing.
- **Maps to:** Slot 5 (character-data) — Narrative variant + GAP-4 (actor-awareness)

### S-026: Tiered history compression using Narrative responses
- **Source:** `RolePlayContinuationService.cs:846-847` (history window)
- **Problem:** Narrative responses are ~5,000 chars each — 3.5x larger than character responses (~1,500 chars). Simple widening to 8 turns (S-014b) would consume the ENTIRE 35K budget on history alone. The current flat `TakeLast(N)` treats all interactions equally, but Narrative responses are both larger AND more valuable as memory (they're already synthesized omniscient accounts).
- **Fix:** Tiered history compression:
  ```
  LAST 2-3 TURNS: All interactions (character + narrative)
    → Full detail — model needs per-character voices for immediate continuity
    → ~9,500 chars/turn × 3 turns = ~28,500 chars
  
  TURNS 4-6: Narrative only
    → Synthesized view — model gets "what happened" without per-character detail
    → ~5,000 chars/turn × 3 turns = ~15,000 chars
  
  TURNS 7+: Session Memory (encounter summaries)
    → Compressed sexual memory
    → ~300 chars/encounter × 5 = ~1,500 chars
  
  Total: ~45,000 chars → trimmed to fit 20K history budget
  ```
- **Maps to:** Slot 9 (interaction-history) + GAP-8 (Tier 3) + GAP-3 (budget)

### S-027: Narrative responses as encounter memory source
- **Source:** `EncounterSummaryJobHandler.cs` (enrichment)
- **Problem:** Currently, encounter summary enrichment starts from raw interactions. But the Narrative response is already an LLM-generated omniscient synthesis — it describes the physical scene, all character positions, sensations, and atmosphere. Starting from the Narrative response would be more efficient and higher quality.
- **Fix:** Feed the Narrative response into the encounter summary enrichment prompt as the base, then add character responses for emotional depth:
  ```
  Input to enrichment LLM:
  1. Narrative response (omniscient physical account)
  2. Character responses (emotional/POV detail)
  
  Output: Sexual memory (what she felt, learned, compared, risked)
  ```
- **Maps to:** GAP-8 (Tier 2 enrichment)

---

## Cross-Cutting Themes

### Duplication (highest impact)
The same content appears 2-3x: Turn Context (2x), Behavioral Frames (2x), Theme Hard Constraints (3x), Intensity Contract (2x). Removing duplicates saves ~5,000+ chars without losing any information.

### Static vs Phase-Aware
Writing Style Rule of Thumb is the only major component that doesn't adapt to phase. Intensity and Pacing both correctly sync to Climax — the static Style fights them both.

### Engine Data Leaking Into Prompt
Raw GUIDs, numeric stats, confidence values, and resistance bands with no narrative awareness all appear as engine internals the model can't productively use.

### HARD CONSTRAINT Dilution
~25 instances of "HARD CONSTRAINT" in a single prompt. Only 2-3 genuinely deserve the label.

---

# PART 3: IMPLEMENTATION ORDER

*When to do what. Phases are sequential — each builds on the previous.*

---

## Phase 1: Define Target (before any code changes)
- [ ] Approve target prompt structure (GAP-1) — both Character and Narrative variants
- [ ] Approve World State architecture (GAP-5) — time, day, season, weather, temporal pressure
- [ ] Approve token budget (GAP-3)
- [ ] Approve actor-awareness principle (GAP-4) — all 5 profiles including Narrative
- [ ] Approve phase-specific Writing Style content (GAP-6)
- [ ] Approve 3-tier memory architecture (GAP-8)

## Phase 2: Remove Waste (P0 items)
- [ ] S-003: Delete duplicate Turn Context injector
- [ ] S-006: Gate POV Persona to player-actor only
- [ ] S-008: Merge Appearance + BEHAVIORAL CONSTRAINT
- [ ] S-013: Make Writing Style Rule of Thumb phase-aware

## Phase 3: Fix Content (P1 items)
- [ ] S-001: Remove "You are continuing..." header
- [ ] S-002: Remove "Behavior mode: TakeTurns"
- [ ] S-009: Reformat non-present chars as comparison reference
- [ ] S-012: Filter locations to current scene + occupied only
- [ ] S-014b: Widen history window (REVISED — use tiered compression per S-026)
- [ ] S-017: Fix theme config misalignment (data issue)
- [ ] S-020: Fix resistance band ignoring narrative state
- [ ] S-023: Omit generic "continue naturally" message
- [ ] S-024: Remove duplicate Narrative directive
- [ ] S-025: Suppress POV Persona for Narrative intent

## Phase 4: Structural Refactor (GAP-7)
- [ ] Create `IPromptSlot` interface and `RolePlayPromptBuilder`
- [ ] Implement each slot by extracting from `BuildPromptAsync`
- [ ] Replace `BuildPromptAsync` call with `RolePlayPromptBuilder.BuildAsync`
- [ ] Delete the 900-line method and coordinator inline duplicates
- [ ] Update tests to verify slot output

## Phase 5: Immersion Features (GAP-5, GAP-5b, GAP-8)
- [ ] World State slot — prompt-side slot ready for B-062 engine (GAP-5)
- [ ] 3-tier memory architecture (enrichment prompt, detection fixes, window widening)
- [ ] S-026: Tiered history compression (recent turns full, older turns Narrative-only)
- [ ] S-027: Feed Narrative responses into encounter summary enrichment
- [ ] NPC agency directives (GAP-5b)
- [ ] Consequence tracking via encounter memory (GAP-5b)
- [ ] Time pressure (world time context) (GAP-5b)
- [ ] **Follow-up**: B-062 — Weather & Environmental System (engine layer that populates Slot 4a)

## Phase 6: Polish (P2 + P3 items)
- [ ] S-004: Pacing-aware Turn Context tuning
- [ ] S-016: Remove raw Adaptive Character Stats
- [ ] S-018: Remove BehavioralFrameInjector stub
- [ ] S-019: Merge Escalation + SceneTime injectors
- [ ] S-021: Pacing-aware word target
- [ ] S-005: Fix "1 character(s)" plural
- [ ] S-010: Replace Intensity Profile GUID with label
- [ ] S-015: Trim Scene Continuity Anchor self-perceptions
