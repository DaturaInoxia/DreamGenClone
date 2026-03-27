# Phase 0 Research: Chat and Roleplay Command Actions

## Decision 1: Keep intent routing centralized in prompt router

- Decision: Continue using a single routing map for `Instruction`, `Message`, and `Narrative by Character`, with explicit route semantics for each action.
- Rationale: The existing roleplay prompt flow already routes intent through a dedicated router service, which is the cleanest place to enforce deterministic action resolution.
- Alternatives considered:
  - Route directly in UI event handlers. Rejected because it duplicates business rules and weakens testability.
  - Route dynamically from configuration-only mappings. Rejected for now because this feature needs strict semantics and deterministic tests first.

## Decision 2: Instruction is character-agnostic and must remain visible

- Decision: Instruction submissions are accepted without character selection, ignore character context during processing, and are rendered as visible interaction events.
- Rationale: User-defined behavior explicitly states instruction is AI direction for story progression, not character speech, and must appear in interaction history.
- Alternatives considered:
  - Require character for instruction. Rejected as contradictory to feature rules.
  - Keep instruction hidden from timeline. Rejected because transparency and auditability are required.

## Decision 3: Message and Narrative by Character are character-required

- Decision: `Message` and `Narrative by Character` both require a selected character identity before submission.
- Rationale: Both modes are explicitly tied to character POV and should fail validation if character context is absent.
- Alternatives considered:
  - Auto-fallback to previous character silently. Rejected because it can create accidental misattribution.
  - Auto-fallback to narrator. Rejected because it changes user intent semantics.

## Decision 4: Continue As generates one output per selected participant

- Decision: Continue As processes selected participants in deterministic order and generates one participant-scoped output for each selected participant.
- Rationale: User expectation is explicit per-participant POV generation, and deterministic ordering ensures reproducible behavior and test assertions.
- Alternatives considered:
  - Merge all selected participants into one blended response. Rejected because POV separation becomes ambiguous.
  - Randomize participant order. Rejected because it breaks deterministic outcomes.

## Decision 5: Continue As narrative is separate from character POV

- Decision: When Continue As narrative is enabled, the system appends additional scene-advancing narrative not attributed to a specific character POV.
- Rationale: User-defined behavior distinguishes narrative progression from participant POV outputs.
- Alternatives considered:
  - Inject narrative into each participant block. Rejected because it dilutes POV boundaries.
  - Attribute narrative to one selected character. Rejected because narrative is intentionally non-character-specific.

## Decision 6: Clear resets all Continue As selections

- Decision: Clear unselects all participant selections and narrative toggle with no retained state.
- Rationale: User requirement explicitly states reset means full clear with no keep behavior.
- Alternatives considered:
  - Partial reset (participants only). Rejected because it leaves hidden state and increases confusion.
  - Keep previous defaults after clear. Rejected because it conflicts with explicit no-keep rule.

## Decision 7: Continue action parity with main overflow continue

- Decision: Continue button inside Continue As popup invokes the same continuation behavior as main chat overflow continue control.
- Rationale: This guarantees consistent behavior regardless of entry point and prevents divergent logic paths.
- Alternatives considered:
  - Separate continuation implementation for popup. Rejected because behavior drift risk is high.
  - Disable one of the entry points. Rejected because both controls are part of intended UX.
