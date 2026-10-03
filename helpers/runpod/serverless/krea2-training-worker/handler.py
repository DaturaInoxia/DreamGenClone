#!/usr/bin/env python3
"""Krea 2 character-LoRA training worker (RunPod Serverless).

One job = one LoRA. The job input is deliberately a thin, explicit description of only three things:

  * WHERE the training images already are on the network volume
  * HOW MANY epochs to run
  * WHAT to call the output

Everything else is the recipe proven on the local 16 GB RTX 5080 host (2026-09-27/28: 5 steps,
~47 s/step, peak VRAM 15,897 of 16,303 MiB), recorded as named constants below rather than scattered
through the code, and echoed back in the result so the caller can never be in doubt about what ran.

This module refuses to guess. A missing dataset directory, an image without a caption, a model file
that is absent or the wrong size, or a malformed parameter fails the job with an explicit message
naming the offending value. It never substitutes a default to keep going.

Job input
---------
  datasetVolumePath  str   REQUIRED. Absolute path on the volume to a directory of training images,
                           each paired with a same-stem .txt caption.
  outputName         str   REQUIRED. Written as <outputVolumeDir>/<outputName>.safetensors.
  epochs             int   optional, default 1.
  maxTrainSteps      int   optional. Caps the run for a cheap endpoint smoke test.
  networkDim         int   optional, default 32.
  networkAlpha       int   optional, default 32.
  learningRate       float optional, default 1e-4.
  blocksToSwap       int   optional, default 26 (the value proven on a 16 GB card).
  seed               int   optional, default 42.
  resolution         [w,h] optional, default [1024, 1024].
  outputVolumeDir    str   optional, default /runpod-volume/loras.

Job output
----------
  status, outputName, loraPath, loraBytes, loraSha256, datasetImages, epochs, steps, blocksToSwap,
  durationSec, gpu, recipe, logs
"""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

import runpod

# ---------------------------------------------------------------------------------------------
# Fixed locations. Both are set by the Dockerfile / by RunPod's volume mount contract, so they are
# constants rather than configuration.
# ---------------------------------------------------------------------------------------------
VOLUME_ROOT = Path("/runpod-volume")
MUSUBI_DIR = Path("/opt/musubi-tuner")
WORK_ROOT = Path("/tmp/krea2-jobs")

# Models on the network volume. Sizes are the verified byte counts recorded in
# helpers/runpod/serverless/krea2-training/download-training-models.sh and independently confirmed
# against HuggingFace's LFS metadata (lfs.oid at /api/models/{repo}/tree/main?recursive=true).
# The size check is a cheap guard against a truncated staging run; it does not replace sha256.
MODELS = {
    "dit": ("models/diffusion_models/krea2_raw_bf16.safetensors", 26283332608),
    "text_encoder": ("models/text_encoders/qwen3vl_4b_bf16.safetensors", 8875719384),
    "vae": ("models/vae/qwen_image_vae.safetensors", 253806246),
}

IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".webp", ".bmp"}

# ---------------------------------------------------------------------------------------------
# The proven recipe. Every value here was validated on real hardware; see the module docstring.
# ---------------------------------------------------------------------------------------------
PROVEN = {
    "epochs": 1,
    "network_dim": 32,
    "network_alpha": 32,
    "learning_rate": 1e-4,
    "optimizer_type": "adamw8bit",
    "mixed_precision": "bf16",
    "timestep_sampling": "shift",
    "weighting_scheme": "none",
    "discrete_flow_shift": 2.5,
    "blocks_to_swap": 26,
    "seed": 42,
    "max_data_loader_n_workers": 2,
    "output_volume_dir": "/runpod-volume/loras",
    "resolution": [1024, 1024],
}

# musubi's own defaults for these two are fine, but we pin them so the dtype can never drift with a
# library upgrade.
VAE_DTYPE = "bfloat16"
TEXT_ENCODER_DTYPE = "bfloat16"

NETWORK_MODULE = "networks.lora_krea2"

LOG_TAIL_CHARS = 6000


class TrainingFailure(RuntimeError):
    """A job-level failure whose message is meant to be read by a human in the RunPod job error."""


# ---------------------------------------------------------------------------------------------
# Input handling
# ---------------------------------------------------------------------------------------------
def _require_str(job_input: dict, key: str) -> str:
    if key not in job_input:
        raise TrainingFailure(f"job input is missing the required key '{key}'")
    value = job_input[key]
    if not isinstance(value, str) or not value.strip():
        raise TrainingFailure(f"job input '{key}' must be a non-empty string, got {value!r}")
    return value.strip()


