# Quickstart: Physical Attributes

**Branch**: `001-physical-attributes` | **Date**: 2026-05-13

This guide describes how to build and manually verify the physical attributes feature end-to-end once implementation is complete.

---

## Prerequisites

- .NET 9 SDK installed
- Solution builds cleanly on the target branch: `git checkout 001-physical-attributes`

---

## Build

```powershell
cd d:\src\DreamGenClone
dotnet build DreamGenClone.sln -v minimal
# Expected: Build succeeded. 0 Error(s)
```

---

## Verification Flows

### Flow 1 — Character Template: Create and round-trip

1. Start the web app: `.\helpers\start-webapp-dev.ps1`
2. Navigate to **Templates**.
3. Create a new template, type **Character**.
4. Fill in physical attributes:
   - Hair Colour → select **Auburn** from dropdown
   - Eye Colour → select **Green**
   - Body Type → select **Athletic**
   - Age → type `32`
   - Attractiveness Rating → type `8`
5. Set Gender → **Female**; confirm **Endowment** fields are hidden and **Female Genitalia** is visible.
6. Click **Save**.
7. Reload the page and reselect the template.
8. ✅ All filled values are present and correct.
9. ✅ Female Genitalia field shows after reload for Female gender.

---

### Flow 2 — Character Template: Custom value round-trip

1. Open the template from Flow 1.
2. For Hair Style, select **(Custom…)** → type `Loose waves`.
3. Save and reload.
4. ✅ Hair Style dropdown shows **(Custom…)**.
5. ✅ Free-text input shows `Loose waves`.

---

### Flow 3 — Scenario Editor: Per-character attributes

1. Navigate to **Scenarios** → open or create a scenario with at least one character.
2. Expand a character card.
3. ✅ Physical attributes editor appears after the Description textarea.
4. Set Body Type → **Curvy**, Skin Tone → **Light Olive**.
5. Set Gender → **Male** on a different character; confirm **Female Genitalia** is hidden and **Endowment** is visible.
6. Save the scenario and navigate away.
7. Reopen the scenario.
8. ✅ Values persisted correctly per character.

---

### Flow 4 — Scenario Editor: Copy-on-add from template

1. Create a Character template with Hair Colour = **Black** and Body Type = **Slim**.
2. Open a Scenario, add a character from that template.
3. ✅ The new character card's physical attributes are pre-filled with the template values.
4. Edit the template to change Hair Colour → **Blonde**. Save.
5. Reopen the scenario.
6. ✅ The scenario character still shows **Black** (snapshot isolation).

---

### Flow 5 — RolePlayCreate: Persona template inheritance

1. Create a Persona template with Age = `28`, Body Type = **Petite**, Attractiveness = `9`.
2. Navigate to **New Roleplay Session**.
3. On the Persona step, select the template.
4. ✅ Physical attributes fields are pre-populated with Age = 28, Body Type = Petite, Attractiveness = 9.
5. Complete session creation.
6. Navigate to the session in **Workspace** → Persona panel.
7. ✅ The inherited attributes are visible.

---

### Flow 6 — RolePlayWorkspace: Edit and save persona attributes

1. Open a roleplay session in **Workspace**.
2. Expand the **Persona** panel.
3. ✅ Physical attributes editor is present.
4. Change Hair Colour → **Silver**.
5. Wait for the existing auto-save (same trigger as Name/Description changes).
6. Refresh the browser.
7. ✅ Hair Colour shows **Silver** after refresh.

---

### Flow 7 — Prompt injection verification

1. Open a session that has a character with Hair Colour = **Auburn** and Body Type = **Athletic**.
2. Send a continue request.
3. Open the application log (`DreamGenClone.Web/logs/`) and find the most recent log entry for `RolePlayContinuationService`.
4. ✅ Log or prompt text contains `Appearance — Hair colour: auburn; Body type: athletic`.
5. ✅ If the character has no attributes, no `Appearance —` block appears in the prompt.

---

### Flow 8 — Retry injection verification

1. In an active session, trigger an **Interaction Retry**.
2. Check the log for the retry prompt.
3. ✅ Character appearance block is present (same format as Flow 7).

---

## Expected Build Output

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

No new warnings should be introduced. If any appear, investigate before marking tasks complete.
