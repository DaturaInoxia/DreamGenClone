# common.ps1 - shared env loader and helpers for RunPod/ComfyUI automation
# Sources local, git-ignored .runpod-env.ps1 for credentials. Never commit secrets.

$ErrorActionPreference = "Stop"

function Get-RunPodEnv {
    $envFile = Join-Path $PSScriptRoot ".runpod-env.ps1"
    if (-not (Test-Path $envFile)) {
        throw "Missing '$envFile'. Create it (git-ignored) with `$env:RUNPOD_API_KEY and `$env:COMFYUI_URL."
    }
    . $envFile
    if ([string]::IsNullOrWhiteSpace($env:RUNPOD_API_KEY)) {
        throw "RUNPOD_API_KEY is not set in $envFile."
    }
    if ([string]::IsNullOrWhiteSpace($env:COMFYUI_URL)) {
        throw "COMFYUI_URL is not set in $envFile."
    }
}

function Invoke-RunPodApi {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [string]$Method = "GET",
        [object]$Body = $null
    )
    Get-RunPodEnv
    $headers = @{ Authorization = "Bearer $env:RUNPOD_API_KEY" }
    $params = @{ Uri = $Url; Method = $Method; Headers = $headers }
    if ($null -ne $Body) {
        $params.ContentType = "application/json"
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
    }
    Invoke-RestMethod @params
}

function Invoke-ComfyUi {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("Prompt", "Queue", "History")][string]$Kind,
        [object]$Body = $null
    )
    Get-RunPodEnv
    $url = switch ($Kind) {
        "Prompt"  { "$env:COMFYUI_URL/prompt" }
        "Queue"   { "$env:COMFYUI_URL/queue" }
        "History" { "$env:COMFYUI_URL/history" }
    }
    if ($Kind -eq "Prompt") {
        $payload = @{ prompt = $Body; client_id = "dreamgen-runpod-script" }
        return Invoke-RestMethod -Uri $url -Method POST -ContentType "application/json" -Body ($payload | ConvertTo-Json -Depth 20)
    }
    return Invoke-RestMethod -Uri $url -Method GET
}