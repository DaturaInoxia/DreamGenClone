# Contract: Role Play Continue Workspace

## 1. Unified Prompt Interaction Contract

### Input Contract

A valid submit action must include:
- `sessionId`
- `promptText`
- `intent` (`Message` | `Narrative` | `Instruction`)
- `selectedIdentity`
- `behaviorMode`

Validation invariants:
- `promptText.Trim().Length > 0`
- `selectedIdentity` belongs to currently rendered identity options.
- `intent` must be selected or defaulted deterministically before dispatch.

### Output Contract

Submit action yields exactly one of:
- `success`: interaction appended/generated with resulting actor and content.
- `validation_error`: explicit field-level or rule-level reason.
- `routing_error`: unknown intent or unsupported route.
- `mode_error`: actor not allowed in current behavior mode.

## 2. Command Routing Contract

### Routing Table

- `Message` -> Execute message continuation command path with selected identity and prompt text.
- `Narrative` -> Execute narrative continuation path using selected identity context and prompt text.
- `Instruction` -> Execute instruction command path using prompt text as instruction payload.

### Determinism Rule

Given the same tuple
- `(session snapshot, intent, selected identity, promptText, behaviorMode)`

the selected command route must always be identical.

## 3. Identity Options Contract

Rendered identity options must include:
- all active scene characters
- persona
- custom character

Behavior mode filtering:
- options disallowed by behavior mode are either hidden or disabled consistently.
- server-side enforcement must mirror UI constraints.

## 4. Settings Panel Contract

- Settings panel is rendered on the right side of workspace.
- Width is user-resizable within defined bounds.
- Behavior mode control is present and updates execution behavior for subsequent submissions.

## 5. UITemplate Trace Contract

Implementations must include a trace mapping from UITemplate references to delivered behavior:
- `PromtBox.png` and `PromptBox_Ready.png`: unified compose control and submit-ready state.
- `Message_Type.png`: intent selector options and descriptions.
- `Message_Characters.png`: character/persona/custom identity chooser.
- `ContinueAs.png` and `ContinueAs_Custom.png`: continue-as actor selection and custom flow.
- `Settings_Behaviour.png`: right-side settings layout with behavior mode controls.
