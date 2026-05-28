<#
.SYNOPSIS
Merges the current branch into development.

.DESCRIPTION
Runs a safe local merge workflow:
1) validates repository and clean working tree,
2) resolves source branch (current branch unless overridden),
3) checks out development,
4) optionally fast-forwards development from origin,
5) merges source branch into development with --no-ff,
6) optionally pushes development to origin.

.EXAMPLE
./helpers/merge-current-branch-to-development.ps1

.EXAMPLE
./helpers/merge-current-branch-to-development.ps1 -Push

.EXAMPLE
./helpers/merge-current-branch-to-development.ps1 -SourceBranch feature/my-work -Push
#>

[CmdletBinding()]
param(
    [string]$RepositoryPath = (Split-Path -Parent $PSScriptRoot),
    [string]$SourceBranch,
    [switch]$Push,
    [switch]$SkipPull
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Cyan
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args
    )

    & git -C $script:RepositoryPath @Args
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: git -C '$script:RepositoryPath' $($Args -join ' ')"
    }
}

function Get-GitOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args
    )

    $output = & git -C $script:RepositoryPath @Args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: git -C '$script:RepositoryPath' $($Args -join ' ')`n$output"
    }

    return ($output | Out-String).Trim()
}

function Test-OriginRemote {
    & git -C $script:RepositoryPath remote get-url origin *> $null
    return ($LASTEXITCODE -eq 0)
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git was not found in PATH. Install Git and try again."
}

$RepositoryPath = (Resolve-Path -Path $RepositoryPath).Path

[void](Get-GitOutput -Args @("rev-parse", "--is-inside-work-tree"))

$status = Get-GitOutput -Args @("status", "--porcelain")
if (-not [string]::IsNullOrWhiteSpace($status)) {
    throw "Working tree is not clean. Commit or stash changes before merging."
}

$currentBranch = Get-GitOutput -Args @("rev-parse", "--abbrev-ref", "HEAD")
if ([string]::IsNullOrWhiteSpace($SourceBranch)) {
    if ($currentBranch -eq "HEAD") {
        throw "HEAD is detached. Provide -SourceBranch explicitly."
    }

    $SourceBranch = $currentBranch
}

if ($SourceBranch -eq "development") {
    throw "Source branch resolves to 'development'. Checkout your feature branch first or pass -SourceBranch."
}

& git -C $RepositoryPath show-ref --verify --quiet "refs/heads/$SourceBranch"
if ($LASTEXITCODE -ne 0) {
    throw "Source branch '$SourceBranch' does not exist locally."
}

& git -C $RepositoryPath show-ref --verify --quiet "refs/heads/development"
if ($LASTEXITCODE -ne 0) {
    throw "Local branch 'development' does not exist."
}

Write-Section "Merging '$SourceBranch' into development"

if ($currentBranch -ne "development") {
    Write-Host "Checking out development..." -ForegroundColor Yellow
    Invoke-Git -Args @("checkout", "development")
}
else {
    Write-Host "Already on development." -ForegroundColor Yellow
}

if (-not $SkipPull) {
    if (Test-OriginRemote) {
        Write-Host "Pulling latest origin/development (ff-only)..." -ForegroundColor Yellow
        Invoke-Git -Args @("pull", "--ff-only", "origin", "development")
    }
    else {
        Write-Host "Remote 'origin' not found. Skipping pull." -ForegroundColor Yellow
    }
}

Write-Host "Merging branch '$SourceBranch'..." -ForegroundColor Yellow
Invoke-Git -Args @("merge", "--no-ff", $SourceBranch)

if ($Push) {
    if (-not (Test-OriginRemote)) {
        throw "-Push was requested but remote 'origin' was not found."
    }

    Write-Host "Pushing development to origin..." -ForegroundColor Yellow
    Invoke-Git -Args @("push", "origin", "development")
}

Write-Host "Merge completed successfully." -ForegroundColor Green
if (-not $Push) {
    Write-Host "Run 'git push origin development' when ready." -ForegroundColor Cyan
}
