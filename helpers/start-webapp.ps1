<#
.SYNOPSIS
Starts and manages the DreamGenClone web application.

.DESCRIPTION
Primary helper entry script for running the local web app.
Supports launching, stopping existing processes, and checking status.

.EXAMPLE
./helpers/start-webapp.ps1

.EXAMPLE
./helpers/start-webapp.ps1 webapp -OpenBrowser

.EXAMPLE
./helpers/start-webapp.ps1 stop

.EXAMPLE
./helpers/start-webapp.ps1 status
#>

param(
    [Parameter(Position = 0)]
    [ValidateSet("webapp", "stop", "status")]
    [string]$Action = "webapp",

    [Parameter()]
    [string]$Urls = "http://localhost:5177",

    [Parameter()]
    [switch]$OpenBrowser,

    [Parameter()]
    [switch]$Restore,

    [Parameter()]
    [switch]$Build
)

# Usage:
#   ./helpers/start-webapp.ps1
#   ./helpers/start-webapp.ps1 webapp -OpenBrowser
#   ./helpers/start-webapp.ps1 webapp -Urls "http://localhost:5001"
#   ./helpers/start-webapp.ps1 webapp -Restore
#   ./helpers/start-webapp.ps1 webapp -Build
#   ./helpers/start-webapp.ps1 stop
#   ./helpers/start-webapp.ps1 status

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$projectPath = Join-Path $repoRoot "DreamGenClone.Web\DreamGenClone.csproj"

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
}

function Get-WebAppProcesses {
    $results = [System.Collections.ArrayList]::new()

    $dotnetProcs = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and (
                $_.CommandLine -like "*DreamGenClone.Web*DreamGenClone.csproj*" -or
                $_.CommandLine -like "*DreamGenClone.Web*DreamGenClone.dll*"
            )
        }
    if ($dotnetProcs) {
        foreach ($p in @($dotnetProcs)) { [void]$results.Add($p) }
    }

    $appHostProcs = Get-Process -Name "DreamGenClone" -ErrorAction SilentlyContinue
    if ($appHostProcs) {
        foreach ($p in @($appHostProcs)) {
            [void]$results.Add([PSCustomObject]@{
                ProcessId = $p.Id
                Name = $p.ProcessName
                CommandLine = "DreamGenClone"
            })
        }
    }

    return $results
}

function Stop-WebAppProcesses {
    Write-Host "Checking for running DreamGenClone web app processes..." -ForegroundColor Yellow
    [array]$processes = @(Get-WebAppProcesses)

    if ($processes.Count -eq 0) {
        Write-Host "No running web app processes found." -ForegroundColor Green
        Write-Host ""
        return
    }

    Write-Host "Found $($processes.Count) running process(es). Stopping..." -ForegroundColor Yellow
    foreach ($process in $processes) {
        Write-Host "  Stopping process $($process.ProcessId) ($($process.Name))" -ForegroundColor Yellow
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Seconds 2
    Write-Host "Processes stopped." -ForegroundColor Green
    Write-Host ""
}

function Show-WebAppStatus {
    Write-Section "DreamGenClone Web App Status"
    [array]$processes = @(Get-WebAppProcesses)

    if ($processes.Count -eq 0) {
        Write-Host "Status: Not running" -ForegroundColor Yellow
    }
    else {
        Write-Host "Status: Running" -ForegroundColor Green
        foreach ($process in $processes) {
            Write-Host "  PID=$($process.ProcessId) Name=$($process.Name)" -ForegroundColor Green
        }
    }
}

function Get-PortFromUrl {
    param([string]$Url)

    $uri = [Uri]$Url
    return $uri.Port
}

function Test-PortAvailable {
    param([int]$Port)

    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($listener) {
            $listener.Stop()
        }
    }
}

function Resolve-AvailableUrl {
    param([string]$RequestedUrl)

    $uri = [Uri]$RequestedUrl
    if ($uri.Host -ne "localhost" -and $uri.Host -ne "127.0.0.1") {
        return $RequestedUrl
    }

    $port = Get-PortFromUrl -Url $RequestedUrl
    if (Test-PortAvailable -Port $port) {
        return $RequestedUrl
    }

    Write-Host "Port $port is in use. Finding an available localhost port..." -ForegroundColor Yellow

    for ($candidate = $port + 1; $candidate -le $port + 50; $candidate++) {
        if (Test-PortAvailable -Port $candidate) {
            return "{0}://{1}:{2}" -f $uri.Scheme, $uri.Host, $candidate
        }
    }

    Write-Host "Error: no available port found in range $($port + 1)-$($port + 50)." -ForegroundColor Red
    exit 1
}

function Invoke-OptionalRestore {
    param([string]$ProjectPath, [switch]$ForceRestore)

    if (-not $ForceRestore) {
        return
    }

    Write-Host "Running restore (explicitly requested) ..." -ForegroundColor Yellow
    & dotnet restore "$ProjectPath"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: restore failed. App was not started." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

function Get-WebAppDllPath {
    param([string]$ProjectPath)

    $projectDir = Split-Path -Parent $ProjectPath
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    return Join-Path $projectDir "bin\Debug\net9.0\$projectName.dll"
}

function Start-DeferredBrowserOpen {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 90
    )

    Start-Job -Name "DreamGenClone.OpenBrowser" -ScriptBlock {
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

switch ($Action) {
    "webapp" {
        Test-Prerequisites
        Stop-WebAppProcesses

        $resolvedUrl = Resolve-AvailableUrl -RequestedUrl $Urls

        Write-Section "Starting DreamGenClone web app"
        Write-Host "Project: $projectPath" -ForegroundColor Cyan
        Write-Host "URLs: $resolvedUrl" -ForegroundColor Cyan

        $env:ASPNETCORE_ENVIRONMENT = "Development"
        Invoke-OptionalRestore -ProjectPath $projectPath -ForceRestore:$Restore

        if ($Build) {
            Write-Host "Build enabled: startup may take longer." -ForegroundColor Yellow
        }
        else {
            Write-Host "Build skipped for fast startup (use -Build after code changes)." -ForegroundColor Yellow
        }

        if ($OpenBrowser) {
            Start-DeferredBrowserOpen -Url $resolvedUrl
        }

        # Avoid implicit restore on every run to keep local startup fast.
        if ($Build) {
            & dotnet run --no-restore --no-launch-profile --project "$projectPath" --urls "$resolvedUrl"
        }
        else {
            $projectDir = Split-Path -Parent $projectPath
            $webAppDll = Get-WebAppDllPath -ProjectPath $projectPath

            if (-not (Test-Path $webAppDll)) {
                Write-Host "Error: build output not found at '$webAppDll'." -ForegroundColor Red
                Write-Host "Run once with -Build to generate binaries." -ForegroundColor Yellow
                exit 1
            }

            Push-Location $projectDir
            try {
                & dotnet "$webAppDll" --urls "$resolvedUrl"
            }
            finally {
                Pop-Location
            }
        }

        if ($LASTEXITCODE -ne 0 -and -not $Build) {
            Write-Host "Tip: rerun with -Build if binaries are missing or stale." -ForegroundColor Yellow
            exit $LASTEXITCODE
        }
    }
    "stop" {
        Stop-WebAppProcesses
        exit 0
    }
    "status" {
        Show-WebAppStatus
        exit 0
    }
    default {
        Write-Host "Unknown action: $Action" -ForegroundColor Red
        exit 1
    }
}
