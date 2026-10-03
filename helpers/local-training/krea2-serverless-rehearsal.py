"""Rehearsal for the Krea 2 serverless dispatch path. Runs ON the ComfyUI host, costs nothing.

It calls the REAL `_run_krea2_serverless` -- not a copy of its logic -- and stubs only RunPod's own /run and
/status responses. Everything else is genuinely executed:

  * the staged dataset is genuinely uploaded to the network volume with the real credentials and the real boto3
    client construction,
  * the /run payload is genuinely built and recorded to runpod-request.json, so its shape can be inspected,
  * a REAL artifact that the endpoint already produced is genuinely downloaded from the volume and its sha256 and
    byte length are genuinely verified by the bridge's own code,
  * it is genuinely published into a SANDBOX loras folder (not the real one).

Only RunPod's side is faked, and the endpoint smoke test already proved that side. What remains unproven after this
is the poll loop's timing against a live job -- six lines that the smoke test's status API already exercised.

The rehearsal uploads under datasets/__rehearsal__ and DELETES it again, so the volume is left as it was found.
"""

from __future__ import annotations

import json
import os
import shutil
import sys
from pathlib import Path

SERVICE_DIR = Path(r"D:\lora-training-service")
SANDBOX = Path(r"D:\lora-training\rehearsal")
sys.path.insert(0, str(SERVICE_DIR))

# The endpoint's own smoke artifact, still on the volume. Using it means the download and checksum code paths run
# against real bytes rather than a fixture that could not fail.
REAL_ARTIFACT_PATH = "/runpod-volume/loras/krea2-endpoint-smoke.safetensors"
REAL_ARTIFACT_SHA = "1302AB75A5F7A536452FC5A6A4C0A2A88EB39F4DB191336540D2FB33595B3726"
REAL_ARTIFACT_BYTES = 469315352
REHEARSAL_RUN_ID = "__rehearsal__"


def load_bat(path: Path) -> None:
    if not path.is_file():
        return
    for line in path.read_bytes().decode("utf-8", errors="replace").splitlines():
        line = line.strip()
        if line.lower().startswith("set ") and "=" in line:
            name, _, value = line[4:].partition("=")
            os.environ[name.strip()] = value.strip()


load_bat(SERVICE_DIR / "start-service.bat")
load_bat(SERVICE_DIR / "serverless-secrets.bat")

import lora_train_service as svc  # noqa: E402

# A sandbox comfyui_root keeps the rehearsal out of the REAL loras folder, so a rehearsal can never overwrite a LoRA
# the operator actually wants.
if SANDBOX.exists():
    shutil.rmtree(SANDBOX)
staged_images = SANDBOX / "run" / "images"
staged_images.mkdir(parents=True, exist_ok=True)
for index in (1, 2):
    (staged_images / f"{index:02d}.png").write_bytes(b"\x89PNG\r\n\x1a\n" + bytes(512))
    (staged_images / f"{index:02d}.txt").write_text(f"a rehearsal caption {index}", encoding="utf-8")

svc.SETTINGS = svc.Settings(
    sd_scripts_dir=Path(r"D:\sd-scripts"),
    comfyui_root=SANDBOX / "comfyui",
    train_root=SANDBOX / "run",
    python=sys.executable,
)

# ---- stub only RunPod ------------------------------------------------------------------------------------------
submitted: dict = {}
status_calls: list[str] = []


def fake_runpod_request(settings, method, path, body=None):
    if method == "POST" and path == "/run":
        submitted.update(body["input"])
        return {"id": "rehearsal-job", "status": "IN_QUEUE"}
    if method == "GET" and path.startswith("/status/"):
        status_calls.append(path)
        return {
            "id": "rehearsal-job",
            "status": "COMPLETED",
            "executionTime": 492571,
            "output": {
                "artifact": {
                    "fileRelativePath": REAL_ARTIFACT_PATH,
                    "loraName": "krea2-endpoint-smoke.safetensors",
                    "sha256": REAL_ARTIFACT_SHA,
                    "byteLength": REAL_ARTIFACT_BYTES,
                },
                "statusHistory": [{"status": "COMPLETED"}],
                "logs": [{"name": "train", "text": "rehearsal"}],
                "samples": [],
                "checkpoints": [],
            },
        }
    raise AssertionError(f"the bridge called something unexpected: {method} {path}")


svc._runpod_request = fake_runpod_request

