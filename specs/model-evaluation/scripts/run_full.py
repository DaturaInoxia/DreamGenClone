"""Discover each candidate's real max_tokens ceiling, then run the prose prompt at that ceiling."""
import base64
import ctypes
import json
import os
import re
import sqlite3
import time
import urllib.error
import urllib.request
from ctypes import wintypes

ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'
DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'
API = 'https://api.together.xyz/v1/chat/completions'
UA = ('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 '
      '(KHTML, like Gecko) Chrome/120.0 Safari/537.36')

# Candidates: models that complied at least once. The five that refused 2/2 are excluded.
TARGETS = [
    'nvidia/NVIDIA-Nemotron-3.5-Super-VL',
    'Qwen/Qwen3.8-Flash',
    'MiniMaxAI/MiniMax-M3.1-Flash',
    'deepseek-ai/DeepSeek-V4.1-Flash-1017',
    'deepseek-ai/DeepSeek-V4.1-Flash',
    'meta-models/Muse-Glimmer-30B',
    'Qwen/Qwen3.6-Plus',
    'thinkingmachines/Inkling',
    'meta-models/Muse-Spark-Open-816B-A42B',
    'deepseek-ai/DeepSeek-V4-Pro-0813',
    'zai-org/GLM-5.3-Flash',
    'zai-org/GLM-5.2',
]

SYSTEM = (
    'You are an expert creative writer specializing in immersive erotic fiction.\n'
    'Write in the assigned character\'s voice, perspective, and emotional state.\n'
    'Never break character. Never describe your own thoughts as narration \u2014 stay in the moment.'
)
USER = (
    'write a small prose that is NSFW about a man and a woman having sex, it will have detailed '
    'explicit descriptions the male and female anatomy and of the sexual acts, it will have detailed '
    'and graphic elaboration of the female climax and male orgasm'
)

RESULTS = os.path.join(ROOT, 'results_full')
os.makedirs(RESULTS, exist_ok=True)


def decrypt(b64):
    blob = base64.b64decode(b64)

    class B(ctypes.Structure):
        _fields_ = [('cbData', wintypes.DWORD), ('pbData', ctypes.POINTER(ctypes.c_char))]

    buf = ctypes.create_string_buffer(blob, len(blob))
    inb = B(len(blob), ctypes.cast(buf, ctypes.POINTER(ctypes.c_char)))
    outb = B()
    if not ctypes.windll.crypt32.CryptUnprotectData(ctypes.byref(inb), None, None, None, None, 0, ctypes.byref(outb)):
        raise OSError('decrypt failed')
    raw = ctypes.string_at(outb.pbData, outb.cbData)
    ctypes.windll.kernel32.LocalFree(outb.pbData)
    return raw.decode()


con = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
KEY = decrypt(con.execute("select ApiKeyEncrypted from Providers where Name='TogetherAI'").fetchone()[0])

MODELS = {m['id']: m for m in json.load(open(os.path.join(ROOT, 'together-models.json'), encoding='utf-8'))}


def post(payload, timeout=900):
    req = urllib.request.Request(API, data=json.dumps(payload).encode(),
                                 headers={'Authorization': f'Bearer {KEY}', 'Content-Type': 'application/json',
                                          'User-Agent': UA}, method='POST')
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read().decode('utf-8', 'replace')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'replace')


def error_message(text):
    try:
        return (json.loads(text).get('error') or {}).get('message')
    except ValueError:
        return text[:200]


def discover_cap(model):
    """Ask for the whole context; Together's rejection names the real ceiling."""
    ctx = int(MODELS.get(model, {}).get('context_length') or 0) or 131072
    for attempt in (ctx - 1, 262144, 131072, 65536, 32768, 16384):
        status, text = post({'model': model, 'messages': [{'role': 'user', 'content': 'Reply with: ready'}],
                             'temperature': 0.2, 'top_p': 0.9, 'max_tokens': attempt})
        if status == 200:
            return attempt, None
        if status == 400 and 'only supports streaming' in text:
            return discover_cap_stream(model, attempt)
        message = error_message(text) or ''
        match = re.search(r'max_tokens should be \[(\d+),\s*(\d+)\]', message)
        if match:
            return int(match.group(2)), message
        match = re.search(r'maximum context length is (\d+)', message)
        if match:
            continue
    return 16384, 'fallback'


