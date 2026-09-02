"""Download the missing Qwen2.5-VL model files from HF and upload them to the network volume.

Root cause: the earlier upload copied only the 4 safetensors shards + partial config, but MISSED
model.safetensors.index.json (the shard index, REQUIRED for vLLM to load sharded weights), the
tokenizer files (tokenizer.json / tokenizer_config.json / vocab.json / special_tokens_map.json),
and preprocessor_config.json (required for vision preprocessing). Without the index vLLM crashes
at startup -> worker unhealthy -> jobs never processed.
"""
import os
import sys

try:
    import boto3
except ImportError:
    print("boto3 not installed - install with: pip install boto3")
    sys.exit(1)

import urllib.request
import json

os.environ["AWS_ACCESS_KEY_ID"] = os.environ.get("S3_ACCESS_KEY", "user_3IHEGw7n81XpJFMEODK1HSOz50M")
os.environ["AWS_SECRET_ACCESS_KEY"] = os.environ.get("S3_SECRET_KEY", "rps_DWNFOZWYKXH1KP3C47HVNWT0A89FYG90BN1O5BIB1xtdpp")

s3 = boto3.client("s3", region_name="EU-RO-1", endpoint_url="https://s3api-eu-ro-1.runpod.io/")
BUCKET = "xkslgh6xo0"
REPO = "huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated"
PREFIX = "qwen-vl-edit-compiler/model/"

# Files REQUIRED for vLLM to serve the sharded multimodal model (missing on volume).
MISSING_FILES = [
    "model.safetensors.index.json",
    "preprocessor_config.json",
    "special_tokens_map.json",
    "tokenizer.json",
    "tokenizer_config.json",
    "vocab.json",
]

def download(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=120) as resp:
        return resp.read()

for fname in MISSING_FILES:
    url = f"https://huggingface.co/{REPO}/resolve/main/{fname}"
    key = PREFIX + fname
    try:
        data = download(url)
        s3.put_object(Bucket=BUCKET, Key=key, Body=data)
        print(f"UPLOADED {key} ({len(data)} bytes)")
    except Exception as e:
        print(f"ERROR {fname}: {e}")
