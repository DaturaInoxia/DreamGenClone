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
    COMFYUI_ROOT     the ComfyUI install; its models/checkpoints, models/diffusion_models and models/loras are used
    LORA_TRAIN_ROOT  where run directories are written (images, captions, logs, checkpoints, artifacts)
    SD_PYTHON        python to run sd-scripts with (defaults to the interpreter running this service)

Serverless training (required only by a job whose trainerId is in SERVERLESS_TRAINERS)
------------------------------------------------------------------------------------
Krea 2 cannot be trained by kohya: it needs musubi-tuner, which is not installed here and whose 12B MMTDiT does
not fit the local card. Those jobs are therefore DISPATCHED to a RunPod Serverless endpoint running the
`dreamgen-krea2-training-worker` image, and this service acts as the bridge:

    images (staged here)  --S3-->  network volume  --train-->  LoRA on the volume  --S3-->  COMFYUI_ROOT/models/loras

Why a bridge rather than letting the app call RunPod directly: the app's RunPod training adapter posts the
canonical request alone, and that request carries dataset MEMBERSHIP, not image bytes - the worker would be told
to train on cells it cannot obtain. The app's LOCAL adapter already uploads the members' bytes as a zip, and
already accepts a host-absolute artifact path. Bridging here therefore needs NO app-side change: the same
adapter, the same request, the same status contract, a different trainer.

    RUNPOD_API_KEY              RunPod API key (submits and polls the training job)
    RUNPOD_TRAIN_ENDPOINT_ID    serverless endpoint id, e.g. h19lk2zv623y83
    RUNPOD_VOLUME_BUCKET        network volume id, e.g. n5rainij0c
    RUNPOD_VOLUME_ENDPOINT      S3-compatible endpoint, e.g. https://s3api-eu-ro-1.runpod.io
    S3_ACCESS_KEY / S3_SECRET_KEY
  boto3 is imported lazily, so a local-only host without it still starts.
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
import urllib.error
import urllib.request
import uuid
import zipfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import JSONResponse

app = FastAPI(title="DreamGenClone LoRA training", version="1")

# Trainer ids this service DISPATCHES to RunPod Serverless instead of training on this host. The app's job carries
# TrainerId as a free-form string (nothing in the app validates it), so registering a trainer is a service-side
# concern - no app code change is needed to add one.
SERVERLESS_TRAINERS = {"musubi-krea2-serverless-v1"}

# Where a RunPod SERVERLESS worker sees the network volume. A pod mounts the same volume at /workspace; only the
# prefix differs, and the worker reports its artifact path under this one.
RUNPOD_VOLUME_MOUNT = "/runpod-volume"


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
class ServerlessSettings:
    """What a serverless-dispatched job needs. Loaded PER JOB, not at startup.

    Deliberately not required at startup: this service is also installed on hosts that only ever train locally,
    and demanding a RunPod key from them would break a working deployment to enable a mode they never use. A
    serverless job that finds this incomplete fails loudly with the missing variable named.
    """

    api_key: str
    endpoint_id: str
    bucket: str
    endpoint: str
    region: str
    access_key: str
    secret_key: str
    job_timeout_seconds: int

    @staticmethod
    def load() -> "ServerlessSettings":
        return ServerlessSettings(
            api_key=required_env("RUNPOD_API_KEY"),
            endpoint_id=required_env("RUNPOD_TRAIN_ENDPOINT_ID"),
            bucket=required_env("RUNPOD_VOLUME_BUCKET"),
            endpoint=required_env("RUNPOD_VOLUME_ENDPOINT"),
            # boto3 requires SOME region name to sign with; the endpoint URL decides where the request actually
            # goes. Defaulting it cannot silently address the wrong volume the way a defaulted endpoint could.
            region=os.environ.get("RUNPOD_VOLUME_REGION", "eu-ro-1"),
            access_key=required_env("S3_ACCESS_KEY"),
            secret_key=required_env("S3_SECRET_KEY"),
            # Mirrors the endpoint's own executionTimeout (4h). Client-side only, so that a wedged job surfaces
            # as a failed run instead of polling until the heat death of the universe.
            job_timeout_seconds=int(os.environ.get("RUNPOD_TRAIN_TIMEOUT_SECONDS", "14400")),
        )


@dataclass
class RunState:
    run_id: str
    status: str = "IN_QUEUE"
    error: str | None = None
    history: list[dict[str, Any]] = field(default_factory=list)
    samples: list[dict[str, Any]] = field(default_factory=list)
    checkpoints: list[dict[str, Any]] = field(default_factory=list)
    logs: list[dict[str, Any]] = field(default_factory=lambda: [{"path": "train.log"}])
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
                "logs": self.logs,
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


