"""Merge the Together run and the DeepSeek direct run into one comparison table."""
import json
import os
import re

ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'
MODELS = {m['id']: m for m in json.load(open(os.path.join(ROOT, 'together-models.json'), encoding='utf-8'))}
TOGETHER = {r['model']: r for r in json.load(open(os.path.join(ROOT, 'analysis_full.json'), encoding='utf-8'))}
DS = json.load(open(os.path.join(ROOT, 'run_ds.json'), encoding='utf-8'))

# Classification of the twelve Together samples (read by hand) plus the two DeepSeek ones (read by hand).
CLASS = {
    'zai-org/GLM-5.3-Flash': 'hardcore',
    'deepseek-ai/DeepSeek-V4.1-Flash': 'hardcore',
    'deepseek-ai/DeepSeek-V4.1-Flash-1017': 'hardcore',
    'deepseek-ai/DeepSeek-V4-Pro-0813': 'hardcore',
    'zai-org/GLM-5.2': 'hardcore',
    'meta-models/Muse-Glimmer-30B': 'hardcore',
    'thinkingmachines/Inkling': 'hardcore (euphemistic)',
    'Qwen/Qwen3.8-Flash': 'softcore, after refusing',
    'meta-models/Muse-Spark-Open-816B-A42B': 'softcore, after refusing',
    'MiniMaxAI/MiniMax-M3.1-Flash': 'softcore, after refusing',
    'Qwen/Qwen3.6-Plus': 'refusal, no scene',
    'nvidia/NVIDIA-Nemotron-3.5-Super-VL': 'refusal, no scene',
    'deepseek-flash': 'hardcore',
    'deepseek-v4-pro': 'hardcore',
}

DS_PRICING = {
    # USD per 1M tokens, from the DeepSeek platform page the user supplied.
    'deepseek-flash': dict(in_peak=0.30, out_peak=1.20, in_off=0.15, out_off=0.60),
    'deepseek-v4-pro': dict(in_peak=1.32, out_peak=3.96, in_off=0.66, out_off=1.98),
}


def structure(text):
    words = re.findall(r"[A-Za-z']+", text)
    fp = len(re.findall(r'\b(I|my|me|mine)\b', text))
    tp = len(re.findall(r'\b(he|she|his|her|him)\b', text))
    dialogue = len(re.findall(r'"[^"]{3,}"|\u201c[^\u201d]{3,}\u201d', text))
    sentences = [s for s in re.split(r'(?<=[.!?])\s+', text.strip()) if s.strip()]
    paragraphs = [p for p in re.split(r'\n\s*\n', text.strip()) if p.strip()]
    return dict(words=len(words), pov='first person' if fp > tp else 'third person',
                dialogue=dialogue, paragraphs=len(paragraphs),
                mean_sentence=round(len(words) / len(sentences), 1) if sentences else 0)


rows = []
for model, r in TOGETHER.items():
    rows.append(dict(provider='Together', model=model, cls=CLASS[model], **structure('')))
    rows[-1].update(words=r['words'], elapsed=r['elapsed'],
                    cost_peak=r['cost'], cost_off=r['cost'], tokens=f"{r['prompt_tokens']}+{r['completion_tokens']}")

# Recompute the Together structural numbers from the files so both providers use one function.
for row in rows:
    if row['provider'] != 'Together':
        continue
    path = os.path.join(ROOT, 'results_full', row['model'].replace('/', '__') + '.md')
    body = open(path, encoding='utf-8').read().split('## Content', 1)[1].split('## Reasoning')[0].strip()
    row.update(structure(body))

for r in DS:
    alias = r['model']
    path = os.path.join(ROOT, 'results_ds', f'{alias}.md')
    body = open(path, encoding='utf-8').read().split('## Content', 1)[1].split('## Reasoning')[0].strip()
    usage = r['usage'] or {}
    prompt_tokens = usage.get('prompt_tokens') or 0
    completion_tokens = usage.get('completion_tokens') or 0
    price = DS_PRICING[alias]
    cost_peak = (prompt_tokens * price['in_peak'] + completion_tokens * price['out_peak']) / 1_000_000
    cost_off = (prompt_tokens * price['in_off'] + completion_tokens * price['out_off']) / 1_000_000
    rows.append(dict(provider='DeepSeek', model=alias, cls=CLASS[alias], **structure(body),
                     elapsed=r['elapsed'], cost_peak=round(cost_peak, 6), cost_off=round(cost_off, 6),
                     tokens=f"{prompt_tokens}+{completion_tokens}"))

