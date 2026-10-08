<#
.SYNOPSIS
Publishes DreamGenClone to an isolated release folder and runs the app from that copy.

.DESCRIPTION
Solves the "cannot build while the app is running" problem.

The other dev starters (start-webapp-dev.ps1, start-webapp.ps1) run the app straight out of
DreamGenClone.Web\bin\Debug\net9.0. Windows keeps a lock on every assembly in that folder for
as long as the app is running, so any rebuild fails with:
    The process cannot access the file ... because it is being used by another process.

This script never runs out of bin. It:
  1) dotnet publish into artifacts\runtime\web\<release-id>   (a throwaway copy of the app)
  2) stops any running DreamGenClone instance
  3) runs the app from that release folder

The running process therefore only locks files inside its own release folder, so agents can keep
editing and building the solution (bin, obj, source) while you keep using the app. When you want
the running app to pick up the latest source code, re-run this script: it publishes a new release
folder and restarts onto it. Older release folders are pruned to -KeepReleases and are never
deleted while a process still has them open.

The app is started with its working directory and content root pinned to DreamGenClone.Web, so all
relative paths resolve exactly as they do under start-webapp-dev.ps1:
    data\dreamgenclone.dev.db   live dev DB (Development environment)
    logs\                       Serilog + role-play debug event logs
    ..\specs\                   theme definitions
That keeps the published process on the real dev database and the real repo assets.

Its web root is pinned separately to the release's published wwwroot, because the fingerprinted
static assets are served from the manifest's relative paths: the generated scoped-CSS bundle
(DreamGenClone.styles.css) only exists in the published wwwroot (in a dev tree it lives in obj\), so
using the source wwwroot makes every scoped style sheet return empty and the app renders unstyled.
The one runtime-written web-root subtree (wwwroot\pose-library, where authored pose skeletons land
and which is git-tracked in the source tree) is junctioned back to the source tree so those writes
are never lost inside a throwaway release folder.

With -PublishOnly the script publishes a release folder and leaves the running app alone, so you can
stage a build without any downtime. Use it when the app is already running from a release folder; a
running instance started from bin\ blocks the publish by design, and the script says so.

With -UseExistingRelease nothing is published: the app is restarted from the newest release folder
that already contains the app assembly. Use it to restart the exact bits already deployed, or to get
the app back up while the working tree does not currently compile.

.EXAMPLE
./helpers/publish-and-run.ps1

.EXAMPLE
./helpers/publish-and-run.ps1 -UseExistingRelease -Background

.EXAMPLE
./helpers/publish-and-run.ps1 -OpenBrowser

.EXAMPLE
./helpers/publish-and-run.ps1 -PublishOnly -ReleaseName before-big-refactor

.EXAMPLE
./helpers/publish-and-run.ps1 -Background -OpenBrowser

.EXAMPLE
./helpers/publish-and-run.ps1 -Configuration Release -Urls http://localhost:5199
#>

param(
    [Parameter()]
    [string]$Urls = "http://localhost:5177",

    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [Parameter()]
    [string]$ReleaseName = "",

    [Parameter()]
    [string]$RuntimeRoot = "",

    [Parameter()]
    [ValidateRange(1, 50)]
    [int]$KeepReleases = 3,

    [Parameter()]
    [switch]$SkipRestore,

    [Parameter()]
    [switch]$PublishOnly,

    [Parameter()]
    [switch]$UseExistingRelease,

    [Parameter()]
    [switch]$Background,

    [Parameter()]
    [switch]$OpenBrowser
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
$projectDir = Join-Path $repoRoot "DreamGenClone.Web"
$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)

if (-not $RuntimeRoot) {
    $RuntimeRoot = Join-Path $repoRoot "artifacts\runtime\web"
}
$RuntimeRoot = [System.IO.Path]::GetFullPath($RuntimeRoot)

$pidFile = Join-Path $RuntimeRoot "app.pid"
$currentFile = Join-Path $RuntimeRoot "current-release.txt"

