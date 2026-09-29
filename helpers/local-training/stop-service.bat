@echo off
REM Stops the LoRA training service by the PORT it holds, never by image name: ComfyUI also runs on python.exe on this
REM host and must stay up.
REM
REM uvicorn will not take a port from a live listener - it exits with "address already in use" and the old process
REM keeps serving, which is how a freshly deployed change appears to do nothing at all.
REM
REM Restart, from another machine:
REM     ssh <host> cmd /c D:\lora-training-service\stop-service.bat
REM     ssh <host> schtasks /run /tn DGLoraTrainService

for /f "tokens=5" %%a in ('netstat -ano ^| findstr LISTENING ^| findstr :8199') do (
    echo stopping pid %%a
    taskkill /f /pid %%a >nul 2>&1
)
