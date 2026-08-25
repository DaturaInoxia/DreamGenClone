#!/usr/bin/env bash
set -euo pipefail

runtime=${1:?Usage: provision-runtime.sh <runtime-path>}
model_dir="$runtime/model"
venv="$runtime/.venv"
python_install_dir="$runtime/python"
model_repo=Qwen/Qwen2.5-VL-7B-Instruct-AWQ
model_revision=536a35794df8831aa814970ee8f89eff577e7718
vllm_version=0.27.1
python_version=3.13.2
min_root_free_bytes=1073741824
minimum_initial_workspace_available_bytes=37044092928
minimum_model_download_available_bytes=28991029248
minimum_final_workspace_available_bytes=21474836480

: "${QWEN_VL_WORKSPACE_CAPACITY_GIB:?Set QWEN_VL_WORKSPACE_CAPACITY_GIB to the configured RunPod volume size.}"
[[ "$QWEN_VL_WORKSPACE_CAPACITY_GIB" =~ ^[1-9][0-9]*$ ]] || {
    printf 'QWEN_VL_WORKSPACE_CAPACITY_GIB must be a positive integer.\n' >&2
    exit 1
}
workspace_capacity_bytes=$((QWEN_VL_WORKSPACE_CAPACITY_GIB * 1024 * 1024 * 1024))

require_free_space() {
    local path=$1 required_bytes=$2 label=$3 available_bytes
    available_bytes=$(df --output=avail -B1 "$path" | tail -n 1 | tr -d ' ')
    (( available_bytes >= required_bytes )) || {
        printf '%s free space is below requirement: available=%s required=%s\n' "$label" "$available_bytes" "$required_bytes" >&2
        exit 1
    }
}

require_workspace_available() {
    local required_bytes=$1 label=$2 used_bytes available_bytes
    used_bytes=$(du -sx -B1 /workspace | cut -f1)
    (( used_bytes <= workspace_capacity_bytes )) || {
        printf '/workspace usage exceeds configured capacity: used=%s capacity=%s\n' "$used_bytes" "$workspace_capacity_bytes" >&2
        exit 1
    }
    available_bytes=$((workspace_capacity_bytes - used_bytes))
    printf 'WORKSPACE_PREFLIGHT label=%s capacity=%s used=%s available=%s required=%s\n' \
        "$label" "$workspace_capacity_bytes" "$used_bytes" "$available_bytes" "$required_bytes"
    (( available_bytes >= required_bytes )) || {
        printf '/workspace available capacity is below requirement for %s.\n' "$label" >&2
        exit 1
    }
}

download_file() {
    local file=$1 target="$model_dir/$1"
    mkdir -p "$(dirname "$target")"
    curl --fail --location --silent --show-error --retry 5 --retry-delay 5 \
        --output "${target}.partial" \
        "https://huggingface.co/${model_repo}/resolve/${model_revision}/${file}"
    mv "${target}.partial" "$target"
}

verify_lfs_file() {
    local file=$1 expected_bytes=$2 expected_sha256=$3 target="$model_dir/$1"
    [[ -f "$target" ]] || { printf 'Missing required model artifact: %s\n' "$file" >&2; exit 1; }
    [[ "$(stat -c%s "$target")" == "$expected_bytes" ]] || { printf 'Unexpected bytes for %s\n' "$file" >&2; exit 1; }
    printf '%s  %s\n' "$expected_sha256" "$target" | sha256sum --check --status || {
        printf 'Unexpected SHA-256 for %s\n' "$file" >&2
        exit 1
    }
}

