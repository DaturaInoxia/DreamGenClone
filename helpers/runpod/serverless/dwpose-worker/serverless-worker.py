"""DWPose Serverless worker (B-101 infra proof).

RunPod worker: lazily boots the in-image ComfyUI once per worker, then each job
runs the DWPreprocessor workflow (same as the pod's dwpose-extract-proof) and
returns the OpenPose JSON + rendered PNG. No SSH; debug via job logs.

Job input:  {"image_b64": "<data:image/png;base64,...> or raw b64>"}
Job output: {"keypoints": <OpenPose JSON>, "image_b64": "<rendered PNG b64>"}
"""

import base64
import json
import os
import subprocess
import time
import uuid
from pathlib import Path

import runpod
import requests

COMFYUI_DIR = "/ComfyUI"
COMFYUI_PORT = 8188
COMFYUI_HOST = f"http://127.0.0.1:{COMFYUI_PORT}"
INPUT_DIR = Path(COMFYUI_DIR) / "input"

# DWPose ckpts resolved by comfyui_controlnet_aux (into its ckpts dir). These are
# the pod's verified files (sha256 in the phase-0 identity model manifest); URLs are
# finalized at P1 by reusing the pod's existing files/URLs.
CKPT_FILES = {
    "dw-ll_ucoco_384_bs5.torchscript.pt": {
        "url": None,  # TODO(P1): exact URL; sha256 d86a0b2b59fddc0901a7076e9f59c9f8602602133ed72511c693fd11eea23d91
        "sha256": "d86a0b2b59fddc0901a7076e9f59c9f8602602133ed72511c693fd11eea23d91",
    },
    "yolox_l.torchscript.pt": {
        "url": None,  # TODO(P1): exact URL; sha256 80bc14b13c260c24b3014cd42c02994bf52296ab8fa2d80a60b6afe08c93ef42
        "sha256": "80bc14b13c260c24b3014cd42c02994bf52296ab8fa2d80a60b6afe08c93ef42",
    },
}

_comfy_started = False
_comfyui_proc = None


def _log(msg: str) -> None:
    print(f"[dwpose-worker] {msg}", flush=True)


def ensure_ckpts() -> None:
    """Download verified DWPose ckpts if not already present (idempotent, small)."""
    # Resolve the aux ckpts dir. ControlNet aux looks in
    #   <repo>/ckpts/dwpose  (i.e. /ComfyUI/custom_nodes/comfyui_controlnet_aux/ckpts/dwpose)
    # and downloads if missing. TODO(P1): implement exact download+sha256 verify here
    # (reuse helpers/runpod/author-touch-pose.py helpers or the pod's files).
    _log("ensure_ckpts: TODO(P1) - verify/download dw-ll_ucoco_384_bs5 + yolox_l")


def ensure_comfyui() -> None:
    """Start ComfyUI once per worker (cached across jobs in the same warm worker)."""
    global _comfy_started, _comfyui_proc
    if _comfy_started:
        return
    _log("booting ComfyUI...")
    _comfyui_proc = subprocess.Popen(
        ["python", "main.py", "--listen", "127.0.0.1", "--port", str(COMFYUI_PORT)],
        cwd=COMFYUI_DIR,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    for _ in range(60):  # up to ~2 min
        try:
            if requests.get(f"{COMFYUI_HOST}/system_stats", timeout=2).ok:
                _comfy_started = True
                _log("ComfyUI ready")
                return
        except requests.RequestException:
            pass
        time.sleep(2)
    raise RuntimeError("ComfyUI failed to become ready; see worker logs")


def _write_input_image(image_b64: str) -> str:
    """Decode the job image to a temp file ComfyUI's LoadImage can read. Returns filename."""
    if image_b64.startswith("data:"):
        image_b64 = image_b64.split(",", 1)[1]
    raw = base64.b64decode(image_b64)
    name = f"{uuid.uuid4().hex}.png"
    (INPUT_DIR / name).write_bytes(raw)
    return name


def build_workflow(image_name: str) -> dict:
    """Same shape as helpers/runpod/workflows/dwpose-extract-proof.json:
    LoadImage -> DWPreprocessor -> SaveImage.
    TODO(P1): finalize exact node ids + how the OpenPose JSON is serialized for
    return (the pod proof writes a .json artifact alongside the PNG)."""
    return {
        "1": {
            "class_type": "LoadImage",
            "inputs": {"image": image_name},
        },
        "2": {
            "class_type": "DWPreprocessor",
            "inputs": {"image": ["1", 0], "detect_hand": "enable", "detect_body": "enable", "detect_face": "enable"},
        },
        "3": {
            "class_type": "SaveImage",
            "inputs": {"filename_prefix": "dwpose_sls", "images": ["2", 0]},
        },
    }


def _run_comfyui_workflow(workflow: dict) -> dict:
    resp = requests.post(f"{COMFYUI_HOST}/prompt", json={"prompt": workflow}, timeout=30)
    resp.raise_for_status()
    prompt_id = resp.json()["prompt_id"]
    for _ in range(180):  # up to 6 min
        hist = requests.get(f"{COMFYUI_HOST}/history/{prompt_id}", timeout=15).json()
        if prompt_id in hist and hist[prompt_id].get("outputs"):
            return hist[prompt_id]["outputs"]
        time.sleep(2)
    raise TimeoutError("workflow did not finish in time")


def _fetch_png(outputs: dict) -> str:
    """Fetch the first SaveImage output as base64."""
    for node_out in outputs.values():
        for img in node_out.get("images", []):
            p = img["filename"]
            subdir = img.get("subfolder", "")
            r = requests.get(
                f"{COMFYUI_HOST}/view", params={"filename": p, "subfolder": subdir, "type": img.get("type", "output")},
                timeout=30,
            )
            r.raise_for_status()
            return base64.b64encode(r.content).decode()
    raise RuntimeError("no image in workflow outputs")


def handler(job):
    """RunPod job entrypoint. Returns {"keypoints": ..., "image_b64": ...}."""
    try:
        job_input = job.get("input") or {}
        image_b64 = job_input.get("image_b64")
        if not image_b64:
            return {"error": "missing 'image_b64' in job.input"}
        ensure_ckpts()
        ensure_comfyui()
        image_name = _write_input_image(image_b64)
        wf = build_workflow(image_name)
        outputs = _run_comfyui_workflow(wf)
        png_b64 = _fetch_png(outputs)
        # TODO(P1): also return the OpenPose JSON keypoints (pose-format JSON the
        # app/helpers consume via decode_json_as_poses) once the workflow emits it.
        return {"image_b64": png_b64, "keypoints": None}
    except Exception as exc:  # worker must always return; log + surface
        _log(f"job failed: {exc!r}")
        return {"error": str(exc)}


runpod.serverless.start({"handler": handler})
