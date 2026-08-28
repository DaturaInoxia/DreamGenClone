---
description: 'How to extract full LLM prompts from RolePlayDebugEvents and save them for analysis. Use when asked to "pull the prompt", "show the full prompt", "extract prompts", or analyze model behavior via prompts.'
applyTo: '**/*.py,**/*.md,**/*.txt'
---

# Extracting LLM Prompts from RolePlayDebugEvents

## Overview

Every prompt sent to the LLM is logged as a `PromptBuilt` event in the `RolePlayDebugEvents` table. The full prompt text is stored in the `MetadataJson` column under the `prompt` key — up to ~85KB per prompt.

## Output Directory

**All extracted prompts MUST be saved to `specs/debug/`** — this is the single, permanent location. Create a subfolder per session using the session short ID.

Example: `specs/debug/prompts_89df/` for session `89dfe0ab-...`

## Quick Command

Use Python with sqlite3 directly against `DreamGenClone.Web/data/dreamgenclone.dev.db`:

```powershell
python -c "
import sqlite3, json
sid = 'SESSION_ID_HERE'
c = sqlite3.connect('DreamGenClone.Web/data/dreamgenclone.dev.db')
evt = c.execute(\"SELECT CreatedUtc, Summary, MetadataJson FROM RolePlayDebugEvents WHERE SessionId=? AND EventKind='PromptBuilt' ORDER BY CreatedUtc\", (sid,)).fetchall()
c.close()
for i,e in enumerate(evt):
    meta = json.loads(e[2])
    prompt = meta.get('prompt','')
    print(f'[{i}] {e[0]} | {e[1][:60]} | {len(prompt)} chars')
"
```

## Finding the Right Prompt for a Specific Interaction

The `PromptBuilt` event does NOT store the interaction ID directly. To match a prompt to an interaction:

1. Find the interaction's `createdAt` timestamp from the session payload
2. Find the last `PromptBuilt` event with `CreatedUtc` <= the interaction's `createdAt`
3. That PromptBuilt is the one that generated the interaction

## Saving a Prompt to Disk

All prompt files MUST be saved under `specs/debug/prompts_{session_short_id}/`.

```python
import sqlite3, json, os

REPO_ROOT = os.path.dirname(__file__)  # adjust as needed
OUTPUT_DIR = os.path.join(REPO_ROOT, "specs", "debug")

sid = "session-guid"
short_sid = sid[:8]  # e.g. "89dfe0ab"
session_dir = os.path.join(OUTPUT_DIR, f"prompts_{short_sid}")
os.makedirs(session_dir, exist_ok=True)

c = sqlite3.connect("DreamGenClone.Web/data/dreamgenclone.dev.db")

# Get the interaction details
r = c.execute("SELECT PayloadJson FROM Sessions WHERE Id=?", (sid,)).fetchone()
d = json.loads(r[0])
ixns = d["interactions"]
target_id = "interaction-guid"  # e.g. "a5fe92bb-06a2-4592-af3d-7779792c6616"

target_idx = None
target_ct = None
for i, x in enumerate(ixns):
    if target_id in x.get("id",""):
        target_idx = i
        target_ct = x.get("createdAt","")
        break

# Find the last PromptBuilt before the interaction
evt = c.execute("""
    SELECT CreatedUtc, Summary, MetadataJson FROM RolePlayDebugEvents
    WHERE SessionId=? AND EventKind='PromptBuilt'
    ORDER BY CreatedUtc
""", (sid,)).fetchall()
c.close()

best_evt = None
for e in evt:
    if e[0] <= target_ct:
        best_evt = e

if best_evt:
    meta = json.loads(best_evt[2])
    prompt = meta.get("prompt", "")
    short_id = target_id[:8]
    safe_time = target_ct.replace(":", "-").replace(" ", "_")[:20]
    fname = f"prompt_{short_id}_[{target_idx}]ActorName_{safe_time}.txt"
    fpath = os.path.join(session_dir, fname)
    with open(fpath, "w", encoding="utf-8") as f:
        f.write(prompt)
    print(f"Saved {fpath} ({len(prompt)} chars)")
```

## Key Notes

- `MetadataJson` contains: `actor`, `customActorName`, `intent`, `prompt` (full text), `promptLength`
- Interaction ID is NOT stored in `MetadataJson` — match by timestamp proximity
- Prompts range from ~30KB (early session) to ~85KB (late session, more context)
- The "Active Instruction (persistent)" section, if present, indicates a user steer or engine instruction was re-injected
- Check `HARD CONSTRAINT` lines for behavioral frames, theme constraints, and scenario rules
- Check the last few lines for the `Message:`, `Instruction:`, or `Narrative Direction:` label with the per-turn prompt text