function Write-Section {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor DarkGray
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

function New-ReleaseId {
    param([string]$Name)

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    if (-not $Name) {
        return $stamp
    }

    $sanitized = ($Name -replace '[^A-Za-z0-9._-]', '-').Trim('-')
    if (-not $sanitized) {
        return $stamp
    }

    return "$stamp-$sanitized"
}

function Get-RunningAppProcesses {
    # Deliberately narrower than the filter in start-webapp.ps1's stop action: that one also matches
    # 'dotnet build|publish|test ...DreamGenClone.csproj' and would kill a concurrent agent build.
    # Matches both layouts: bin\...\DreamGenClone.dll (other helpers) and
    # artifacts\runtime\web\<release>\DreamGenClone.dll (this helper).
    $results = [System.Collections.Generic.List[object]]::new()

    $dotnetProcs = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*DreamGenClone.dll*" }
    foreach ($proc in @($dotnetProcs)) {
        if ($null -eq $proc) { continue }
        $results.Add([PSCustomObject]@{ ProcessId = $proc.ProcessId; CommandLine = $proc.CommandLine })
    }

    $hostProcs = Get-Process -Name $projectName -ErrorAction SilentlyContinue
    foreach ($proc in @($hostProcs)) {
        if ($null -eq $proc) { continue }
        $results.Add([PSCustomObject]@{ ProcessId = $proc.Id; CommandLine = "$($proc.Path)" })
    }

    return $results.ToArray()
}

function Resolve-ExistingRelease {
    param([string]$Root)

    $candidates = @(
        Get-ChildItem -Path $Root -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
    )

    foreach ($dir in $candidates) {
        if (Test-Path (Join-Path $dir.FullName "$projectName.dll")) {
            return $dir.FullName
        }
    }

    return $null
}

function Stop-Processes {
    param($Processes, [string]$Reason)

    $list = @(@($Processes) | Where-Object { $null -ne $_ })
    if ($list.Count -eq 0) {
        return
    }

    Write-Host "Stopping $($list.Count) running DreamGenClone process(es) ($Reason)" -ForegroundColor Yellow
    foreach ($proc in $list) {
        try {
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop
            Write-Step "stopped PID $($proc.ProcessId)"
        }
        catch {
            Write-Step "PID $($proc.ProcessId) already exited"
        }
    }

    Start-Sleep -Seconds 2
}

function Invoke-Publish {
    param([string]$ReleaseDir)

    Write-Section "Publishing latest source"
    Write-Host "Running: dotnet publish DreamGenClone.Web -c $Configuration -o $ReleaseDir" -ForegroundColor DarkCyan

    $publishArgs = @("publish", $projectPath, "-c", $Configuration, "-o", $ReleaseDir, "--nologo")
    if ($SkipRestore) {
        $publishArgs += "--no-restore"
    }

    # Capture and re-emit so the publish output never lands in this function's return value.
    $publishOutput = & dotnet @publishArgs 2>&1
    $exitCode = $LASTEXITCODE
    foreach ($line in $publishOutput) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        Write-Host "Error: publish failed with exit code $exitCode. The running app was left untouched." -ForegroundColor Red
        Write-Host "If the output mentions 'file is locked by', stop the app (./helpers/start-webapp.ps1 stop) and retry." -ForegroundColor Yellow
        try {
            Remove-Item -LiteralPath $ReleaseDir -Recurse -Force -ErrorAction Stop
            Write-Step "removed incomplete release folder"
        }
        catch {
            Write-Step "left incomplete release folder in place"
        }
        exit $exitCode
    }

    $webAppDll = Join-Path $ReleaseDir "$projectName.dll"
    if (-not (Test-Path $webAppDll)) {
        Write-Host "Error: publish output is missing '$webAppDll'." -ForegroundColor Red
        exit 1
    }

    return $webAppDll
}

