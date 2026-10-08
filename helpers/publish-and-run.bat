@echo off
REM ---------------------------------------------------------------------------
REM Double-click launcher for helpers\publish-and-run.ps1
REM
REM Publishes the latest source to artifacts\runtime\web\<release-id> and runs
REM the app from that copy, so the running app never locks DreamGenClone.Web\bin
REM or obj: agents can keep building and testing while you keep using the app.
REM
REM The app is started detached (-Background) and keeps running after this window
REM is closed. Extra arguments are passed straight through, e.g.:
REM   publish-and-run.bat -PublishOnly                  stage a build, leave the app up
REM   publish-and-run.bat -UseExistingRelease           restart the last published build
REM   publish-and-run.bat -Configuration Release        publish in Release
REM   publish-and-run.bat -Urls http://localhost:5199   run on another port
REM
REM Stop the app with:  powershell -ExecutionPolicy Bypass -File helpers\start-webapp.ps1 stop
REM Live app logs:      artifacts\runtime\web\logs\<release-id>.out.log
REM                     DreamGenClone.Web\logs\dreamgenclone-<date>.log
REM For a foreground run with live console logs instead, run the .ps1 directly
REM without -Background.
REM ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0.."
powershell -NoLogo -NoExit -ExecutionPolicy Bypass -File ".\helpers\publish-and-run.ps1" -Background -OpenBrowser %*
