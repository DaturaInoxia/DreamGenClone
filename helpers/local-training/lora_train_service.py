"""The LoRA training service that runs ON the ComfyUI host.

Why it lives there and not in the app
-------------------------------------
The app runs on a desktop with an 8 GB card; the base checkpoints, the ComfyUI runtime and the GPU that can hold a
1024 px SDXL LoRA all live on the ComfyUI host. The app therefore does not spawn a trainer - it posts a job over
HTTP and polls it, which is the same shape the RunPod serverless adapter already uses.

Contract (implemented by LocalCharacterLoraTrainingDispatchAdapter on the app side)
----------------------------------------------------------------------------------
POST {submit path}          multipart/form-data
      request  application/json   the app's canonical training request: job id, dataset id + manifest sha, base
                                  model id/version/sha, the training profile snapshot, the member list, seed,
                                  artifact version, and the recipe.
      dataset  application/zip    images/<NNN>_<cellKey>.<ext> and the same plus .txt (the caption), plus
                                  manifest.json tying each file to its member (ordinal, cellKey, split, role).
   -> {"id": "<runId>"}

GET  {status template}      -> {"status": "IN_QUEUE"|"IN_PROGRESS"|"COMPLETED"|"FAILED"|"CANCELLED",
                                "error": null | "...",
                                "output": {"artifact": {"fileRelativePath": "...", "sha256": "...",
                                                        "byteLength": 123, "loraName": "..."},
                                           "statusHistory": [...], "logs": [...], "samples": [...],
                                           "checkpoints": [...]}}

POST {cancel template}      -> {"status": "CANCELLED"} (best effort)

Run
---
    python -m venv .venv && .venv/Scripts/pip install -r requirements.txt
    set SD_SCRIPTS_DIR=D:\\sd-scripts
    set COMFYUI_ROOT=D:\\ComfyUI
    set LORA_TRAIN_ROOT=D:\\lora-training
    .venv/Scripts/python -m uvicorn lora_train_service:app --host 0.0.0.0 --port 8199

Environment (all required - the service refuses to start without them rather than guessing a path)
    SD_SCRIPTS_DIR   checkout of kohya's sd-scripts (must contain sdxl_train_network.py)
    COMFYUI_ROOT     the ComfyUI install; its models/checkpoints and models/loras are used
    LORA_TRAIN_ROOT  where run directories are written (images, captions, logs, checkpoints, artifacts)
    SD_PYTHON        python to run sd-scripts with (defaults to the interpreter running this service)
"""

from __future__ import annotations

import asyncio
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import time
import uuid
import zipfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import JSONResponse

app = FastAPI(title="DreamGenClone LoRA training", version="1")


