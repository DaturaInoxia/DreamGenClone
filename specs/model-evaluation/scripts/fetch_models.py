import json
import sqlite3
import sys
import urllib.request
import base64
import ctypes
from ctypes import wintypes

DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'


def decrypt(encrypted_b64: str) -> str:
    """DPAPI CurrentUser unprotect, matching ApiKeyEncryptionService."""
    blob = base64.b64decode(encrypted_b64)

    class DATA_BLOB(ctypes.Structure):
        _fields_ = [('cbData', wintypes.DWORD), ('pbData', ctypes.POINTER(ctypes.c_char))]

    def make_blob(data: bytes):
        buffer = ctypes.create_string_buffer(data, len(data))
        return DATA_BLOB(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_char))), buffer

    crypt32 = ctypes.windll.crypt32
    kernel32 = ctypes.windll.kernel32

    in_blob, keep_in = make_blob(blob)
    out_blob = DATA_BLOB()

    if not crypt32.CryptUnprotectData(ctypes.byref(in_blob), None, None, None, None, 0, ctypes.byref(out_blob)):
        raise OSError('CryptUnprotectData failed')

    try:
        raw = ctypes.string_at(out_blob.pbData, out_blob.cbData)
    finally:
        kernel32.LocalFree(out_blob.pbData)

    return raw.decode('utf-8')


con = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
row = con.execute("select ApiKeyEncrypted from Providers where Name='TogetherAI'").fetchone()
if not row or not row[0]:
    sys.exit('TogetherAI provider key missing')

api_key = decrypt(row[0])

request = urllib.request.Request(
    'https://api.together.xyz/v1/models',
    headers={
        'Authorization': f'Bearer {api_key}',
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 '
                      '(KHTML, like Gecko) Chrome/120.0 Safari/537.36',
        'Accept': 'application/json',
    })
with urllib.request.urlopen(request, timeout=90) as response:
    payload = json.load(response)

out = sys.argv[1]
with open(out, 'w', encoding='utf-8') as f:
    json.dump(payload, f, indent=1)

models = payload if isinstance(payload, list) else payload.get('data', [])
chat = [m for m in models if m.get('type') == 'chat']
print(f'total={len(models)} chat={len(chat)} -> {out}')
