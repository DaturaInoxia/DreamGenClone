# Quickstart: Role Play Continue Workspace Refresh

## Prerequisites

- .NET 9 SDK installed.
- LM Studio (or configured local OpenAI-compatible endpoint) available for continuation generation.
- Repository root at `D:\src\DreamGenClone`.

## 1. Build and run

1. Restore and build solution:
   - `dotnet restore`
   - `dotnet build DreamGenClone.sln`
2. Run web app:
   - `dotnet run --project DreamGenClone.Web/DreamGenClone.csproj`
3. Open the role-play workspace for a test session.

## 2. Validate unified prompt flow

1. Confirm there is a single prompt input (no separate Continue As and Message text entry surfaces).
2. Open intent selector popup and verify options: Message, Narrative, Instruction.
3. Open identity selector popup and verify available options include scene characters, persona, and custom character.
4. Enter prompt text and submit.

Expected:
- Submission validates and executes without leaving the workspace.
- Routed command path matches selected intent.

## 3. Validate command routing logic

1. Submit with `Message` intent and a selected character.
2. Submit with `Instruction` intent and explicit instruction text.
3. Submit with `Narrative` intent.

Expected:
- Message path executes message continuation behavior.
- Instruction path executes instruction command behavior.
- Narrative path executes narrative continuation behavior.

## 4. Validate behavior mode and settings panel

1. Resize right-side settings panel narrower/wider.
2. Change behavior mode.
3. Submit unified prompt with actor choices that are allowed and disallowed by mode.

Expected:
- Panel resizing stays within bounds and keeps prompt usable.
- Updated behavior mode is applied to subsequent submissions.
- Disallowed actor submits are blocked with explicit feedback.

## 5. Validate settings resize persistence bounds model

1. Run role-play tests filtered to settings panel behavior:
    - `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --configuration Release --filter "FullyQualifiedName~RolePlaySettingsPanelTests"`
2. Confirm min/max/default constraints are enforced by `WorkspaceSettingsState`.

Expected:
- Panel width values are clamped to configured min/max.
- Reset operation restores default panel width.

## 6. Validate behavior mode snapshot at submit

1. Run behavior-mode submit tests:
    - `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --configuration Release --filter "FullyQualifiedName~RolePlayBehaviorModeSubmitTests"`
2. Verify submitted `BehaviorModeAtSubmit` is applied to the session.

Expected:
- Submit path captures and applies mode snapshot deterministically.
- Unavailable identities are rejected with clear errors.

## 7. Run tests

- Run role-play related tests in `DreamGenClone.Tests`.
- Ensure tests cover:
  - intent-to-command routing
  - behavior mode actor gating
  - identity option resolution (scene/persona/custom)
  - prompt validation (empty/whitespace blocking)
   - settings panel width bounds state model

## 8. Visual parity check against UITemplate

Compare implemented UI states against:
- `specs/UITemplate/PromtBox.png`
- `specs/UITemplate/PromptBox_Ready.png`
- `specs/UITemplate/Message_Type.png`
- `specs/UITemplate/Message_Characters.png`
- `specs/UITemplate/ContinueAs.png`
- `specs/UITemplate/ContinueAs_Custom.png`
- `specs/UITemplate/Settings_Behaviour.png`

Expected:
- Unified compose experience, popup behavior, identity selection, and right-side settings behavior align with reference interaction intent.

## 9. Verify logs for new paths

1. Ensure development logging is enabled for role-play namespaces in `DreamGenClone.Web/appsettings.Development.json`.
2. Exercise these flows in the workspace:
   - prompt submission
   - behavior mode change
   - settings panel resize

Expected:
- Information-level logs are emitted for route selection/execution, behavior mode updates, and panel resize changes.
