# Quickstart: Testing Encounter-Start Detection & Memory Enrichment

**Feature**: 028-encounter-start-detection  
**Date**: 2026-07-08

## Prerequisites

- .NET 9 SDK
- Local LLM running (Ollama/LM Studio) with the `roleplay-summary-enhancement` model slot configured
- Dev database at `DreamGenClone.Web/data/dreamgenclone.dev.db`

## Build

```powershell
dotnet build DreamGenClone.Web --no-restore
dotnet build DreamGenClone.Tests --no-restore
```

## Manual Smoke Test

### 1. Start the app

```powershell
.\helpers\start-webapp-dev.ps1
```

### 2. Create a test session

- Use any theme that has `encounter-completed` semantic mapping configured
- Ensure `RolePlayFeatureFlags.EnableSemanticInference` is `true`
- Set `RolePlayMemory.EncounterStartConfidenceThreshold` to `0.70` (or your test value)

### 3. Verify encounter-start detection

| Step | Action | Expected |
|------|--------|----------|
| 1 | Play through flirtation / sexy conversation | No `EncounterStartDetected` event fires |
| 2 | First actual sexual contact (touching, oral, etc.) | `EncounterStartDetected` fires. `CurrentEncounterNumber` set to 1. `CurrentEncounterStartInteractionIndex` set. |
| 3 | Play through encounter to completion | Normal encounter flow |
| 4 | Verify `EncounterCompletion` record | `StartInteractionIndex` matches step 2, `EndInteractionIndex` matches encounter end |
| 5 | Aftermath fires | `HusbandAftermathInjector` renders first-person memory |

### 4. Verify bug fixes

| Bug | Test | Expected |
|-----|------|----------|
| Part C (Climax clobber) | Encounter begins in BuildUp, Climax entered later | `StartInteractionIndex` = BuildUp start, NOT Climax entry |
| Part D (stale index) | Encounter #1 completes, AdvanceTime skipped, encounter #2 starts | Encounter #2 start detected correctly, StartInteractionIndex correct |

### 5. Verify memory enrichment

| Step | Action | Expected |
|------|--------|----------|
| 1 | Complete an encounter | `EncounterCompletion` written |
| 2 | Wait for enrichment job (async, ~few seconds) | `LlmSummary` populated |
| 3 | Inspect `LlmSummary` | First person ("I..."), includes who, what acts, orgasms, sensory detail |

## DB Verification

```powershell
# Check encounter start detection
.\helpers\dbq.ps1 sql artifacts/tmp/dbquery/queries/session_intensity_detail.sql <sessionId>

# Check encounter completion memory quality
.\helpers\dbq.ps1 sql -c "
SELECT EncounterNumber, CycleIndex, 
       substr(LlmSummary, 1, 200) as MemoryStart,
       StartInteractionIndex, EndInteractionIndex
FROM RolePlayV2EncounterSummaries
WHERE SessionId = '<sessionId>' AND SummaryType = 'EncounterCompletion'
ORDER BY OccurredUtc;
"
```

## Configuration Reference

```json
// appsettings.Development.json
{
  "RolePlayMemory": {
    "EncounterStartConfidenceThreshold": 0.70,
    "EnableLlmSummaryEnhancement": true
  },
  "RolePlayFeatureFlags": {
    "EnableSemanticInference": true
  }
}
```

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Encounter-start never fires | `EnableSemanticInference` is `true`? LLM is running? `EncounterStartConfidenceThreshold` not too high? |
| Memory is third person | Enrichment job ran with old prompt? Restart app and re-test. |
| Wrong interaction range | Check Part C/D bug fixes applied. Verify `StartInteractionIndex` in DB. |
| "Missing encounter-started mapping" error | Should NOT appear — encounter-started is universal. If seen, the old mapping-check code wasn't removed. |
