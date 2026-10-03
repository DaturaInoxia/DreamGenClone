# Krea 2 character-LoRA TRAINING worker (RunPod Serverless).
#
# Unlike every other worker in this folder, there is NO stock base that does this job. The ComfyUI
# workers inherit their handler from `runpod/worker-comfyui`; nothing upstream trains Krea 2 LoRAs.
# So this image carries its own handler.py and its own runtime.
#
# BASE = pytorch/pytorch:2.11.0-cuda12.8-cudnn9-runtime, an EXACT match for the torch the recipe
# was proven on locally (torch 2.11.0+cu128, CUDA 12.8). Nothing downloads torch at build time.
#
# WHY cu128 COVERS THE WHOLE GPU CASCADE: cu128 ships kernels for sm_80 (A100), sm_89 (RTX 4090)
# and sm_120 (Blackwell RTX PRO 6000 / RTX 50-series), so ONE image can back every pool in the
# endpoint's candidate list. Blackwell sm_120 requires torch >= 2.7 built for cu128; an older cu12x
# wheel builds fine and then fails at kernel launch. Verify any base-image change against this.
#
# MODELS ARE NOT BAKED -- they live on network volume n5rainij0c (DreamGen_Krea2_Training, EU-RO-1)
# at /runpod-volume/models/*, staged and sha256-verified by
# helpers/runpod/serverless/krea2-training/download-training-models.sh.
# The bf16 weights are REQUIRED: musubi rejects the ComfyUI fp8 repacks of both the DiT
# ("--fp8_scaled optimization should not be applied") and the text encoder ("unexpected keys
# weight_scale / comfy_quant"). Do not "optimise" this back to the fp8 files.
#
# A serverless worker mounts a network volume at /runpod-volume (a pod mounts the same volume at
# /workspace -- only the prefix differs).
#
# DEPLOYMENT: RunPod GitHub Integration ("Start from GitHub Repo", repo DaturaInoxia/DreamGenClone,
# branch development), same as the DWPose/Juggernaut workers. Images are NOT published to GHCR.
FROM pytorch/pytorch:2.11.0-cuda12.8-cudnn9-runtime

# PYTHONUTF8 / PYTHONIOENCODING are MANDATORY, not cosmetic. musubi prints Japanese progress text
# and dies with UnicodeEncodeError(cp1252) inside accelerator.print() at trainer_base.py when they
# are unset. This cost a debugging cycle on the local host; do not remove them.
ENV DEBIAN_FRONTEND=noninteractive \
    PYTHONUNBUFFERED=1 \
    PYTHONUTF8=1 \
    PYTHONIOENCODING=utf-8 \
    PIP_NO_CACHE_DIR=1 \
    HF_HUB_ENABLE_HF_TRANSFER=0 \
    MUSUBI_DIR=/opt/musubi-tuner

# libgl1 + libglib2.0-0 are required by opencv-python (a declared musubi dependency). Without them
# `import cv2` fails with "libGL.so.1: cannot open shared object file", which surfaces as an opaque
# import error deep inside the dataset loader.
RUN apt-get update \
 && apt-get install -y --no-install-recommends git libgl1 libglib2.0-0 \
 && rm -rf /var/lib/apt/lists/*

# musubi-tuner pinned to the exact upstream revision the recipe was proven on (branch main, clean
# tree -- verified not a patched fork).
# `pip install <dir>` pulls musubi's declared runtime deps (diffusers==0.40.0, transformers==5.17.0,
# accelerate==1.15.0, bitsandbytes, safetensors==0.8.0, ...) but NOT torch: torch is only an optional
# extra in musubi's pyproject, and the base image already provides the matching cu128 build. That is
# deliberate -- it keeps torch 2.11.0+cu128 exactly as proven and avoids a ~3 GB download.
#
# --break-system-packages IS REQUIRED, NOT LAZY. This base is Ubuntu 24.04 with the distro Python
# 3.12 (the log path /usr/share/doc/python3.12/README.venv confirms it), so PEP 668 marks the
# interpreter EXTERNALLY-MANAGED and a plain `pip install` aborts with `error:
# externally-managed-environment`. That is what killed the first build (run 37097128803, 2026-10-03).
# The flag is the correct answer here because the base image's own torch install is already in this
# interpreter -- we are adding to the same environment the image ships, not fighting it. A venv would
# also work but would then have to inherit system site-packages to see that torch, which is a more
# fragile arrangement for no benefit in a single-purpose container.
# The build-check line below is the real guard: if pip and `python` ever resolve to different
# interpreters, it fails loudly here instead of at job runtime.
ARG MUSUBI_REV=f8a1b03794a49239a3539015075f5123d6c07d66
RUN git clone https://github.com/kohya-ss/musubi-tuner.git "$MUSUBI_DIR" \
 && git -C "$MUSUBI_DIR" checkout "$MUSUBI_REV" \
 && pip install --break-system-packages "$MUSUBI_DIR" \
 && pip install --break-system-packages "runpod==1.12.0" \
 && python -c "import torch, musubi_tuner, runpod; print('build check: torch', torch.__version__, '| cuda', torch.version.cuda)"

# The Krea 2 CLI entry points are NOT console scripts -- they are loose files at the musubi repo root
# that do `from musubi_tuner.krea2_train_network import main`. The handler therefore invokes them as
# `<python> /opt/musubi-tuner/krea2_train_network.py ...` with cwd=$MUSUBI_DIR, which is exactly how
# they were run on the local host.
WORKDIR /opt/worker
COPY handler.py /opt/worker/handler.py
CMD ["python", "-u", "/opt/worker/handler.py"]
