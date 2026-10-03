# Local LoRA training — the service that runs on the ComfyUI host

The app does **not** train on its own machine. The desktop runs the app on an 8 GB card with no ComfyUI; the base
checkpoints, the GPU and the ComfyUI runtime that will *use* the LoRA all live on the ComfyUI host
(`WOOD-GAME-MAIN`, RTX 5080 16 GB, `D:\ComfyUI`).

So training is split in two:

| Side | What it is |
|---|---|
| **App** | `LocalCharacterLoraTrainingDispatchAdapter` (`DreamGenClone.Infrastructure/Models/`) — adapter key `local-kohya-training-v1`. Zips the dataset members and posts them, then polls. |
| **Host** | `lora_train_service.py` (this folder) — receives the job, runs kohya `sd-scripts` on the 5080, publishes the `.safetensors` into ComfyUI's loras folder. |

The dataset travels **with** the job, so the host never has to reach back into the desktop.

## 1. Install on the ComfyUI host

```powershell
# kohya's sd-scripts, beside ComfyUI
cd D:\
git clone https://github.com/kohya-ss/sd-scripts
cd D:\sd-scripts
python -m venv .venv
.\.venv\Scripts\pip install -r requirements.txt
.\.venv\Scripts\pip install --upgrade torch torchvision --index-url https://download.pytorch.org/whl/cu124

# this service
cd D:\DreamGenClone\helpers\local-training
python -m venv .venv
.\.venv\Scripts\pip install -r requirements.txt
```

## 2. Run it

```powershell
$env:SD_SCRIPTS_DIR  = "D:\sd-scripts"     # must contain sdxl_train_network.py
$env:COMFYUI_ROOT    = "D:\ComfyUI"        # models\checkpoints is read, models\loras is written
$env:LORA_TRAIN_ROOT = "D:\lora-training"  # run directories: images, captions, logs, artifacts
$env:SD_PYTHON       = "D:\sd-scripts\.venv\Scripts\python.exe"

.\.venv\Scripts\python -m uvicorn lora_train_service:app --host 0.0.0.0 --port 8199
```

Every one of those paths is **required**. The service refuses to start without them rather than guessing, because a
guessed `COMFYUI_ROOT` trains against the wrong checkpoints and publishes a LoRA into the wrong folder.
### Restarting it (uvicorn will NOT take a live port)

A restart has to stop the old listener first. uvicorn exits with "address already in use" and the **old process keeps
serving**, so a freshly deployed change silently does nothing:

```powershell
ssh -i ~/.ssh/dgcomfy_ed25519 'wood-game-main\kenac@192.168.0.11' 'cmd /c D:\lora-training-service\stop-service.bat'
ssh -i ~/.ssh/dgcomfy_ed25519 'wood-game-main\kenac@192.168.0.11' 'schtasks /run /tn DGLoraTrainService'
```

`stop-service.bat` kills by the **port**, never by image name: ComfyUI runs on `python.exe` on this host too.

Two traps worth knowing: a `.bat` copied with **LF-only** line endings silently produces an empty log and no service
(keep it CRLF), and the remote shell is `cmd.exe`, so complex quoting through PowerShell → ssh → cmd gets mangled —
put commands in a `.bat` on the host instead.

## 2b. The endpoints the app calls

| Endpoint | Why it exists |
|---|---|
| `GET /train/health` | Liveness, plus `trainer`: the **measured** versions the sd-scripts interpreter runs with (probed once through `SD_PYTHON`, cached). A profile records these, so no version is ever typed or invented. |
| `GET /checkpoints` | Every `models/checkpoints/*.safetensors` with its byte length and **SHA-256**. This is what turns the profile form into pick lists and removes the 64-character checksum an operator would otherwise have to type correctly. The first call hashes every checkpoint (a 7 GB file takes ~a minute); the result is cached in `LORA_TRAIN_ROOT\checkpoints.json`, keyed on file size. |
| `POST /train` | Accepts the job (canonical request JSON + a zip of members, captions and `manifest.json`). |
| `GET /train/{id}` | Status, and on success the artifact path, checksum and byte length. |
| `GET /train/{id}/log` | The tail of the run's `train.log`. A run id the service has no record of answers **200 with `FAILED`** and says so, instead of a bare 404 that reads like "still working". |
| `POST /train/{id}/cancel` | Best-effort stop. |
Smoke test from the machine that runs the app:

