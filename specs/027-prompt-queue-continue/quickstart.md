# Quickstart: Prompt Queue — Navigation-Resilient RP Submissions

**Feature Branch**: `027-prompt-queue-continue`  
**Date**: 2026-05-29

---

## Prerequisites

- The web app is running (`helpers/start-webapp-dev.ps1` or equivalent).
- A role-play session exists with at least one configured scenario and character.
- The local LLM model is loaded and responding.

---

## Manual Test 1: Response Survives Navigation (US1 — P1)

**Goal**: Verify a Continue prompt completes and persists even after navigating away.

1. Open the RP workspace for a session.
2. Click **Continue** (or type a prompt and send).
3. **Immediately** (before the response arrives) navigate to a different page (e.g., Home, or another session).
4. Wait for the LLM response latency to elapse (typically 5–30 seconds).
5. Navigate back to the same workspace.

**Expected**: The response is present in the interaction history. No re-submit required.  
**Failure indicator**: History ends at the last interaction before the submit; workspace is in idle state with no new entry.

---

## Manual Test 2: All Prompt Types Survive Navigation (US2 — P1)

**Goal**: Verify custom prompts and commands are equally resilient.

1. Open the workspace.
2. Type a custom prompt in the prompt box and send.
3. Navigate away before response arrives.
4. Return — verify response in history.
5. Repeat with an interaction command (e.g., Steer or Instruction intent).

**Expected**: Both custom prompt and command results appear in history on return.

---

## Manual Test 3: In-Progress Indicator on Return (US3 — P2)

**Goal**: Verify the workspace shows a processing indicator when returning during an in-flight submission.

1. Open the workspace.
2. Send a Continue (or use a slow model to increase latency).
3. Navigate away and **quickly** return (within 1–2 seconds, before the LLM responds).
4. Observe the workspace state.

**Expected**: A processing indicator is visible; the prompt send button is disabled. When the response arrives, the indicator clears and the new interaction appears.

---

## Manual Test 4: Duplicate Submit Blocked

**Goal**: Verify a second submit is rejected while one is in-flight.

1. Send a prompt and do NOT navigate away.
2. While awaiting the response (processing indicator visible), attempt to send another prompt.

**Expected**: The second submit is rejected with a visible indicator ("response already in progress" or similar). The first response completes normally.

---

## Manual Test 5: Failure Re-Submit (US3 edge case)

**Goal**: Verify the inline error and pre-filled re-submit affordance when a background submission fails.

*(This requires simulating a failure — easiest by stopping the local model server mid-request.)*

1. Send a prompt.
2. Stop the local model server before the response arrives.
3. Navigate away, then return.

**Expected**: An inline error banner is shown: "Last response failed — re-submit?" The prompt input is pre-filled with the original prompt text. Clicking Re-send shows a confirmation step. Clicking Confirm fires the submission (restart the model server first). Clicking Dismiss clears the error and unblocks the prompt box.

---

## Verifying DB Persistence

To confirm the response was persisted, use the DB query tool:

```powershell
dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/check_latest_interaction.sql <session-id>
```

The latest `RolePlayInteractions` row for the session should contain the response text.
