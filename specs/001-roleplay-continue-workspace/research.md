# Phase 0 Research: Role Play Continue Workspace Refresh

## Decision 1: Use UITemplate-driven state mapping as explicit design input

- Decision: Treat each image in `specs/UITemplate` as a named UI state or interaction reference and convert them into implementation checkpoints.
- Rationale: The feature request is visual-first and interaction-specific; image-state mapping reduces ambiguity and prevents accidental divergence from expected UX.
- Alternatives considered: Rely only on textual spec requirements without per-image mapping. Rejected because popup behavior and command-intent affordances are hard to infer consistently from text alone.

## Decision 2: Adopt a unified prompt submission model with explicit intent

- Decision: Represent submissions as one compose action with an intended command value: `Message`, `Narrative`, or `Instruction`.
- Rationale: UITemplate shows one prompt control with message-type selector and a tune/options entry point; explicit intent is required to route to the correct backend command path deterministically.
- Alternatives considered: Infer command path from free-text prompt heuristics. Rejected because heuristic parsing is nondeterministic and risks incorrect message-vs-instruction execution.

## Decision 3: Resolve identity options dynamically from scene + persona + custom

- Decision: Build the identity popup from active scene characters, always include persona, and always include custom character.
- Rationale: The request mandates these three sources and preserving custom character support; dynamic resolution ensures current scene state drives choices.
- Alternatives considered: Keep static enum-only actor choices (`You`, `Npc`, `Custom`) in the UI. Rejected because static options cannot reflect real character lists from the active scene.

## Decision 4: Deterministic command routing table for unified prompt execution

- Decision: Define and enforce this routing contract:
  - `Message` intent routes through the message continuation command path.
  - `Narrative` intent routes through narrative continuation generation path using selected identity context.
  - `Instruction` intent routes through instruction command pathway.
- Rationale: Existing services already separate continuation and instruction semantics; a routing table makes behavior explicit, testable, and constitution-compliant for deterministic transitions.
- Alternatives considered: A single continuation path with optional instruction text only. Rejected because it hides command semantics and makes UI intent selection less meaningful.

## Decision 5: Right-side settings panel as resizable layout region

- Decision: Implement settings as a persistent right-side pane with user-resizable width and behavior mode controls inside that pane.
- Rationale: UITemplate shows settings as a dedicated area and the feature explicitly requires size adjustment and behavior mode access during prompt workflow.
- Alternatives considered: Keep settings in stacked cards within the main scroll flow. Rejected because this conflicts with requested side-space behavior and reduces concurrent prompt/settings visibility.

## Decision 6: Preserve and extend existing behavior mode gating

- Decision: Continue enforcing behavior mode actor constraints at execution time and at selector rendering time.
- Rationale: Existing `BehaviorModeService` already enforces allowed actors; extending this to dynamic identity list filtering preserves current rules while adding scene/persona options.
- Alternatives considered: UI-only disabling without server-side checks. Rejected because client-only gating is bypassable and non-deterministic under concurrent state changes.

## Decision 7: Testing strategy combines UI flow checks and service-level routing tests

- Decision: Add tests for routing decisions, behavior mode enforcement with unified prompt model, and identity-option resolution from scene/persona/custom sources.
- Rationale: The highest risk in this feature is incorrect command routing and actor selection under combined popups.
- Alternatives considered: Manual validation only. Rejected because routing regressions are easy to reintroduce and difficult to detect without automated coverage.