```powershell
Invoke-WebRequest http://<comfyui-host>:8199/train/health -UseBasicParsing
```

## 2c. Krea 2 — trained on RunPod Serverless, dispatched by this service

Krea 2 has no kohya network module and its 12B MMTDiT does not fit this host's 16 GB card, so a Krea 2 job is
**not trained here**. The service uploads the dataset to the RunPod network volume and dispatches the job to a
serverless endpoint, then downloads the returned `.safetensors` into `COMFYUI_ROOT\models\loras` exactly as a kohya
job would. **Nothing on the app side changes**: same provider row, same adapter key and same submit path.

Which path a job takes is decided by the **profile's trainer id**, not by the adapter key:

| Trainer id | Trainer | Where it runs |
|---|---|---|
| `sd-scripts` | kohya `sd-scripts` | this host |
| `musubi-krea2-serverless-v1` | `musubi-tuner` | RunPod Serverless |

The app derives that id from the profile's **model family** (`LoraTrainingProfiles.razor` → `TrainerIdForFamily`),
so a Krea 2 profile cannot be pointed at kohya by hand and a kohya family cannot be pointed at musubi. The id must
match `SERVERLESS_TRAINERS` in `lora_train_service.py`; the two are named in both places and proved by the endpoint
smoke test.

### Configuration

Five non-secret values live in `start-service.bat`. The three **secrets** do not. Put them in
`D:\lora-training-service\serverless-secrets.bat`, which `start-service.bat` calls when present:

```bat
set RUNPOD_API_KEY=...
set S3_ACCESS_KEY=...
set S3_SECRET_KEY=...
```

Start from the committed template `serverless-secrets.bat.example`. The S3 pair is the **network volume's own**
credential (RunPod → Storage → the volume → S3 API), *not* the account API key. If the file is absent, kohya jobs
keep working and a Krea 2 job fails with the named missing setting rather than a guessed value.

### Deploying a change to this service

```powershell
# 1. copy lora_train_service.py to D:\lora-training-service\
# 2. install requirements (this now adds boto3, used only by the serverless path)
ssh -i ~/.ssh/dgcomfy_ed25519 'wood-game-main\kenac@192.168.0.11' ^
  'D:\lora-training-service\venv\Scripts\pip.exe install -r D:\lora-training-service\requirements.txt'
# 3. restart — the uvicorn trap above applies: the OLD process keeps serving otherwise
ssh -i ~/.ssh/dgcomfy_ed25519 'wood-game-main\kenac@192.168.0.11' 'cmd /c D:\lora-training-service\stop-service.bat'
ssh -i ~/.ssh/dgcomfy_ed25519 'wood-game-main\kenac@192.168.0.11' 'schtasks /run /tn DGLoraTrainService'
```

### Proving the dispatch path WITHOUT spending money

`krea2-serverless-preflight.py` runs **on the host** and proves every part of the path short of the run itself: it
imports the serverless dependencies, resolves every setting, lists the network volume with the configured S3
credentials, and authenticates to the RunPod endpoint's API. Run it with the service's own interpreter:

```powershell
ssh -i ~/.ssh/dgcomfy_ed25519 'wood-game-main\kenac@192.168.0.11' ^
  'D:\lora-training-service\venv\Scripts\python.exe D:\lora-training-service\krea2-serverless-preflight.py'
```

It reads its settings from `start-service.bat` and `serverless-secrets.bat` rather than taking them on a command
line, so it also catches a `.bat` saved with LF-only endings — the documented trap. A real Krea 2 run costs about
an hour and $3, so a wrong bucket or a mistyped key should fail here instead. Copy the script to the host alongside
`lora_train_service.py`.

### Rehearsing the bridge itself, for free

`krea2-serverless-rehearsal.py` runs the **real** `_run_krea2_serverless` and stubs only RunPod's `/run` and
`/status` responses, so the upload, the payload, the download, the checksum verification and the publish into this
host's loras folder all genuinely execute. It downloads an artifact the endpoint already produced and verifies it
against the worker's reported SHA-256 and byte length, publishes into a SANDBOX loras folder rather than the real
one, and deletes what it uploaded to the volume afterwards. Use it whenever the dispatch path changes; it costs
nothing.

```powershell
ssh -i ~/.ssh/dgcomfy_ed25519 'wood-game-main\kenac@192.168.0.11' ^
  'D:\lora-training-service\venv\Scripts\python.exe D:\lora-training-service\krea2-serverless-rehearsal.py'
```