def _optional_str(job_input: dict, key: str, default: str) -> str:
    if key not in job_input:
        return default
    value = job_input[key]
    if not isinstance(value, str) or not value.strip():
        raise TrainingFailure(f"job input '{key}' must be a non-empty string, got {value!r}")
    return value.strip()


def _optional_int(job_input: dict, key: str, default: int, minimum: int) -> int:
    if key not in job_input:
        return default
    value = job_input[key]
    if isinstance(value, bool) or not isinstance(value, int):
        raise TrainingFailure(f"job input '{key}' must be an integer, got {value!r}")
    if value < minimum:
        raise TrainingFailure(f"job input '{key}' must be >= {minimum}, got {value}")
    return value


def _optional_float(job_input: dict, key: str, default: float, minimum: float) -> float:
    if key not in job_input:
        return default
    value = job_input[key]
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise TrainingFailure(f"job input '{key}' must be a number, got {value!r}")
    value = float(value)
    if value < minimum:
        raise TrainingFailure(f"job input '{key}' must be >= {minimum}, got {value}")
    return value


def _optional_resolution(job_input: dict) -> list[int]:
    if "resolution" not in job_input:
        return list(PROVEN["resolution"])
    value = job_input["resolution"]
    if (
        not isinstance(value, list)
        or len(value) != 2
        or any(isinstance(v, bool) or not isinstance(v, int) or v <= 0 for v in value)
    ):
        raise TrainingFailure(f"job input 'resolution' must be [width, height] positive ints, got {value!r}")
    return [int(value[0]), int(value[1])]


# ---------------------------------------------------------------------------------------------
# Preflight validation
# ---------------------------------------------------------------------------------------------
def _verify_models() -> dict[str, Path]:
    resolved: dict[str, Path] = {}
    for name, (rel, expected_size) in MODELS.items():
        path = VOLUME_ROOT / rel
        if not path.is_file():
            raise TrainingFailure(
                f"model '{name}' is missing at {path}. Stage the volume first with "
                "helpers/runpod/serverless/krea2-training/download-training-models.sh"
            )
        actual = path.stat().st_size
        if actual != expected_size:
            raise TrainingFailure(
                f"model '{name}' at {path} is {actual} bytes but {expected_size} were expected - "
                "the staging download is incomplete or corrupt; re-run download-training-models.sh"
            )
        resolved[name] = path
    return resolved


def _verify_dataset(dataset_dir: Path) -> list[Path]:
    if not dataset_dir.is_dir():
        raise TrainingFailure(f"datasetVolumePath {dataset_dir} is not a directory")

    images = sorted(p for p in dataset_dir.iterdir() if p.is_file() and p.suffix.lower() in IMAGE_SUFFIXES)
    if not images:
        raise TrainingFailure(
            f"datasetVolumePath {dataset_dir} contains no images "
            f"({', '.join(sorted(IMAGE_SUFFIXES))})"
        )

    uncaptioned = [p.name for p in images if not p.with_suffix(".txt").is_file()]
    if uncaptioned:
        raise TrainingFailure(
            f"{len(uncaptioned)} of {len(images)} image(s) have no same-stem .txt caption: "
            f"{uncaptioned[:8]}"
        )
    return images


def _verify_environment() -> None:
    for script in (
        "krea2_cache_latents.py",
        "krea2_cache_text_encoder_outputs.py",
        "krea2_train_network.py",
    ):
        if not (MUSUBI_DIR / script).is_file():
            raise TrainingFailure(f"musubi-tuner entry point {MUSUBI_DIR / script} is missing from the image")


# ---------------------------------------------------------------------------------------------
# Execution helpers
# ---------------------------------------------------------------------------------------------
def _env() -> dict[str, str]:
    env = dict(os.environ)
    # Belt and braces: the Dockerfile sets these, but a job must not die of a cp1252 encode error
    # because an image was rebuilt without them.
    env["PYTHONUTF8"] = "1"
    env["PYTHONIOENCODING"] = "utf-8"
    return env