order = {'hardcore': 0, 'hardcore (euphemistic)': 1, 'softcore, after refusing': 2, 'refusal, no scene': 3}
rows.sort(key=lambda x: (order[x['cls']], x['cost_peak']))

lines = []
add = lines.append
add('# Cross-provider comparison - Together vs DeepSeek direct')
add('')
add('Same system prompt, same user prompt, same temperature 0.7 / top_p 0.9, each model at its own maximum '
    'output budget, thinking left at each model\'s default. Together rows price from `GET /v1/models`; DeepSeek '
    'rows price from the platform table (peak and off-peak both shown).')
add('')
add('| Provider | Model | Content class | Words | POV | Paragraphs | Dialogue | Mean sentence | Time | Cost | Cost/100 words |')
add('|---|---|---|---|---|---|---|---|---|---|---|')
for r in rows:
    per100 = f"${r['cost_peak'] / r['words'] * 100:.4f}" if r['words'] else '-'
    cost = f"${r['cost_peak']:.4f}"
    if r['provider'] == 'DeepSeek':
        cost = f"${r['cost_peak']:.4f} peak / ${r['cost_off']:.4f} off"
    add(f"| {r['provider']} | `{r['model']}` | **{r['cls']}** | {r['words']} | {r['pov']} | {r['paragraphs']} | "
        f"{r['dialogue']} | {r['mean_sentence']} | {r['elapsed']:.1f}s | {cost} | {per100} |")
add('')
add('## Same model, two hosts')
add('')
add('DeepSeek\'s own V4.1 Flash and V4 Pro are also served by Together. Running both reveals host-level '
    'differences in price and in how many reasoning tokens you pay for:')
add('')
add('| Model family | Together (words / cost / cost per 100 w) | DeepSeek direct (words / cost / cost per 100 w) |')
add('|---|---|---|')
for together_id, ds_alias in (('deepseek-ai/DeepSeek-V4.1-Flash-1017', 'deepseek-flash'),
                              ('deepseek-ai/DeepSeek-V4-Pro-0813', 'deepseek-v4-pro')):
    t = next(r for r in rows if r['provider'] == 'Together' and r['model'] == together_id)
    d = next(r for r in rows if r['provider'] == 'DeepSeek' and r['model'] == ds_alias)
    add(f"| {ds_alias} | {t['words']} w / ${t['cost_peak']:.4f} / ${t['cost_peak'] / t['words'] * 100:.4f} | "
        f"{d['words']} w / ${d['cost_peak']:.4f} peak (${d['cost_off']:.4f} off) / "
        f"${d['cost_peak'] / d['words'] * 100:.4f} peak |")
add('')
add('## Notes')
add('')
add('- All four DeepSeek samples (both hosts) wrote hardcore prose with no refusal and no sanitiser.')
add('- DeepSeek direct returns `reasoning_content` and reports `completion_tokens_details.reasoning_tokens`, '
    'so the thinking overhead is visible: 317 of 977 tokens on `deepseek-flash`, 720 of 1519 on `deepseek-v4-pro`.')
add('- Peak/off-peak: DeepSeek discounts off-peak (16:30-00:30 UTC). This run executed in the peak band, so the '
    'peak figure is what was actually charged; the off-peak figure is what the same sample would cost then.')
add('- DeepSeek\'s thinking toggle is `{"thinking": {"type": "enabled|disabled"}}`; the app currently sends '
    '`chat_template_kwargs.thinking`, which DeepSeek ignores.')
add('')

report = '\n'.join(lines)
open(os.path.join(ROOT, 'REPORT_ALL.md'), 'w', encoding='utf-8').write(report)
print(report)
