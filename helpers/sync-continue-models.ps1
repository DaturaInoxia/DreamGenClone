param(
    [string]$SourcePath = ".continue/models.source.json",
    [string]$ConfigPath = ".continue/config.yml",
    [string]$ApiKeyEnvVar = "OPENROUTER_API_KEY"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $SourcePath)) {
    throw "Source file not found: $SourcePath"
}

if (-not (Test-Path -Path $ConfigPath)) {
    throw "Config file not found: $ConfigPath"
}

function Get-FirstValue {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    foreach ($name in $Names) {
        if ($Object.PSObject.Properties.Match($name).Count -gt 0) {
            $value = $Object.$name
            if ($null -ne $value -and [string]::IsNullOrWhiteSpace([string]$value) -eq $false) {
                return [string]$value
            }
        }
    }

    return $null
}

function Get-NumberValue {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string[]]$Names,
        [Parameter(Mandatory = $true)][double]$DefaultValue
    )

    foreach ($name in $Names) {
        if ($Object.PSObject.Properties.Match($name).Count -gt 0) {
            $value = $Object.$name
            if ($null -ne $value) {
                return [double]$value
            }
        }
    }

    return $DefaultValue
}

function Escape-YamlSingleQuoted {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value.Replace("'", "''")
}

$raw = Get-Content -Path $SourcePath -Raw
$data = $raw | ConvertFrom-Json

$inputModels = @()
if ($data -is [System.Collections.IEnumerable] -and $data -isnot [string]) {
    $inputModels = @($data)
} elseif ($data.PSObject.Properties.Match("models").Count -gt 0) {
    $inputModels = @($data.models)
} else {
    throw "Source JSON must be either an array or an object with a 'models' property."
}

if ($inputModels.Count -eq 0) {
    throw "No models found in source JSON."
}

$entries = New-Object System.Collections.Generic.List[string]
$added = 0

foreach ($m in $inputModels) {
    $enabled = $true
    if ($m.PSObject.Properties.Match("enabled").Count -gt 0 -and $null -ne $m.enabled) {
        $enabled = [bool]$m.enabled
    }

    if (-not $enabled) {
        continue
    }

    $provider = Get-FirstValue -Object $m -Names @("provider")
    if ([string]::IsNullOrWhiteSpace($provider)) {
        $provider = "openrouter"
    }

    $modelId = Get-FirstValue -Object $m -Names @("model", "id", "modelId")
    if ([string]::IsNullOrWhiteSpace($modelId)) {
        continue
    }

    $displayName = Get-FirstValue -Object $m -Names @("name", "displayName")
    if ([string]::IsNullOrWhiteSpace($displayName)) {
        $displayName = $modelId
    }

    $apiKey = Get-FirstValue -Object $m -Names @("apiKey")
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        $apiKey = "`${$ApiKeyEnvVar}"
    }

    $temperature = Get-NumberValue -Object $m -Names @("temperature") -DefaultValue 0.2
    $maxTokens = [int](Get-NumberValue -Object $m -Names @("maxTokens", "maxOutputTokens") -DefaultValue 8000)

    $roles = @("chat", "edit")
    if ($m.PSObject.Properties.Match("roles").Count -gt 0 -and $null -ne $m.roles) {
        $roles = @($m.roles)
    }

    $entries.Add("  - name: " + (Escape-YamlSingleQuoted -Value $displayName))
    $entries.Add("    provider: " + (Escape-YamlSingleQuoted -Value $provider))
    $entries.Add("    model: " + (Escape-YamlSingleQuoted -Value $modelId))
    $entries.Add("    apiKey: " + $apiKey)
    $entries.Add("    roles:")
    foreach ($role in $roles) {
        $entries.Add("      - " + (Escape-YamlSingleQuoted -Value ([string]$role)))
    }
    $entries.Add("    defaultCompletionOptions:")
    $entries.Add("      temperature: " + $temperature)
    $entries.Add("      maxTokens: " + $maxTokens)
    $entries.Add("")

    $added++
}

if ($added -eq 0) {
    throw "No enabled models with a model ID were found in source JSON."
}

if ($entries.Count -gt 0 -and [string]::IsNullOrWhiteSpace($entries[$entries.Count - 1])) {
    $entries.RemoveAt($entries.Count - 1)
}

$startMarker = "  # AUTO-GENERATED MODELS START"
$endMarker = "  # AUTO-GENERATED MODELS END"
$generatedBlock = @($startMarker) + @($entries) + @($endMarker)
$replacement = [string]::Join("`r`n", $generatedBlock)

$configText = Get-Content -Path $ConfigPath -Raw
if ($configText.Contains($startMarker) -eq $false -or $configText.Contains($endMarker) -eq $false) {
    throw "Could not find model markers in $ConfigPath. Add '$startMarker' and '$endMarker' under the models section first."
}

$pattern = "(?ms)^  # AUTO-GENERATED MODELS START.*?^  # AUTO-GENERATED MODELS END"
$updated = [regex]::Replace($configText, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $replacement }, 1)

Set-Content -Path $ConfigPath -Value $updated -Encoding UTF8
Write-Host "Synced $added model(s) into $ConfigPath"
