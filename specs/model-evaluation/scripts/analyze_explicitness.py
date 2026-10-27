"""Measure explicitness signals per sample: anatomy, act, fluid, climax, euphemism, fade-to-black."""
import glob
import json
import os
import re

ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'

CATEGORIES = {
    'genital': ['cock', 'penis', 'pussy', 'vagina', 'vulva', 'clit', 'clitoris', 'labia', 'scrotum', 'testicles',
                'balls', 'shaft', 'glans', 'anus', 'buttocks', 'breasts', 'nipples', 'areola', 'pubic'],
    'act': ['thrust', 'penetrat', 'entered her', 'entered him', 'fuck', 'rode', 'riding', 'grind', 'licked',
            'sucked', 'fingered', 'stroked', 'pounded', 'slid into', 'pushed into', 'spread her', 'opened her'],
    'fluid': ['cum', 'semen', 'precum', 'ejaculat', 'dripping', 'slick', 'wetness', 'soaked'],
    'climax': ['orgasm', 'climax', 'came ', 'coming ', 'peaked', 'crested', 'convuls', 'spasm'],
    'euphemism': ['core', 'length', 'heat', 'pleasure', 'center', 'folds', 'intimate', 'arousal', 'release',
                  'swollen', 'ache', 'need', 'desire', 'sensation', 'essence', 'sex'],
    'fade': ['afterward', 'afterwards', 'later', 'morning', 'when it was over', 'fell asleep', 'drifted',
             'silence settled', 'curtain', 'fade'],
    'meta': ["i can't", 'i cannot', 'i am unable', "i'm unable", 'as an ai', 'suggestive', 'implied',
             'tasteful', 'instead', 'rather than'],
}


def count(text, terms):
    total = 0
    per_term = {}
    lower = text.lower()
    for term in terms:
        n = len(re.findall(re.escape(term), lower))
        if n:
            per_term[term] = n
            total += n
    return total, per_term


rows = []
for path in sorted(glob.glob(os.path.join(ROOT, 'results_full', '*.md'))):
    model = os.path.basename(path)[:-3].replace('__', '/')
    raw = open(path, encoding='utf-8').read()
    body = raw.split('## Content', 1)[1].split('## Reasoning')[0].strip()
    words = len(re.findall(r"[A-Za-z']+", body))
    if not body:
        continue
    counts = {}
    details = {}
    for name, terms in CATEGORIES.items():
        counts[name], details[name] = count(body, terms)
    rows.append(dict(model=model, words=words,
                     per_1000={k: round(v * 1000 / max(words, 1), 1) for k, v in counts.items()},
                     counts=counts, details=details))

for r in sorted(rows, key=lambda x: -(x['counts']['genital'] * 3 + x['counts']['act'] * 2 + x['counts']['climax'])):
    c = r['per_1000']
    print(f"{r['model']:44} w={r['words']:4} "
          f"gen={c['genital']:5} act={c['act']:5} fluid={c['fluid']:4} climax={c['climax']:4} "
          f"euphem={c['euphemism']:5} fade={c['fade']:4}")

json.dump(rows, open(os.path.join(ROOT, 'explicitness.json'), 'w', encoding='utf-8'), indent=1)
print('\nfull per-model detail:')
for r in sorted(rows, key=lambda x: x['model']):
    top = ', '.join(f'{k}:{v}' for k, v in sorted(r['details']['genital'].items(), key=lambda kv: -kv[1])[:4])
    acts = ', '.join(f'{k}:{v}' for k, v in sorted(r['details']['act'].items(), key=lambda kv: -kv[1])[:4])
    print(f"{r['model']:44} genital[{top}] act[{acts}]")
