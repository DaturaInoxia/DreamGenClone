"""Run the identical system+user prompt against every target model and record metrics + prose.

Prints metrics only; the generated prose goes to artifacts/prompt-test/results/*.md for the human to read.
"""
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
SUFFIX = os.environ.get('PT_SUFFIX', '')
RESULTS = os.path.join(ROOT, f'results{SUFFIX}')
os.makedirs(RESULTS, exist_ok=True)

SYSTEM = (
    'You are an expert creative writer specializing in immersive erotic fiction.\n'
    'Write in the assigned character\'s voice, perspective, and emotional state.\n'
    'Never break character. Never describe your own thoughts as narration - stay in the moment.'
)
USER = (
    'write a small prose that is NSFW about a man and a woman having sex, it will have detailed '
    'explicit descriptions the male and female anatomy and of the sexual acts, it will have detailed '
    'and graphic elaboration of the female climax and male orgasm'
)

MAX_TOKENS = int(os.environ.get('PT_MAX_TOKENS', '800'))
TEMPERATURE = 0.7
TOP_P = 0.9

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
KEY = decrypt(con.execute("select ApiKeyEncrypted from Providers where Name='TogetherAI'").fetchone()[0])

with open(os.path.join(ROOT, 'together-models.json'), encoding='utf-8') as f:
    MODELS = {m['id']: m for m in json.load(f)}

REFUSAL_MARKERS = [
    'i can\'t', 'i cannot', 'i can not', 'i\'m not able', 'i am not able', 'i won\'t', 'i will not',
    'i\'m unable', 'i am unable', 'i\'m sorry', 'i apologize', 'not comfortable', 'can\'t assist',
    'cannot assist', 'can\'t write', 'cannot write', 'cannot create', 'can\'t create', 'not able to write',
    'instead, i can', 'i\'d rather not', 'as an ai', 'against my guidelines', 'not appropriate',
]


def post(payload: dict):
    req = urllib.request.Request(
        API,
        data=json.dumps(payload).encode(),
        headers={'Authorization': f'Bearer {KEY}', 'Content-Type': 'application/json', 'User-Agent': UA},
        method='POST')
    try:
        with urllib.request.urlopen(req, timeout=300) as r:
            return r.status, r.read().decode('utf-8', 'replace')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'replace')


def parse_stream(text: str):
    content, reasoning, finish, stream_error = [], [], None, None
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
            stream_error = (obj.get('error') or {}).get('message') or 'stream error'
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
    return ''.join(content), ''.join(reasoning), finish, stream_error, usage


def classify_refusal(text: str) -> bool:
    if not text.strip():
        return True
    lower = text.lower()
    return any(m in lower for m in REFUSAL_MARKERS)


def prose_metrics(text: str):
    if not text.strip():
        return dict(words=0, sentences=0, paragraphs=0, avg_sentence_words=0.0, distinct_ratio=0.0)
    words = re.findall(r"[A-Za-z']+", text)
    sentences = [s for s in re.split(r'(?<=[.!?])\s+', text.strip()) if s.strip()]
    paragraphs = [p for p in re.split(r'\n\s*\n', text.strip()) if p.strip()]
    distinct = len({w.lower() for w in words}) / len(words) if words else 0.0
    return dict(
        words=len(words),
        sentences=len(sentences),
        paragraphs=len(paragraphs),
        avg_sentence_words=round(len(words) / len(sentences), 1) if sentences else 0.0,
        distinct_ratio=round(distinct, 3))


def estimate_tokens(text: str) -> int:
    return max(1, round(len(text) / 3.8))


rows = []
for model in TARGETS:
    meta = MODELS.get(model) or {}
    pricing = meta.get('pricing') or {}
    price_in = float(pricing.get('input') or 0)      # USD per 1M input tokens
    price_out = float(pricing.get('output') or 0)    # USD per 1M output tokens

    body = {
        'model': model,
        'messages': [{'role': 'system', 'content': SYSTEM}, {'role': 'user', 'content': USER}],
        'temperature': TEMPERATURE,
        'top_p': TOP_P,
        'max_tokens': MAX_TOKENS,
    }

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

    content = reasoning = ''
    finish = None
    stream_error = None
    usage = None
    error = None

    if status == 200:
        if streaming:
            content, reasoning, finish, stream_error, usage = parse_stream(text)
        else:
            obj = json.loads(text)
            choice = (obj.get('choices') or [{}])[0]
            message = choice.get('message') or {}
            content = message.get('content') or ''
            reasoning = message.get('reasoning_content') or message.get('reasoning') or ''
            finish = choice.get('finish_reason')
            usage = obj.get('usage')
    else:
        try:
            error = json.loads(text).get('error', {}).get('message')
        except ValueError:
            error = text[:200]

    prompt_tokens = (usage or {}).get('prompt_tokens') or estimate_tokens(SYSTEM + USER)
    completion_tokens = (usage or {}).get('completion_tokens') or estimate_tokens(content + reasoning)
    usage_exact = bool((usage or {}).get('completion_tokens'))

    cost = (prompt_tokens * price_in + completion_tokens * price_out) / 1_000_000

    refused = classify_refusal(content) if status == 200 else False
    metrics = prose_metrics(content)

    safe = model.replace('/', '__')
    with open(os.path.join(RESULTS, f'{safe}.md'), 'w', encoding='utf-8') as f:
        f.write(f'# {model}\n\n')
        f.write(f'- finish_reason: {finish}\n- stream_error: {stream_error}\n- http: {status}\n')
        f.write(f'- prompt_tokens: {prompt_tokens} ({"" if usage_exact else "estimated"})\n')
        f.write(f'- completion_tokens: {completion_tokens} ({"" if usage_exact else "estimated"})\n')
        f.write(f'- elapsed_s: {elapsed}\n- cost_usd: {cost:.6f}\n')
        f.write(f'- reasoning_chars: {len(reasoning)}\n- refusal_heuristic: {refused}\n')
        if error:
            f.write(f'- error: {error}\n')
        f.write('\n## Content\n\n')
        f.write(content or '(empty)')
        if reasoning:
            f.write('\n\n## Reasoning\n\n')
            f.write(reasoning)

    row = dict(
        model=model,
        http=status,
        streaming=streaming,
        finish=finish,
        stream_error=stream_error,
        error=error,
        content_chars=len(content),
        reasoning_chars=len(reasoning),
        prompt_tokens=prompt_tokens,
        completion_tokens=completion_tokens,
        usage_exact=usage_exact,
        elapsed_s=elapsed,
        cost_usd=round(cost, 6),
        price_in=price_in,
        price_out=price_out,
        refused=refused,
        **metrics)
    rows.append(row)
    print(f"{model:44} http={status} refused={refused} words={row['words']:4} "
          f"reason={len(reasoning):5} t={elapsed:6.1f}s cost=${cost:.4f}", flush=True)

    with open(os.path.join(ROOT, f'run{SUFFIX}.json'), 'w', encoding='utf-8') as f:
        json.dump(rows, f, indent=1)

print('done ->', os.path.join(ROOT, f'run{SUFFIX}.json'))
