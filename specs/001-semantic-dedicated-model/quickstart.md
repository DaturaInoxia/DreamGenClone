# Quickstart: Semantic Analysis — Dedicated Model & Concurrent Processing

**Branch**: `001-semantic-dedicated-model`  
**Date**: 2026-05-28

Minimal steps to build, configure, and validate this feature end-to-end after implementation.

---

## Prerequisites

- DreamGenClone solution builds: `dotnet build DreamGenClone.sln -v minimal`
- At least one LLM provider and model registered in Model Manager
- App running locally: `helpers/start-webapp-dev.ps1`

---

## Step 1 — Assign a Model to RP Semantic Analysis

1. Open `http://localhost:5000/model-manager`
2. Scroll to **Function Defaults**
3. Find the row **"RP Semantic Analysis (Background)"**
4. If no model is assigned, a warning badge is visible on the row
5. Select a model from the dropdown (a small/fast model is recommended)
6. Optionally set **Max Parallel** (1–16; default 2 applies if left blank)
7. Click **Save**
8. Observe the inline hint _"Takes effect on next restart."_ below the Max Parallel field

---

## Step 2 — Verify Model Assignment Persisted

1. Restart the app
2. Reopen Model Manager → Function Defaults
3. Confirm the "RP Semantic Analysis (Background)" row shows the saved model and Max Parallel value
4. Confirm the warning badge is no longer shown

---

## Step 3 — Verify Semantic Analysis Uses the Dedicated Slot

1. Open any active RP session
2. Advance the session by one interaction (submit prompt or continue)
3. Open app logs (`DreamGenClone.Web/logs/` or console)
4. Search for `SemanticInteractionAnalysis` log entries
5. Confirm log entries show the model assigned to `RolePlaySemanticAnalysis`, **not** `RolePlayGeneration`

---

## Step 4 — Verify Fail-Fast on Missing Model

1. In Model Manager → Function Defaults, clear the model for "RP Semantic Analysis (Background)" (use Clear button)
2. Advance an RP session by one interaction
3. In logs, confirm a **Warning** entry: _"No model configured for function 'RolePlaySemanticAnalysis'..."_
4. Confirm the analysis state for that interaction is recorded as `Error` in the DB:
   ```
   dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/dbquery/check-semantic-error.sql
   ```
5. Confirm the RP session itself continued normally (RP output was generated)

---

## Step 5 — Verify Concurrency Cap

1. Set Max Parallel to 2, save, restart
2. Open 3+ RP sessions and generate interactions in all simultaneously
3. In logs, confirm no more than 2 `SemanticInteractionAnalysis` jobs appear with status `Analyzing` at the same time
4. Confirm all jobs eventually complete

---

## Running Tests

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj -v minimal
```

Key test classes to verify:
- `SemanticEventInferenceServiceTests` — confirms `AppFunction.RolePlaySemanticAnalysis` is used
- `SemanticBackgroundJobWorkerTests` — confirms semaphore cap is respected
- `FunctionDefaultRepositoryTests` — confirms `MaxConcurrentJobs` roundtrip

---

## DB Query Reference

Check semantic analysis state for recent interactions:

```sql
SELECT SessionId, InteractionId, Status, ErrorMessage, AnalyzedUtc
FROM RolePlaySemanticInteractionAnalysisState
ORDER BY UpdatedUtc DESC
LIMIT 20;
```

Check FunctionModelDefault for the new slot:

```sql
SELECT FunctionName, ModelId, MaxConcurrentJobs, UpdatedUtc
FROM FunctionModelDefaults
WHERE FunctionName = 'RolePlaySemanticAnalysis';
```
