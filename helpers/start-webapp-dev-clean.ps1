<#
.SYNOPSIS
Clean rebuild and dev startup for DreamGenClone.

.DESCRIPTION
Forces a full clean + rebuild before launching the web app.
Use this when incremental builds are stale or producing unexpected behavior.

Flow:
1) Stop existing web app processes
2) dotnet clean (forces full recompile)
3) dotnet build (no-restore)
4) Run without build/restore

.EXAMPLE
./helpers/start-webapp-dev-clean.ps1

.EXAMPLE
./helpers/start-webapp-dev-clean.ps1 -OpenBrowser
#>

param(
    [Parameter()]
    [string]$Urls = "http://localhost:5177",

    [Parameter()]
    [ValidateSet("minimal", "normal", "detailed", "diagnostic")]
    [string]$BuildVerbosity = "normal",

    [Parameter()]
    [switch]$OpenBrowser,

    [Parameter()]
    [switch]$SkipStop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_XMLDOC_MODE = "skip"

$projectPath = Join-Path $repoRoot "DreamGenClone.Web\DreamGenClone.csproj"
$solutionPath = Join-Path $repoRoot "DreamGenClone.sln"
$stopScript = Join-Path $repoRoot "helpers\start-webapp.ps1"

function Write-Section {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Cyan
}

function Test-Prerequisites {
    if (-not (Test-Path $projectPath)) {
        Write-Host "Error: web project not found at '$projectPath'." -ForegroundColor Red
        exit 1
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host "Error: dotnet SDK not found on PATH." -ForegroundColor Red
        exit 1
    }
}

function Stop-ExistingWebApp {
    if ($SkipStop) { return }
    Write-Section "Stopping existing DreamGenClone web app"
    if (Test-Path $stopScript) {
        & $stopScript stop
    }
    Start-Sleep -Seconds 1
}

function Start-DeferredBrowserOpen {
    param([string]$Url)
    Start-Job -ScriptBlock {
        param($u)
        Start-Sleep -Seconds 3
        Start-Process $u
    } -ArgumentList $Url | Out-Null
}

Test-Prerequisites
Write-Section "Dev target: $projectPath"
Write-Section "Mode: clean-rebuild, no-restore, stop-existing"

Stop-ExistingWebApp

# Step 1: Clean the solution to force full recompilation
Write-Section "Cleaning solution (dotnet clean)"
Write-Host "Running: dotnet clean $solutionPath -v $BuildVerbosity" -ForegroundColor DarkCyan
$cleanLines = & dotnet clean "$solutionPath" -v $BuildVerbosity 2>&1
foreach ($line in $cleanLines) {
    Write-Host $line
}
if ($LASTEXITCODE -ne 0) {
    Write-Host "Warning: clean exited with code $LASTEXITCODE (continuing with build)" -ForegroundColor Yellow
}

# Step 2: Build the web project (and dependencies) from scratch
Write-Section "Building web app (full rebuild after clean)"
Write-Host "Build command target: $projectPath" -ForegroundColor DarkCyan
Write-Host "Running: dotnet build $projectPath -v $BuildVerbosity --no-restore" -ForegroundColor DarkCyan
$buildLines = & dotnet build "$projectPath" -v $BuildVerbosity --no-restore 2>&1
foreach ($line in $buildLines) {
    Write-Host $line
}
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: build failed for '$projectPath'. App was not started." -ForegroundColor Red
    exit $LASTEXITCODE
}

if ($OpenBrowser) {
    Start-DeferredBrowserOpen -Url $Urls
}

$env:ASPNETCORE_ENVIRONMENT = "Development"

Write-Section "Starting web app (no-build, no-restore)"
$projectDir = Split-Path -Parent $projectPath
$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
$webAppDll = Join-Path $projectDir "bin\Debug\net9.0\$projectName.dll"

if (-not (Test-Path $webAppDll)) {
    Write-Host "Error: expected build output not found at '$webAppDll'." -ForegroundColor Red
    exit 1
}

Push-Location $projectDir
try {
    & dotnet "$webAppDll" --urls "$Urls"
}
finally {
    Pop-Location
}
