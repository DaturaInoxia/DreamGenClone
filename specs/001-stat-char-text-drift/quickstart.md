# Quickstart: Stat-Driven Character Instruction Text & Encounter Dimension Drift

**Branch**: `001-stat-char-text-drift`

---

## Build & Run

```powershell
# From repo root
dotnet build DreamGenClone.sln -v minimal
```

```powershell
# Start the app (dev mode with Serilog console output)
.\helpers\start-webapp-dev.ps1
# Or: dotnet run --project DreamGenClone.Web
```

---

## Run Tests

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj -v normal
```

Run only the stat drift tests:
```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "Category=StatDrift" -v normal
```

---

## Verify Drift in the Database

Use the project DB query tool to inspect runtime encounter stats after interactions:

```powershell
dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/dbquery/queries/inspect_runtime_encounter_stats.sql
```

Or write an ad-hoc query:
```sql
-- inspect_runtime_encounter_stats.sql
SELECT
    SessionId,
    json_extract(value, '$.CharacterId') AS CharId,
    json_extract(value, '$.RuntimeEncounterStats') AS RuntimeEncounterStats
FROM RolePlayV2AdaptiveStates,
     json_each(CharacterSnapshotsJson)
WHERE SessionId = ?
ORDER BY SessionId;
```

Run:
```powershell
dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/dbquery/queries/inspect_runtime_encounter_stats.sql <your-session-id>
```

Repeat after posting a message in a session to see `RuntimeEncounterStats` values drift relative to their starting profile values.

---

## Verify Stat State Text Injection

The stat state text is injected into continuation prompts. With `Serilog` at `Debug` level, the full assembled prompt is logged by `RolePlayContinuationService`. Check:

```powershell
# View the most recent log file
Get-Content (Get-ChildItem DreamGenClone.Web/logs -Filter "*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName) -Tail 200
```

Search for the stat constraint lines:
```
HARD CONSTRAINT — enforce in this response:.*current state:
```

Example expected output (Wife character, high Desire + low Loyalty):
```
HARD CONSTRAINT — enforce in this response: Sarah (Wife) current state: she craves physical intensity with urgency; she will initiate, escalate, and pursue without hesitation; her commitment to her marriage is effectively absent; she feels no guilt and faces no internal resistance to transgression
```

---

## Manual Test: Trigger Drift

1. Start or resume a session with a Wife character bound to an encounter profile.
2. Open DevTools or session debug panel — note the Wife's current Desire stat.
3. Post several messages with content implying desire escalation.
4. After scoring completes, check the DB (see above) — `RuntimeEncounterStats.Exhibitionism` should have increased and `DiscoveryCaution` decreased proportional to the Desire delta.

---

## Manual Test: Profile Rebind Resets Drift

1. With drift applied to a character, open the character's encounter profile picker in the workspace.
2. Select a different profile.
3. Query `RuntimeEncounterStats` in the DB — values should now match the new profile's `EncounterStats` exactly (no residual drift).

---

## Manual Test: Session Resume Uses Saved Drift

1. After drift is visible in the DB, close the session (navigate away or reload).
2. Resume the same session via the session list.
3. Post one turn.
4. Check the prompt log — stat state text should appear immediately (no warm-up turn required) using the pre-existing `RuntimeEncounterStats`.

---

## Notes

- **Neutral band** (35–65): No stat state text is injected when all a character's stats are in this range. This is by design — avoids cluttering the prompt for baseline, unremarkable character state.
- **Tension and Connection removed**: These stats no longer appear in the UI stat panels, are not scored, and do not produce drift. Any existing DB rows with Tension/Connection values in `CharacterSnapshotsJson` are silently ignored during deserialisation.