# The folders a base model can live in, because the families disagree. kohya/sd-scripts load SDXL and Pony bases
# from models/checkpoints; musubi loads a Krea 2 (or Flux) base from models/diffusion_models. Reading only the first
# is why a Krea 2 base could not be picked at all - it was on the disk, in a folder nobody looked in.
CHECKPOINT_FOLDERS = ("checkpoints", "diffusion_models")


@app.get("/checkpoints")
def checkpoints() -> JSONResponse:
    """The base models this host can train against, with the checksum a profile must record.

    This exists so an operator never types a model file name or a SHA-256: both are facts of THIS machine, and a
    typed checksum is a checksum that can be wrong. Hashing a 7 GB checkpoint takes about a minute, so the result is
    cached next to the service and keyed on the file's byte length — the file changing its size invalidates it.

    Every entry carries the FOLDER it came from, and the name stays bare, because the name is the id the trainer
    joins with its own folder (a profile's baseModelId goes straight into that join). Without the folder, two files
    with the same name in different folders would be indistinguishable in the pick list.
    """
    if SETTINGS is None:
        raise HTTPException(status_code=503, detail="The service is not configured yet.")

    models_root = SETTINGS.comfyui_root / "models"
    cache_path = SETTINGS.train_root / "checkpoints.json"
    cache: dict[str, dict[str, Any]] = {}
    if cache_path.is_file():
        try:
            # Keyed on folder AND name. A cache written before the folder was recorded has no 'folder', so it raises
            # KeyError and the whole cache is discarded and rebuilt - a one-time rehash, rather than an entry matched
            # against the WRONG folder.
            cache = {
                f"{entry['folder']}/{entry['name']}": entry
                for entry in json.loads(cache_path.read_text(encoding="utf-8"))
            }
        except (OSError, ValueError, KeyError):
            cache = {}

    entries: list[dict[str, Any]] = []
    for folder in CHECKPOINT_FOLDERS:
        directory = models_root / folder
        if not directory.is_dir():
            continue
        for path in sorted(directory.glob("*.safetensors")):
            known = cache.get(f"{folder}/{path.name}")
            if known is not None and known.get("byteLength") == path.stat().st_size:
                entries.append(known)
                continue
            entries.append({
                "name": path.name,
                "folder": folder,
                "byteLength": path.stat().st_size,
                "sha256": sha256_of(path),
            })

    cache_path.parent.mkdir(parents=True, exist_ok=True)
    cache_path.write_text(json.dumps(entries), encoding="utf-8")
    return JSONResponse({
        "checkpointsRoot": str(models_root),
        "checkpointFolders": list(CHECKPOINT_FOLDERS),
        "checkpoints": entries,
    })


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
def _runpod_request(settings: "ServerlessSettings", method: str, path: str, body: dict[str, Any] | None = None) -> dict[str, Any]:
    """One call against the RunPod Serverless REST API, with the key in a header (never in a URL)."""
    url = f"https://api.runpod.ai/v2/{settings.endpoint_id}{path}"
    headers = {"Authorization": f"Bearer {settings.api_key}"}
    data = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body).encode("utf-8")
    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exception:
        detail = exception.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"RunPod {method} {path} failed with HTTP {exception.code}: {detail}") from exception


