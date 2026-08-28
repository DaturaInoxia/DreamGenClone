"""Report progress of the in-flight Juggernaut multipart upload to the RunPod volume.

Counts parts under .s3compat_uploads/ for the object and prints progress. The final
object only appears under models/checkpoints/ after CompleteMultipartUpload is called.
"""
import os
import sys

import boto3

BUCKET = os.environ["RUNPOD_VOLUME_BUCKET"]
ENDPOINT = os.environ.get("RUNPOD_VOLUME_ENDPOINT", "https://s3api-eu-ro-1.runpod.io")
REGION = os.environ.get("RUNPOD_VOLUME_REGION", "eu-ro-1")

# 8 MB part size (default boto3 / s3-volume.py chunk)
PART_BYTES = 8 * 1024 * 1024

def main() -> int:
    prefix = sys.argv[1] if len(sys.argv) > 1 else ".s3compat_uploads/"
    s3 = boto3.client(
        "s3",
        endpoint_url=ENDPOINT,
        region_name=REGION,
        aws_access_key_id=os.environ["S3_ACCESS_KEY"],
        aws_secret_access_key=os.environ["S3_SECRET_KEY"],
    )
    paginator = s3.get_paginator("list_objects_v2")
    n = 0
    parts = []
    for page in paginator.paginate(Bucket=BUCKET, Prefix=prefix):
        for o in page.get("Contents", []):
            key = o["Key"]
            if key.endswith("/"):
                continue
            try:
                parts.append(int(key.rsplit("/", 1)[1]))
            except ValueError:
                pass
            n += 1
    if parts:
        parts.sort()
        print(f"objects under {prefix}: {n}")
        print(f"uploaded parts: {len(parts)} (indices {parts[0]}-{parts[-1]})")
        print(f"est uploaded: {len(parts) * PART_BYTES / 1e9:.2f} GB")
    else:
        print(f"no multipart parts found under {prefix}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