def _run_step(label: str, cmd: list[str], log_path: Path) -> float:
    started = time.monotonic()
    with log_path.open("w", encoding="utf-8") as handle:
        handle.write("$ " + " ".join(cmd) + "\n\n")
        handle.flush()
        completed = subprocess.run(
            cmd,
            cwd=str(MUSUBI_DIR),
            stdout=handle,
            stderr=subprocess.STDOUT,
            env=_env(),
            check=False,
        )
    elapsed = time.monotonic() - started

    if completed.returncode != 0:
        raise TrainingFailure(
            f"step '{label}' failed with exit code {completed.returncode} after {elapsed:.1f}s.\n"
            f"--- tail of {log_path} ---\n{_tail(log_path)}"
        )
    return elapsed


def _tail(path: Path) -> str:
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:  # pragma: no cover - log unreadable
        return f"<could not read {path}: {exc}>"
    return text[-LOG_TAIL_CHARS:]


def _write_dataset_toml(path: Path, image_dir: Path, cache_dir: Path, resolution: list[int]) -> None:
    # Mirrors the dataset.toml that produced the proven run. enable_bucket + bucket_no_upscale=false
    # means musubi picks the nearest bucket >= the source resolution, which is what gave the
    # 0896x1152 and 1024x1024 buckets on the local host.
    path.write_text(
        "[general]\n"
        f"resolution = [{resolution[0]}, {resolution[1]}]\n"
        'caption_extension = ".txt"\n'
        "batch_size = 1\n"
        "enable_bucket = true\n"
        "bucket_no_upscale = false\n"
        "\n"
        "[[datasets]]\n"
        f'image_directory = "{image_dir}"\n'
        f'cache_directory = "{cache_dir}"\n'
        "num_repeats = 1\n",
        encoding="utf-8",
    )


