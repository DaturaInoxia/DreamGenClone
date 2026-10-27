"""Thinking-toggle verification via streaming (the Qwen models reject non-streaming)."""
import base64
import ctypes
import json
import sqlite3
import urllib.error
import urllib.request
from ctypes import wintypes

DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'
API = 'https://api.together.xyz/v1/chat/completions'
UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36'


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

PROMPT = ('A farmer has 17 sheep and all but 9 run away. Then he sells half of what is left, '
          'and buys 3 more. How many does he have? Show your reasoning, then answer.')


def stream_probe(model, kwargs):
    body = {'model': model, 'messages': [{'role': 'user', 'content': PROMPT}],
            'temperature': 0.6, 'top_p': 0.9, 'max_tokens': 2000, 'stream': True}
    if kwargs is not None:
        body['chat_template_kwargs'] = kwargs
    req = urllib.request.Request(API, data=json.dumps(body).encode(),
                                 headers={'Authorization': f'Bearer {KEY}', 'Content-Type': 'application/json',
                                          'User-Agent': UA}, method='POST')
    try:
        with urllib.request.urlopen(req, timeout=240) as r:
            text = r.read().decode('utf-8', 'replace')
    except urllib.error.HTTPError as e:
        return e.code, None, 0, 0, e.read().decode('utf-8', 'replace')[:100]

    content = reasoning = 0
    finish = None
    err = None
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
        for ch in obj.get('choices') or []:
            if ch.get('finish_reason'):
                finish = ch['finish_reason']
            delta = ch.get('delta') or {}
            content += len(delta.get('content') or '')
            reasoning += len(delta.get('reasoning_content') or '') + len(delta.get('reasoning') or '')
    return 200, finish, content, reasoning, err


for model in ('Qwen/Qwen3.6-Plus', 'Qwen/Qwen3.8-Flash', 'Qwen/Qwen3.7-Plus', 'meta-models/Muse-Glimmer-30B'):
    for label, kwargs in (('default', None), ('thinking=false', {'thinking': False}), ('enable_thinking=false', {'enable_thinking': False})):
        status, finish, content, reasoning, err = stream_probe(model, kwargs)
        print(f'{model:32} {label:22} http={status} finish={str(finish):7} content={content:5} '
              f'reasoning={reasoning:6} err={str(err)[:40]}', flush=True)
