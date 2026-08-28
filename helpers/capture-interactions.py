"""Capture latest interactions from a session's PayloadJson."""
import sqlite3, json, sys

db_path = "DreamGenClone.Web/data/dreamgenclone.dev.db"
session_id = sys.argv[1] if len(sys.argv) > 1 else "3a74a033-3a4f-4083-b002-6985fec24bc3"
count = int(sys.argv[2]) if len(sys.argv) > 2 else 5

c = sqlite3.connect(db_path)
row = c.execute("SELECT PayloadJson FROM Sessions WHERE Id=?", (session_id,)).fetchone()
if row and row[0]:
    p = json.loads(row[0])
    ints = p.get('interactions', [])
    print(f'Total interactions: {len(ints)}')
    print()
    for i in ints[-count:]:
        iid = i.get('id', '?')[:8]
        aname = i.get('actorName', '?')
        role = i.get('interactionType', '?')
        content = (i.get('content') or '')
        phase = i.get('narrativePhaseAtCreation', '?')
        print(f'--- [{iid}] {aname} (type={role}, phase={phase}) [{len(content)} chars] ---')
        print(content[:500])
        print('...')
        print()
else:
    print(f'Session {session_id} not found or has no PayloadJson')
c.close()