# --------------------------------------------------------------------------------------------------------------
# Configuration. Every path is an environment variable: this service is installed on one specific machine, and a
# guessed default would silently train against the wrong model.
# --------------------------------------------------------------------------------------------------------------
def required_env(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise RuntimeError(f"{name} must be set; this service has no default for it.")
    return value


@dataclass
class Settings:
    sd_scripts_dir: Path
    comfyui_root: Path
    train_root: Path
    python: str

    @staticmethod
    def load() -> "Settings":
        settings = Settings(
            sd_scripts_dir=Path(required_env("SD_SCRIPTS_DIR")),
            comfyui_root=Path(required_env("COMFYUI_ROOT")),
            train_root=Path(required_env("LORA_TRAIN_ROOT")),
            python=os.environ.get("SD_PYTHON", sys.executable),
        )
        trainer = settings.sd_scripts_dir / "sdxl_train_network.py"
        if not trainer.is_file():
            raise RuntimeError(f"SD_SCRIPTS_DIR does not contain sdxl_train_network.py: {trainer}")
        settings.train_root.mkdir(parents=True, exist_ok=True)
        return settings


@dataclass
class RunState:
    run_id: str
    status: str = "IN_QUEUE"
    error: str | None = None
    history: list[dict[str, Any]] = field(default_factory=list)
    samples: list[dict[str, Any]] = field(default_factory=list)
    checkpoints: list[dict[str, Any]] = field(default_factory=list)
    artifact: dict[str, Any] | None = None
    process: subprocess.Popen | None = None

    def note(self, status: str) -> None:
        self.status = status
        self.history.append({"status": status, "at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())})

    def to_json(self) -> dict[str, Any]:
        return {
            "status": self.status,
            "error": self.error,
            "output": None if self.status != "COMPLETED" else {
                "artifact": self.artifact,
                "statusHistory": self.history,
                "logs": [{"path": "train.log"}],
                "samples": self.samples,
                "checkpoints": self.checkpoints,
            },
        }


SETTINGS: Settings | None = None
RUNS: dict[str, RunState] = {}
TRAINER_STACK: dict[str, str] = {}


@app.on_event("startup")
def _startup() -> None:
    global SETTINGS
    SETTINGS = Settings.load()


def trainer_stack() -> dict[str, str]:
    """The versions the TRAINER runs with, probed once through SD_PYTHON and cached.

    Probed rather than assumed: the app records these on every training profile as the environment the recipe was
    qualified in, and a hard-coded version there would be a claim about a machine this service cannot see from the
    app's side. They come from the interpreter that actually executes sdxl_train_network.py, not from this service's
    own environment, which is a different venv.
    """
    global TRAINER_STACK
    if TRAINER_STACK or SETTINGS is None:
        return TRAINER_STACK
    probe = (
        "import importlib.metadata as m, json\n"
        "out = {}\n"
        "for name in ('torch', 'torchvision', 'diffusers', 'transformers', 'accelerate', 'bitsandbytes', 'safetensors'):\n"
        "    try:\n"
        "        out[name] = m.version(name)\n"
        "    except Exception:\n"
        "        pass\n"
        "print(json.dumps(out))\n"
    )
    try:
        completed = subprocess.run(
            [SETTINGS.python, "-c", probe], capture_output=True, text=True, timeout=120, check=True)
        TRAINER_STACK = json.loads(completed.stdout.strip().splitlines()[-1])
    except Exception as exception:
        raise HTTPException(status_code=503, detail=f"The trainer stack could not be read: {exception}") from exception
    return TRAINER_STACK


def run_dir(run_id: str) -> Path:
    if SETTINGS is None:
        raise HTTPException(status_code=503, detail="The service is not configured yet.")
    return SETTINGS.train_root / run_id


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def tail_of(path: Path, lines: int = 12) -> str:
    """The interesting end of a log, so a failure reason travels to the app instead of only living on the host."""
    if not path.is_file():
        return "(no log was written)"
    text = path.read_text(encoding="utf-8", errors="replace").splitlines()
    return "\n".join(text[-lines:])


def write_state(directory: Path, state: RunState) -> None:
    """Persist the run's verdict. Every terminal path calls this, so a run ALWAYS has an on-disk answer.

    The first three real attempts did not: the early guard set the error in memory only, so the app held a
    submitted attempt whose run could not be found afterwards - no state, no log, no explanation.
    """
    (directory / "state.json").write_text(json.dumps(state.to_json()), encoding="utf-8")


# --------------------------------------------------------------------------------------------------------------
# Job intake
# --------------------------------------------------------------------------------------------------------------
@app.post("/train")
async def train(request: str = Form(...), dataset: UploadFile = File(...)) -> JSONResponse:
    if SETTINGS is None:
        raise HTTPException(status_code=503, detail="The service is not configured yet.")

    # Everything is validated and staged BEFORE the run is acknowledged. A 200 has to mean "this job exists on
    # disk", not "a directory was created": the app records a 200 as a submitted attempt, and an attempt with no
    # run behind it is worse than a refusal it can record as a failed submission.
    try:
        payload = json.loads(request)
    except ValueError as exception:
        raise HTTPException(status_code=400, detail=f"The job request was not valid JSON: {exception}") from exception

    body = await dataset.read()
    try:
        with zipfile.ZipFile(io.BytesIO(body)) as archive:
            names = [entry.filename for entry in archive.infolist() if not entry.is_dir()]
    except zipfile.BadZipFile as exception:
        raise HTTPException(status_code=400, detail=f"The uploaded dataset was not a zip: {exception}") from exception

    if "images/manifest.json" not in names:
        raise HTTPException(
            status_code=400,
            detail=f"The uploaded dataset carried no images/manifest.json, only: {', '.join(sorted(names)[:12])}")

    run_id = uuid.uuid4().hex
    directory = run_dir(run_id)
    directory.mkdir(parents=True)

    state = RunState(run_id=run_id)
    RUNS[run_id] = state
    state.note("IN_QUEUE")

    (directory / "request.json").write_text(json.dumps(payload), encoding="utf-8")
    (directory / "dataset.zip").write_bytes(body)

    images = directory / "images"
    images.mkdir()
    with zipfile.ZipFile(io.BytesIO(body)) as archive:
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            target = (images / Path(entry.filename).name)
            with archive.open(entry) as source, target.open("wb") as destination:
                shutil.copyfileobj(source, destination)

    manifest = json.loads((images / "manifest.json").read_text(encoding="utf-8"))
    if not manifest:
        state.error = "The uploaded dataset's manifest listed no members, so there is nothing to train on."
        state.note("FAILED")
        write_state(directory, state)
        return JSONResponse({"id": run_id})

    write_state(directory, state)
    state.note("IN_PROGRESS")
    write_state(directory, state)
    loop = asyncio.get_running_loop()
    loop.run_in_executor(None, _run_training, state, directory, payload, manifest)
    return JSONResponse({"id": run_id})


@app.get("/checkpoints")
def checkpoints() -> JSONResponse:
    """The checkpoints this host can actually train against, with the checksum a profile must record.

    This exists so an operator never types a model file name or a SHA-256: both are facts of THIS machine, and a
    typed checksum is a checksum that can be wrong. Hashing a 7 GB checkpoint takes about a minute, so the result is
    cached next to the service and keyed on the file's byte length — the file changing its size invalidates it.
    """
    if SETTINGS is None:
        raise HTTPException(status_code=503, detail="The service is not configured yet.")

    directory = SETTINGS.comfyui_root / "models" / "checkpoints"
    cache_path = SETTINGS.train_root / "checkpoints.json"
    cache: dict[str, dict[str, Any]] = {}
    if cache_path.is_file():
        try:
            cache = {entry["name"]: entry for entry in json.loads(cache_path.read_text(encoding="utf-8"))}
        except (OSError, ValueError, KeyError):
            cache = {}

    entries: list[dict[str, Any]] = []
    for path in sorted(directory.glob("*.safetensors")):
        known = cache.get(path.name)
        if known is not None and known.get("byteLength") == path.stat().st_size:
            entries.append(known)
            continue
        entries.append({"name": path.name, "byteLength": path.stat().st_size, "sha256": sha256_of(path)})

    cache_path.parent.mkdir(parents=True, exist_ok=True)
    cache_path.write_text(json.dumps(entries), encoding="utf-8")
    return JSONResponse({"checkpointsRoot": str(directory), "checkpoints": entries})


@app.get("/train/health")
def health() -> JSONResponse:
    """Declared before /train/{run_id} so the literal path wins. Used as the install smoke test."""
    if SETTINGS is None:
        raise HTTPException(status_code=503, detail="The service is not configured yet.")
    return JSONResponse({
        "status": "ok",
        "sdScriptsDir": str(SETTINGS.sd_scripts_dir),
        "comfyuiRoot": str(SETTINGS.comfyui_root),
        "trainRoot": str(SETTINGS.train_root),
        "trainer": trainer_stack(),
    })


@app.get("/train/{run_id}/log")
def run_log(run_id: str, tail: int = 200) -> JSONResponse:
    """The tail of a run's log, so a failure can be read without SSH and without the operator leaving the app."""
    path = run_dir(run_id) / "train.log"
    if not path.is_file():
        raise HTTPException(status_code=404, detail=f"Run '{run_id}' has no training log.")
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    return JSONResponse({"path": str(path), "lines": lines[-max(1, tail):]})


@app.get("/train/{run_id}")
def status(run_id: str) -> JSONResponse:
    state = RUNS.get(run_id)
    if state is None:
        directory = run_dir(run_id)
        if (directory / "state.json").is_file():
            return JSONResponse(json.loads((directory / "state.json").read_text(encoding="utf-8")))

        # An id this host has no trace of. Reporting it as a failure is the honest answer, not a 404 the caller has
        # to interpret: the app holds an attempt that claims it was submitted, and from the trainer's side the job
        # does not exist. Saying so lets that attempt reach a terminal state instead of hanging forever.
        return JSONResponse({
            "status": "FAILED",
            "error": (f"The training host has no record of run '{run_id}': no directory, no state and no log. "
                      "The job was acknowledged but never written to disk."),
            "output": None,
        })
    return JSONResponse(state.to_json())


@app.post("/train/{run_id}/cancel")
def cancel(run_id: str) -> JSONResponse:
    state = RUNS.get(run_id)
    if state is None:
        raise HTTPException(status_code=404, detail=f"Unknown run '{run_id}'.")
    if state.process is not None and state.process.poll() is None:
        state.process.terminate()
    state.note("CANCELLED")
    return JSONResponse({"status": "CANCELLED"})


# --------------------------------------------------------------------------------------------------------------
# Training
# --------------------------------------------------------------------------------------------------------------
def _run_training(state: RunState, directory: Path, payload: dict[str, Any], manifest: list[dict[str, Any]]) -> None:
    assert SETTINGS is not None
    log_path = directory / "train.log"
    try:
        recipe = payload["recipe"]
        base_model = SETTINGS.comfyui_root / "models" / "checkpoints" / str(payload["baseModelId"])
        if not base_model.is_file():
            raise RuntimeError(f"Base checkpoint not found on the training host: {base_model}")

        # kohya takes the repeat count from the TRAINING FOLDER's "N_concept" name. sdxl_train_network.py has no
        # --num_repeats argument at all: passing one makes the trainer refuse to start with "unrecognized
        # arguments", which is exactly how the first real run died.
        repeats = max(1, int(recipe.get("repeats", 1)))
        dataset_dir = directory / "dataset" / f"{repeats}_concept"
        dataset_dir.mkdir(parents=True, exist_ok=True)
        for member in manifest:
            shutil.copy(directory / "images" / member["fileName"], dataset_dir / member["fileName"])
            caption = (directory / "images" / member["fileName"]).with_suffix(".txt")
            if caption.is_file():
                shutil.copy(caption, dataset_dir / caption.name)

        staged = [entry for entry in dataset_dir.iterdir() if entry.suffix.lower() in (".png", ".jpg", ".jpeg", ".webp")]
        if not staged:
            raise RuntimeError(
                f"No training images were staged in {dataset_dir}. The uploaded dataset was empty, or its manifest "
                "named files the zip did not contain.")

        output_name = _output_name(payload)
        arguments = _arguments(SETTINGS, payload, recipe, base_model, dataset_dir, directory, output_name)
        (directory / "command.json").write_text(json.dumps(arguments), encoding="utf-8")

        with log_path.open("wb") as log:
            # UTF-8 stdio is REQUIRED, not cosmetic. sd-scripts prints localized (Japanese) messages, and a Windows
            # cp1252 stdout cannot encode them: the process dies with UnicodeEncodeError on its first print, AFTER
            # loading the dataset and building the LoRA, which reads like a training failure and is not one.
            environment = dict(os.environ)
            environment["PYTHONIOENCODING"] = "utf-8"
            environment["PYTHONUTF8"] = "1"
            state.process = subprocess.Popen(
                arguments, cwd=str(SETTINGS.sd_scripts_dir), stdout=log, stderr=subprocess.STDOUT, env=environment)
            code = state.process.wait()

        if code != 0:
            raise RuntimeError(f"Training exited with code {code}. Last lines of the log:\n{tail_of(log_path)}")

        trained = directory / "output" / f"{output_name}.safetensors"
        if not trained.is_file():
            raise RuntimeError(f"Training reported success but produced no file: {trained}")

        # The LoRA has to be where the ComfyUI on THIS host can load it, because that is the runtime that will use
        # it. The app records the host path; the loras folder is the name it loads it by.
        loras_dir = SETTINGS.comfyui_root / "models" / "loras"
        loras_dir.mkdir(parents=True, exist_ok=True)
        published = loras_dir / trained.name
        shutil.copy(trained, published)

        state.artifact = {
            "fileRelativePath": str(published),
            "sha256": sha256_of(published),
            "byteLength": published.stat().st_size,
            "loraName": published.name,
        }
        state.checkpoints = [{"path": str(entry.name)} for entry in (directory / "output").glob("*.safetensors")]
        state.samples = [{"path": str(entry.name)} for entry in (directory / "output").glob("sample*.png")]
        state.note("COMPLETED")
    except Exception as exception:  # the poll response is the only place the operator can see this
        state.error = str(exception)
        state.note("FAILED")
    finally:
        write_state(directory, state)


def _output_name(payload: dict[str, Any]) -> str:
    """Deterministic from the job's own identity, so a retry cannot overwrite an earlier artifact by accident."""
    model = Path(str(payload["baseModelId"])).stem
    return f"dgc_lora_{str(payload['datasetId'])[:8]}_{model}_v{int(payload['artifactVersion'])}"


def _arguments(
    settings: Settings,
    payload: dict[str, Any],
    recipe: dict[str, Any],
    base_model: Path,
    dataset_dir: Path,
    directory: Path,
    output_name: str,
) -> list[str]:
    """The kohya command line, built ONLY from the recipe the operator's profile carries.

    Every value comes from the profile: nothing here is a default of this service's own choosing, so the run is
    reproducible from the app's record of it.
    """
    output_dir = directory / "output"
    output_dir.mkdir(exist_ok=True)
    resolution = int(max(recipe["resolutionBuckets"]))
    arguments = [
        settings.python,
        str(settings.sd_scripts_dir / "sdxl_train_network.py"),
        "--pretrained_model_name_or_path", str(base_model),

        # kohya wants the PARENT of the folder(s) holding images, and takes the repeat count from the folder's own
        # "N_concept" name. Passing the concept folder itself makes it look for a subfolder and find no data.
        "--train_data_dir", str(dataset_dir.parent),
        "--output_dir", str(output_dir),
        "--output_name", output_name,
        "--caption_extension", ".txt",
        "--resolution", str(resolution),
        "--network_module", "networks.lora",
        "--network_dim", str(int(recipe["rank"])),
        "--network_alpha", str(int(recipe["alpha"])),
        "--learning_rate", str(float(recipe["unetLearningRate"])),
        "--text_encoder_lr", str(float(recipe["textEncoderLearningRate"])),
        "--max_train_epochs", str(int(recipe["epochs"])),
        "--train_batch_size", "1",
        "--save_model_as", "safetensors",
        "--mixed_precision", str(recipe["precision"]),
        "--save_precision", str(recipe["precision"]),
        "--optimizer_type", "AdamW8bit",
        "--lr_scheduler", "cosine_with_restarts",
        "--cache_latents",
        "--gradient_checkpointing",
        "--seed", str(int(payload["seed"])),
        "--noise_offset", "0.05",
        "--min_snr_gamma", "5",
        "--caption_dropout_rate", str(float(recipe["captionDropout"])),
    ]
    if int(recipe["steps"]) > 0:
        arguments += ["--max_train_steps", str(int(recipe["steps"]))]

    # No --num_repeats and no --save_every_n_steps. Repeats travel in the training folder's name, and the
    # checkpoint cadence lives in the profile's CheckpointCadenceJson, which the canonical request does not carry.
    # Rather than invent either here, the run saves its single final artifact.
    return arguments
