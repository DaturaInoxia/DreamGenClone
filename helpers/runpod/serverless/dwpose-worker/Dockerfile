# DWPose Serverless worker (B-101) - OFFICIAL RunPod worker-comfyui base + official tooling.
#
# Pivot (2026-08-28, user "are we doing it properly?" -> yes, pivot + use official tools):
#   - Base: RunPod's maintained `runpod/worker-comfyui:5.8.4-base` (AGPL-3.0). Standard API
#     contract (input.workflow + input.images -> output.images), WebSocket completion, SSH
#     debug via PUBLIC_KEY, native network-volume support. Our hand-rolled
#     serverless-worker.py is deleted (base provides the handler + ComfyUI startup).
#   - Node install: OFFICIAL `comfy-node-install` (installs node + pip deps, surfaces errors).
#   - Deployment: RunPod GitHub Integration ("Start from GitHub Repo", docs/deployment.md
#     Method 2) - RunPod clones this repo, builds this Dockerfile, auto-updates the endpoint
#     on every push. NO GHCR, NO GitHub Actions worker workflow.
#   - Network-volume integration DEFERRED (user: "we will look at this after, we may not need it").
#
# -base = clean ComfyUI runtime, NO models (~11-16 GB, vs 33 GB pod image). Newest -base tag
# is 5.8.4-base (5.8.7 has no -base on Docker Hub; verified 2026-08-28).
FROM runpod/worker-comfyui:5.8.4-base

# [1] DWPose custom node via the OFFICIAL tool (Comfy Registry release + its pip deps).
RUN comfy-node-install comfyui_controlnet_aux

# [2] DWPose ckpts - SPECIAL CASE, baked in. They live in the node's own
#     ckpts/<hf_repo_id>/<filename> layout (custom_hf_download), NOT ComfyUI models/, so the
#     official `comfy model download` cannot place them - hence this minimal Python step.
#     sha256 = pod-verified (identity model manifest). Deterministic cold start (no runtime
#     download; that hung the workflow on the pod image). Base has no curl/wget -> Python.
#     Base does not set hf_transfer, but keep it off defensively.
ENV HF_HUB_ENABLE_HF_TRANSFER=0
ARG AUX_CKPTS_ROOT=/comfyui/custom_nodes/comfyui_controlnet_aux/ckpts
RUN export AUX_CKPTS_ROOT="$AUX_CKPTS_ROOT" && python3 - <<'EOF'
import hashlib, os, urllib.request
root = os.environ["AUX_CKPTS_ROOT"]
files = {
    "hr16/DWPose-TorchScript-BatchSize5/dw-ll_ucoco_384_bs5.torchscript.pt": (
        "https://huggingface.co/hr16/DWPose-TorchScript-BatchSize5/resolve/main/dw-ll_ucoco_384_bs5.torchscript.pt",
        "d86a0b2b59fddc0901a7076e9f59c9f8602602133ed72511c693fd11eea23d91",
    ),
    "hr16/yolox-onnx/yolox_l.torchscript.pt": (
        "https://huggingface.co/hr16/yolox-onnx/resolve/main/yolox_l.torchscript.pt",
        "80bc14b13c260c24b3014cd42c02994bf52296ab8fa2d80a60b6afe08c93ef42",
    ),
}
for rel, (url, expected) in files.items():
    dest = os.path.join(root, rel)
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    print(f"downloading {url} -> {dest}")
    urllib.request.urlretrieve(url, dest)
    h = hashlib.sha256(open(dest, "rb").read()).hexdigest()
    assert h == expected, f"sha256 mismatch for {dest}: {h} (expected {expected})"
    print(f"verified {dest}")
EOF

# The official base image provides the handler entrypoint + ComfyUI startup. No CMD/COPY.
