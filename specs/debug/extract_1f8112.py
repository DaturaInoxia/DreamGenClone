import sqlite3, json, os

sid = '1f8112fb-a557-4bd3-a07e-935b4cfbcce8'
target_id = 'db6412b1-f149-4170-a51b-524cd0a0ddb8'

c = sqlite3.connect('DreamGenClone.Web/data/dreamgenclone.dev.db')
row = c.execute('SELECT PayloadJson FROM Sessions WHERE Id=?', (sid,)).fetchone()
c.close()

if not row:
    print('Session not found!')
    exit()

payload = json.loads(row[0])
ixns = payload.get('interactions', [])

target = None
target_idx = None
for i, ix in enumerate(ixns):
    if target_id in ix.get('id', ''):
        target = ix
        target_idx = i
        break

if not target:
    print(f'Interaction {target_id} not found. Available ({len(ixns)} total):')
    for i, ix in enumerate(ixns):
        ix_id = ix.get('id', '?')[:8]
        actor = ix.get('actorName', '?')
        itype = ix.get('interactionType', '?')
        print(f'  [{i}] {ix_id} | {actor} | {itype}')
    exit()

prompt = target.get('promptText') or target.get('PromptText', '')
reasoning = target.get('reasoningContent') or target.get('ReasoningContent', '')

print(f'Interaction [{target_idx}]: {target["id"][:8]}')
print(f'  Actor: {target.get("actorName", "?")}')
print(f'  Type: {target.get("interactionType", "?")}')
print(f'  Phase: {target.get("narrativePhaseAtCreation", "?")}')
print(f'  Prompt: {len(prompt)} chars')
print(f'  Reasoning: {len(reasoning)} chars')
print(f'  Content preview: {target.get("content", "")[:200]}')

short_sid = sid[:8]
outdir = f'specs/debug/prompts_{short_sid}'
os.makedirs(outdir, exist_ok=True)

short_iid = target_id[:8]
safe_actor = target.get('actorName', 'unknown').replace(' ', '_').replace(':', '-')[:20]
fname = f'prompt_{short_iid}_[{target_idx}]_{safe_actor}.txt'
fpath = os.path.join(outdir, fname)
with open(fpath, 'w', encoding='utf-8') as f:
    f.write(prompt)
print(f'Saved prompt to: {fpath}')

if reasoning:
    rname = f'reasoning_{short_iid}_[{target_idx}]_{safe_actor}.txt'
    rpath = os.path.join(outdir, rname)
    with open(rpath, 'w', encoding='utf-8') as f:
        f.write(reasoning)
    print(f'Saved reasoning to: {rpath}')