Keep `start-service.bat` and `serverless-secrets.bat` **CRLF** — an LF-only `.bat` silently produces an empty log
and no service.

## 3. Register the provider in the app

Model Manager → add a provider:

| Field | Value |
|---|---|
| Name | `Local LoRA trainer` (must match the endpoint snapshot exactly) |
| Base URL | `http://<comfyui-host>:8199` |
| Enabled | yes |

Then on `/asset-studio/lora-datasets/{id}/training`:

| Field | Value |
|---|---|
| Adapter key | `local-kohya-training-v1` |
| Submit path | `/train` |
| Status path | `/train/{jobId}` |
| Cancel path | `/train/{jobId}/cancel` |

## 4. The training profile's base model

**`BaseModelId` must be the checkpoint FILE NAME as ComfyUI knows it**, e.g. `juggernautXL_ragnarok.safetensors` or
`bigLust_v16.safetensors`. The service joins it with `COMFYUI_ROOT\models\checkpoints`, so a name ComfyUI can render
with is a name this service can train with.

You do not type any of this. On `/asset-studio/lora-training-profiles`, pick the training host, then pick the
checkpoint from the list this service publishes — its SHA-256 arrives with it — then pick a recipe. If you do need a
checksum by hand:

```powershell
Get-FileHash D:\ComfyUI\models\checkpoints\juggernautXL_ragnarok.safetensors -Algorithm SHA256
```

## 5. What the service does with a job

1. reads `request` (the app's canonical request: ids, manifest sha, base model, recipe, seed, artifact version) and
   the `dataset` zip (images + captions + `manifest.json`);
2. copies the members into kohya's folder layout;
3. builds the `sdxl_train_network.py` command line **only** from the recipe the profile carries — rank, alpha,
   learning rates, epochs, steps, repeats, caption dropout, precision, resolution bucket — plus 8-bit AdamW,
   cosine-with-restarts, latent caching, gradient checkpointing, `min_snr_gamma 5`, `noise_offset 0.05`;
4. computes the artifact's SHA-256 and byte length, copies the `.safetensors` into `COMFYUI_ROOT\models\loras`, and
   returns its path, name and checksum.

### 5b. A submitted job is on disk before the app is told the id

`POST /train` validates the request JSON **and** the zip (it must contain `images/manifest.json`), writes
`request.json`, `dataset.zip`, extracts it, and writes `state.json` — *then* answers with the run id. State is
re-written on every terminal path. So an accepted job is never a job with nothing behind it, and an app-side attempt
that holds an id can always be reconciled from the host. Bad input answers **400** (with the reason) rather than
accepting work it cannot run.

### 5c. UTF-8 stdio is required, not cosmetic

The trainer runs with `PYTHONIOENCODING=utf-8` and `PYTHONUTF8=1` in its environment. sd-scripts prints localized
(Japanese) text, and a Windows `cp1252` stdout cannot encode it: the process dies with `UnicodeEncodeError` **on its
first print** — after it has loaded the dataset and built the LoRA. Read cold, that looks like a training failure and
is not one. The exit code is 1 and the log's last line is the traceback.

### 5d. Two kohya conventions that cost runs

| Assumption | Reality |
|---|---|
| `--num_repeats N` sets repeats | **That flag does not exist.** Repeats come from the training folder's *name*: `{repeats}_concept`. The service writes `images/NNN_…`, so the folder tree carries the number. |
| `--train_data_dir <concept folder>` | It must be the **parent** of the `{repeats}_concept` folder. Pointing it at the concept folder trains on zero images. |

Both refuse loudly (exit 2 / a "success" with no artifact), and both were found by reading `train.log` — which is why
`GET /train/{id}/log` exists and why a non-zero exit carries the tail of the log into the failure message the app
records. Verify a flag against the trainer's own `--help` before adding it to the command line.

Known gap, deliberately not guessed: the profile's checkpoint **cadence** (`CheckpointCadenceJson` /
`SampleCadenceJson`) does not travel in the canonical request yet, so the run saves its single final artifact and no
intermediate checkpoints. Adding those two fields to the compiled request is the next app-side change.

## 6. VRAM

SDXL at 1024 px, rank 32, batch 1 with gradient checkpointing, latent caching and 8-bit AdamW is a routine 16 GB
workload on the 5080. It is **not** routine on the app desktop's 8 GB card, which is why the host is the trainer.
