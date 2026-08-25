#!/usr/bin/env python3

import base64
import json
import mimetypes
import sys
import time
import urllib.request
from pathlib import Path

from PIL import Image


MODEL_NAME = "qwen2.5-vl-7b-edit-compiler"
MAX_IMAGE_BYTES = 10 * 1024 * 1024
MAX_IMAGE_PIXELS = 1_048_576
HTTP_TIMEOUT_SECONDS = 90
ALLOWED_MIME_TYPES = {"image/jpeg", "image/png", "image/webp"}
REQUIRED_FIELDS = {"source_summary", "edit_instruction", "preserve", "avoid"}


def fail(message: str) -> None:
    raise SystemExit(message)


if len(sys.argv) not in (4, 5):
    fail("Usage: prove-one-image.py <port> <source-image> <raw-response-path> [edit-intent]")

port = int(sys.argv[1])
source_path = Path(sys.argv[2])
response_path = Path(sys.argv[3])
edit_intent_argument = (
    sys.argv[4]
    if len(sys.argv) == 5
    else (
        "Change the main symbol to warm red while preserving its identity, composition, "
        "background, and clean icon rendering."
    )
)
edit_intent = (
    base64.b64decode(edit_intent_argument.removeprefix("base64:")).decode("utf-8")
    if edit_intent_argument.startswith("base64:")
    else edit_intent_argument
)
image_bytes = source_path.read_bytes()
mime_type = mimetypes.guess_type(source_path.name)[0]

if mime_type not in ALLOWED_MIME_TYPES:
    fail(f"Unsupported source image MIME type: {mime_type}")
if len(image_bytes) > MAX_IMAGE_BYTES:
    fail(f"Source image exceeds {MAX_IMAGE_BYTES} bytes: {len(image_bytes)}")

with Image.open(source_path) as image:
    width, height = image.size
    image.verify()

if width * height > MAX_IMAGE_PIXELS:
    fail(f"Source image exceeds {MAX_IMAGE_PIXELS} pixels: {width * height}")

schema = {
    "name": "image_edit_instruction",
    "strict": True,
    "schema": {
        "type": "object",
        "properties": {
            "source_summary": {"type": "string"},
            "edit_instruction": {"type": "string"},
            "preserve": {"type": "array", "items": {"type": "string"}},
            "avoid": {"type": "array", "items": {"type": "string"}},
        },
        "required": sorted(REQUIRED_FIELDS),
        "additionalProperties": False,
    },
}
data_url = f"data:{mime_type};base64,{base64.b64encode(image_bytes).decode('ascii')}"
payload = {
    "model": MODEL_NAME,
    "messages": [
        {
            "role": "system",
            "content": (
                "Analyze the supplied source image and compile the requested change into one "
                "precise instruction for an image-editing model. Ground every field in visible "
                "source evidence. Return only the required JSON object."
            ),
        },
        {
            "role": "user",
            "content": [
                {"type": "text", "text": edit_intent},
                {"type": "image_url", "image_url": {"url": data_url}},
            ],
        },
    ],
    "response_format": {"type": "json_schema", "json_schema": schema},
    "temperature": 0,
    "max_tokens": 512,
}
request = urllib.request.Request(
    f"http://127.0.0.1:{port}/v1/chat/completions",
    data=json.dumps(payload).encode("utf-8"),
    headers={"Content-Type": "application/json"},
    method="POST",
)

started = time.monotonic()
with urllib.request.urlopen(request, timeout=HTTP_TIMEOUT_SECONDS) as response:
    raw_response = response.read()
elapsed_seconds = time.monotonic() - started

response_path.parent.mkdir(parents=True, exist_ok=True)
response_path.write_bytes(raw_response)
response_document = json.loads(raw_response)
content = response_document["choices"][0]["message"]["content"]
compiled = json.loads(content)

if set(compiled) != REQUIRED_FIELDS:
    fail(f"Compiler response fields do not match schema: {sorted(compiled)}")
if not isinstance(compiled["source_summary"], str) or not compiled["source_summary"].strip():
    fail("Compiler response has an invalid source_summary.")
if not isinstance(compiled["edit_instruction"], str) or not compiled["edit_instruction"].strip():
    fail("Compiler response has an invalid edit_instruction.")
if not all(isinstance(compiled[field], list) for field in ("preserve", "avoid")):
    fail("Compiler response preserve/avoid values must be arrays.")
if not all(isinstance(item, str) for field in ("preserve", "avoid") for item in compiled[field]):
    fail("Compiler response preserve/avoid entries must be strings.")

print(
    json.dumps(
        {
            "elapsed_seconds": round(elapsed_seconds, 3),
            "image": {
                "bytes": len(image_bytes),
                "height": height,
                "mime_type": mime_type,
                "width": width,
            },
            "model": response_document["model"],
            "output": compiled,
            "raw_response_path": str(response_path),
        },
        indent=2,
    )
)