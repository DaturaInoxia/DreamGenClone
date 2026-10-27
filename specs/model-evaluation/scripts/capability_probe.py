"""Probe every target model on Together AI for callability, thinking-control support and output ceiling.

Records only status codes, token counts and lengths - never the generated text.
"""
import base64
import ctypes
import json
import sqlite3
import time
import urllib.error
import urllib.request
from ctypes import wintypes

DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'
OUT = r'D:\src\DreamGenClone\artifacts\prompt-test\capability.json'
API = 'https://api.together.xyz/v1/chat/completions'
UA = ('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 '
      '(KHTML, like Gecko) Chrome/120.0 Safari/537.36')

TARGETS = [
    'zai-org/GLM-5.3-Flash',
    'nvidia/NVIDIA-Nemotron-3.5-Super-VL',
    'Qwen/Qwen3.8-Flash',
    'openai/gpt-oss-120b',
    'MiniMaxAI/MiniMax-M3.1-Flash',
    'Qwen/Qwen3.5-9B',
    'deepseek-ai/DeepSeek-V4.1-Flash-1017',
    'deepseek-ai/DeepSeek-V4.1-Flash',
    'MiniMaxAI/MiniMax-M3',
    'Qwen/Qwen3.7-Plus',
    'meta-models/Muse-Glimmer-30B',
    'Qwen/Qwen3.6-Plus',
    'thinkingmachines/Inkling',
    'meta-models/Muse-Spark-Open-816B-A42B',
    'deepseek-ai/DeepSeek-V4-Pro-0813',
    'zai-org/GLM-5.3',
    'zai-org/GLM-5.2',
]


def decrypt(encrypted_b64: str) -> str:
    blob = base64.b64decode(encrypted_b64)

    class DATA_BLOB(ctypes.Structure):
        _fields_ = [('cbData', wintypes.DWORD), ('pbData', ctypes.POINTER(ctypes.c_char))]

    def make_blob(data):
        buf = ctypes.create_string_buffer(data, len(data))
        return DATA_BLOB(len(data), ctypes.cast(buf, ctypes.POINTER(ctypes.c_char))), buf

    crypt32, kernel32 = ctypes.windll.crypt32, ctypes.windll.kernel32
    in_blob, keep = make_blob(blob)
    out_blob = DATA_BLOB()
    if not crypt32.CryptUnprotectData(ctypes.byref(in_blob), None, None, None, None, 0, ctypes.byref(out_blob)):
        raise OSError('CryptUnprotectData failed')
    try:
        raw = ctypes.string_at(out_blob.pbData, out_blob.cbData)
    finally:
        kernel32.LocalFree(out_blob.pbData)
    return raw.decode('utf-8')


con = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
key = decrypt(con.execute("select ApiKeyEncrypted from Providers where Name='TogetherAI'").fetchone()[0])


def call(model: str, extra: dict, max_tokens: int):
    body = {
        'model': model,
        'messages': [{'role': 'user', 'content': 'Reply with the single word: ready'}],
        'temperature': 0.2,
        'top_p': 0.9,
        'max_tokens': max_tokens,
    }
    body.update(extra)

    def post(payload):
        req = urllib.request.Request(
            API,
            data=json.dumps(payload).encode(),
            headers={'Authorization': f'Bearer {key}', 'Content-Type': 'application/json', 'User-Agent': UA},
            method='POST')
        try:
            with urllib.request.urlopen(req, timeout=150) as r:
                return r.status, r.read().decode('utf-8', 'replace')
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode('utf-8', 'replace')

    started = time.time()
    status, text = post(body)
    elapsed = time.time() - started

    if status == 400 and 'only supports streaming' in text:
        streamed = dict(body)
        streamed['stream'] = True
        started = time.time()
        status, text = post(streamed)
        elapsed = time.time() - started
        if status == 200:
            content = reasoning = 0
            finish = None
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
                    return {'status': status, 'streaming': True, 'error': obj.get('error', {}).get('message'),
                            'elapsed_s': round(elapsed, 1), 'content_chars': content, 'reasoning_chars': reasoning}
                for ch in obj.get('choices') or []:
                    if ch.get('finish_reason'):
                        finish = ch['finish_reason']
                    delta = ch.get('delta') or {}
                    content += len(delta.get('content') or '')
                    reasoning += len(delta.get('reasoning_content') or '')
            return {'status': status, 'streaming': True, 'finish': finish,
                    'content_chars': content, 'reasoning_chars': reasoning, 'elapsed_s': round(elapsed, 1)}

    result = {'status': status, 'streaming': False, 'elapsed_s': round(elapsed, 1)}
    if status == 200:
        try:
            obj = json.loads(text)
            msg = (obj.get('choices') or [{}])[0].get('message') or {}
            usage = obj.get('usage') or {}
            result.update(
                finish=(obj.get('choices') or [{}])[0].get('finish_reason'),
                content_chars=len(msg.get('content') or ''),
                reasoning_chars=len(msg.get('reasoning_content') or msg.get('reasoning') or ''),
                prompt_tokens=usage.get('prompt_tokens'),
                completion_tokens=usage.get('completion_tokens'))
        except ValueError:
            result['error'] = 'unparseable body'
    else:
        try:
            result['error'] = json.loads(text).get('error', {}).get('message')
        except ValueError:
            result['error'] = text[:160]
    return result


variants = [
    ('base', {}, 24),
    ('enable_thinking_false', {'chat_template_kwargs': {'enable_thinking': False}}, 24),
    ('thinking_false', {'chat_template_kwargs': {'thinking': False}}, 24),
    ('output_ceiling', {}, 131072),
]

report = {}
for model in TARGETS:
    report[model] = {}
    for name, extra, max_tokens in variants:
        entry = call(model, extra, max_tokens)
        report[model][name] = entry
        print(f"{model:44} {name:24} {entry.get('status')} "
              f"reasoning={entry.get('reasoning_chars', '')} err={str(entry.get('error'))[:70]}", flush=True)
    with open(OUT, 'w', encoding='utf-8') as f:
        json.dump(report, f, indent=1)

print('done ->', OUT)