def _sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _gpu_description() -> str:
    try:
        out = subprocess.run(
            ["nvidia-smi", "--query-gpu=name,memory.total", "--format=csv,noheader"],
            capture_output=True,
            text=True,
            check=False,
            timeout=30,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        return f"<nvidia-smi unavailable: {exc}>"
    return out.stdout.strip() or "<nvidia-smi returned nothing>"


# ---------------------------------------------------------------------------------------------
# Handler
# ---------------------------------------------------------------------------------------------
def handler(job: dict) -> dict:
    job_input = job.get("input") or {}
    if not isinstance(job_input, dict):
        raise TrainingFailure("job input must be an object")

    dataset_dir = Path(_require_str(job_input, "datasetVolumePath"))
    output_name = _require_str(job_input, "outputName")
    if "/" in output_name or "\\" in output_name:
        raise TrainingFailure(f"outputName must be a bare file name, got {output_name!r}")

    epochs = _optional_int(job_input, "epochs", PROVEN["epochs"], minimum=1)
    max_train_steps = _optional_int(job_input, "maxTrainSteps", 0, minimum=0)
    if max_train_steps == 0:
        # 0 is the sentinel meaning "no step cap"; musubi has no --max_train_steps of 0.
        max_train_steps = None
    network_dim = _optional_int(job_input, "networkDim", PROVEN["network_dim"], minimum=1)
    network_alpha = _optional_int(job_input, "networkAlpha", PROVEN["network_alpha"], minimum=1)
    learning_rate = _optional_float(job_input, "learningRate", PROVEN["learning_rate"], minimum=0.0)
    blocks_to_swap = _optional_int(job_input, "blocksToSwap", PROVEN["blocks_to_swap"], minimum=0)
    seed = _optional_int(job_input, "seed", PROVEN["seed"], minimum=0)
    resolution = _optional_resolution(job_input)
    output_volume_dir = Path(_optional_str(job_input, "outputVolumeDir", PROVEN["output_volume_dir"]))

    started = time.monotonic()
    gpu = _gpu_description()

    _verify_environment()
    models = _verify_models()
    images = _verify_dataset(dataset_dir)

    work_dir = WORK_ROOT / f"job-{job.get('id', 'local')}"
    if work_dir.exists():
        shutil.rmtree(work_dir)
    cache_dir = work_dir / "cache"
    train_out_dir = work_dir / "out"
    cache_dir.mkdir(parents=True)
    train_out_dir.mkdir(parents=True)

    dataset_toml = work_dir / "dataset.toml"
    _write_dataset_toml(dataset_toml, dataset_dir, cache_dir, resolution)

    step_seconds: dict[str, float] = {}
    step_seconds["cache_latents"] = _run_step(
        "cache_latents",
        [
            sys.executable,
            str(MUSUBI_DIR / "krea2_cache_latents.py"),
            "--dataset_config",
            str(dataset_toml),
            "--vae",
            str(models["vae"]),
            "--vae_dtype",
            VAE_DTYPE,
            "--skip_existing",
        ],
        work_dir / "cache_latents.log",
    )
    step_seconds["cache_text_encoder"] = _run_step(
        "cache_text_encoder",
        [
            sys.executable,
            str(MUSUBI_DIR / "krea2_cache_text_encoder_outputs.py"),
            "--dataset_config",
            str(dataset_toml),
            "--text_encoder",
            str(models["text_encoder"]),
            "--text_encoder_dtype",
            TEXT_ENCODER_DTYPE,
            "--skip_existing",
        ],
        work_dir / "cache_text_encoder.log",
    )

    train_cmd = [
        sys.executable,
        str(MUSUBI_DIR / "krea2_train_network.py"),
        "--dit",
        str(models["dit"]),
        "--vae",
        str(models["vae"]),
        "--dataset_config",
        str(dataset_toml),
        "--sdpa",
        "--mixed_precision",
        PROVEN["mixed_precision"],
        "--timestep_sampling",
        PROVEN["timestep_sampling"],
        "--weighting_scheme",
        PROVEN["weighting_scheme"],
        "--discrete_flow_shift",
        str(PROVEN["discrete_flow_shift"]),
        "--optimizer_type",
        PROVEN["optimizer_type"],
        "--learning_rate",
        str(learning_rate),
        "--gradient_checkpointing",
        "--max_data_loader_n_workers",
        str(PROVEN["max_data_loader_n_workers"]),
        "--persistent_data_loader_workers",
        "--network_module",
        NETWORK_MODULE,
        "--network_dim",
        str(network_dim),
        "--network_alpha",
        str(network_alpha),
        "--max_train_epochs",
        str(epochs),
        "--save_every_n_epochs",
        "1",
        "--seed",
        str(seed),
        "--fp8_base",
        "--fp8_scaled",
        "--blocks_to_swap",
        str(blocks_to_swap),
        "--output_dir",
        str(train_out_dir),
        "--output_name",
        output_name,
        # NOTE: deliberately NO --save_model_as. musubi's krea2_train_network.py does not recognise
        # that flag and exits 2; safetensors is already the default.
        # NOTE: deliberately NO --turbo_dit. It is incompatible with --blocks_to_swap.
    ]
    if max_train_steps is not None:
        train_cmd += ["--max_train_steps", str(max_train_steps)]

    step_seconds["train"] = _run_step("train", train_cmd, work_dir / "train.log")

    produced = train_out_dir / f"{output_name}.safetensors"
    if not produced.is_file():
        raise TrainingFailure(
            f"training reported success but {produced} does not exist. Files present: "
            f"{sorted(p.name for p in train_out_dir.iterdir())}\n"
            f"--- tail of train log ---\n{_tail(work_dir / 'train.log')}"
        )

    output_volume_dir.mkdir(parents=True, exist_ok=True)
    final_path = output_volume_dir / produced.name
    shutil.copyfile(produced, final_path)

    duration = time.monotonic() - started
    return {
        "status": "ok",
        "outputName": output_name,
        "loraPath": str(final_path),
        "loraBytes": final_path.stat().st_size,
        "loraSha256": _sha256_of(final_path),
        "datasetImages": len(images),
        "datasetVolumePath": str(dataset_dir),
        "gpu": gpu,
        "durationSec": round(duration, 1),
        "stepSeconds": {k: round(v, 1) for k, v in step_seconds.items()},
        "recipe": {
            "epochs": epochs,
            "maxTrainSteps": max_train_steps,
            "networkDim": network_dim,
            "networkAlpha": network_alpha,
            "learningRate": learning_rate,
            "blocksToSwap": blocks_to_swap,
            "seed": seed,
            "resolution": resolution,
            "optimizerType": PROVEN["optimizer_type"],
            "mixedPrecision": PROVEN["mixed_precision"],
            "discreteFlowShift": PROVEN["discrete_flow_shift"],
            "networkModule": NETWORK_MODULE,
            "vaeDtype": VAE_DTYPE,
            "textEncoderDtype": TEXT_ENCODER_DTYPE,
        },
        "logs": {
            "cacheLatents": _tail(work_dir / "cache_latents.log"),
            "cacheTextEncoder": _tail(work_dir / "cache_text_encoder.log"),
            "train": _tail(work_dir / "train.log"),
        },
    }


if __name__ == "__main__":
    runpod.serverless.start({"handler": handler})
