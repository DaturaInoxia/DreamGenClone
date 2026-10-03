"""Cheap preflight for the Krea 2 serverless dispatch path, run ON the ComfyUI host.

Proves, without spending any GPU time, that the training service can

  1. import its serverless dependencies (boto3 is installed), and
  2. read every required setting, and
  3. reach the RunPod network volume with the configured S3 credentials, and
  4. authenticate to the RunPod API for the configured endpoint.

A real training run costs about an hour and roughly $3, so a mistyped credential or a wrong bucket should fail
here instead of an hour from now.

It reads the settings the same way the service does -- from start-service.bat and serverless-secrets.bat -- rather
than taking them on a command line, so it also catches a malformed .bat (a batch file saved with LF-only endings
is silently ignored by cmd.exe, which is a documented trap on this host) and a missing trailing value.
"""

from __future__ import annotations

import json
import os
import sys
import urllib.request
from pathlib import Path

SERVICE_DIR = Path(r"D:\lora-training-service")
sys.path.insert(0, str(SERVICE_DIR))


def load_bat(path: Path) -> list[str]:
    """Apply the `set NAME=value` lines of a .bat to this process's environment.

    Mirrors what cmd.exe does, so the values under test are the ones the service will actually get.
    """
    applied: list[str] = []
    if not path.is_file():
        print(f"   MISSING {path}")
        return applied
    raw = path.read_bytes()
    if b"\r\n" not in raw:
        print(f"   WARNING {path.name} has NO CRLF line endings - cmd.exe will ignore it")
    for line in raw.decode("utf-8", errors="replace").splitlines():
        line = line.strip()
        if not line.lower().startswith("set ") or "=" not in line:
            continue
        name, _, value = line[4:].partition("=")
        name = name.strip()
        value = value.strip()
        os.environ[name] = value
        applied.append(f"{name}({len(value)})")
    return applied


print("0. launcher files        :")
print("   start-service.bat     : " + ", ".join(load_bat(SERVICE_DIR / "start-service.bat")))
print("   secrets              : " + ", ".join(load_bat(SERVICE_DIR / "serverless-secrets.bat")))

import lora_train_service as svc  # noqa: E402  (after the environment is loaded)

print("1. module import         : OK (boto3 present)")

settings = svc.ServerlessSettings.load()
print(
    "2. settings              : endpoint=%s bucket=%s region=%s timeout=%ss"
    % (settings.endpoint_id, settings.bucket, settings.region, settings.job_timeout_seconds)
)

import boto3  # noqa: E402

client = boto3.client(
    "s3",
    endpoint_url=settings.endpoint,
    aws_access_key_id=settings.access_key,
    aws_secret_access_key=settings.secret_key,
    region_name=settings.region,
)
listing = client.list_objects_v2(Bucket=settings.bucket, MaxKeys=1000)
keys = [item["Key"] for item in listing.get("Contents", [])]
models = [key for key in keys if key.startswith("models/")]
datasets = sorted(key for key in keys if key.startswith("datasets/"))
print("3. volume reachable      : OK (%d objects)" % len(keys))
print("   models/              : %d file(s)" % len(models))
for key in models:
    print("     %s" % key)
print("   datasets/            : %d file(s)" % len(datasets))
for key in datasets[:6]:
    print("     %s" % key)

request = urllib.request.Request(
    "https://api.runpod.ai/v2/%s/health" % settings.endpoint_id,
    headers={"Authorization": "Bearer %s" % settings.api_key},
)
with urllib.request.urlopen(request, timeout=30) as response:
    health = json.loads(response.read().decode("utf-8"))
print("4. runpod api            : OK (workers=%s)" % (health.get("workers"),))

print("PREFLIGHT PASS")
