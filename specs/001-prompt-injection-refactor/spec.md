# Feature Specification: Full Prompt Injection Refactor

**Feature Branch**: `001-prompt-injection-refactor`  
**Created**: 2026-06-28  
**Status**: Draft  
**Input**: User description: "B-052 Full Prompt Injection Refactor: Centralize 37+ independently-added prompt injects into a coordinated service with a clear engine vs theme split. Refactor BuildPromptAsync from a 1000-line procedural pipeline into a priority-sorted injector loop orchestrated by SceneDirectionCoordinator. Resolve all known contradictions (Time Span Reminder vs Location Continuity, Scene Deepening vs Pacing, BuildUp guard vs time advancement) by replacing hardcoded phase detection with marker-driven decisions."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Consistent Turn Structure Enforcement (Priority: P1)

As a story writer using the roleplay system, I need each turn in a multi-character scene to follow a clear structural contract: the first responding character establishes the time and location anchor, and subsequent characters write from that same anchor. This prevents timeline splits where two characters write from different moments or locations in the same turn, breaking narrative coherence.

**Why this priority**: Timeline splits are the most visible and immersion-breaking bug. When a turn's characters write from different times or locations, the story becomes incoherent and unusable. This is the primary user-facing symptom that the refactor must fix.

**Independent Test**: Can be fully tested by simulating a multi-character turn where position 1 sets a specific time/location and position 2 generates a response — position 2's prompt must contain unambiguous location continuity directives with no contradictory time-shift permission.

**Acceptance Scenarios**:

1. **Given** a session with two characters in a turn, **When** position 1's prompt is built, **Then** it includes a Time Span Reminder allowing time/location establishment and does NOT include Location Continuity constraints.
2. **Given** a session where position 1 has set the scene at "mid-afternoon beach", **When** position 2's prompt is built, **Then** it includes an enhanced Location Continuity directive stating the current setting and forbidding silent relocation, and does NOT include a standalone Time Span Reminder without override markers.
3. **Given** a session with `[Pacing:fast]` or `[TimeShift:within-timeframe]` marker in the current phase guidance, **When** position 2's prompt is built, **Then** it includes a modified time-shift permission text alongside the location continuity directive.

---

### User Story 2 - Theme-Controlled Narrative Pacing (Priority: P2)

As a theme designer, I need to control how fast scenes advance and whether subsequent characters deepen existing beats or advance to new ones — all through markers in the theme's phase guidance prose, without any hardcoded application logic overriding my choices. When I write `[Pacing:slow]` in BuildUp guidance, the system should instruct the LLM to linger; when I write `[Pacing:fast]` in Climax guidance, it should instruct rapid advancement.

**Why this priority**: Theme-controlled pacing is the primary mechanism for differentiating themes. Without it, all themes behave identically regardless of their phase guidance, reducing the system's creative range and making new theme creation pointless.

**Independent Test**: Can be fully tested by configuring a theme with specific pacing markers and verifying the generated prompt contains the corresponding escalation guidance text, then switching markers and verifying the text changes.

**Acceptance Scenarios**:

1. **Given** a theme with `[Pacing:slow]` in current phase guidance, **When** a prompt is built, **Then** the escalation guidance instructs "Advance within the same beat — deepen, do not leap."
2. **Given** a theme with `[Pacing:fast]` in current phase guidance, **When** a prompt is built, **Then** the escalation guidance instructs "Compress multiple beats into this response. Advance to a new beat or position."
3. **Given** a theme with `[Deepening:subsequent-actors]` marker, **When** position 2's prompt is built, **Then** it receives deepening-from-POV guidance regardless of the current pacing setting.
4. **Given** a theme with no pacing markers in current phase guidance, **When** a prompt is built, **Then** the system falls back to reasonable phase-appropriate defaults without error.

---

### User Story 3 - Single Coordinator Pipeline (Priority: P3)

As a developer maintaining the prompt assembly code, I need a single coordinator service that orchestrates all prompt injections through a priority-sorted loop of injectors implementing a common interface. This replaces the current ~1000-line procedural method where 37+ injects are interleaved with ad-hoc conditions, making it impossible to reason about prompt structure or resolve conflicts between directives.

**Why this priority**: Developer maintainability is critical for long-term project health. The current `BuildPromptAsync` is a known bottleneck — every new feature requires touching an already-unwieldy method, and every bug fix risks introducing new contradictions. A coordinator loop with injector interfaces makes prompt structure explicit and auditable.

**Independent Test**: Can be fully tested by instantiating the coordinator with registered injectors, providing a prompt injection context, and asserting the assembled prompt contains all expected sections in the correct order with no contradictory directives.

**Acceptance Scenarios**:

1. **Given** a complete set of registered injectors, **When** the coordinator builds a prompt for any session state, **Then** the output contains the same behavioral directives as the pre-refactor pipeline (structural parity), and no injector contains `if (phase == "Climax")` or similar hardcoded phase-branching logic.
2. **Given** an injector that should not fire under certain conditions (e.g., EscalationInjector when a profile DirectorNote is present), **When** the coordinator evaluates it, **Then** `ShouldFire` returns false and the injector emits no text.
3. **Given** the coordinator loop, **When** a new prompt injection need arises, **Then** a developer can add it by implementing the injector interface and registering it with a priority, without modifying any existing injector code.

---

### Edge Cases

- What happens when a theme has no phase guidance prose for the current phase? The system must fall back to phase-appropriate defaults from the SceneDirectionResolver's tier-3 safety net, without error.
- What happens when markers conflict (e.g., `[Pacing:fast]` and `[Deepening:subsequent-actors]` both present)? The Deepening marker takes precedence for position 2+, per the orthogonal override rule.
- What happens when a profile-configured DirectorNote is present? EscalationInjector and SceneTimeDirectionInjector must be suppressed; only DirectorNoteInjector fires for beat/time direction.
- What happens when no markers exist in any phase guidance? The SceneDirectionResolver falls back to phase defaults (tier 3), and prompt assembly continues without error.
- What happens when position 1 has no assigned character? The turn structure contract still fires — the first responding actor always sets the anchor regardless of identity.
- What happens during single-character turns (no position 2)? Position 1 still receives the Time Span Reminder and may establish/shift time and location. The Turn Structure Contract is about position roles, not turn size — a solo position 1 is still the anchor-setter. The narrative close that follows handles turn closure. No turn-size branching in injector logic.
- What happens during parallel NPC generation? Each NPC's prompt must independently receive the correct turn structure directives based on their position.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST have a single `SceneDirectionCoordinator` service that orchestrates all behavioral prompt injections through a priority-sorted loop over registered injectors.
- **FR-002**: Every behavioral prompt inject MUST implement a common interface (`IPromptInjector`) with `Id`, `Priority`, `ShouldFire(context)`, and `BuildText(context)` methods.
- **FR-003**: The coordinator MUST build a `PromptInjectionContext` once per prompt, containing all resolved values that injectors need — injectors MUST read from context rather than detecting phase or resolving markers themselves.
- **FR-004**: Turn structure rules MUST be engine-owned and baked into every prompt: position 1 receives Time Span Reminder (may establish/shift time and location), position 2+ receives Location Continuity (must maintain the anchor set by position 1).
- **FR-005**: Narrative behavior controls (pacing speed, beat advancement, time shifting, deepening policy) MUST be theme-controlled via markers in phase guidance prose and resolved by `SceneDirectionResolver` onto a `SceneDirection` record.
- **FR-006**: Behavioral directives MUST NOT be selected by hardcoded phase-detection logic in application code. Phase may only be used for data selection (e.g., selecting which phase's guidance prose to inject). Any phase-aware data selection MUST be explicitly documented and justified.
- **FR-007**: The intensity level MUST control writing style and explicitness only — no injector may gate on intensity to decide whether to fire, suppress, or modify its behavioral text.
- **FR-008**: When a profile-configured DirectorNote is present, the EscalationInjector and SceneTimeDirectionInjector MUST be suppressed, and only DirectorNoteInjector fires for beat/time/scene direction.
- **FR-009**: All narrative framing guidance currently embedded in application code MUST be migrated to theme phase guidance prose fields in the database. After migration, no narrative framing text may remain hardcoded in application code. **Migration ordering**: All existing themes MUST be updated with equivalent prose BEFORE the old code is removed — no theme may silently lose its framing.
- **FR-010**: `SceneDirectionResolver` MUST resolve pacing, beat scope, time shift policy, and deepening policy from a 3-tier precedence: profile directive (tier 1) > theme markers in current-phase guidance (tier 2) > phase-appropriate defaults (tier 3).
- **FR-011**: Marker resolution MUST be scoped to the current phase's guidance lines only — a `[Pacing:fast]` marker in Climax guidance only activates during Climax.
- **FR-012**: All 37+ existing prompt injects MUST be catalogued in a single source-of-truth document with id, type, source location, conditions, and desired control classification.
- **FR-013**: The system MUST produce structurally equivalent prompts after refactoring — the same behavioral directives must appear, critical strings must be present, and contradictory directives must be absent.
- **FR-014**: A regression test for session efcbf70f (the campground intimacy timeline split) MUST pass, proving that position 2 prompts no longer contain contradictory time/location directives.
- **FR-015**: If any injector throws an exception during prompt assembly, the coordinator MUST let the exception propagate and abort the prompt build. The coordinator MUST NOT catch, log, and skip failing injectors — this aligns with the repo's "no silent fallback" rule and ensures configuration bugs surface explicitly rather than producing subtly broken prompts.
- **FR-016**: The coordinator MUST emit an Information-level log entry for every prompt build containing: the injector firing sequence (injector Id + Priority for each injector that fired), and a full `PromptInjectionContext` snapshot (session id, phase, intent, position in turn, actor name, resolved SceneDirection values, active theme id). This satisfies the repo's major-execution-path logging rule and enables post-mortem analysis of prompt assembly issues without requiring repro.
- **FR-017**: Coordinator logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices, consistent with the repo's logging conventions.
- **FR-018**: Log levels for coordinator logging MUST be configurable via settings (including Verbose) without code changes, consistent with the repo's configurable-logging rule.

### Key Entities

- **PromptInjectionContext**: Holds all resolved values that injectors consume — session, scene direction (pacing, time shift, deepening, beat scope), phase identifier, intent, position in turn, actor name, active theme, and theme guidance/constraint data. Built once per prompt by the coordinator.
- **SceneDirection**: A resolved record containing the final pacing, beat scope, time shift policy, deepening policy, climax sub-phase, and optional director note for the current prompt. Produced by SceneDirectionResolver. **Lifecycle**: Resolved per-prompt — each prompt built gets a fresh `SceneDirection` based on the current phase at build time. No turn-scoped or session-scoped caching. This matches the existing per-prompt `BuildPromptAsync` flow and avoids introducing shared state across positions in a turn.
- **IPromptInjector**: The interface contract for behavioral injectors — each has a unique Id, a numeric Priority for ordering, a ShouldFire predicate, and a BuildText output method.
- **Theme Phase Guidance Prose**: Free-text narrative direction stored in the theme database, organized by phase. Contains markers (e.g., `[Pacing:fast]`) for machine resolution and prose for LLM consumption. Replaces all hardcoded C# phase text from `BuildFramingGuards()`.
- **Turn Structure Contract**: The engine-enforced rule that position 1 sets the time/location anchor and position 2+ follows it. Not data — a structural invariant baked into the coordinator's injector configuration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The prompt assembly pipeline is reduced from a single long procedure (~1000 lines) to a centralized orchestration point where each behavioral directive is managed independently. The orchestration logic itself does not exceed 30 lines.
- **SC-002**: Zero behavioral directives are selected by hardcoded phase-detection logic in application code. All phase-aware behavior is driven by theme configuration data, not by conditional branches in code.
- **SC-003**: The full test suite passes with zero new test failures beyond the 15 pre-existing tolerated failures.
- **SC-004**: A multi-character turn with position 1 setting a specific time/location produces position 2 prompts that contain exactly one time/location authority directive (Location Continuity) with no contradictory Time Span Reminder — verified by the efcbf70f regression test.
- **SC-005**: A theme designer can change pacing behavior (slow/medium/fast) for any phase by editing only the theme's phase guidance prose markers — no code changes required.
- **SC-006**: All 37+ existing prompt injects are catalogued with id, type, source location, conditions, and desired control classification in a single auditable document.
- **SC-007**: All narrative framing guidance lives exclusively in theme configuration data. No narrative framing text remains in application code, and every framing string previously in code is now stored in and served from theme phase guidance prose.

## Assumptions

- The existing scene direction resolution component will be completed as part of this feature. Its 3-tier resolution design (profile directive > theme markers > phase defaults) is already specified and aligned.
- Data assembly operations (fetching scenario, characters, locations, memory, stats, etc.) remain in their current location — only the behavioral and structural directives are converted to the injector pattern. This scope boundary is explicit.
- Phase defaults (tier 3 fallback) are kept in `SceneDirectionResolver` as a safety net for themes with no markers. They are not exposed to injectors directly.
- Seed data updates (adding markers to existing themes, migrating prose from application code to theme configuration) are in scope.
- **Migration ordering**: All existing themes MUST be updated with migrated phase guidance prose BEFORE the old `BuildFramingGuards()` code is removed. No theme may silently lose its framing. The migration is atomic — old code is deleted only after every theme's seed data is verified to contain the equivalent prose.
- Narrative mode markers are explicitly deferred (out of scope for this feature).
- The existing test suite baseline of 15 pre-existing behavioral test failures is tolerated and not expected to be fixed by this refactor.
- Session efcbf70f prompt data is available in the dev database for regression test construction.

## Clarifications

### Session 2026-06-29

- Q: When `BuildFramingGuards()` is deleted and its prose moves to theme configuration, what is the migration scope for existing themes? → A: All existing themes updated before code removal (zero regression).
- Q: What is the lifecycle scope of `SceneDirection` — per-prompt, per-turn, or per-session? → A: Per-prompt. Each prompt gets a fresh resolution based on the current phase at build time. No turn-scoped or session-scoped caching.
- Q: What happens if an injector throws an exception during prompt assembly? → A: Fail fast. Let the exception propagate and abort the prompt build. No catch-log-skip. Aligns with repo "no silent fallback" rule.
- Q: How does the Turn Structure Contract behave for single-character turns (no position 2)? → A: Position 1 always gets the Time Span Reminder regardless of turn size. The contract is about position roles, not turn size. No turn-size branching in injectors.
- Q: What observability should the coordinator provide? → A: Information-level log for every prompt build with full context snapshot (injector firing sequence + PromptInjectionContext). Enables post-mortem analysis without repro. Uses Serilog structured templates, configurable levels.
