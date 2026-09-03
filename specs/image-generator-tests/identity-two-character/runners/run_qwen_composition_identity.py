#!/usr/bin/env python3
"""Run the frozen C1/C2/C3 matrix on the Qwen Rapid-AIO serverless editor."""

import argparse
import base64
import hashlib
import json
import os
import sys
import time
import urllib.request
from datetime import datetime, timezone


HERE = os.path.dirname(os.path.abspath(__file__))
SUITE = os.path.dirname(HERE)
DEFAULT_SOURCE_DIR = os.path.join(SUITE, "images", "matrix")
DEFAULT_REF_DIR = os.path.join(SUITE, "refs")
DEFAULT_OUT = os.path.join("artifacts", "tmp", "qwen-composition-identity")
DEFAULT_ENDPOINT_ID = "79wkn5jz5d5txx"
DEFAULT_BASE = f"https://api.runpod.ai/v2/{DEFAULT_ENDPOINT_ID}"
ENDPOINT_KEY = "img-qwen-edit-serverless"
USER_AGENT = "Mozilla/5.0 (compatible; DreamGenClone/1.0)"
CELLS = ("c1", "c2", "c3")
SEEDS = (1001, 1002)

PROMPTS = {
    "c1": (
        "Image 1 is the source composition. Image 2 is Dean's identity reference and image 3 "
        "is Becky's identity reference. Preserve the source composition, camera, bodies, pose, "
        "clothing, lighting, and background. Replace only the left person's face and hair with "
        "Dean from image 2 and only the right person's face and hair with Becky from image 3. "
        "Keep both people facing the camera. Do not swap or blend identities."
    ),
    "c2": (
        "Image 1 is the source composition. Image 2 is Dean's identity reference and image 3 "
        "is Becky's identity reference. Preserve the source face-to-face composition, camera, "
        "bodies, pose, clothing, lighting, and background. Replace only the left person's face "
        "and hair with Dean from image 2 and only the right person's face and hair with Becky "
        "from image 3, preserving their inward head angles. Do not swap or blend identities."
    ),
    "c3": (
        "Image 1 is the source composition. Image 2 is Dean's identity reference and image 3 "
        "is Becky's identity reference. Preserve the source embrace composition, camera, bodies, "
        "contact, clothing, lighting, and background. Replace only the left person's face and "
        "hair with Dean from image 2 and only the right person's face and hair with Becky from "
        "image 3, preserving their head angles. Do not swap or blend identities."
    ),
}


def find_reference(directory, stem):
    for extension in (".png", ".jpg", ".jpeg", ".webp"):
        candidate = os.path.join(directory, stem + extension)
        if os.path.isfile(candidate):
            return candidate
    raise FileNotFoundError(f"Missing {stem} reference in {directory}")


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def build_workflow(source_name, dean_name, becky_name, cell, seed):
    images = {
        "1": {"class_type": "LoadImage", "inputs": {"image": source_name}},
        "17": {"class_type": "LoadImage", "inputs": {"image": dean_name}},
        "18": {"class_type": "LoadImage", "inputs": {"image": becky_name}},
        "2": {"class_type": "FluxKontextImageScale", "inputs": {"image": ["1", 0]}},
        "19": {"class_type": "FluxKontextImageScale", "inputs": {"image": ["17", 0]}},
        "20": {"class_type": "FluxKontextImageScale", "inputs": {"image": ["18", 0]}},
    }
    encode_inputs = {
        "clip": ["16", 1],
        "vae": ["16", 2],
        "image1": ["2", 0],
        "image2": ["19", 0],
        "image3": ["20", 0],
    }
    return images | {
        "3": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["14", 0], "positive": ["12", 0], "negative": ["13", 0],
                "latent_image": ["8", 0], "seed": seed, "steps": 8, "cfg": 1.0,
                "sampler_name": "euler_ancestral", "scheduler": "beta", "denoise": 1.0,
            },
        },
        "5": {"class_type": "ModelSamplingAuraFlow", "inputs": {"model": ["16", 0], "shift": 3.1}},
        "6": {"class_type": "TextEncodeQwenImageEditPlus", "inputs": encode_inputs | {"prompt": PROMPTS[cell]}},
        "7": {"class_type": "TextEncodeQwenImageEditPlus", "inputs": encode_inputs | {"prompt": ""}},
        "8": {"class_type": "VAEEncode", "inputs": {"pixels": ["2", 0], "vae": ["16", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"images": ["15", 0], "filename_prefix": f"qwen-composition-identity/{cell}_s{seed}"}},
        "12": {"class_type": "FluxKontextMultiReferenceLatentMethod", "inputs": {"conditioning": ["6", 0], "reference_latents_method": "index_timestep_zero"}},
        "13": {"class_type": "FluxKontextMultiReferenceLatentMethod", "inputs": {"conditioning": ["7", 0], "reference_latents_method": "index_timestep_zero"}},
        "14": {"class_type": "CFGNorm", "inputs": {"model": ["5", 0], "strength": 1.0}},
        "15": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["16", 2]}},
        "16": {
            "class_type": "CheckpointLoaderSimple",
            "inputs": {"ckpt_name": "Qwen-Rapid-AIO-NSFW-v23.safetensors"},
        },
    }


def canonical_json(value):
    return json.dumps(value, ensure_ascii=True, separators=(",", ":"), sort_keys=True)


