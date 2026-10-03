@echo off
REM Launches the LoRA training service on the ComfyUI host (WOOD-GAME-MAIN, RTX 5080).
REM
REM Every path is absolute and explicit on purpose: this service refuses to start without them, because a guessed
REM COMFYUI_ROOT would train against the wrong checkpoints and publish a LoRA into the wrong loras folder.
REM
REM Run it on the host:
REM     schtasks /create /tn DGLoraTrainService /tr "D:\lora-training-service\start-service.bat" /sc onlogon /f
REM or start it once, detached, from another machine:
REM     ssh <host> powershell -NoProfile -Command "Start-Process 'D:\lora-training-service\start-service.bat' -WindowStyle Hidden"

set SD_SCRIPTS_DIR=D:\sd-scripts
set COMFYUI_ROOT=D:\ComfyUI
set LORA_TRAIN_ROOT=D:\lora-training
set SD_PYTHON=D:\sd-scripts\venv\Scripts\python.exe

REM --- Krea 2 serverless training ------------------------------------------------------------------
REM Krea 2 has no kohya network module and its 12B MMTDiT does not fit this host's card, so a Krea 2 job is not
REM trained here: the service uploads the dataset to the RunPod network volume and dispatches the job to a
REM serverless endpoint, then downloads the returned .safetensors into COMFYUI_ROOT\models\loras exactly as a
REM kohya job would. Which path a job takes is decided by the PROFILE'S TRAINER ID, not by the adapter key.
REM
REM These five values are not secrets. The three that ARE secrets - the RunPod API key and the network volume's
REM S3 key pair - live in serverless-secrets.bat beside this file, so they are never in git and never in here.
REM When that file is absent, kohya jobs keep working and a Krea 2 job fails naming the missing setting.
if exist "D:\lora-training-service\serverless-secrets.bat" call "D:\lora-training-service\serverless-secrets.bat"
REM The endpoint id is the source of truth in helpers/runpod/serverless/endpoints.json (krea2-training-serverless).
set RUNPOD_TRAIN_ENDPOINT_ID=h19lk2zv623y83
REM The network volume: the bucket name IS the volume id.
set RUNPOD_VOLUME_BUCKET=n5rainij0c
set RUNPOD_VOLUME_ENDPOINT=https://s3api-eu-ro-1.runpod.io
set RUNPOD_VOLUME_REGION=eu-ro-1
set RUNPOD_TRAIN_TIMEOUT_SECONDS=14400

REM NOTE: this launcher does NOT stop a previous instance. uvicorn will not take a port from a live listener, so to
REM load new code on the host, stop the old process FIRST, then run this:
REM     powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort 8199 -State Listen | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }"
REM     schtasks /run /tn DGLoraTrainService
cd /d D:\lora-training-service
venv\Scripts\python.exe -m uvicorn lora_train_service:app --host 0.0.0.0 --port 8199 >> service.log 2>&1