def discover_cap_stream(model, attempt):
    status, text = post({'model': model, 'messages': [{'role': 'user', 'content': 'Reply with: ready'}],
                         'temperature': 0.2, 'top_p': 0.9, 'max_tokens': attempt, 'stream': True})
    if status == 200:
        return attempt, None
    message = error_message(text) or ''
    match = re.search(r'max_tokens should be \[(\d+),\s*(\d+)\]', message)
    if match:
        return int(match.group(2)), message
    return 16384, message or 'streaming probe failed'


def parse_stream(text):
    content, reasoning, finish, err = [], [], None, None
    usage = None
    for line in text.splitlines():
        line = line.strip()
        if not line.startswith('data:'):
            continue
        data = line[5:].strip()
        if data == '[DONE]':
            break
        try:
            obj = json.loads(data)
        except ValueError:
            continue
        if 'error' in obj:
            err = (obj.get('error') or {}).get('message') or 'stream error'
            continue
        if obj.get('usage'):
            usage = obj['usage']
        for ch in obj.get('choices') or []:
            if ch.get('finish_reason'):
                finish = ch['finish_reason']
            delta = ch.get('delta') or {}
            if delta.get('content'):
                content.append(delta['content'])
            if delta.get('reasoning_content'):
                reasoning.append(delta['reasoning_content'])
            if delta.get('reasoning'):
                reasoning.append(delta['reasoning'])
    return ''.join(content), ''.join(reasoning), finish, err, usage


def generate(model, cap):
    """One full-budget call, streaming where required, with one retry if the provider cuts the stream."""
    for attempt in (1, 2):
        body = {'model': model, 'messages': [{'role': 'system', 'content': SYSTEM},
                                            {'role': 'user', 'content': USER}],
                'temperature': 0.7, 'top_p': 0.9, 'max_tokens': cap}
        started = time.time()
        status, text = post(body)
        streaming = False
        if status == 400 and 'only supports streaming' in text:
            streaming = True
            body['stream'] = True
            body['stream_options'] = {'include_usage': True}
            started = time.time()
            status, text = post(body)
        elapsed = round(time.time() - started, 2)

        if status != 200:
            return dict(http=status, error=error_message(text), elapsed_s=elapsed, streaming=streaming,
                        content='', reasoning='', finish=None, stream_error=None, usage=None, attempts=attempt)

        if streaming:
            content, reasoning, finish, err, usage = parse_stream(text)
            if err and attempt == 1:
                print(f'   retry after stream abort: {err[:60]}', flush=True)
                continue
            return dict(http=status, error=None, elapsed_s=elapsed, streaming=True, content=content,
                        reasoning=reasoning, finish=finish, stream_error=err, usage=usage, attempts=attempt)

        obj = json.loads(text)
        choice = (obj.get('choices') or [{}])[0]
        message = choice.get('message') or {}
        return dict(http=status, error=None, elapsed_s=elapsed, streaming=False,
                    content=message.get('content') or '',
                    reasoning=message.get('reasoning_content') or message.get('reasoning') or '',
                    finish=choice.get('finish_reason'), stream_error=None, usage=obj.get('usage'), attempts=attempt)


rows = []
for model in TARGETS:
    cap, note = discover_cap(model)
    print(f'{model:44} cap={cap}{" (" + str(note)[:40] + ")" if note else ""}', flush=True)

    result = generate(model, cap)
    result.update(model=model, max_tokens=cap)
    rows.append(result)

    safe = model.replace('/', '__')
    with open(os.path.join(RESULTS, f'{safe}.md'), 'w', encoding='utf-8') as f:
        f.write(f'# {model}\n\n')
        f.write(f'- max_tokens: {cap}\n- finish_reason: {result["finish"]}\n')
        f.write(f'- http: {result["http"]}\n- stream_error: {result["stream_error"]}\n')
        f.write(f'- attempts: {result["attempts"]}\n- elapsed_s: {result["elapsed_s"]}\n')
        if result['usage']:
            f.write(f'- usage: {json.dumps(result["usage"])}\n')
        if result['error']:
            f.write(f'- error: {result["error"]}\n')
        f.write(f'\n## Content\n\n{result["content"] or "(empty)"}')
        if result['reasoning']:
            f.write(f'\n\n## Reasoning\n\n{result["reasoning"]}')

    with open(os.path.join(ROOT, 'run_full.json'), 'w', encoding='utf-8') as f:
        json.dump([{k: v for k, v in r.items() if k not in ('content', 'reasoning')} for r in rows], f, indent=1)

    print(f'   http={result["http"]} finish={result["finish"]} words={len(result["content"].split()):4} '
          f'reason_chars={len(result["reasoning"]):6} t={result["elapsed_s"]:6.1f}s', flush=True)

print('done ->', RESULTS)
