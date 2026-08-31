"""B-105 stage 1 proof: generate the approved SFW gpt-image-2 base (verified 2026-08-30).

Recreates the approved SFW position base used for the base-then-edit pipeline
(base -> faces -> detail). Requires env TOGETHER_API_KEY (set it in the terminal
yourself — do not paste it in chat).

Output: specs/image-generator-tests/baseline/positions/runs/kneeling-dean/gpt-base.png
"""

import base64
import json
import os
import sys
import urllib.request

API_URL = "https://api.together.xyz/v1/images/generations"
MODEL = "openai/gpt-image-2"
OUTPUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "positions", "runs", "kneeling-dean", "gpt-base.png",
)

# Approved SFW base prompt (verified 2026-08-30). The base is intentionally SFW — explicit detail
# is added later by the Qwen edit stages.
PROMPT = (
    "Full-body side-view photograph. A young woman in jeans and a button-up shirt kneels on the "
    "floor on both knees directly in front of a standing man, facing him. The man stands straight "
    "and upright facing forward, wearing blue jeans and a dark shirt, his hands at his sides. The "
    "woman's head is tilted down, her eyes looking toward the man's waist. Both are fully clothed. "
    "Warm indoor living-room lighting, photorealistic, 35mm, sharp focus, natural skin texture."
)


def main() -> int:
    key = os.environ.get("TOGETHER_API_KEY")
    if not key:
        print(
            "TOGETHER_API_KEY is not set. Set it in the terminal yourself "
            "(do not paste it in chat), e.g.  $env:TOGETHER_API_KEY='tgp_v1_...'  "
            "then re-run this script.",
            file=sys.stderr,
        )
        return 2

    body = json.dumps(
        {
            "model": MODEL,
            "prompt": PROMPT,
            "n": 1,
            "width": 1024,
            "height": 1024,
            "response_format": "b64_json",
        }
    ).encode("utf-8")

    req = urllib.request.Request(API_URL, data=body, method="POST")
    req.add_header("Authorization", f"Bearer {key}")
    req.add_header("Content-Type", "application/json")
    # Browser UA is required: Together's Cloudflare returns 403 error 1010 for non-browser UAs.
    req.add_header(
        "User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
        "(KHTML, like Gecko) Chrome/120.0 Safari/537.36",
    )

    print("Model:", MODEL)
    print("Size: 1024x1024")
    print("Prompt:")
    print(PROMPT)
    print("---")

    with urllib.request.urlopen(req, timeout=180) as resp:
        payload = json.loads(resp.read().decode("utf-8"))

    data = payload.get("data") or []
    if not data or not data[0].get("b64_json"):
        print("No image data in response:", payload, file=sys.stderr)
        return 1

    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    with open(OUTPUT, "wb") as f:
        f.write(base64.b64decode(data[0]["b64_json"]))
    print("Saved:", OUTPUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
