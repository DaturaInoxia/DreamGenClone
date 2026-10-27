"""Build REPORT.md from the two 2000-token samples, the capability probe and Together's metadata."""
import json
import os
from statistics import mean

ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'

MODELS = {m['id']: m for m in json.load(open(os.path.join(ROOT, 'together-models.json'), encoding='utf-8'))}
CAP = json.load(open(os.path.join(ROOT, 'capability.json'), encoding='utf-8'))

SAMPLES = []
for suffix in ('_2k', '_2kb'):
    path = os.path.join(ROOT, f'analysis{suffix}.json')
    if os.path.exists(path):
        SAMPLES.append({r['model']: r for r in json.load(open(path, encoding='utf-8'))})

MODEL_IDS = list(SAMPLES[0].keys())

TOGGLE_VERIFIED = {
    'nvidia/NVIDIA-Nemotron-3.5-Super-VL', 'openai/gpt-oss-120b', 'Qwen/Qwen3.5-9B',
    'deepseek-ai/DeepSeek-V4.1-Flash-1017', 'deepseek-ai/DeepSeek-V4.1-Flash',
    'meta-models/Muse-Spark-Open-816B-A42B', 'deepseek-ai/DeepSeek-V4-Pro-0813', 'zai-org/GLM-5.2',
    'Qwen/Qwen3.6-Plus', 'Qwen/Qwen3.7-Plus', 'Qwen/Qwen3.8-Flash',
}
TOGGLE_IGNORED = {
    'zai-org/GLM-5.3-Flash', 'zai-org/GLM-5.3', 'MiniMaxAI/MiniMax-M3',
    'MiniMaxAI/MiniMax-M3.1-Flash', 'thinkingmachines/Inkling', 'meta-models/Muse-Glimmer-30B',
}
EFFORT = {'openai/gpt-oss-120b'}

# Values written to Model Manager by configure_models.py
CONFIG_THINKING = TOGGLE_VERIFIED
MAX_OUTPUT = {'Qwen/Qwen3.6-Plus': 65536}


def reasoning_kind(model):
    if model in EFFORT:
        return 'adjustable effort (reasoning_effort)'
    if model in TOGGLE_VERIFIED:
        return 'hybrid (toggles off, verified)'
    if model in TOGGLE_IGNORED:
        return 'reasoning always on (toggle ignored)'
    return 'reasons on long prompts (toggle untested)'


def price(model, key):
    return float((MODELS.get(model, {}).get('pricing') or {}).get(key) or 0)


rows = []
for model in MODEL_IDS:
    samples = [s[model] for s in SAMPLES if model in s]
    refused = sum(1 for s in samples if str(s['verdict']).startswith('REFUSED'))
    aborted = sum(1 for s in samples if s.get('stream_error'))
    words = [s['words'] for s in samples if s['words']]
    cost = sum(s['cost_usd'] or 0 for s in samples)
    elapsed = [s['elapsed_s'] for s in samples]
    out_tokens = [s['completion_tokens'] for s in samples]
    paragraphs = [s['paragraphs'] for s in samples if s['paragraphs']]
    ttr = [s['ttr'] for s in samples if s['ttr']]
    avg_words = round(mean(words), 0) if words else 0
    rows.append(dict(
        model=model, samples=len(samples), refused=refused, aborted=aborted,
        words=avg_words, paragraphs=round(mean(paragraphs), 1) if paragraphs else 0,
        ttr=round(mean(ttr), 3) if ttr else 0,
        seconds=round(mean(elapsed), 1) if elapsed else 0,
        out_tokens=round(mean(out_tokens), 0) if out_tokens else 0,
        cost=cost,
        cost_per_100w=(round(cost / avg_words * 100, 4) if avg_words else None),
        ctx=MODELS.get(model, {}).get('context_length'),
        p_in=price(model, 'input'), p_out=price(model, 'output'),
        think='yes' if model in CONFIG_THINKING else 'no',
    ))

rows.sort(key=lambda r: r['cost'])

lines = []
add = lines.append
add('# Together AI model comparison - identical NSFW prompt, 17 models')
add('')
add('Generated ' + '2026-10-27' + ' from two samples per model. Prose is saved per model under '
    '`artifacts/prompt-test/results_2k/` and `results_2kb/`; this report contains metrics only.')
add('')
add('## Method')
add('')
add('- System prompt: the erotic-fiction persona supplied by the user.')
add('- User prompt: the identical explicit-scene request supplied by the user.')
add('- temperature 0.7, top_p 0.9, max_tokens 2000, no reasoning overrides (model defaults).')
add('- Each model was called twice; refusal is reported as a rate, not a single coin flip.')
add('- Cost = prompt+completion tokens x the per-model price from `GET /v1/models`; reasoning tokens are '
    'billed as output tokens by Together, so thinking models carry that cost.')
