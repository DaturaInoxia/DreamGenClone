# Feature Specification: Fix Climax Time-Skip System

**Feature Branch**: `001-fix-climax-timeskip`  
**Created**: 2026-06-21  
**Status**: Draft  
**Input**: User description: "Fix three bugs in the multi-encounter Climax time-skip system: (1) One-shot injection instead of persistent re-injection of time-skip Instruction, (2) Remove stale encounter number from directive text, (3) Skip engine time-skip injection when user already has an active Instruction interaction"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Time-Skip Fires Once Per Encounter Boundary (Priority: P1)

When the roleplay engine detects an encounter boundary and injects a time-skip directive to close the current encounter and advance time, the model receives that directive exactly once. On subsequent turns within the same boundary window, the directive is not repeated, preventing the model from looping on the same closing action across multiple turns.

**Why this priority**: The persistent re-injection bug is the root cause of the most visible defect — the model stuck in a loop repeatedly attempting to "close the current encounter" turn after turn. This directly degrades the user's roleplay experience by stalling narrative progression.

**Independent Test**: Trigger an encounter boundary in a multi-encounter session. Verify the time-skip directive appears in exactly one prompt (the turn immediately following boundary detection). Verify it does not appear in any subsequent prompt until the next distinct boundary event.

**Acceptance Scenarios**:

1. **Given** a roleplay session is in progress and the engine detects an encounter boundary (TimeSkipPending fires), **When** the next continuation prompt is built, **Then** the time-skip directive text appears in that prompt's "Message:" block, and no Instruction interaction is created.
2. **Given** the time-skip directive was injected in the previous turn, **When** the next continuation prompt is built for the same session, **Then** the time-skip directive text does NOT appear in the prompt.
3. **Given** the time-skip directive was injected, and the session continues for 3 more turns without another boundary event, **When** each of those 3 prompts is built, **Then** none of them contain the time-skip directive text.

---

### User Story 2 - Directive Text Is Always Current (Priority: P2)