def validate_workflow(workflow):
    sampler = workflow["3"]["inputs"]
    assert (sampler["steps"], sampler["cfg"], sampler["sampler_name"], sampler["scheduler"]) == (8, 1.0, "euler_ancestral", "beta")
    assert workflow["5"]["inputs"]["shift"] == 3.1
    assert workflow["14"]["inputs"]["strength"] == 1.0
    assert workflow["16"]["class_type"] == "CheckpointLoaderSimple"
    assert workflow["16"]["inputs"]["ckpt_name"] == "Qwen-Rapid-AIO-NSFW-v23.safetensors"
    workflow_json = canonical_json(workflow)
    for forbidden_loader in ("UNETLoader", "CLIPLoader", "VAELoader"):
        assert forbidden_loader not in workflow_json
    for encoder_id in ("6", "7"):
        inputs = workflow[encoder_id]["inputs"]
        assert inputs["image1"] == ["2", 0]
        assert inputs["image2"] == ["19", 0]
        assert inputs["image3"] == ["20", 0]


def image_payload(path):
    with open(path, "rb") as stream:
        encoded = base64.b64encode(stream.read()).decode("ascii")
    mime_type = "image/jpeg" if path.lower().endswith((".jpg", ".jpeg")) else "image/webp" if path.lower().endswith(".webp") else "image/png"
    return {"name": os.path.basename(path), "image": f"data:{mime_type};base64,{encoded}"}


def request_json(base_url, path, api_key, body=None, timeout=120):
    data = None if body is None else json.dumps(body).encode()
    headers = {"User-Agent": USER_AGENT, "Authorization": f"Bearer {api_key}"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(base_url + path, data=data, headers=headers)
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode())


def execute_case(base_url, api_key, payload, output_path):
    response = request_json(base_url, "/run", api_key, payload, timeout=120)
    job_id = response.get("id")
    if not job_id:
        raise RuntimeError(f"RunPod submit returned no job id: {json.dumps(response)}")
    deadline = time.time() + 900
    while time.time() < deadline:
        time.sleep(5)
        status = request_json(base_url, f"/status/{job_id}", api_key, timeout=60)
        state = status.get("status")
        if state not in ("COMPLETED", "FAILED", "CANCELLED", "TIMED_OUT"):
            continue
        if state != "COMPLETED":
            raise RuntimeError(f"RunPod job {job_id} ended as {state}: {json.dumps(status)}")
        images = status.get("output", {}).get("images", [])
        encoded = next((image.get("data") for image in images if image.get("type") == "base64" and image.get("data")), None)
        if not encoded:
            raise RuntimeError(f"RunPod job {job_id} completed without output.images[].data")
        if encoded.startswith("data:"):
            encoded = encoded.split(",", 1)[1]
        with open(output_path, "wb") as output_stream:
            output_stream.write(base64.b64decode(encoded))
        return job_id
    raise TimeoutError(f"Qwen edit timed out for {output_path}")


def case_inputs(source_dir, reference_dir, cell, seed):
    source = os.path.join(source_dir, f"{cell}_s{seed}.png")
    if not os.path.isfile(source):
        raise FileNotFoundError(f"Missing frozen source composition: {source}")
    return source, find_reference(reference_dir, "dean_face"), find_reference(reference_dir, "becky_face")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default=DEFAULT_BASE, help=f"RunPod Serverless endpoint base (default: {DEFAULT_BASE})")
    parser.add_argument("--source-dir", default=DEFAULT_SOURCE_DIR)
    parser.add_argument("--ref-dir", default=DEFAULT_REF_DIR)
    parser.add_argument("--out", default=DEFAULT_OUT)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    cases = []
    for cell in CELLS:
        for seed in SEEDS:
            source, dean, becky = case_inputs(args.source_dir, args.ref_dir, cell, seed)
            workflow = build_workflow(os.path.basename(source), os.path.basename(dean), os.path.basename(becky), cell, seed)
            validate_workflow(workflow)
            cases.append((cell, seed, source, dean, becky, workflow))
    if args.validate_only:
        print(f"VALIDATED {len(cases)} frozen Qwen composition-first cases")
        return
    api_key = os.environ.get("RUNPOD_API_KEY")
    if not api_key:
        parser.error("RUNPOD_API_KEY is required for live execution")

    os.makedirs(args.out, exist_ok=True)
    manifest = {
        "runId": os.path.basename(os.path.abspath(args.out)),
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "endpoint": args.base,
        "endpointKey": ENDPOINT_KEY,
        "endpointId": DEFAULT_ENDPOINT_ID,
        "protocol": "ComfyUiServerless",
        "model": "Qwen-Rapid-AIO-NSFW-v23.safetensors",
        "settings": {"steps": 8, "cfg": 1.0, "sampler": "euler_ancestral", "scheduler": "beta", "denoise": 1.0, "shift": 3.1, "cfgNorm": 1.0},
        "referenceOrder": ["source-composition", "dean-identity", "becky-identity"],
        "cases": [],
    }
    for cell, seed, source, dean, becky, workflow in cases:
        payload = {"input": {"workflow": workflow, "images": [image_payload(path) for path in (source, dean, becky)]}}
        output = os.path.join(args.out, f"{cell}_s{seed}.png")
        job_id = execute_case(args.base.rstrip("/"), api_key, payload, output)
        manifest["cases"].append({
            "cell": cell, "seed": seed, "jobId": job_id,
            "requestSha256": hashlib.sha256(canonical_json(payload).encode()).hexdigest(),
            "workflowSha256": hashlib.sha256(canonical_json(workflow).encode()).hexdigest(),
            "sourceSha256": sha256_file(source), "deanReferenceSha256": sha256_file(dean),
            "beckyReferenceSha256": sha256_file(becky), "outputSha256": sha256_file(output),
            "output": os.path.basename(output), "review": None,
        })
        print(f"SAVED {output} job_id={job_id}")
    with open(os.path.join(args.out, "run-manifest.json"), "w", encoding="ascii") as stream:
        json.dump(manifest, stream, indent=2, ensure_ascii=True)
        stream.write("\n")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, FileNotFoundError, RuntimeError, TimeoutError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(1)