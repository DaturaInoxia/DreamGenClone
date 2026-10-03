#!/usr/bin/env bash
# download-training-models.sh - fetch + verify the Krea 2 LoRA TRAINING stack onto the network volume.
#
# WHY THIS EXISTS (do not "simplify" it back to the ComfyUI fp8 repacks):
#   Krea 2 is a SPLIT model (12B single-stream MMDiT + Qwen3-VL-4B-Instruct text encoder + Qwen Image
#   VAE). The ComfyUI variants of the DiT and the text encoder are fp8 repacks, and musubi-tuner
#   REJECTS them during training:
#     - TE  : loads with missing=[] but unexpected=['...weight_scale', '...comfy_quant']
#     - DiT : "Layer blocks.0.attn.gate.weight is already in torch.float8_e4m3fn format.
#              --fp8_scaled optimization should not be applied."
#   So TRAINING requires the **bf16** weights. (Inference on the local 5080 host still uses fp8.)
#
# Contents. Every size + sha256 below is cross-checked against the HuggingFace LFS metadata
# (`/api/models/{repo}/tree/main?recursive=true` -> `lfs.oid`), which is the authoritative source.
# They also match the copies already proven on the local training host WOOD-GAME-MAIN during the
# Phase 0 Krea 2 LoRA training proof (2026-09-27/28):
#   text encoder : Qwen3-VL 4B bf16                8,875,719,384 B   (ungated)
#   DiT          : Krea-2-Raw bf16, single file   26,283,332,608 B   (GATED -> needs HF_TOKEN)
#   VAE          : Qwen Image VAE                   253,806,246 B    (ungated)
#   total ~33 GiB - sized for the 50 GB "DreamGen_Krea2_Training" volume.
#
# Usage (on the provisioning pod, volume mounted at /workspace):
#   HF_TOKEN="$(cat /root/.hf_token)" bash download-training-models.sh [/workspace]
#
# NOTE ON MOUNT POINTS: a RunPod CPU/GPU *pod* mounts a network volume at /workspace, while a
# *serverless worker* mounts the SAME volume at /runpod-volume. Only the prefix differs - the
# layout inside the volume is identical, so $root defaults to /workspace here and the worker
# reads from /runpod-volume.
#
# Idempotent + resumable: a complete file is size-checked and sha256-verified then skipped; a
# partial file is resumed with curl --continue-at -. Safe to re-run after a pod restart/recycle.

set -euo pipefail

root="${1:-/workspace}"
token="${HF_TOKEN:-}"

curl_args=(--silent --show-error --fail --location --retry 20 --retry-delay 10 --continue-at -)
has_token=0
if [[ -n "$token" ]]; then
    curl_args+=(-H "Authorization: Bearer ${token}")
    has_token=1
fi

download_and_verify() {
    local url=$1 target=$2 expected_size=$3 expected_sha=$4 module=$5 partial="${2}.partial"
    mkdir -p "$(dirname "$target")"

    # Guard the hand-written constants. A truncated hash (63 chars instead of 64) makes
    # `sha256sum -c` answer "no properly formatted SHA256 checksum lines found", which reads like a
    # tooling bug rather than a typo - and `set -e` then aborts the run AFTER the multi-GiB download
    # has already finished. Cost of this guard: nothing. Cost of not having it: a stalled pipeline.
    if [[ ! "$expected_sha" =~ ^[0-9a-f]{64}$ ]]; then
        echo "ERROR: sha256 constant for ${target} is malformed: '${expected_sha}' (len ${#expected_sha}; need 64 lowercase hex)." >&2
        exit 1
    fi

    if [[ "$module" == "gated" && "$has_token" -eq 0 ]]; then
        echo "ERROR: ${url} is gated but HF_TOKEN is empty - refusing to start a 24 GiB download that will 401." >&2
        exit 1
    fi

    if [[ -f "$target" ]]; then
        if [[ "$(stat -c%s "$target")" == "$expected_size" ]]; then
            if ! echo "${expected_sha}  ${target}" | sha256sum -c -; then
                echo "ERROR: ${target} exists but its sha256 does NOT match - remove it and re-run." >&2
                exit 1
            fi
            echo "present+verified ${target}"
            return
        fi
        echo "ERROR: ${target} exists with size $(stat -c%s "$target") != expected ${expected_size} - remove it and re-run." >&2
        exit 1
    fi

    echo "downloading $(basename "$target") (${expected_size} bytes) ..."
    # shellcheck disable=SC2086
    curl "${curl_args[@]}" --output "$partial" "$url"

    if [[ "$(stat -c%s "$partial")" != "$expected_size" ]]; then
        echo "ERROR: ${partial} size $(stat -c%s "$partial") != expected ${expected_size} (partial kept, re-run to resume)." >&2
        exit 1
    fi
    if ! echo "${expected_sha}  ${partial}" | sha256sum -c -; then
        echo "ERROR: sha256 mismatch for ${partial} - the download is CORRUPT. Delete it and re-run." >&2
        exit 1
    fi
    mv "$partial" "$target"
    echo "ok ${target}"
}

download_and_verify \
    https://huggingface.co/Comfy-Org/Qwen3-VL/resolve/main/text_encoders/qwen3vl_4b_bf16.safetensors \
    "$root/models/text_encoders/qwen3vl_4b_bf16.safetensors" \
    8875719384 \
    36f3ff447ef59201722e8f9ce6020c9819fdcfba6aa2608c4e09b1c0ce114e34 \
    ungated

download_and_verify \
    https://huggingface.co/krea/Krea-2-Raw/resolve/main/raw.safetensors \
    "$root/models/diffusion_models/krea2_raw_bf16.safetensors" \
    26283332608 \
    f99bb0ff8e362b77342bc4994e0c50906fe7ef7074864b181b7d48d2fa6d03d7 \
    gated

download_and_verify \
    https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI/resolve/main/split_files/vae/qwen_image_vae.safetensors \
    "$root/models/vae/qwen_image_vae.safetensors" \
    253806246 \
    a70580f0213e67967ee9c95f05bb400e8fb08307e017a924bf3441223e023d1f \
    ungated

printf 'KREA2_TRAINING_MODELS_VERIFIED\n'
