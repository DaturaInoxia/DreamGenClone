import sqlite3, json, os

sid = 'fe35cd50-8be0-45f7-92a3-ae9ec217a8d8'
c = sqlite3.connect('DreamGenClone.Web/data/dreamgenclone.dev.db')
evt = c.execute(
    "SELECT CreatedUtc, Summary, MetadataJson FROM RolePlayDebugEvents WHERE SessionId=? AND EventKind='PromptBuilt' AND ActorName='Becky' ORDER BY CreatedUtc",
    (sid,)
).fetchall()
c.close()

for i, e in enumerate(evt):
    ts = e[0]
    if ts <= '2026-07-01T03:52:06.7862809' and (i == len(evt)-1 or evt[i+1][0] > '2026-07-01T03:52:06.7862809'):
        meta = json.loads(e[2])
        prompt = meta.get('prompt', '')
        print(f'Found at index {i}, {len(prompt)} chars')
        outdir = 'specs/debug/prompts_fe35'
        os.makedirs(outdir, exist_ok=True)
        fpath = os.path.join(outdir, 'prompt_becky_buildup_03-52-06.txt')
        with open(fpath, 'w', encoding='utf-8') as f:
            f.write(prompt)
        print(f'Saved to {fpath}')
        print(f'--- LAST 3000 CHARS ---')
        print(prompt[-3000:])
        break