model_artifacts_complete() {
    local file
    for file in \
        LICENSE README.md added_tokens.json chat_template.json config.json generation_config.json \
        merges.txt model-00001-of-00002.safetensors model-00002-of-00002.safetensors \
        model.safetensors.index.json preprocessor_config.json special_tokens_map.json tokenizer.json \
        tokenizer_config.json vocab.json; do
        [[ -f "$model_dir/$file" ]] || return 1
    done
    [[ "$(stat -c%s "$model_dir/model-00001-of-00002.safetensors")" == 3982163944 ]] &&
        [[ "$(sha256sum "$model_dir/model-00001-of-00002.safetensors" | cut -d' ' -f1)" == 4f75e3de726546ee43620d1227d3596cd3ba0fdd19f11faeea71de578d2d1052 ]] &&
        [[ "$(stat -c%s "$model_dir/model-00002-of-00002.safetensors")" == 2941808440 ]] &&
        [[ "$(sha256sum "$model_dir/model-00002-of-00002.safetensors" | cut -d' ' -f1)" == dae4128bbfd2b8d489e838048edc0bbe6e31f269d9b96fa3effe11cc534b8f0c ]] &&
        [[ "$(stat -c%s "$model_dir/tokenizer.json")" == 11422063 ]] &&
        [[ "$(sha256sum "$model_dir/tokenizer.json" | cut -d' ' -f1)" == 5eee858c5123a4279c3e1f7b81247343f356ac767940b2692a928ad929543214 ]]
}

require_free_space / "$min_root_free_bytes" container-root

command -v uv >/dev/null || { printf 'uv is required but was not found.\n' >&2; exit 1; }
staging_dir=$(mktemp -d /dev/shm/qwen-vl-provision.XXXXXX)
trap 'rm -rf "$staging_dir"' EXIT
export TMPDIR="$staging_dir/tmp"
export UV_PYTHON_INSTALL_DIR="$python_install_dir"
mkdir -p "$TMPDIR"

model_download_required=true
if model_artifacts_complete; then
    model_download_required=false
fi

if [[ -d "$venv" ]] && ! "$venv/bin/python" -c \
    "import sys, vllm; assert sys.version_info[:3] == (3, 13, 2); assert vllm.__version__ == '${vllm_version}'" >/dev/null 2>&1; then
    printf 'Existing runtime is not Python %s with vLLM %s; remove the incompatible runtime before provisioning.\n' "$python_version" "$vllm_version" >&2
    exit 1
fi

if [[ ! -d "$venv" ]]; then
    require_workspace_available "$minimum_initial_workspace_available_bytes" runtime-and-model
    mkdir -p "$runtime"
    uv venv --python "$python_version" --python-preference only-managed "$venv"
    uv pip install --python "$venv/bin/python" \
        --no-cache --link-mode copy "vllm==${vllm_version}"
else
    require_workspace_available "$minimum_model_download_available_bytes" model-download
fi

"$venv/bin/python" -c \
    "import sys, torch, vllm; assert sys.version_info[:3] == (3, 13, 2); assert vllm.__version__ == '${vllm_version}'; assert torch.version.cuda == '13.0'; assert torch.cuda.is_available()"

if [[ "$model_download_required" == true ]]; then
    require_workspace_available "$minimum_model_download_available_bytes" model-download

    for file in \
        LICENSE README.md added_tokens.json chat_template.json config.json generation_config.json \
        merges.txt model-00001-of-00002.safetensors model-00002-of-00002.safetensors \
        model.safetensors.index.json preprocessor_config.json special_tokens_map.json tokenizer.json \
        tokenizer_config.json vocab.json; do
        download_file "$file"
    done
fi

verify_lfs_file model-00001-of-00002.safetensors 3982163944 4f75e3de726546ee43620d1227d3596cd3ba0fdd19f11faeea71de578d2d1052
verify_lfs_file model-00002-of-00002.safetensors 2941808440 dae4128bbfd2b8d489e838048edc0bbe6e31f269d9b96fa3effe11cc534b8f0c
verify_lfs_file tokenizer.json 11422063 5eee858c5123a4279c3e1f7b81247343f356ac767940b2692a928ad929543214
require_workspace_available "$minimum_final_workspace_available_bytes" final-headroom
require_free_space / "$min_root_free_bytes" container-root

printf 'QWEN_VL_RUNTIME_PROVISIONED revision=%s vllm=%s path=%s\n' "$model_revision" "$vllm_version" "$runtime"