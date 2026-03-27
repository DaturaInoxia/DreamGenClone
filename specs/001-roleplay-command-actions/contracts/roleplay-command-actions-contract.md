# Contract: Chat and Roleplay Command Actions

## 1. Manual Command Submission Contract

### Input

`SubmitCommandRequest`:
- `sessionId` (required)
- `intent` (required): `Instruction | Message | NarrativeByCharacter`
- `promptText` (required, trimmed non-empty)
- `selectedCharacterId` (required for `Message` and `NarrativeByCharacter`; omitted for `Instruction`)
- `selectedCharacterType` (optional)
- `submittedVia` (required): `PlusButton | SendButton`

### Validation Invariants

- `promptText.Trim().Length > 0`
- `intent=Instruction` -> no character requirement; existing character selection ignored for execution semantics
- `intent=Message|NarrativeByCharacter` -> character must be selected and available

### Output

`SubmitCommandResult`:
- `success`: command routed and processed
- `validation_error`: explicit field/rule violation
- `routing_error`: intent route missing/invalid
- `processing_error`: downstream execution failure

## 2. Intent Routing Contract

Routing table:
- `Instruction` -> instruction command path, visible interaction event in timeline
- `Message` -> character message generation path
- `NarrativeByCharacter` -> character narrative-expansion path

Determinism rule:
Given identical `(sessionSnapshot, intent, promptText, selectedCharacter, behaviorMode)`, route selection and validation outcome must be identical.

## 3. Continue As Contract

### Input

`ContinueAsRequest`:
- `sessionId` (required)
- `selectedParticipants` (0..n): `You | Npc | Custom`
- `includeNarrative` (bool)
- `selectionOrder` (required deterministic ordering snapshot)
- `triggeredBy` (required): `ContinueAsPopupContinue | MainOverflowContinue`

`ClearContinueAsRequest`:
- `sessionId` (required)

### Validation Invariants

- Continue requires at least one selected participant OR `includeNarrative=true`
- Selected participants must be available in current mode
- `selectionOrder` must include selected participants exactly once each

### Output

`ContinueAsResult`:
- `participantOutputs[]`: exactly one output for each selected participant POV in deterministic order
- `narrativeOutput` (optional): non-character scene/tone progression when `includeNarrative=true`
- `status`: `success | validation_error | processing_error`

Parity rule:
- Continue from popup and continue from main overflow must invoke equivalent continuation semantics.

## 4. Clear Contract

- Clear unselects all participants and narrative option.
- No selection state is retained after clear.
- Clear response returns reset state snapshot for UI sync.

## 5. Observability Contract

For each submit/continue/clear operation, emit structured logs with:
- `SessionId`
- `Intent` (if applicable)
- `ParticipantScope` (if applicable)
- `OutcomeStatus`
- `CorrelationId`/operation id

Information-level logs are required for accepted operations; failures include error-level logs with actionable context.