When the engine injects a time-skip directive, the text instructs the model to close the current encounter and advance to ordinary life — without referencing a specific encounter number. This prevents the model from receiving stale encounter numbers (e.g., "encounter #10" when the session is now on #12) and avoids inadvertently prompting the model to begin the next encounter immediately.

**Why this priority**: The stale encounter number causes confusion (wrong number) and the "before encounter #N begins" phrasing invites the model to jump into the next encounter right away, undermining the natural pause between encounters. This is a content quality issue that affects every time-skip.

**Independent Test**: Trigger encounter boundaries at different encounter numbers. Verify the injected directive text never contains an encounter number. Verify the directive text instead uses encounter-number-agnostic phrasing.

**Acceptance Scenarios**:

1. **Given** a session at encounter #5 where a boundary is detected, **When** the time-skip directive is injected, **Then** the directive text does NOT contain "encounter #5", "encounter #6", or any numeric encounter reference.
2. **Given** a session at encounter #12 where a boundary is detected, **When** the time-skip directive is injected, **Then** the directive text does NOT contain any encounter number.
3. **Given** the time-skip directive is injected, **When** the model processes it, **Then** the directive focuses only on closing the current encounter and establishing ordinary life, without language that prompts starting the next encounter.

---

### User Story 3 - User Steer Instructions Take Priority (Priority: P3)

When a user has manually typed a steer instruction (e.g., directing specific character actions), the engine recognizes this and skips its own time-skip injection for that turn. The user's instruction takes precedence, preventing competing directives that split character behavior.

**Why this priority**: Competing directives (user steer vs. engine time-skip) cause characters to behave inconsistently — some following the user's explicit direction, others following the engine's time-skip. This is a quality-of-life fix that prevents a specific conflict scenario. It is lower priority than P1/P2 because it only triggers when the user actively types a steer instruction near a boundary event.

**Independent Test**: In a session near an encounter boundary, type a user steer instruction. Verify no engine time-skip directive is injected for that turn. Verify the user's steer instruction is processed normally without interference.

**Acceptance Scenarios**:

1. **Given** a session where TimeSkipPending is true and the most recent interactions include a user-typed (non-engine) Instruction, **When** the continuation prompt is built, **Then** the engine does NOT inject its time-skip directive for that turn.
2. **Given** a session where TimeSkipPending is true and there are NO recent user-typed Instructions, **When** the continuation prompt is built, **Then** the engine injects the time-skip directive normally.
3. **Given** the engine skipped injection due to a user Instruction on the previous turn, and TimeSkipPending is still true on the next turn with no new user Instruction, **When** the next continuation prompt is built, **Then** the engine injects the time-skip directive (the skip is per-turn, not persistent).

---

### Edge Cases

- What happens when TimeSkipPending fires but the session has only one interaction (insufficient history for "last few interactions" check)? The engine should still inject the time-skip directive since there's no competing user Instruction.
- What happens when a user Instruction appears after the engine injected its time-skip directive earlier in the same turn? The engine injection already happened; the user's subsequent Instruction is independent and is processed normally on the next turn.
- What happens when the encounter boundary fires mid-generation (streaming) rather than between turns? The boundary detection triggers on turn completion, so injection always happens before the next prompt build — no mid-stream conflict.
- What happens if the overflow loop where injection occurs encounters an error? The system should fail gracefully — log the error and continue building the prompt without the time-skip directive rather than blocking the continuation entirely.
- What happens with very long sessions where the interaction history count is large? The "last few interactions" check for user Instructions should use a fixed small window (e.g., last 3 interactions) to remain performant.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The time-skip directive MUST be injected directly into the first actor's prompt text using `PromptIntent.Instruction` (not as a persistent Instruction interaction). The directive appears in the "Instruction:" block of the prompt, giving it maximum authority while remaining one-shot.
- **FR-002**: The time-skip directive MUST appear in exactly one prompt per boundary event — it MUST NOT persist or be re-injected across subsequent turns.
- **FR-003**: The time-skip directive text MUST NOT reference any encounter number (current or upcoming).
- **FR-004**: The time-skip directive text MUST instruct the model to close the current encounter naturally, advance time, and establish ordinary life — without language that prompts beginning the next encounter.
- **FR-005**: Before injecting the time-skip directive, the engine MUST check the most recent interactions (fixed small window of 3) for the presence of a user-typed (non-engine) Instruction interaction. A user-typed Instruction is identified by `ActorName="Instruction"` AND `GeneratedByCommand` being null/empty. Engine-generated Instructions have `GeneratedByCommand="MultiEncounterTimeSkip"`.
- **FR-006**: If a user-typed Instruction is found in the recent interaction window, the engine MUST skip time-skip directive injection for that turn. `TimeSkipPending` MUST remain true so the engine retries on the next turn.
- **FR-007**: The skip decision (FR-006) MUST be per-turn only — `TimeSkipPending` MUST persist across turns until injection succeeds. The engine retries on each subsequent turn until no competing user Instruction is present.
- **FR-008**: When the engine skips injection due to a competing user Instruction, it MUST log an informational event indicating the skip occurred.
- **FR-009**: The overflow loop in the engine service MUST be the single decision point for time-skip directive injection — there MUST NOT be duplicate injection logic elsewhere.
- **FR-010**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-011**: Major execution paths across the time-skip injection flow MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-012**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **Time-Skip Directive**: A short text instruction appended to the prompt's "Message:" block that directs the model to close the current encounter and advance time. It is ephemeral (one-shot), encounter-number-agnostic, and conditionally injected based on user Instruction presence.
- **User-Typed Instruction**: An interaction created by the user (not the engine) of type Instruction, representing an explicit steer or direction from the user. Distinguished from engine-generated Instructions by `GeneratedByCommand` being null/empty (engine Instructions set it to `"MultiEncounterTimeSkip"`).
- **GeneratedByCommand**: A new field on `RolePlayInteraction` that identifies the engine command that created the interaction. Null for user-authored interactions. Set to `"MultiEncounterTimeSkip"` for engine-injected time-skip directives.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After an encounter boundary fires, the time-skip directive appears in exactly one prompt — verified by inspecting debug events for duplicate "Active Instruction" occurrences across consecutive turns.
- **SC-002**: The time-skip directive text contains zero encounter number references — verified by static inspection of the directive string across all injection paths.
- **SC-003**: When a user Instruction is present in the last 3 interactions, the engine skips time-skip injection 100% of the time — verified by testing with pre-seeded sessions containing user Instructions near boundary events.
- **SC-004**: No regression in normal encounter boundary detection — boundaries still fire at the same rate and under the same conditions as before the fix.
- **SC-005**: Users no longer observe the model looping on "close the current encounter" across multiple turns after a single boundary event.

## Assumptions

- The "last few interactions" window for checking user Instructions is defined as the last 3 interactions. This is a reasonable default that balances detection accuracy with performance.
- A "user-typed Instruction" is distinguished from an engine-generated Instruction by the new `GeneratedByCommand` field: engine Instructions set it to `"MultiEncounterTimeSkip"`, user Instructions leave it null.
- The time-skip directive text replacement (removing encounter number) applies to all code paths where the directive is constructed — there is only one construction site to update.
- The overflow loop in `RolePlayEngineService.cs` is the correct and only injection point; the persistent re-injection logic in `BuildPromptAsync` in `RolePlayContinuationService.cs` will be removed or bypassed.
- Existing roleplay sessions with persisted Instruction interactions from previous time-skips will not be affected — only new boundary events use the new injection mechanism.

## Clarifications

### Session 2026-06-22

- Q: How to distinguish user-typed Instructions from engine-generated Instructions? → A: Option B — Add a new `GeneratedByCommand` field set to `"MultiEncounterTimeSkip"` on engine Instructions; user Instructions leave it null.
- Q: Should the first actor use `PromptIntent.Message` or `PromptIntent.Instruction` for the time-skip directive? → A: Option A — Use `PromptIntent.Instruction` for the first actor when time-skip fires. Matches the proven working pattern, gives the directive maximum authority, and still avoids persistence because no Instruction interaction is created in `session.Interactions`.
- Q: Should `TimeSkipPending` persist across turns when skipped due to a user Instruction? → A: Option A — Keep `TimeSkipPending` true across turns until injection succeeds. The engine retries on each turn until it can inject. This ensures the time-skip eventually happens even if the user steers for several turns.