def _run_krea2_serverless(
    state: RunState, directory: Path, payload: dict[str, Any], manifest: list[dict[str, Any]]
) -> None:
    """Dispatch a musubi/Krea 2 LoRA to RunPod Serverless, then publish it into this host's loras folder.

    See the module docstring for why this is a bridge rather than app-side dispatch.
    """
    # Imported here so a local-only host without boto3 can still start and train.
    import boto3

    settings = ServerlessSettings.load()
    output_name = _output_name(payload)
    remote_prefix = f"datasets/{state.run_id}"
    log_path = directory / "runpod.log"
    client = boto3.client(
        "s3",
        aws_access_key_id=settings.access_key,
        aws_secret_access_key=settings.secret_key,
        region_name=settings.region,
        endpoint_url=settings.endpoint,
    )

    # 1. Push the staged images to the volume. They go under the run id, so two runs can never collide, and the
    #    worker is handed a directory rather than a manifest it would have to fetch from somewhere it cannot reach.
    images = directory / "images"
    uploaded = 0
    for entry in sorted(images.iterdir()):
        if not entry.is_file() or entry.name == "manifest.json":
            continue
        client.upload_file(str(entry), settings.bucket, f"{remote_prefix}/{entry.name}")
        uploaded += 1
    if uploaded == 0:
        raise RuntimeError(
            f"No dataset files were staged in {images} to upload. The uploaded dataset was empty, or its "
            "manifest named files the zip did not contain.")

    # 2. Submit. Every training value comes from the recipe the operator's profile carries, exactly as the kohya
    #    path does, so the run stays reproducible from the app's record of it.
    recipe = payload["recipe"]
    submitted: dict[str, Any] = {
        "datasetVolumePath": f"{RUNPOD_VOLUME_MOUNT}/{remote_prefix}",
        "outputName": output_name,
        "epochs": int(recipe["epochs"]),
        "networkDim": int(recipe["rank"]),
        "networkAlpha": int(recipe["alpha"]),
        "learningRate": float(recipe["unetLearningRate"]),
        "seed": int(payload["seed"]),
    }
    if int(recipe.get("steps", 0)) > 0:
        submitted["maxTrainSteps"] = int(recipe["steps"])
    (directory / "runpod-request.json").write_text(json.dumps(submitted, indent=2), encoding="utf-8")

    submission = _runpod_request(settings, "POST", "/run", {"input": submitted})
    remote_job = submission.get("id")
    if not remote_job:
        raise RuntimeError(f"RunPod accepted the job but returned no id: {submission}")
    state.note("IN_PROGRESS")

    # 3. Poll to a terminal state. `/runsync` is unusable here: it retains results for only a minute.
    terminal = {"COMPLETED", "FAILED", "CANCELLED", "TIMED_OUT"}
    deadline = time.monotonic() + settings.job_timeout_seconds
    status: dict[str, Any] = {}
    while True:
        if time.monotonic() > deadline:
            raise RuntimeError(
                f"RunPod training job {remote_job} did not reach a terminal state within "
                f"{settings.job_timeout_seconds}s (last status: {status.get('status')}).")
        status = _runpod_request(settings, "GET", f"/status/{remote_job}")
        if status.get("status") in terminal:
            break
        time.sleep(20)
    (directory / "runpod-status.json").write_text(json.dumps(status), encoding="utf-8")

    if status.get("status") != "COMPLETED":
        raise RuntimeError(
            f"RunPod training ended as {status.get('status')}: {status.get('error') or '(no diagnostic returned)'}")

    output = status.get("output") or {}
    artifact = output.get("artifact") or {}
    remote_path = str(artifact.get("fileRelativePath") or "")
    if not remote_path.startswith(f"{RUNPOD_VOLUME_MOUNT}/"):
        raise RuntimeError(
            f"The worker reported artifact path '{remote_path}', which is not under {RUNPOD_VOLUME_MOUNT}/, so it "
            "cannot be mapped back to an object key on the volume.")
    key = remote_path[len(RUNPOD_VOLUME_MOUNT) + 1 :]

    # 4. Publish where THIS host's ComfyUI can load it. Krea 2 renders on this machine, so a LoRA that stayed on
    #    the RunPod volume would be an artifact nobody could use.
    loras_dir = SETTINGS.comfyui_root / "models" / "loras"
    loras_dir.mkdir(parents=True, exist_ok=True)
    published = loras_dir / Path(remote_path).name
    client.download_file(settings.bucket, key, str(published))

    actual = sha256_of(published)
    expected = str(artifact.get("sha256") or "").upper()
    if expected and actual != expected:
        published.unlink(missing_ok=True)
        raise RuntimeError(f"Downloaded LoRA checksum {actual} does not match the worker's {expected}; artifact removed.")

    byte_length = published.stat().st_size
    reported_bytes = int(artifact.get("byteLength") or 0)
    if reported_bytes and byte_length != reported_bytes:
        raise RuntimeError(f"Downloaded LoRA is {byte_length} bytes but the worker reported {reported_bytes}.")

    state.artifact = {
        "fileRelativePath": str(published),
        "sha256": actual,
        "byteLength": byte_length,
        "loraName": published.name,
    }
    worker_logs = output.get("logs") or []
    if isinstance(worker_logs, list) and worker_logs:
        state.logs = [
            {"path": f"{entry.get('name', 'log')}.log"} for entry in worker_logs if isinstance(entry, dict)
        ]
        (directory / "runpod-logs.json").write_text(json.dumps(worker_logs), encoding="utf-8")
    state.note("COMPLETED")


def _run_training(state: RunState, directory: Path, payload: dict[str, Any], manifest: list[dict[str, Any]]) -> None:
    assert SETTINGS is not None

    # Trainer dispatch. A serverless trainer never touches kohya or this host's GPU; it stages here, trains on
    # RunPod, and comes back through the same RunState the app already polls.
    if str(payload.get("trainerId", "")).strip() in SERVERLESS_TRAINERS:
        try:
            _run_krea2_serverless(state, directory, payload, manifest)
        except Exception as exception:  # the poll response is the only place the operator can see this
            state.error = str(exception)
            state.note("FAILED")
        finally:
            write_state(directory, state)
        return

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
