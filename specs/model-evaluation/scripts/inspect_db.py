import json
import sqlite3

DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'
con = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)

targets = [
    'zai-org/GLM-5.3-Flash',
    'nvidia/NVIDIA-Nemotron-3.5-Super-VL',
    'Qwen/Qwen3.8-Flash',
    'openai/gpt-oss-120b',
    'MiniMaxAI/MiniMax-M3.1-Flash',
    'Qwen/Qwen3.5-9B',
    'deepseek-ai/DeepSeek-V4.1-Flash-1017',
    'deepseek-ai/DeepSeek-V4.1-Flash',
    'MiniMaxAI/MiniMax-M3',
    'Qwen/Qwen3.7-Plus',
    'meta-models/Muse-Glimmer-30B',
    'Qwen/Qwen3.6-Plus',
    'thinkingmachines/Inkling',
    'meta-models/Muse-Spark-Open-816B-A42B',
    'deepseek-ai/DeepSeek-V4-Pro-0813',
    'zai-org/GLM-5.3',
    'zai-org/GLM-5.2',
]

rows = con.execute("""
    select m.ModelIdentifier, m.DisplayName, m.IsEnabled, m.SupportsThinkingControl,
           m.MaximumContextTokens, m.MaximumOutputTokens, m.ContextWindowSize, p.Name
    from RegisteredModels m join Providers p on p.Id = m.ProviderId
    where p.Name = 'TogetherAI'
""").fetchall()

by_id = {r[0]: r for r in rows}
print(f'{len(rows)} TogetherAI rows in Model Manager\n')
print(f"{'target id':44} {'present':8} {'enabled':8} {'thinking':9} {'maxCtx':>8} {'maxOut':>8}")
for t in targets:
    r = by_id.get(t)
    if r:
        print(f'{t:44} {"yes":8} {str(bool(r[2])):8} {str(bool(r[3])):9} {str(r[4]) if r[4] is not None else "-":>8} {str(r[5]) if r[5] is not None else "-":>8}')
    else:
        print(f'{t:44} {"NO":8}')

print('\n--- registered but not in the 17 (for cleanup context) ---')
for r in rows:
    if r[0] not in targets:
        print(f'  {r[0]}')
