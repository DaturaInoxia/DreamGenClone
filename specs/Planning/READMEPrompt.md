# Climax Pacing Analysis — Session 9b347e61

## Problem

In session `9b347e61-92f4-47e8-9099-aa5e2a20425c`, the `pace:fast` tag and Hard Constraints do not appear to work properly during the climax phase. The session output shows ~40+ consecutive `PromptBuilt` events in the climax phase, all with identical prompt content — the same "Location Continuity" hard constraint, the same scenario narrative ("fast pacing"), and no visible variation in pacing directives across turns. The story seems stuck in a loop where every turn produces the same prompt scaffold.

## Root Cause Analysis

### 1. Pacing is baked into the scenario narrative, not turn-level directives

The prompt contains:
```
Narrative: Erotic, conversational, playful, and focused on physical pleasure.
First-person limited perspective with fast pacing and low to moderate language complexity.
```

This "fast pacing" is a **static scenario-level attribute** — it's written once in the scenario definition and never changes. There is no mechanism to override pacing per-turn or per-beat. The `pace:fast` tag (if it exists as a separate directive) is not visible in the prompt output at all — it's either not being injected, or it's being overridden/ignored by the scenario narrative text.

### 2. Hard Constraints are static and generic

Every single `PromptBuilt` event shows the same hard constraint:
```
HARD CONSTRAINT — Location Continuity: The physical setting established in the previous
response must be maintained in this response...
```

This constraint is **not climax-specific**. It's a generic location-continuity constraint that fires on every turn regardless of phase. There's no evidence of climax-specific hard constraints (e.g., "escalate intensity," "resolve within N turns," "no time skips during climax").

### 3. No turn-level pacing vocabulary exists

The system has no concept of **slow / medium / fast** as per-turn pacing directives. The only "fast" reference is in the scenario narrative description, which is immutable. There's no way for the theme engine, story analyzer, or continuation service to say "this turn should be slow-paced" vs "this turn should be fast-paced."

### 4. No beat-duration or time-shift directives

There's no mechanism to instruct:
- "This beat lasts exactly one turn, then time-skip"
- "This beat has no time shift — stay in the moment"
- "This beat allows a small time shift (minutes/hours)"
- "This beat allows a large time shift (days/evening-to-morning)"

The `Time Span Reminder` in the prompt says "Scenes may skip forward in time" — but this is a **blanket permission**, not a directive. The model has no guidance on *when* to skip vs when to stay.

### 5. Climax phase lacks escalation/resolve directives

The climax phase should have specific guidance:
- **Early climax**: escalate, build tension, stay in the moment
- **Mid climax**: maintain intensity, no time skips
- **Late climax**: resolve, wind down, allow time shift to reset

None of this exists in the current prompt. The climax is treated the same as every other phase.

## Recommendations

### A. Pacing Vocabulary (Theme Guidance, Not Code Injection)

Add a **pacing directive** to the prompt that varies per turn based on phase and story state. This should be theme-level guidance injected into the narrative instructions section:

| Directive | Meaning |
|-----------|---------|
| `pace:slow` | Linger in the moment. Focus on sensory detail, internal monologue, emotional nuance. Cover only seconds-to-minutes of story time per turn. |
| `pace:medium` | Normal narrative speed. Balance action and description. Cover minutes-to-hours of story time per turn. |
| `pace:fast` | Move quickly through events. Summarize transitions, focus on key moments. Cover hours-to-days of story time per turn. |

**Implementation approach**: Inject a `Pacing: <directive>` line into the prompt's narrative section, computed from phase + story state. This is a text injection, not a code-level flow control.

### B. Beat Duration Directives

Add a **beat scope** directive that tells the model how long the current beat should last:

| Directive | Meaning |
|-----------|---------|
| `beat:single` | This beat lasts exactly one turn. Resolve the current moment, then allow a time shift next turn. |
| `beat:short` | This beat lasts 2-3 turns. Build the moment across a few exchanges before shifting. |
| `beat:extended` | This beat lasts 4+ turns. Stay in the current moment/scene for several exchanges. |

