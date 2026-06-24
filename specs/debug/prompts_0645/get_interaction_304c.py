"""Find interaction 304c916f in session 0645de90 and its matching prompt"""
import sqlite3, json, os

db = r'DreamGenClone.Web/data/dreamgenclone.dev.db'
sid = '0645de90-ed0e-437f-808f-4529bab85276'
target = '304c916f-b17e-4b9a-8283-f89237464fa7'

c = sqlite3.connect(db)
row = c.execute('SELECT PayloadJson FROM Sessions WHERE Id=?', (sid,)).fetchone()
d = json.loads(row[0])
ixns = d.get('interactions', [])

# Find the interaction
found = None
for i, x in enumerate(ixns):
    if target in x.get('id', ''):
        found = (i, x)
        break

if not found:
    print(f"Interaction {target} not found in payload")
    c.close()
    exit()

idx, x = found
target_ct = x.get('createdAt', '')
print(f"Interaction index: {idx}")
print(f"ID: {x.get('id')}")
print(f"createdAt: {target_ct}")
print(f"actor: {x.get('actor')}")
print(f"content (first 300): {str(x.get('content', ''))[:300]}")
print()

# Find the last PromptBuilt event before this interaction's createdAt
evts = c.execute(
    "SELECT CreatedUtc, Summary, MetadataJson FROM RolePlayDebugEvents "
    "WHERE SessionId=? AND EventKind='PromptBuilt' ORDER BY CreatedUtc",
    (sid,)
).fetchall()
c.close()

print(f"Total PromptBuilt events: {len(evts)}")

best_evt = None
best_idx = None
for i, e in enumerate(evts):
    if e[0] <= target_ct:
        best_evt = e
        best_idx = i

if best_evt:
    meta = json.loads(best_evt[2])
    prompt = meta.get('prompt', '')
    actor = meta.get('actor', '')
    custom_name = meta.get('customActorName', '')
    intent = meta.get('intent', '')
    print(f"\nMatching prompt: [{best_idx}] {best_evt[0]}")
    print(f"Actor: {actor} | Name: {custom_name} | Intent: {intent}")
    print(f"Summary: {best_evt[1]}")
    print(f"Prompt length: {len(prompt)} chars")
    print()

    # Save to specs/debug/
    out_dir = os.path.join('specs', 'debug', 'prompts_0645')
    os.makedirs(out_dir, exist_ok=True)
    fname = f"prompt_{best_idx:02d}_{target[:8]}_{best_evt[0][:19].replace(':','-')}.txt"
    with open(os.path.join(out_dir, fname), 'w', encoding='utf-8') as f:
        f.write(prompt)
    print(f"Saved: {out_dir}/{fname}")

    # Search for Phase Guidance and Direction/HARD CONSTRAINT sections
    lines = prompt.split('\n')
    print("\n=== Lines containing 'Phase Guidance' or 'Direction' or 'HARD CONSTRAINT' ===")
    for i, line in enumerate(lines):
        lower = line.strip()
        if 'phase guidance' in lower.lower() or 'narrative direction' in lower.lower() or lower.startswith('direction:'):
            print(f"  L{i+1}: {line[:200]}")
        if line.strip().startswith('HARD CONSTRAINT'):
            # Show the constraint line and a bit of context
            print(f"  L{i+1} [HC]: {line[:200]}")
            # Also show next few lines of the same HC block
            for j in range(1, 5):
                if i+j < len(lines) and lines[i+j].strip().startswith(('HARD ', 'The ', 'He ', 'She ', 'BEHAVIORAL', 'Active', 'Continue', 'Write', '--')):
                    break
                if i+j < len(lines) and lines[i+j].strip():
                    print(f"    L{i+j+1}: {lines[i+j][:200]}")

else:
    print("No PromptBuilt event found before this interaction")
