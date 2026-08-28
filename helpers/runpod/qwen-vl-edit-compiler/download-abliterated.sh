#!/usr/bin/env bash
# One-off: download + verify the abliterated Qwen2.5-VL-7B compiler model into model-new/
# (stock model keeps serving until the swap step swaps directories and restarts vLLM).
set -euo pipefail

runtime=/workspace/qwen-vl-edit-compiler
target="$runtime/model-new"
model_repo=huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated
model_revision=fa935a7958b3669b194c7ba4d1cfcebbe222641d

mkdir -p "$target"
cd "$target"

for file in \
    README.md added_tokens.json chat_template.json config.json generation_config.json \
    merges.txt model-00001-of-00004.safetensors model-00002-of-00004.safetensors \
    model-00003-of-00004.safetensors model-00004-of-00004.safetensors \
    model.safetensors.index.json preprocessor_config.json special_tokens_map.json tokenizer.json \
    tokenizer_config.json vocab.json; do
    echo "DOWNLOAD $file"
    curl --fail --location --silent --show-error --retry 5 --retry-delay 5 \
        --output "${file}.partial" \
        "https://huggingface.co/${model_repo}/resolve/${model_revision}/${file}"
    mv "${file}.partial" "$file"
done

verify() {
    local f=$1 b=$2 h=$3
    test "$(stat -c%s "$f")" = "$b" || { echo "BYTES FAIL $f"; exit 1; }
    echo "$h  $f" | sha256sum --check --status || { echo "SHA FAIL $f"; exit 1; }
    echo "VERIFIED $f"
}
verify model-00001-of-00004.safetensors 4968243304 f0e50bc0335e3df461a6074427f515d1a72a382baa4db543f750458dbb08784e
verify model-00002-of-00004.safetensors 4991495816 1868c4a49b6efdfe5bc013caad8c011ba5c792c1b42f8299dd40f4d0bf88d9d1
verify model-00003-of-00004.safetensors 4932751040 95b14c72ecc9ad756e370adf0b0fd4bb93ffc00b9903db594c7d693c82fa7dbd
verify model-00004-of-00004.safetensors 1691924384 77aa50ed00c662b9e8d211cdd240d3383398f471fc12d1ca4a0473506831ae2a
verify tokenizer.json 7031645 c0382117ea329cdf097041132f6d735924b697924d6f6fc3945713e96ce87539

echo "DOWNLOAD_AND_VERIFY_COMPLETE"
