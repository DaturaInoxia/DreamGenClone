import sqlite3, json
c = sqlite3.connect('DreamGenClone.Web/data/dreamgenclone.dev.db')
sid = '0aad7fd1-5c7e-4e19-a79d-516af1658987'

row = c.execute("SELECT Id, PayloadJson FROM Sessions WHERE Id=?", (sid,)).fetchone()
if row:
    p = json.loads(row[1])
    state = p.get("adaptiveState", {})
    phase = state.get("currentPhase", "?")
    obs = state.get("observedTurnCount", "?")
    ixn_count = len(p.get("interactions", []))
    print(f"Payload Phase: {phase}, ObservedTurnCount: {obs}, Interactions: {ixn_count}")
else:
    print("Session not in Sessions table")

# Check V2 columns
cols = c.execute("PRAGMA table_info('RolePlayV2AdaptiveStates')").fetchall()
col_names = [x[1] for x in cols]
print(f"V2 columns: {col_names}")

# Find the adaptive state JSON column
json_col = [c for c in col_names if 'Json' in c or 'State' in c]
print(f"JSON-like columns: {json_col}")

# Try common column names
for col in ['StateJson','AdaptiveStateJson','PayloadJson','V2StateJson','SnapshotJson','ScenarioStateJson']:
    try:
        r = c.execute(f"SELECT {col} FROM RolePlayV2AdaptiveStates WHERE SessionId=? LIMIT 1", (sid,)).fetchone()
        if r:
            s = json.loads(r[0])
            v2_phase = s.get("CurrentPhase", "?")
            v2_obs = s.get("ObservedTurnCount", "?")
            print(f"V2 via {col}: Phase={v2_phase}, ObservedTurnCount={v2_obs}")
            break
    except Exception as e:
        pass
else:
    # Try reading all columns
    r = c.execute("SELECT * FROM RolePlayV2AdaptiveStates WHERE SessionId=?", (sid,)).fetchone()
    if r:
        print(f"V2 row: {len(r)} columns")
        for i, (name, val) in enumerate(zip(col_names, r)):
            if val and isinstance(val, str) and len(val) > 20:
                print(f"  [{i}] {name}: {val[:200]}...")
            elif val is not None:
                print(f"  [{i}] {name}: {val}")
    else:
        print("NO V2 row found at all")
c.close()
