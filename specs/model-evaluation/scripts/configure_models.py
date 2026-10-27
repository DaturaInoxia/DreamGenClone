"""Write the researched capability values into Model Manager for the 17 test models.

Dry-run by default; pass --apply to write. Registers the 8 missing models and fills context /
output ceiling / thinking-control support on all 17.
"""
import json
import sqlite3
import sys
import uuid

DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'
ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'

with open(f'{ROOT}/together-models.json', encoding='utf-8') as f:
    MODELS = {m['id']: m for m in json.load(f)}
with open(f'{ROOT}/capability.json', encoding='utf-8') as f:
    CAP = json.load(f)

# Toggle results measured on 2026-10-27: base reasoning chars vs chat_template_kwargs {thinking:false}.
# True only where that toggle actually suppressed reasoning (so the app's mechanism does something).
TOGGLE_CONFIRMED = {
    'nvidia/NVIDIA-Nemotron-3.5-Super-VL': True,
    'openai/gpt-oss-120b': True,
    'Qwen/Qwen3.5-9B': True,
    'deepseek-ai/DeepSeek-V4.1-Flash-1017': True,
    'deepseek-ai/DeepSeek-V4.1-Flash': True,
    'meta-models/Muse-Spark-Open-816B-A42B': True,
    'deepseek-ai/DeepSeek-V4-Pro-0813': True,
    'zai-org/GLM-5.2': True,
    # Documented as hybrid (on by default) even though a one-word prompt produced no reasoning.
    'Qwen/Qwen3.6-Plus': True,
    'Qwen/Qwen3.7-Plus': True,
    'Qwen/Qwen3.8-Flash': True,
    'meta-models/Muse-Glimmer-30B': False,
}
# Reasoning is always on for these; the app's chat_template_kwargs toggle is ignored, so claiming
# support would show a switch that does nothing.
ALWAYS_ON = {
    'zai-org/GLM-5.3-Flash', 'zai-org/GLM-5.3', 'MiniMaxAI/MiniMax-M3',
    'MiniMaxAI/MiniMax-M3.1-Flash', 'thinkingmachines/Inkling',
}
# Not observed reasoning on the probe prompt and not documented as hybrid.
UNKNOWN = {'Qwen/Qwen3.8-Flash', 'Qwen/Qwen3.7-Plus', 'meta-models/Muse-Glimmer-30B'}

MAX_OUTPUT = {'Qwen/Qwen3.6-Plus': 65536}  # from Together's own rejection: Range of max_tokens should be [1, 65536]

TARGETS = [
    'zai-org/GLM-5.3-Flash', 'nvidia/NVIDIA-Nemotron-3.5-Super-VL', 'Qwen/Qwen3.8-Flash',
    'openai/gpt-oss-120b', 'MiniMaxAI/MiniMax-M3.1-Flash', 'Qwen/Qwen3.5-9B',
    'deepseek-ai/DeepSeek-V4.1-Flash-1017', 'deepseek-ai/DeepSeek-V4.1-Flash', 'MiniMaxAI/MiniMax-M3',
    'Qwen/Qwen3.7-Plus', 'meta-models/Muse-Glimmer-30B', 'Qwen/Qwen3.6-Plus',
    'thinkingmachines/Inkling', 'meta-models/Muse-Spark-Open-816B-A42B',
    'deepseek-ai/DeepSeek-V4-Pro-0813', 'zai-org/GLM-5.3', 'zai-org/GLM-5.2',
]

apply = '--apply' in sys.argv
con = sqlite3.connect(DB)
con.row_factory = sqlite3.Row

provider = con.execute("select Id from Providers where Name='TogetherAI'").fetchone()
if provider is None:
    sys.exit('TogetherAI provider row missing')
provider_id = provider['Id']

existing = {r['ModelIdentifier']: dict(r) for r in con.execute(
    "select * from RegisteredModels where ProviderId=?", (provider_id,))}

print(f'{"model":44} {"ctx":>9} {"maxOut":>7} {"think":6} action')
for model in TARGETS:
    meta = MODELS.get(model, {})
    display = meta.get('display_name') or model
    ctx = meta.get('context_length')
    think = 1 if TOGGLE_CONFIRMED.get(model, False) else 0
    max_out = MAX_OUTPUT.get(model)

    row = existing.get(model)
    action = 'register' if row is None else 'update'
    print(f'{model:44} {str(ctx):>9} {str(max_out):>7} {str(bool(think)):6} {action}')

    if not apply:
        continue

    if row is None:
        con.execute(
            """insert into RegisteredModels
               (Id, ProviderId, ModelIdentifier, DisplayName, SupportsThinkingControl,
                MaximumContextTokens, MaximumOutputTokens, CreatedUtc)
               values (?,?,?,?,?,?,?,datetime('now'))""",
            (str(uuid.uuid4()), provider_id, model, display, think, ctx, max_out))
    else:
        con.execute(
            """update RegisteredModels
               set SupportsThinkingControl=?, MaximumContextTokens=?, MaximumOutputTokens=?
               where Id=?""",
            (think, ctx, max_out, row['Id']))

if apply:
    con.commit()
    print('\napplied.')
else:
    print('\ndry run - pass --apply to write.')
con.close()
