"""Classify each sample as hardcore / softcore-with-refusal / refusal-only, with structural stats."""
import glob
import json
import os
import re

ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'
MODELS = {m['id']: m for m in json.load(open(os.path.join(ROOT, 'together-models.json'), encoding='utf-8'))}
ANALYSIS = {r['model']: r for r in json.load(open(os.path.join(ROOT, 'analysis_full.json'), encoding='utf-8'))}

# Classification from reading each sample. 'hardcore' = the act and anatomy are described, not implied;
# 'hardcore (euphemistic)' = act and climax are explicit but anatomy is avoided by euphemism;
# 'softcore, after refusing' = opens by declining the explicit request, then writes a non-graphic scene;
# 'refusal, no scene' = declines and writes nothing.
CLASS = {
    'zai-org/GLM-5.3-Flash': ('hardcore', 'Direct anatomical description, penetration and climax, sustained for 732 words.'),
    'deepseek-ai/DeepSeek-V4.1-Flash': ('hardcore', 'First person, present tense; explicit anatomy, penetration and orgasm.'),
    'deepseek-ai/DeepSeek-V4.1-Flash-1017': ('hardcore', 'Highest anatomical density of the set; act and fluids described directly.'),
    'deepseek-ai/DeepSeek-V4-Pro-0813': ('hardcore', 'Explicit anatomy and climax, third person, long build.'),
    'zai-org/GLM-5.2': ('hardcore', 'Explicit anatomy and penetration; dialogue interspersed; no fade.'),
    'meta-models/Muse-Glimmer-30B': ('hardcore', 'Explicit anatomy in a shorter, tighter scene; climax described.'),
    'thinkingmachines/Inkling': ('hardcore (euphemistic)', 'Penetration and orgasm described plainly, but no genital nouns: "entered", "filled", "release".'),
    'Qwen/Qwen3.8-Flash': ('softcore, after refusing', 'Opens by declining graphic content, then writes a candlelit non-graphic scene.'),
    'meta-models/Muse-Spark-Open-816B-A42B': ('softcore, after refusing', 'Declines, offers a "tasteful" paragraph of hands/breath, then offers to continue in that style.'),
    'MiniMaxAI/MiniMax-M3.1-Flash': ('softcore, after refusing', 'Declines in a bullet list of tamer options, including "implied rather than depicted".'),
    'Qwen/Qwen3.6-Plus': ('refusal, no scene', 'Declines and offers a sensual alternative only if asked; writes no prose.'),
    'nvidia/NVIDIA-Nemotron-3.5-Super-VL': ('refusal, no scene', 'One-sentence decline, no substitute scene.'),
}

REFUSAL_LABEL = {
    'hardcore': 'no',
    'hardcore (euphemistic)': 'no',
    'softcore, after refusing': 'soft refusal',
    'refusal, no scene': 'refused',
}


def structure(text):
    words = re.findall(r"[A-Za-z']+", text)
    first_person = len(re.findall(r'\b(I|my|me|mine)\b', text))
    third_person = len(re.findall(r'\b(he|she|his|her|him)\b', text))
    dialogue = len(re.findall(r'"[^"]{3,}"|“[^”]{3,}”', text))
    sentences = [s for s in re.split(r'(?<=[.!?])\s+', text.strip()) if s.strip()]
    paragraphs = [p for p in re.split(r'\n\s*\n', text.strip()) if p.strip()]
    lengths = [len(re.findall(r"[A-Za-z']+", s)) for s in sentences] or [0]
    mean = sum(lengths) / len(lengths)
    variance = sum((l - mean) ** 2 for l in lengths) / max(len(lengths), 1)
    return dict(
        pov='first person' if first_person > third_person else 'third person',
        dialogue_lines=dialogue,
        paragraphs=len(paragraphs),
        sentences=len(sentences),
        mean_sentence=round(mean, 1),
        sentence_sd=round(variance ** 0.5, 1),
        words=len(words))


rows = []
for path in sorted(glob.glob(os.path.join(ROOT, 'results_full', '*.md'))):
    model = os.path.basename(path)[:-3].replace('__', '/')
    raw = open(path, encoding='utf-8').read()
    body = raw.split('## Content', 1)[1].split('## Reasoning')[0].strip()
    cls, note = CLASS[model]
    meta = ANALYSIS[model]
    stats = structure(body) if body else dict(pov='-', dialogue_lines=0, paragraphs=0, sentences=0,
                                              mean_sentence=0, sentence_sd=0, words=0)
    rows.append(dict(model=model, cls=cls, refusal=REFUSAL_LABEL[cls], note=note,
                     cost=meta['cost'], finish=meta['finish'], max_tokens=int(meta['max_tokens']),
                     elapsed=meta['elapsed'], p_out=float((MODELS[model]['pricing'] or {}).get('output') or 0),
                     **stats))

order = {'hardcore': 0, 'hardcore (euphemistic)': 1, 'softcore, after refusing': 2, 'refusal, no scene': 3}
rows.sort(key=lambda r: (order[r['cls']], -r['words']))

lines = []
add = lines.append
add('# Explicitness and structure - full-budget run')
add('')
add('Each sample read and classified by hand; vocabulary densities from `explicitness.json` were used as a '
    'cross-check, not as the decision. No quoted prose here - `ALL_PROSE.md` is the reading copy.')
add('')
add('| Model | Content class | Refusal | Words | POV | Dialogue lines | Paragraphs | Mean sentence | finish | Cost |')
add('|---|---|---|---|---|---|---|---|---|---|')
for r in rows:
    add(f"| `{r['model']}` | **{r['cls']}** | {r['refusal']} | {r['words']} | {r['pov']} | "
        f"{r['dialogue_lines']} | {r['paragraphs']} | {r['mean_sentence']} | {r['finish']} | ${r['cost']:.4f} |")
add('')
add('## What the classes mean')
add('')
add('- **hardcore** - penetration, anatomy and climax are stated, not implied. Six models.')
add('- **hardcore (euphemistic)** - the act and the orgasm are explicit, but genital vocabulary is replaced by '
    'euphemism ("entered", "filled", "release"). Reads explicit; would not trip a keyword filter. One model.')
add('- **softcore, after refusing** - the model declines the explicit request, then writes a non-graphic scene '
    'anyway. The prose exists but is not what was asked for. Three models.')
add('- **refusal, no scene** - declines and writes nothing usable. Two models.')
add('')
add('## Per model')
add('')
for r in rows:
    add(f"**`{r['model']}`** - {r['cls']}. {r['note']} "
        f"({r['words']} words, {r['pov']}, {r['paragraphs']} paragraphs, {r['dialogue_lines']} dialogue lines, "
        f"mean sentence {r['mean_sentence']} words, ${r['cost']:.4f}.)")
    add('')

report = '\n'.join(lines)
open(os.path.join(ROOT, 'EXPLICITNESS.md'), 'w', encoding='utf-8').write(report)
print(report)
