# Quickstart: Validate Chat and Roleplay Command Actions

## Prerequisites

- Branch checked out: `001-roleplay-command-actions`
- .NET 9 SDK installed
- Local LM Studio endpoint configured per application settings

## 1. Build and run tests

Before running tests, ensure no existing `DreamGenClone` web process is using files under `DreamGenClone.Web/bin/Debug/net9.0`.

```powershell
dotnet test DreamGenClone.sln --filter "RolePlay"
```

Expected:
- Existing and new roleplay routing/validation tests pass.

If a lock error appears (`MSB3021`/`MSB3027`), stop the running web app process and rerun the test command.

## 2. Start web app

```powershell
pwsh ./helpers/start-webapp.ps1
```

Open roleplay workspace and load an existing session.

## 3. Validate Instruction behavior

1. Set action type to Instruction.
2. Do not select any character.
3. Enter broad direction text and submit via plus control.

Expected:
- Submission is accepted without character requirement.
- Instruction appears in interaction history.
- Story direction affects following interactions.

## 4. Validate Message behavior

1. Set action type to Message.
2. Select a character.
3. Enter direction text (tone/mood/action) and submit.

Expected:
- AI output is generated for selected character POV.
- Missing character selection triggers validation feedback.

## 5. Validate Narrative by Character behavior

1. Set action type to Narrative by Character.
2. Select a character.
3. Enter short phrase/tone seed and submit.

Expected:
- AI expands seed into fuller narrative from selected character POV.
- Missing character selection triggers validation feedback.

## 6. Validate Continue As behavior

1. Open Continue As popup.
2. Select multiple participants (for example You + NPC).
3. Enable narrative toggle.
4. Execute Continue.

Expected:
- One generated interaction per selected participant POV.
- Additional non-character narrative advances scene/tone.
- Continue semantics match main overflow continue action.

## 7. Validate Clear behavior

1. In Continue As popup, select participants and narrative.
2. Click Clear.

Expected:
- All selections are unselected.
- No selection state is retained.

## 8. Validate error and logging behavior

1. Submit empty/whitespace text.
2. Attempt character-scoped action without character.

Expected:
- User-facing actionable errors are shown.
- Operation failures produce structured logs with session and outcome context.