function Remove-OldReleases {
    param([string]$Root, [string]$CurrentReleaseDir, [int]$Keep)

    $others = @(
        Get-ChildItem -Path $Root -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -ne $CurrentReleaseDir } |
            Sort-Object Name -Descending
    )

    $keepOthers = [Math]::Max(0, $Keep - 1)
    $stale = @($others | Select-Object -Skip $keepOthers)
    if ($stale.Count -eq 0) {
        return
    }

    Write-Section "Pruning old releases (keeping $Keep)"
    foreach ($dir in $stale) {
        try {
            Remove-Item -LiteralPath $dir.FullName -Recurse -Force -ErrorAction Stop
            Write-Step "removed $($dir.Name)"
        }
        catch {
            Write-Step "kept $($dir.Name) (still in use)"
        }
    }
}

function New-SharedWebRootLinks {
    param([string]$WebRoot, [string]$ProjectWebRoot)

    # The published wwwroot serves every static asset (including the generated scoped-CSS bundle that has
    # no source copy), but one subtree is written at runtime and is also the input of the next publish:
    # pose-library holds authored/re-rendered pose skeletons and is git-tracked in the source tree. Point
    # it back at the source tree so runtime writes are not lost inside a throwaway release.
    foreach ($name in @("pose-library")) {
        $target = Join-Path $ProjectWebRoot $name
        if (-not (Test-Path $target)) {
            continue
        }

        $link = Join-Path $WebRoot $name
        if (Test-Path $link) {
            $item = Get-Item -LiteralPath $link -Force
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                # Remove the link itself. Never recurse here: PowerShell would empty the junction target.
                [System.IO.Directory]::Delete($link, $false)
            }
            else {
                Remove-Item -LiteralPath $link -Recurse -Force
            }
        }

        New-Item -ItemType Junction -Path $link -Target $target -ErrorAction Stop | Out-Null
        Write-Step "linked wwwroot\$name -> $target"
    }
}

