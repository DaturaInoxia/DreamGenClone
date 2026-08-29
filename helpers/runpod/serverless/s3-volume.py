#!/usr/bin/env python3
"""s3-volume.py - RunPod Network Volume S3-compatible API helper.

Requires env vars (from helpers/runpod/.runpod-env.ps1, git-ignored):
  S3_ACCESS_KEY  -> AWS_ACCESS_KEY_ID   (RunPod user ID, e.g. user_...)
  S3_SECRET_KEY  -> AWS_SECRET_ACCESS_KEY (S3 API key secret, rps_...)
Also reads: RUNPOD_VOLUME_ENDPOINT, RUNPOD_VOLUME_REGION (optional overrides).

Subcommands:
  buckets                    List the network volumes (buckets) this key can access.
  ls <prefix>                List objects under s3://<bucket>/<prefix>.
  upload <local> <object>    Upload a local file to s3://<bucket>/<object> (multipart for >500MB).
  sha <local> <expected>     Print the local file's sha256 and compare to expected.
"""
import argparse
import hashlib
import os
import sys

import boto3
from botocore.config import Config as BotoConfig
from boto3.s3.transfer import TransferConfig

ENDPOINT = os.environ.get("RUNPOD_VOLUME_ENDPOINT", "https://s3api-eu-ro-1.runpod.io")
REGION = os.environ.get("RUNPOD_VOLUME_REGION", "eu-ro-1")
BUCKET = os.environ.get("RUNPOD_VOLUME_BUCKET", "")  # filled from `buckets` if not set

# 64 MiB parts + bounded concurrency: the boto3 default is 8 MiB parts, which for a ~28 GB file
# means ~3400 separate UploadPart requests - each one a chance to hit a Cloudflare 524 on the
# flaky dev-host upstream. Larger parts (fewer requests) + long read timeout + many retries make
# big uploads far more resilient.
MULTIPART_CHUNK = 64 * 1024 * 1024


def _client():
    ak = os.environ.get("S3_ACCESS_KEY")
    sk = os.environ.get("S3_SECRET_KEY")
    if not ak or not sk:
        raise SystemExit("S3_ACCESS_KEY / S3_SECRET_KEY not set (source .runpod-env.ps1)")
    return boto3.client(
        "s3",
        aws_access_key_id=ak,
        aws_secret_access_key=sk,
        region_name=REGION,
        endpoint_url=ENDPOINT,
        config=BotoConfig(
            connect_timeout=30,
            read_timeout=600,          # 10 min per request; slow links must not drop mid-part
            retries={"max_attempts": 25, "mode": "standard"},
            max_pool_connections=16,
        ),
    )


def _bucket():
    return BUCKET or _buckets()[0]


def _buckets():
    resp = _client().list_buckets()
    names = [b["Name"] for b in resp.get("Buckets", [])]
    if not names:
        raise SystemExit("No network volumes (buckets) visible with this key")
    return names


def cmd_buckets(args):
    for n in _buckets():
        print(n)


def cmd_ls(args):
    c = _client()
    bucket = _bucket()
    prefix = args.prefix or ""
    print(f"# bucket={bucket} prefix={prefix or '<root>'}")
    paginator = c.get_paginator("list_objects_v2")
    for page in paginator.paginate(Bucket=bucket, Prefix=prefix):
        for o in page.get("Contents", []):
            print(f"{o['Key']}\t{o['Size']}")


def cmd_upload(args):
    c = _client()
    bucket = _bucket()
    local = args.local
    obj = args.object
    size = os.path.getsize(local)
    print(f"uploading {local} ({size} bytes) -> s3://{bucket}/{obj} (chunk={MULTIPART_CHUNK // (1024*1024)}MiB, retries=25, read_timeout=600s)")
    tcfg = TransferConfig(
        multipart_threshold=MULTIPART_CHUNK,
        multipart_chunksize=MULTIPART_CHUNK,
        max_concurrency=4,
        use_threads=True,
    )
    c.upload_file(local, bucket, obj, Config=tcfg)
    print("upload complete")


def cmd_sha(args):
    h = hashlib.sha256()
    with open(args.local, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    actual = h.hexdigest()
    print(f"sha256 {actual}  {args.local}")
    if args.expected:
        print("MATCH" if actual == args.expected else f"MISMATCH (expected {args.expected})")
        sys.exit(0 if actual == args.expected else 1)


def main():
    p = argparse.ArgumentParser(description="RunPod Network Volume S3 helper")
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("buckets", help="list network volumes")
    l = sub.add_parser("ls", help="list objects")
    l.add_argument("prefix", nargs="?", default="")
    u = sub.add_parser("upload", help="upload local file")
    u.add_argument("local")
    u.add_argument("object")
    s = sub.add_parser("sha", help="sha256 of a local file")
    s.add_argument("local")
    s.add_argument("expected", nargs="?", default="")
    args = p.parse_args()
    {"buckets": cmd_buckets, "ls": cmd_ls, "upload": cmd_upload, "sha": cmd_sha}[args.cmd](args)


if __name__ == "__main__":
    main()
