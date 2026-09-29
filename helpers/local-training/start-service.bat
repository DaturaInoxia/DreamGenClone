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

REM NOTE: this launcher does NOT stop a previous instance. uvicorn will not take a port from a live listener, so to
REM load new code on the host, stop the old process FIRST, then run this:
REM     powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort 8199 -State Listen | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }"
REM     schtasks /run /tn DGLoraTrainService
cd /d D:\lora-training-service
venv\Scripts\python.exe -m uvicorn lora_train_service:app --host 0.0.0.0 --port 8199 >> service.log 2>&1
