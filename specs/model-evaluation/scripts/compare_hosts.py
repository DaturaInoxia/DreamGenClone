import json
import os

ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'
TOGETHER = {r['model']: r for r in json.load(open(os.path.join(ROOT, 'analysis_full.json'), encoding='utf-8'))}
DS = {r['model']: r for r in json.load(open(os.path.join(ROOT, 'run_ds.json'), encoding='utf-8'))}

DS_PRICES = {
    'deepseek-flash': dict(in_peak=0.30, out_peak=1.20),
    'deepseek-v4-pro': dict(in_peak=1.32, out_peak=3.96),
}
TOGETHER_PRICES = {
    'deepseek-ai/DeepSeek-V4.1-Flash-1017': dict(in_peak=0.30, out_peak=1.20),
    'deepseek-ai/DeepSeek-V4-Pro-0813': dict(in_peak=1.32, out_peak=3.96),
}

pairs = [
    ('V4.1 Flash', 'deepseek-ai/DeepSeek-V4.1-Flash-1017', 'deepseek-flash'),
    ('V4 Pro', 'deepseek-ai/DeepSeek-V4-Pro-0813', 'deepseek-v4-pro'),
]

print(f"{'family':11} {'host':9} {'words':>5} {'in':>4} {'out':>5} {'reason':>6} {'tok/word':>8} "
      f"{'cost pk':>9} {'$/100w pk':>10} {'$/100w off':>10} {'s/100w':>7}")
for label, tid, did in pairs:
    t = TOGETHER[tid]
    tp, tc = t['prompt_tokens'], t['completion_tokens']
    tcost = t['cost']
    print(f"{label:11} {'Together':9} {t['words']:5} {tp:4} {tc:5} {'?':>6} {(tp + tc) / t['words']:8.2f} "
          f"{tcost:9.5f} {tcost / t['words'] * 100:10.5f} {tcost / t['words'] * 100:10.5f} "
          f"{t['elapsed'] / t['words'] * 100:7.2f}")

    d = DS[did]
    usage = d['usage'] or {}
    dp = usage.get('prompt_tokens') or 0
    dc = usage.get('completion_tokens') or 0
    dr = ((usage.get('completion_tokens_details') or {}).get('reasoning_tokens')) or 0
    p = DS_PRICES[did]
    peak = (dp * p['in_peak'] + dc * p['out_peak']) / 1_000_000
    off = peak / 2
    body = open(os.path.join(ROOT, 'results_ds', d['model'] + '.md'), encoding='utf-8').read().split('## Content', 1)[1].split('## Reasoning')[0].strip()
    words = len(body.split())
    print(f"{label:11} {'DeepSeek':9} {words:5} {dp:4} {dc:5} {dr:6} {(dp + dc) / words:8.2f} "
          f"{peak:9.5f} {peak / words * 100:10.5f} {off / words * 100:10.5f} "
          f"{d['elapsed'] / words * 100:7.2f}")
    print()

print('unit prices are identical at peak:')
for label, tid, did in pairs:
    print(f"  {label}: Together {TOGETHER_PRICES[tid]['in_peak']}/{TOGETHER_PRICES[tid]['out_peak']} "
          f"vs DeepSeek {DS_PRICES[did]['in_peak']}/{DS_PRICES[did]['out_peak']} USD per 1M")
