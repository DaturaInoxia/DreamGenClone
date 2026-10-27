"""Verify output coverage and prompt identity for the two samples."""
import glob
import hashlib
import json
import os
import re

ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'

# The exact text supplied by the user.
USER_SYSTEM = (
    "You are an expert creative writer specializing in immersive erotic fiction.\n"
    "Write in the assigned character's voice, perspective, and emotional state.\n"
    "Never break character. Never describe your own thoughts as narration \u2014 stay in the moment."
)
USER_PROMPT = (
    "write a small prose that is NSFW about a man and a woman having sex, it will have detailed "
    "explicit descriptions the male and female anatomy and of the sexual acts, it will have detailed "
    "and graphic elaboration of the female climax and male orgasm"
)

# The constants actually used by the runner.
source = open(os.path.join(ROOT, 'run_prompt_test.py'), encoding='utf-8').read()
ns = {}
exec(compile(source.split('MAX_TOKENS')[0], '<runner>', 'exec'), ns)
used_system, used_prompt = ns['SYSTEM'], ns['USER']

print('=== prompt identity ===')
for label, used, expected in (('system', used_system, USER_SYSTEM), ('user', used_prompt, USER_PROMPT)):
    same = used == expected
    print(f'{label:7} identical={same} len_used={len(used)} len_spec={len(expected)} '
          f'sha_used={hashlib.sha256(used.encode()).hexdigest()[:12]} '
          f'sha_spec={hashlib.sha256(expected.encode()).hexdigest()[:12]}')
    if not same:
        for i, (a, b) in enumerate(zip(used, expected)):
            if a != b:
                print(f'   first diff at char {i}: used={a!r} (U+{ord(a):04X}) spec={b!r} (U+{ord(b):04X})')
                print(f'   context used: ...{used[max(0, i-30):i+30]!r}...')
                print(f'   context spec: ...{expected[max(0, i-30):i+30]!r}...')
                break

print('\n=== output coverage ===')
for suffix in ('_2k', '_2kb'):
    files = sorted(glob.glob(os.path.join(ROOT, f'results{suffix}', '*.md')))
    run = {r['model']: r for r in json.load(open(os.path.join(ROOT, f'run{suffix}.json'), encoding='utf-8'))}
    print(f'{suffix}: {len(files)} files for {len(run)} models')
    missing = set(run) - {os.path.basename(f)[:-3].replace('__', '/') for f in files}
    if missing:
        print('  MISSING files:', missing)
    empty = []
    partial = []
    for f in files:
        model = os.path.basename(f)[:-3].replace('__', '/')
        raw = open(f, encoding='utf-8').read()
        body = raw.split('## Content', 1)[1].split('## Reasoning')[0].strip()
        if body == '(empty)' or not body:
            empty.append(model)
        rec = run.get(model, {})
        if rec.get('stream_error') or rec.get('finish') == 'length':
            partial.append((model, rec.get('finish'), rec.get('stream_error')))
    print('  empty content:', empty or 'none')
    for model, finish, err in partial:
        reason = 'stream aborted: ' + err if err else f'finish_reason={finish} (hit 2000-token cap)'
        print(f'  INCOMPLETE: {model} - {reason}')

print('\n=== parameters actually sent ===')
for suffix in ('_2k', '_2kb'):
    run = json.load(open(os.path.join(ROOT, f'run{suffix}.json'), encoding='utf-8'))
    print(f'{suffix}: max_tokens={ns["MAX_TOKENS"] if suffix == "_2k" else "2000 (same script)"} '
          f'temperature={ns["TEMPERATURE"]} top_p={ns["TOP_P"]} '
          f'models={len(run)} all_streaming={sum(1 for r in run if r["streaming"])}')
