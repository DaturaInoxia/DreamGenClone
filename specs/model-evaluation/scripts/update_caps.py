"""Store the measured output ceilings in Model Manager."""
import sqlite3

DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'

# Measured: the largest max_tokens each endpoint accepted (probe + full run).
CAPS = {
    'nvidia/NVIDIA-Nemotron-3.5-Super-VL': 131072,
    'Qwen/Qwen3.8-Flash': 131072,
    'MiniMaxAI/MiniMax-M3.1-Flash': 262144,
    'deepseek-ai/DeepSeek-V4.1-Flash': 262144,
    'deepseek-ai/DeepSeek-V4.1-Flash-1017': 262144,
    'meta-models/Muse-Glimmer-30B': 65536,
    'Qwen/Qwen3.6-Plus': 65536,
    'thinkingmachines/Inkling': 262144,
    'meta-models/Muse-Spark-Open-816B-A42B': 262144,
    'deepseek-ai/DeepSeek-V4-Pro-0813': 262144,
    'zai-org/GLM-5.3-Flash': 262144,
    'zai-org/GLM-5.2': 262144,
    # Not re-measured this pass; 131072 was accepted by the capability probe, so it is a safe floor.
    'openai/gpt-oss-120b': 131072,
    'MiniMaxAI/MiniMax-M3': 131072,
    'Qwen/Qwen3.5-9B': 131072,
    'Qwen/Qwen3.7-Plus': 131072,
    'zai-org/GLM-5.3': 131072,
}

con = sqlite3.connect(DB)
for model, cap in CAPS.items():
    cur = con.execute('update RegisteredModels set MaximumOutputTokens=? where ModelIdentifier=?', (cap, model))
    print(f'{model:44} -> {cap:>7}  rows={cur.rowcount}')
con.commit()

print('\nverify:')
for row in con.execute("""select ModelIdentifier, MaximumContextTokens, MaximumOutputTokens, SupportsThinkingControl
                          from RegisteredModels where ModelIdentifier in (%s) order by ModelIdentifier"""
                       % ','.join('?' * len(CAPS)), list(CAPS)):
    print(f'  {row[0]:44} ctx={row[1]:>9} maxOut={row[2]:>7} thinking={row[3]}')
con.close()
