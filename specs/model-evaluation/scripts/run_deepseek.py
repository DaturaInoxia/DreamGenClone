"""Run the identical prompt against the DeepSeek direct provider (api.deepseek.com)."""
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
UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36'

SYSTEM = ('You are an expert creative writer specializing in immersive erotic fiction.\n'
          "Write in the assigned character's voice, perspective, and emotional state.\n"
          'Never break character. Never describe your own thoughts as narration \u2014 stay in the moment.')
USER = ('write a small prose that is NSFW about a man and a woman having sex, it will have detailed '
        'explicit descriptions the male and female anatomy and of the sexual acts, it will have detailed '
        'and graphic elaboration of the female climax and male orgasm')

# Declared by the user from the DeepSeek platform page.
MODELS = [
    ('deepseek-flash', 'DeepSeek-V4.1-Flash-1017', 1048576, 393216),
    ('deepseek-v4-pro', 'DeepSeek-V4-Pro-0813', 1048576, 393216),
]

RESULTS = os.path.join(ROOT, 'results_ds')
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
row = con.execute("select BaseUrl, ChatCompletionsPath, ApiKeyEncrypted from Providers where Name='DeepSeek'").fetchone()
BASE, PATH, KEY = row[0].rstrip('/'), row[1], decrypt(row[2])
URL = BASE + PATH


def post(payload, timeout=1800):
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(),
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


def discover_cap(alias, declared):
    status, text = post({'model': alias, 'messages': [{'role': 'user', 'content': 'Reply with: ready'}],
                         'max_tokens': declared})
    if status == 200:
        return declared, None
    message = error_message(text) or ''
    match = re.search(r'max_tokens.{0,40}\[(\d+),\s*(\d+)\]', message)
    if match:
        return int(match.group(2)), message
    return declared, message


rows = []
for alias, version, ctx, declared in MODELS:
    cap, note = discover_cap(alias, declared)
    print(f'{alias:20} cap={cap}{" (" + str(note)[:60] + ")" if note else ""}', flush=True)

    body = {'model': alias,
            'messages': [{'role': 'system', 'content': SYSTEM}, {'role': 'user', 'content': USER}],
            'temperature': 0.7, 'top_p': 0.9, 'max_tokens': cap}
    started = time.time()
    status, text = post(body)
    elapsed = round(time.time() - started, 2)

    content = reasoning = ''
    finish = None
    usage = None
    error = None
    if status == 200:
        obj = json.loads(text)
        choice = (obj.get('choices') or [{}])[0]
        message = choice.get('message') or {}
        content = message.get('content') or ''
        reasoning = message.get('reasoning_content') or message.get('reasoning') or ''
        finish = choice.get('finish_reason')
        usage = obj.get('usage')
    else:
        error = error_message(text)

    rows.append(dict(model=alias, version=version, http=status, finish=finish, usage=usage,
                     content=content, reasoning=reasoning, elapsed=elapsed, max_tokens=cap,
                     error=error, ctx=ctx))

    with open(os.path.join(RESULTS, f'{alias}.md'), 'w', encoding='utf-8') as f:
        f.write(f'# {alias} ({version})\n\n')
        f.write(f'- provider: DeepSeek direct ({URL})\n- max_tokens: {cap}\n- finish_reason: {finish}\n')
        f.write(f'- http: {status}\n- elapsed_s: {elapsed}\n')
        if usage:
            f.write(f'- usage: {json.dumps(usage)}\n')
        if error:
            f.write(f'- error: {error}\n')
        f.write(f'\n## Content\n\n{content or "(empty)"}')
        if reasoning:
            f.write(f'\n\n## Reasoning\n\n{reasoning}')

    print(f'   http={status} finish={finish} words={len(content.split()):4} '
          f'reason_chars={len(reasoning):6} t={elapsed:6.1f}s usage={json.dumps(usage) if usage else None}', flush=True)

with open(os.path.join(ROOT, 'run_ds.json'), 'w', encoding='utf-8') as f:
    json.dump([{k: v for k, v in r.items() if k not in ('content', 'reasoning')} for r in rows], f, indent=1)
print('done ->', RESULTS)