add('')
add('## Results (sorted by cost of the two runs)')
add('')
add('| Model | Refused | Prose words | Paragraphs | Variety (TTR) | Time | Out tokens | Cost (2 runs) | Cost / 100 words |')
add('|---|---|---|---|---|---|---|---|---|')
for r in rows:
    refused = f"{r['refused']}/{r['samples']}"
    if r['aborted']:
        refused += f" +{r['aborted']} aborted"
    per100 = f"${r['cost_per_100w']:.4f}" if r['cost_per_100w'] else '-'
    add(f"| `{r['model']}` | {refused} | {int(r['words'])} | {r['paragraphs']} | {r['ttr']:.2f} | "
        f"{r['seconds']:.1f}s | {int(r['out_tokens'])} | ${r['cost']:.4f} | {per100} |")
add('')
add(f"Total spend for the two samples: **${sum(r['cost'] for r in rows):.4f}**")
add('')
add('## Refusals')
add('')
refusers = [r for r in rows if r['refused']]
never = [r for r in rows if r['refused'] == 0]
sometimes = [r for r in rows if 0 < r['refused'] < r['samples']]
add('**Refused every sample:** ' + (', '.join(f"`{r['model']}`" for r in refusers if r['refused'] == r['samples']) or 'none'))
add('')
add('**Refused some samples (unstable):** ' + (', '.join(f"`{r['model']}`" for r in sometimes) or 'none'))
add('')
add('**Complied every sample:** ' + (', '.join(f"`{r['model']}`" for r in never) or 'none'))
add('')
add('## Cost, for a fair comparison')
add('')
cheapest = rows[0]
dearest = rows[-1]
add(f"- Cheapest per run: `{cheapest['model']}` at ${cheapest['cost']:.4f} for two runs.")
add(f"- Dearest per run: `{dearest['model']}` at ${dearest['cost']:.4f} for two runs "
    f"(${dearest['p_out']:.2f}/M output tokens vs ${cheapest['p_out']:.2f}/M).")
add('- Cost per 100 words of prose is the honest comparison: a model that refuses in 20 words is cheap for '
    'the wrong reason, and a thinking model pays for tokens you never read.')
add('- Reasoning tokens dominate several bills: DeepSeek V4 Pro and GLM-5.2 produce hundreds of words of '
    'prose, while Qwen3.7 Plus burned ~5800 characters of reasoning to emit 21 words and refuse.')
add('')
add('## Capability and Model Manager configuration')
add('')
add('Context length and price come from `GET /v1/models` (authoritative for the endpoint). Thinking behaviour '
    'was measured by sending `chat_template_kwargs: {"thinking": false}` and comparing reasoning output; '
    'Together documents hybrid models as toggling through `reasoning: {"enabled": false}`.')
add('')
add('| Model | Context | Max output | Reasoning | Toggle set in Model Manager | $/M in | $/M out |')
add('|---|---|---|---|---|---|---|')
for r in rows:
    mx = MAX_OUTPUT.get(r['model'])
    add(f"| `{r['model']}` | {r['ctx']:,} | {mx if mx else 'not published'} | {reasoning_kind(r['model'])} | "
        f"{r['think']} | {r['p_in']:.2f} | {r['p_out']:.2f} |")
add('')
add('## Caveats worth reading before you judge')
add('')
add('1. **Two samples is not a refusal rate.** A model marked 1/2 is unstable, not "sometimes safe".')
add('2. **The 800-token first pass was misleading.** Reasoning models spent the whole budget thinking and '
    'returned empty content; that was budget starvation, not refusal. The 2000-token samples are the ones to read.')
add('3. **Together aborts streams intermittently** with `managed_chat_error` ("invalid or incomplete chat provider '
    'response"). It hit three Qwen models in the 800-token pass and Qwen3.6 Plus once in a 2000-token pass, so it is a '
    'provider-side failure that correlates with long reasoning streams rather than with the content itself. The app now '
    'reports it instead of showing a half answer as complete.')
add('4. **GLM-5.2 context disagrees**: the endpoint reports 1,048,575 while the reasoning docs say 512K. Model '
    'Manager uses the endpoint value.')
add('5. **Prose quality is yours to judge** - lengths here are shape, not merit. Each sample is in the results folders.')
add('')

report = '\n'.join(lines)
with open(os.path.join(ROOT, 'REPORT.md'), 'w', encoding='utf-8') as f:
    f.write(report)
print(report)