payload = {
    # These three are the keys the bridge reads to name the artifact. They are NOT invented here: the app's
    # canonical request carries all three (CharacterLoraTrainingService.CompileRequestAsync), and a fixture that
    # omitted one would let the rehearsal pass while a real run failed on a missing key.
    "baseModelId": "krea2_raw_bf16.safetensors",
    "datasetId": "rehearsal-dataset-0001",
    "artifactVersion": 1,
    "seed": 42,
    "recipe": {
        "epochs": 8,
        "rank": 32,
        "alpha": 32,
        "unetLearningRate": 0.0001,
        "steps": 576,
    },
}
state = svc.RunState(run_id=REHEARSAL_RUN_ID)
directory = SANDBOX / "run"

print("--- running the real _run_krea2_serverless with RunPod stubbed ---")
svc._run_krea2_serverless(state, directory, payload, manifest=[])

# ---- assertions ----------------------------------------------------------------------------------------------
def check(label: str, ok: bool, detail: str = "") -> None:
    print(f"{'PASS' if ok else 'FAIL'}  {label}{(' -- ' + detail) if detail else ''}")
    if not ok:
        raise SystemExit(f"rehearsal failed: {label}")


request_file = directory / "runpod-request.json"
check("the /run payload was recorded", request_file.is_file())
recorded = json.loads(request_file.read_text(encoding="utf-8"))
print("      payload: " + json.dumps(recorded))
check("datasetVolumePath points at the run prefix",
      recorded.get("datasetVolumePath") == f"/runpod-volume/datasets/{REHEARSAL_RUN_ID}",
      str(recorded.get("datasetVolumePath")))
check("the recipe reached the payload",
      recorded.get("epochs") == 8 and recorded.get("networkDim") == 32
      and recorded.get("networkAlpha") == 32 and recorded.get("maxTrainSteps") == 576)
check("seed reached the payload", recorded.get("seed") == 42)
expected_name = (
    f"dgc_lora_{payload['datasetId'][:8]}_{Path(payload['baseModelId']).stem}"
    f"_v{payload['artifactVersion']}"
)
check("the deterministic artifact name was computed from the job's own identity",
      recorded.get("outputName") == expected_name, f"{recorded.get('outputName')} vs {expected_name}")
check("status was polled", len(status_calls) == 1, f"{len(status_calls)} call(s)")

artifact = state.artifact or {}
check("the bridge reported an artifact", bool(artifact), json.dumps(artifact))
published = Path(str(artifact.get("fileRelativePath", "")))
check("the artifact was published into the loras folder", published.is_file() and published.parent.name == "loras",
      str(published))
check("published byte length matches the worker",
      artifact.get("byteLength") == REAL_ARTIFACT_BYTES, str(artifact.get("byteLength")))
check("published sha256 matches the worker",
      str(artifact.get("sha256", "")).upper() == REAL_ARTIFACT_SHA, str(artifact.get("sha256")))
check("logs were carried through", state.logs == [{"path": "train.log"}], json.dumps(state.logs))

# ---- clean the volume up --------------------------------------------------------------------------------------
import boto3  # noqa: E402

settings = svc.ServerlessSettings.load()
client = boto3.client(
    "s3",
    endpoint_url=settings.endpoint,
    aws_access_key_id=settings.access_key,
    aws_secret_access_key=settings.secret_key,
    region_name=settings.region,
)
prefix = f"datasets/{REHEARSAL_RUN_ID}/"
listing = client.list_objects_v2(Bucket=settings.bucket, Prefix=prefix)
keys = [item["Key"] for item in listing.get("Contents", [])]
for key in keys:
    client.delete_object(Bucket=settings.bucket, Key=key)
print(f"      uploaded {len(keys)} object(s) then deleted them: {keys}")
check("the rehearsal uploaded the staged dataset to the volume", len(keys) == 4, f"{len(keys)} object(s)")
remaining = client.list_objects_v2(Bucket=settings.bucket, Prefix=prefix).get("Contents")
# A list right after a delete can still show the object on an eventually-consistent store, so this reports rather
# than fails: an object left behind costs a few KB, and a false failure here would hide the checks that matter.
print(f"{'PASS' if not remaining else 'WARN'}  volume clean-up ({len(remaining or [])} object(s) still listed)")

# The container disk is tiny; do not leave a 469 MB rehearsal LoRA behind.
if SANDBOX.exists():
    shutil.rmtree(SANDBOX)
print("REHEARSAL PASS")
