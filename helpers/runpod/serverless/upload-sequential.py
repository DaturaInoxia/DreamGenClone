#!/usr/bin/env python3
"""Sequential multipart uploader for RunPod network volumes.

Uploads one part at a time (no thread contention) with per-request timeouts
and capped exponential backoff. Robust against Cloudflare 524s and hangs.
Usage:
    python upload-sequential.py -b <bucket> -k <key> -f <local> \
        -e <s3_endpoint> -r <region> -a <access> -s <secret>
"""

import argparse
import logging
import math
import os
import sys
import time

import boto3
from botocore.config import Config
from botocore.exceptions import (
    BotoCoreError,
    ClientError,
    ConnectTimeoutError,
    ReadTimeoutError,
)

logging.basicConfig(
    level=logging.INFO,
    format="[%(asctime)s] %(levelname)8s %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logger = logging.getLogger(__name__)


def is_524(exc):
    if isinstance(exc, ClientError):
        return exc.response.get("ResponseMetadata", {}).get("HTTPStatusCode") == 524
    return False


def is_timeout(exc):
    return isinstance(exc, (ReadTimeoutError, ConnectTimeoutError))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("-b", "--bucket", required=True)
    p.add_argument("-k", "--key", required=True)
    p.add_argument("-f", "--file", dest="file_path", required=True)
    p.add_argument("-e", "--endpoint", required=True)
    p.add_argument("-r", "--region", required=True)
    p.add_argument("-a", "--access_key", required=True)
    p.add_argument("-s", "--secret_key", required=True)
    p.add_argument("--part-size", type=int, default=50 * 1024 * 1024)
    p.add_argument("--max-retries", type=int, default=6)
    p.add_argument("--read-timeout", type=int, default=300)
    p.add_argument("--quiet", action="store_true")
    args = p.parse_args()

    if args.quiet:
        logging.getLogger().setLevel(logging.WARNING)

    file_size = os.path.getsize(args.file_path)
    total_parts = math.ceil(file_size / args.part_size)
    logger.info(
        "File size: %d bytes; will upload in %d parts of up to %d bytes each",
        file_size,
        total_parts,
        args.part_size,
    )

    cfg = Config(
        region_name=args.region,
        read_timeout=args.read_timeout,
        connect_timeout=60,
        retries={"max_attempts": 2, "mode": "standard"},
    )
    s3 = boto3.client(
        "s3",
        config=cfg,
        endpoint_url=args.endpoint,
        region_name=args.region,
        aws_access_key_id=args.access_key,
        aws_secret_access_key=args.secret_key,
    )

    def call(func, what, retries=args.max_retries):
        for attempt in range(1, retries + 1):
            try:
                return func()
            except Exception as exc:
                if is_524(exc) or is_timeout(exc):
                    logger.warning(
                        "%s failed (attempt %d/%d): %s", what, attempt, retries, exc
                    )
                    if attempt == retries:
                        raise
                    backoff = min(2 ** (attempt + 1), 60)
                    logger.info("%s retrying in %ds...", what, backoff)
                    time.sleep(backoff)
                else:
                    raise

    logger.info("Initiating multipart upload")
    resp = call(
        lambda: s3.create_multipart_upload(Bucket=args.bucket, Key=args.key),
        "create_multipart_upload",
    )
    upload_id = resp["UploadId"]
    logger.info("Initiated multipart upload: UploadId=%s", upload_id)

    parts = []
    start = time.time()
    try:
        for part_num in range(1, total_parts + 1):
            offset = (part_num - 1) * args.part_size
            size = min(args.part_size, file_size - offset)

            def do_upload(part_num=part_num, offset=offset, size=size):
                with open(args.file_path, "rb") as f:
                    f.seek(offset)
                    data = f.read(size)
                return s3.upload_part(
                    Bucket=args.bucket,
                    Key=args.key,
                    PartNumber=part_num,
                    UploadId=upload_id,
                    Body=data,
                )

            resp = call(
                do_upload,
                "Part %d/%d" % (part_num, total_parts),
            )
            parts.append({"PartNumber": part_num, "ETag": resp["ETag"]})
            elapsed = time.time() - start
            frac = part_num / total_parts
            eta = max(0, elapsed * (1 / frac - 1)) if frac > 0 else 0
            logger.info(
                "Part %d/%d uploaded (%.1f%%, elapsed %ds, ETA %ds)",
                part_num,
                total_parts,
                100.0 * part_num / total_parts,
                int(elapsed),
                int(eta),
            )

        # Verify all parts present
        seen = call(
            lambda: s3.list_parts(
                Bucket=args.bucket, Key=args.key, UploadId=upload_id
            ).get("Parts", []),
            "list_parts",
        )
        logger.info("Verified %d of %d parts on server", len(seen), total_parts)
        if len(seen) != total_parts:
            raise RuntimeError("Expected %d parts but saw %d" % (total_parts, len(seen)))

        logger.info("Sending complete_multipart_upload")
        call(
            lambda: s3.complete_multipart_upload(
                Bucket=args.bucket,
                Key=args.key,
                UploadId=upload_id,
                MultipartUpload={"Parts": sorted(parts, key=lambda x: x["PartNumber"])},
            ),
            "complete_multipart_upload",
            retries=args.max_retries + 4,  # allow long backoff for merge
        )

        head = call(
            lambda: s3.head_object(Bucket=args.bucket, Key=args.key),
            "head_object",
        )
        remote = head["ContentLength"]
        if remote != file_size:
            raise RuntimeError(
                "Size mismatch: remote %d vs local %d" % (remote, file_size)
            )
        logger.info("VERIFIED: remote object size %d matches local", remote)
    except Exception as exc:
        logger.error("Upload failed: %s", exc)
        logger.info("UploadId %s left open for resumption", upload_id)
        sys.exit(1)


if __name__ == "__main__":
    main()