### C. Time Shift Directives

Add a **time shift** directive that controls when/how the story jumps forward:

| Directive | Meaning |
|-----------|---------|
| `time:none` | No time shift. Continue from the exact moment the last response ended. |
| `time:small` | Small time shift allowed (minutes to a few hours). E.g., skip to later that same day. |
| `time:medium` | Medium time shift allowed (hours to half a day). E.g., morning to evening. |
| `time:large` | Large time shift allowed (days or more). E.g., skip to next day or later in the week. |

### D. Phase-Specific Pacing Defaults

| Phase | Default Pace | Default Beat | Default Time Shift |
|-------|-------------|-------------|-------------------|
| BuildUp | medium | short | small |
| Committed | slow | extended | none |
| Approaching | slow | extended | none |
| Climax (early) | slow | extended | none |
| Climax (mid) | medium | short | none |
| Climax (late) | fast | single | small |
| Reset | medium | short | medium |

### E. Climax-Specific Hard Constraints

Replace the generic "Location Continuity" hard constraint with phase-appropriate constraints:

**Climax (early/mid):**
```
HARD CONSTRAINT — Climax Immersion: Stay in the current moment. Do not skip forward
in time. Maintain and escalate the physical/emotional intensity. Every response must
advance the current scene, not start a new one.
```

**Climax (late):**
```
HARD CONSTRAINT — Climax Resolution: Bring the current climax scene to a satisfying
conclusion this turn. A time shift to a reset/aftermath scene is permitted next turn.
```

### F. How These Should Be Injected

These directives should appear in the prompt as a **Director's Notes** or **Scene Direction** block, placed after the scenario narrative but before the intensity profile:

```
Scene Direction:
- Pacing: slow
- Beat Scope: extended
- Time Shift: none
- Director Note: Stay in the moment. Linger on sensory and emotional detail.
  Do not skip forward in time. Escalate intensity gradually.
```

This is **theme guidance** — it tells the model what *kind* of response to write, not how the code should behave. The model interprets these as narrative instructions, not as code-level flow control.

## Why `pace:fast` Currently Fails

1. **It's not in the prompt.** The `pace:fast` tag (if set anywhere) does not appear in the `PromptBuilt` output. It's either not being injected into the prompt, or it's being set in a data structure but never rendered to text.

2. **The scenario narrative overrides it.** The scenario says "fast pacing" statically. Even if a `pace:slow` directive were injected, the model would see conflicting instructions: the scenario says "fast" but the directive says "slow."

3. **No phase awareness.** The pacing doesn't change based on phase. Climax turns get the same pacing text as buildup turns.

## Why Hard Constraints Currently Fail

1. **Only one constraint type fires.** "Location Continuity" is the only hard constraint visible across all 40+ turns. It's not climax-specific.

2. **No escalation constraint.** There's no constraint telling the model to escalate or maintain intensity during climax.

3. **No time-shift constraint.** There's no constraint preventing time skips during climax immersion.

4. **No resolution constraint.** There's no constraint telling the model when to wrap up the climax.

## Suggested Implementation Steps

1. **Add pacing/beat/timeshift fields** to the story state or theme observer state (domain level).
2. **Compute defaults** from phase in `RolePlayContinuationService` or a pacing service.
3. **Inject as text** into the prompt via `RolePlayAssistantPrompts` — a "Scene Direction" block.
4. **Add climax-specific hard constraints** that replace or supplement the generic location constraint.
5. **Allow user override** — let the user set pacing via a command (e.g., `/pace slow`, `/timeshift none`).
6. **Make scenario "fast pacing" text non-binding** — treat it as a default, not an override.

## Key Files

- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` — continuation flow, phase detection
- `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` — prompt building, hard constraints
- `DreamGenClone.Domain/RolePlay/` — domain models for phase, story state
- `DreamGenClone.Infrastructure/RolePlay/` — persistence, theme observer