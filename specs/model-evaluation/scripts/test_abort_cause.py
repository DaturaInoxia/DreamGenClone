"""Test whether Together's streaming abort correlates with hitting max_tokens rather than moderation."""
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

SYSTEM = ('You are an expert creative writer specializing in immersive erotic fiction.\n'
          "Write in the assigned character's voice, perspective, and emotional state.\n"
          'Never break character. Never describe your own thoughts as narration - stay in the moment.')
USER = ('write a small prose that is NSFW about a man and a woman having sex, it will have detailed '
        'explicit descriptions the male and female anatomy and of the sexual acts, it will have detailed '
        'and graphic elaboration of the female climax and male orgasm')


def post(payload):
    req = urllib.request.Request(API, data=json.dumps(payload).encode(),
                                 headers={'Authorization': f'Bearer {KEY}', 'Content-Type': 'application/json',
                                          'User-Agent': UA}, method='POST')
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            return r.status, r.read().decode('utf-8', 'replace')
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode('utf-8', 'replace')


def run(label, model, max_tokens, stream):
    body = {'model': model, 'messages': [{'role': 'system', 'content': SYSTEM},
                                         {'role': 'user', 'content': USER}],
            'temperature': 0.7, 'top_p': 0.9, 'max_tokens': max_tokens}
    if stream:
        body['stream'] = True
    status, text = post(body)
    finish, err, chars = None, None, 0
    if status == 200 and stream:
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
                chars += len((ch.get('delta') or {}).get('content') or '')
    elif status == 200:
        obj = json.loads(text)
        choice = (obj.get('choices') or [{}])[0]
        finish = choice.get('finish_reason')
        chars = len(((choice.get('message') or {}).get('content')) or '')
    else:
        try:
            err = json.loads(text).get('error', {}).get('message')
        except ValueError:
            err = text[:120]

    print(f'{label:34} http={status} finish={str(finish):7} chars={chars:5} err={str(err)[:60]}')


run('Qwen3.8 stream max_tokens=40', 'Qwen/Qwen3.8-Flash', 40, True)
run('Qwen3.8 nonstream max_tokens=40', 'Qwen/Qwen3.8-Flash', 40, False)
run('Qwen3.8 stream max_tokens=2000', 'Qwen/Qwen3.8-Flash', 2000, True)
run('GLM5.2 stream max_tokens=40', 'zai-org/GLM-5.2', 40, True)
run('DeepSeek4.1 stream max_tokens=40', 'deepseek-ai/DeepSeek-V4.1-Flash', 40, True)
