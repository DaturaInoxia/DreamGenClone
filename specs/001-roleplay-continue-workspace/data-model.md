# Data Model: Role Play Continue Workspace Refresh

## 1. UnifiedPromptSubmission

Represents one user submit action from the unified prompt box.

Fields:
- `sessionId` (string, required): active role-play session identifier.
- `promptText` (string, required): user-entered text from unified input.
- `intent` (enum, required): `Message`, `Narrative`, `Instruction`.
- `selectedIdentityId` (string, required): unique identity option id selected in popup.
- `selectedIdentityType` (enum, required): `SceneCharacter`, `Persona`, `CustomCharacter`.
- `customIdentityName` (string, optional): required when selected type is `CustomCharacter` and no predefined custom identity exists.
- `behaviorModeAtSubmit` (enum, required): behavior mode snapshot at submit time.
- `timestampUtc` (DateTime, required): submission timestamp.

Validation rules:
- `promptText` must be non-empty after trim.
- `intent` must be one of the allowed enum values.
- `selectedIdentityId` must resolve to a currently available option.
- If `selectedIdentityType = CustomCharacter`, either `selectedIdentityId` resolves to existing custom entry or `customIdentityName` must be provided.

State transitions:
- `Draft` -> `Validated` -> `Routed` -> `Executed` or `Rejected`.

## 2. IdentityOption

Represents one selectable identity in the continue-as popup.

Fields:
- `id` (string, required): stable id for UI selection and routing.
- `displayName` (string, required): label shown in popup.
- `sourceType` (enum, required): `SceneCharacter`, `Persona`, `CustomCharacter`.
- `isAvailable` (bool, required): reflects behavior-mode and scene-state constraints.
- `availabilityReason` (string, optional): reason when unavailable.

Validation rules:
- `displayName` must be non-empty.
- `sourceType` must be one of the supported values.
- `id` must be unique across rendered list.

State transitions:
- `Discovered` -> `FilteredByBehaviorMode` -> `Renderable`.

## 3. PromptCommandRoute

Represents deterministic routing outcome from selected intent to execution path.

Fields:
- `intent` (enum, required): `Message`, `Narrative`, `Instruction`.
- `targetCommand` (string, required): logical command name executed.
- `requiresInstructionPayload` (bool, required): true for instruction routing.
- `requiresActorContext` (bool, required): true for message/narrative continuation.

Validation rules:
- Every supported `intent` must map to exactly one route.
- Routing table must be exhaustive and have no duplicate intent keys.

State transitions:
- `Resolved` -> `Executed`.

## 4. WorkspaceSettingsState

Represents right-side settings panel state relevant to continuation behavior.

Fields:
- `behaviorMode` (enum, required): selected behavior mode.
- `panelWidthPx` (int, required): persisted width within min/max bounds.
- `isSettingsVisible` (bool, required): whether panel is shown.

Validation rules:
- `panelWidthPx` must remain within configured min and max.
- `behaviorMode` must map to supported mode in domain.

State transitions:
- `Loaded` -> `Modified` -> `Applied`.

## Relationships

- One `UnifiedPromptSubmission` references exactly one `IdentityOption` at submit time.
- One `UnifiedPromptSubmission` resolves through one `PromptCommandRoute`.
- One `UnifiedPromptSubmission` captures one `WorkspaceSettingsState.behaviorMode` snapshot for deterministic execution.
