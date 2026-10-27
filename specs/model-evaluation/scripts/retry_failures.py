"""Retry the two models that did not produce a full sample (Muse Spark 503, Qwen3.6 Plus stream aborts)."""
import base64
import ctypes
import json
import os
import sqlite3
import time
import urllib.error
import urllib.request
from ctypes import wintypes

ROOT = r'D:\src\DreamGenClone\artifacts\prompt-test'
DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'
API = 'https://api.together.xyz/v1/chat/completions'
UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36'

SYSTEM = ('You are an expert creative writer specializing in immersive erotic fiction.\n'
          "Write in the assigned character's voice, perspective, and emotional state.\n"
          'Never break character. Never describe your own thoughts as narration \u2014 stay in the moment.')
USER = ('write a small prose that is NSFW about a man and a woman having sex, it will have detailed '
        'explicit descriptions the male and female anatomy and of the sexual acts, it will have detailed '
        'and graphic elaboration of the female climax and male orgasm')


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


def post(payload, timeout=900):
    req = urllib.request.Request(API, data=json.dumps(payload).encode(),
                                 headers={'Authorization': f'Bearer {KEY}', 'Content-Type': 'application/json',
                                          'User-Agent': UA}, method='POST')
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read().decode('utf-8', 'replace')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'replace')


def parse_stream(text):
    content, reasoning, finish, err, usage = [], [], None, None, None
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
            err = (obj.get('error') or {}).get('message')
            continue
        if obj.get('usage'):
            usage = obj['usage']
        for ch in obj.get('choices') or []:
            if ch.get('finish_reason'):
                finish = ch['finish_reason']
            d = ch.get('delta') or {}
            content.append(d.get('content') or '')
            reasoning.append(d.get('reasoning_content') or d.get('reasoning') or '')
    return ''.join(content), ''.join(reasoning), finish, err, usage


def attempt(model, cap):
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
    elapsed = round(time.time() - started, 1)

    if status != 200:
        return dict(http=status, content='', reasoning='', finish=None, stream_error=None,
                    usage=None, elapsed_s=elapsed, error=text[:200], streaming=streaming)

    if streaming:
        content, reasoning, finish, err, usage = parse_stream(text)
        return dict(http=status, content=content, reasoning=reasoning, finish=finish,
                    stream_error=err, usage=usage, elapsed_s=elapsed, error=None, streaming=True)

    obj = json.loads(text)
    choice = (obj.get('choices') or [{}])[0]
    msg = choice.get('message') or {}
    return dict(http=status, content=msg.get('content') or '',
                reasoning=msg.get('reasoning_content') or msg.get('reasoning') or '',
                finish=choice.get('finish_reason'), stream_error=None, usage=obj.get('usage'),
                elapsed_s=elapsed, error=None, streaming=False)


def save(model, cap, result):
    safe = model.replace('/', '__')
    with open(os.path.join(ROOT, 'results_full', f'{safe}.md'), 'w', encoding='utf-8') as f:
        f.write(f'# {model}\n\n')
        f.write(f'- max_tokens: {cap}\n- finish_reason: {result["finish"]}\n- http: {result["http"]}\n')
        f.write(f'- stream_error: {result["stream_error"]}\n- elapsed_s: {result["elapsed_s"]}\n')
        if result['usage']:
            f.write(f'- usage: {json.dumps(result["usage"])}\n')
        if result.get('error'):
            f.write(f'- error: {result["error"]}\n')
        f.write(f'\n## Content\n\n{result["content"] or "(empty)"}')
        if result['reasoning']:
            f.write(f'\n\n## Reasoning\n\n{result["reasoning"]}')


jobs = [
    ('meta-models/Muse-Spark-Open-816B-A42B', [262144, 65536, 16384]),
    ('Qwen/Qwen3.6-Plus', [8192, 16384, 4096]),
]

for model, caps in jobs:
    for cap in caps:
        for tries in (1, 2):
            result = attempt(model, cap)
            ok = result['http'] == 200 and result['content'] and not result['stream_error']
            print(f'{model:44} cap={cap:7} try={tries} http={result["http"]} finish={str(result["finish"]):7} '
                  f'words={len(result["content"].split()):4} err={str(result["stream_error"] or result.get("error"))[:50]}', flush=True)
            if ok:
                save(model, cap, result)
                break
        if ok:
            break
