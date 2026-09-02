"""Verify the Qwen VL / Qwen Edit model paths on network volume xkslgh6xo0 via the S3-compatible API."""
import os
import sys

try:
    import boto3
except ImportError:
    print("boto3 not installed - install with: pip install boto3")
    sys.exit(1)

os.environ["AWS_ACCESS_KEY_ID"] = os.environ.get("S3_ACCESS_KEY", "user_3IHEGw7n81XpJFMEODK1HSOz50M")
os.environ["AWS_SECRET_ACCESS_KEY"] = os.environ.get("S3_SECRET_KEY", "rps_DWNFOZWYKXH1KP3C47HVNWT0A89FYG90BN1O5BIB1xtdpp")

s3 = boto3.client("s3", region_name="EU-RO-1", endpoint_url="https://s3api-eu-ro-1.runpod.io/")
BUCKET = "xkslgh6xo0"

prefixes = [
    "qwen-vl-edit-compiler/",
    "qwen-vl-edit-compiler/model/",
    "models/checkpoints/",
]

for prefix in prefixes:
    try:
        r = s3.list_objects_v2(Bucket=BUCKET, Prefix=prefix)
        keys = r.get("Contents", [])
        print(f"--- {prefix} ({len(keys)} objects) ---")
        for o in keys:
            print(f"  {o['Key']}  {o['Size']}")
    except Exception as e:
        print(f"{prefix} ERROR: {e}")
