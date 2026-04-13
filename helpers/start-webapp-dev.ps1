<#
.SYNOPSIS
Fast dev startup for DreamGenClone.

.DESCRIPTION
Enforces a predictable local dev flow:
1) Stop existing web app processes
2) Optionally restore
3) Build once
4) Run without build/restore

Supports optional watch mode and delayed browser open.

.EXAMPLE
./helpers/start-webapp-dev.ps1

.EXAMPLE
./helpers/start-webapp-dev.ps1 -OpenBrowser

.EXAMPLE
./helpers/start-webapp-dev.ps1 -Restore -OpenBrowser

.EXAMPLE
./helpers/start-webapp-dev.ps1 -Watch -OpenBrowser
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
    [switch]$Restore,

    [Parameter()]
    [switch]$Watch,

    [Parameter()]
    [switch]$SkipStop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Reduce CLI startup/background checks that add delay without useful local dev value.
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:NUGET_XMLDOC_MODE = "skip"

$projectPath = Join-Path $repoRoot "DreamGenClone.Web\DreamGenClone.csproj"
$stopScript = Join-Path $repoRoot "helpers\start-webapp.ps1"

function Write-Section {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Cyan
}

function Test-Prerequisites {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host "Error: dotnet CLI not found. Install .NET SDK 9+ first." -ForegroundColor Red
        exit 1
    }

    if (-not (Test-Path $projectPath)) {
        Write-Host "Error: web project not found at '$projectPath'." -ForegroundColor Red
        exit 1
    }

    if (-not (Test-Path $stopScript)) {
        Write-Host "Error: stop helper not found at '$stopScript'." -ForegroundColor Red
        exit 1
    }
}

function Get-PortFromUrl {
    param([string]$Url)

    $uri = [Uri]$Url
    return $uri.Port
}

function Start-DeferredBrowserOpen {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 90
    )

    Start-Job -Name "DreamGenClone.Dev.OpenBrowser" -ScriptBlock {
        param([string]$TargetUrl, [int]$Timeout)

        $deadline = (Get-Date).AddSeconds($Timeout)
        $uri = [Uri]$TargetUrl
        $hostName = $uri.Host
        $port = $uri.Port

        while ((Get-Date) -lt $deadline) {
            $client = New-Object System.Net.Sockets.TcpClient
            try {
                $asyncConnect = $client.BeginConnect($hostName, $port, $null, $null)
                if ($asyncConnect.AsyncWaitHandle.WaitOne(750) -and $client.Connected) {
                    Start-Process $TargetUrl | Out-Null
                    return
                }
            }
            catch {
                # Retry until timeout.
            }
            finally {
                $client.Close()
            }

            Start-Sleep -Milliseconds 500
        }
    } -ArgumentList $Url, $TimeoutSeconds | Out-Null

    Write-Host "Browser launch scheduled: will open when app is ready at $Url" -ForegroundColor Yellow
}

function Invoke-BuildWithLockRecovery {
    param(
        [string]$ProjectPath,
        [string]$Verbosity
    )

    Write-Host "Running: dotnet build --no-restore -v $Verbosity" -ForegroundColor DarkCyan
    $buildOutput = @()
    & dotnet build "$ProjectPath" -v $Verbosity --no-restore 2>&1 |
        Tee-Object -Variable buildOutput |
        ForEach-Object { Write-Host $_ }
    $buildExit = $LASTEXITCODE

    if ($buildExit -eq 0) {
        return 0
    }

    $lockPids = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($line in $buildOutput) {
        if ($line -match 'file is locked by: ".*\((\d+)\)"') {
            [void]$lockPids.Add([int]$Matches[1])
        }
    }

    if ($lockPids.Count -eq 0) {
        return $buildExit
    }

    Write-Host "Detected build file lock(s). Stopping lock-holder process(es): $($lockPids -join ', ')" -ForegroundColor Yellow
    foreach ($lockPid in $lockPids) {
        try {
            Stop-Process -Id $lockPid -Force -ErrorAction Stop
            Write-Host "  Stopped PID $lockPid" -ForegroundColor Yellow
        }
        catch {
            Write-Host "  Could not stop PID $lockPid (it may have already exited)." -ForegroundColor DarkYellow
        }
    }

    Write-Host "Retrying build once after clearing lock(s)..." -ForegroundColor Yellow
    $retryOutput = @()
    & dotnet build "$ProjectPath" -v $Verbosity --no-restore 2>&1 |
        Tee-Object -Variable retryOutput |
        ForEach-Object { Write-Host $_ }
    return $LASTEXITCODE
}

Test-Prerequisites

if (-not $SkipStop) {
    Write-Section "Stopping existing DreamGenClone web app"
    & $stopScript stop
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($Restore) {
    Write-Section "Restoring packages"
    & dotnet restore "$projectPath"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: restore failed." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

if (-not $Watch) {
    Write-Section "Building web app (one-time)"
    $buildExit = Invoke-BuildWithLockRecovery -ProjectPath $projectPath -Verbosity $BuildVerbosity
    if ($buildExit -ne 0) {
        Write-Host "Error: build failed. App was not started." -ForegroundColor Red
        exit $buildExit
    }
}

if ($OpenBrowser) {
    Start-DeferredBrowserOpen -Url $Urls
}

$env:ASPNETCORE_ENVIRONMENT = "Development"

if ($Watch) {
    Write-Section "Starting web app in watch mode"
    & dotnet watch --project "$projectPath" run --no-launch-profile --no-restore --urls "$Urls"
}
else {
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
}

exit $LASTEXITCODE