function Start-DeferredBrowserOpen {
    param([string]$Url, [int]$TimeoutSeconds = 90)

    Start-Job -Name "DreamGenClone.PublishAndRun.OpenBrowser" -ScriptBlock {
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

function Start-PublishedApp {
    param([string]$WebAppDll, [string]$ReleaseId)

    $env:ASPNETCORE_ENVIRONMENT = "Development"

    $webRoot = Join-Path (Split-Path -Parent $WebAppDll) "wwwroot"
    if (-not (Test-Path $webRoot)) {
        Write-Host "Error: published web root not found at '$webRoot'." -ForegroundColor Red
        exit 1
    }

    if ($OpenBrowser) {
        Start-DeferredBrowserOpen -Url $Urls
    }

    if (-not $Background) {
        Write-Section "Starting web app from release $ReleaseId (Ctrl+C to stop)"
        Write-Host "Working directory / content root: $projectDir" -ForegroundColor DarkCyan
        Write-Host "Web root: $webRoot" -ForegroundColor DarkCyan
        Push-Location $projectDir
        try {
            # Out-Host keeps the app's own stdout in the console instead of this function's return value.
            & dotnet "$WebAppDll" --urls "$Urls" --contentRoot "$projectDir" --webroot "$webRoot" | Out-Host
        }
        finally {
            Pop-Location
        }

        return
    }

    $logDir = Join-Path $RuntimeRoot "logs"
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $stdoutLog = Join-Path $logDir "$ReleaseId.out.log"
    $stderrLog = Join-Path $logDir "$ReleaseId.err.log"

    Write-Section "Starting web app from release $ReleaseId in the background"
    Write-Host "Web root: $webRoot" -ForegroundColor DarkCyan
    $proc = Start-Process -FilePath "dotnet" `
        -ArgumentList @("`"$WebAppDll`"", "--urls", "`"$Urls`"", "--contentRoot", "`"$projectDir`"", "--webroot", "`"$webRoot`"") `
        -WorkingDirectory $projectDir `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog `
        -PassThru

    Set-Content -Path $pidFile -Value $proc.Id

    Write-Host "PID: $($proc.Id)" -ForegroundColor Green
    Write-Host "Stdout: $stdoutLog" -ForegroundColor DarkCyan
    Write-Host "Stderr: $stderrLog" -ForegroundColor DarkCyan
    Write-Host "Stop with: ./helpers/start-webapp.ps1 stop" -ForegroundColor DarkCyan
}

Test-Prerequisites

if ($UseExistingRelease -and $PublishOnly) {
    Write-Host "Error: -UseExistingRelease and -PublishOnly cannot be combined." -ForegroundColor Red
    exit 1
}

$releaseId = New-ReleaseId -Name $ReleaseName
$releaseDir = Join-Path $RuntimeRoot $releaseId
$webAppDll = $null

if ($UseExistingRelease) {
    $existingRelease = Resolve-ExistingRelease -Root $RuntimeRoot
    if (-not $existingRelease) {
        Write-Host "Error: no existing release containing $projectName.dll was found under '$RuntimeRoot'." -ForegroundColor Red
        Write-Host "Run without -UseExistingRelease to publish one." -ForegroundColor Yellow
        exit 1
    }

    $releaseDir = $existingRelease
    $releaseId = Split-Path -Leaf $releaseDir
    $webAppDll = Join-Path $releaseDir "$projectName.dll"
}

Write-Section "Publish-and-run"
Write-Host "Configuration: $Configuration" -ForegroundColor DarkCyan
Write-Host "Release dir:   $releaseDir" -ForegroundColor DarkCyan
if ($UseExistingRelease) {
    Write-Host "Mode:          reuse existing release (no publish) + run" -ForegroundColor DarkCyan
}
elseif ($PublishOnly) {
    Write-Host "Mode:          publish only (running app is not restarted)" -ForegroundColor DarkCyan
}
elseif ($Background) {
    Write-Host "Mode:          publish + run in background" -ForegroundColor DarkCyan
}
else {
    Write-Host "Mode:          publish + run in foreground" -ForegroundColor DarkCyan
}

$runningProcesses = @(Get-RunningAppProcesses)
$legacyProcesses = @($runningProcesses | Where-Object { $_.CommandLine -like "*\bin\*" })

if ($legacyProcesses.Count -gt 0) {
    if ($PublishOnly) {
        Write-Host "Note: a running instance is using bin\Debug\net9.0, which publish must overwrite." -ForegroundColor Yellow
        Write-Host "      -PublishOnly never stops the app, so the publish may fail on locked files." -ForegroundColor Yellow
    }
    elseif ($UseExistingRelease) {
        Write-Host "A running instance is using bin\Debug\net9.0; it will be stopped before the release copy starts." -ForegroundColor Yellow
    }
    else {
        Write-Host "A running instance is using bin\Debug\net9.0, which publish must overwrite." -ForegroundColor Yellow
        Stop-Processes -Processes $legacyProcesses -Reason "legacy bin-based instance blocks publish"
    }
}

if (-not $UseExistingRelease) {
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    $webAppDll = Invoke-Publish -ReleaseDir $releaseDir
}

New-SharedWebRootLinks -WebRoot (Join-Path $releaseDir "wwwroot") -ProjectWebRoot (Join-Path $projectDir "wwwroot")

Set-Content -Path $currentFile -Value $releaseDir
Remove-OldReleases -Root $RuntimeRoot -CurrentReleaseDir $releaseDir -Keep $KeepReleases

if ($PublishOnly) {
    Write-Section "Publish complete (app not restarted)"
    Write-Host "Release: $releaseDir" -ForegroundColor Green
    Write-Host "The previous app instance is still running and this release folder is now on disk." -ForegroundColor DarkGray
    Write-Host "Re-run without -PublishOnly to restart the app onto it." -ForegroundColor DarkGray
    exit 0
}

$runningProcesses = @(Get-RunningAppProcesses)
Stop-Processes -Processes $runningProcesses -Reason "restarting onto release $releaseId"

Start-PublishedApp -WebAppDll $webAppDll -ReleaseId $releaseId
$exitCode = $LASTEXITCODE

if ($Background) {
    Write-Section "Running from release $releaseId"
    Write-Host "Release: $releaseDir" -ForegroundColor Green
    Write-Host "URL:     $Urls" -ForegroundColor Green
}
else {
    Write-Section "App stopped (release $releaseId)"
    Write-Host "Release: $releaseDir" -ForegroundColor DarkGray
}

exit $exitCode
