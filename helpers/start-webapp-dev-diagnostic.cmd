@echo off
setlocal
cd /d "%~dp0.."
powershell -NoLogo -NoExit -ExecutionPolicy Bypass -File ".\helpers\start-webapp-dev.ps1" -OpenBrowser -BuildVerbosity diagnostic
