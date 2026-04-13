<#
.SYNOPSIS
Runs the DreamGenClone web app with build and browser launch enabled.

.DESCRIPTION
Convenience wrapper around helpers/start-webapp.ps1 for daily local startup.

.EXAMPLE
./helpers/start-webapp-build-browser.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetScript = Join-Path $scriptRoot "start-webapp.ps1"

& $targetScript webapp -Build -OpenBrowser
exit $LASTEXITCODE
