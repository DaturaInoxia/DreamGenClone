#!/usr/bin/env bash
set -euo pipefail

runtime="${1:-/workspace/comfyui-qwen-2511}"

download_and_verify() {
    local url=$1 target=$2 expected_size=$3 expected_sha=$4 partial="${2}.partial"
    mkdir -p "$(dirname "$target")"
    if [[ -f "$target" ]]; then
        [[ "$(stat -c%s "$target")" == "$expected_size" ]]
        echo "$expected_sha  $target" | sha256sum -c -
        return
    fi
    curl --silent --show-error --fail --location --retry 20 --retry-delay 10 --continue-at - --output "$partial" "$url"
    [[ "$(stat -c%s "$partial")" == "$expected_size" ]]
    echo "$expected_sha  $partial" | sha256sum -c -
    mv "$partial" "$target"
}

download_and_verify https://huggingface.co/Comfy-Org/Qwen-Image-Edit_ComfyUI/resolve/main/split_files/diffusion_models/qwen_image_edit_2511_fp8mixed.safetensors "$runtime/models/diffusion_models/qwen_image_edit_2511_fp8mixed.safetensors" 20533762817 c9fdc158e46d3b61ef75f21ae866ca2fe808bf4a53643120d1c1e87c19280a4e
download_and_verify https://huggingface.co/Comfy-Org/HunyuanVideo_1.5_repackaged/resolve/main/split_files/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors "$runtime/models/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors" 9384670680 cb5636d852a0ea6a9075ab1bef496c0db7aef13c02350571e388aea959c5c0b4
download_and_verify https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI/resolve/main/split_files/vae/qwen_image_vae.safetensors "$runtime/models/vae/qwen_image_vae.safetensors" 253806246 a70580f0213e67967ee9c95f05bb400e8fb08307e017a924bf3441223e023d1f
# MCNL v1 (ScottzillaSystems/qwen-image-edit-plus-nsfw-lora) - NSFW adapter for Qwen-Image-Edit-2511 (openrail++, ungated). Added 2026-08-28.
download_and_verify https://huggingface.co/ScottzillaSystems/qwen-image-edit-plus-nsfw-lora/resolve/main/qwen-image-edit-plus-nsfw-lora.safetensors "$runtime/models/loras/qwen-image-edit-plus-nsfw-lora.safetensors" 590058864 16c4841028615bb82c38e79756c0abad42494d85bca0daebc2939384a74d86bb

printf 'QWEN_MODELS_VERIFIED\n'