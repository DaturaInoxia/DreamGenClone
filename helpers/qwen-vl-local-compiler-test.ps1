# qwen-vl-local-compiler-test.ps1
# -----------------------------------------------------------------------------
# Realistic end-to-end test of the local LM Studio Qwen2.5-VL compiler endpoint.
#
# Mirrors EXACTLY what the app does in SceneImageEditCompilationJobHandler +
# QwenSceneImageEditPromptCompiler:
#   * same system prompt  (QwenSceneImageEditPromptCompiler.BuildSystemMessage)
#   * same user-message format  ("Raw edit intent:" + intent)
#   * same json_schema response_format  (scene_image_edit_compilation)
#   * same strict parse rules  (QwenSceneImageEditPromptCompiler.Parse)
#
# Run from the repo root. Requires the LM Studio server serving on the LAN.
# -----------------------------------------------------------------------------
param(
    [string]$BaseUrl = "http://192.168.0.16:1234",
    [string]$Model = "qwen2.5-vl-7b-instruct-abliterated",
    [string]$ChatPath = "/v1/chat/completions",
    [string]$Intent = "Change the woman's red tank top to black.",
    [string]$ImagePath = "specs\image-generator-tests\identity-two-character\runs\qwenpack-multiangle-20260827\c1_s1001.png",
    [string]$AppendSystemGuidance = "",
    [int]$MaxTokens = 256,
    [double]$Temperature = 0.2,
    [double]$TopP = 0.8
)

$ErrorActionPreference = "Stop"

# --- 1. Load the source image exactly as SceneImageMultimodalInput does ---------
if (-not (Test-Path $ImagePath)) { throw "Image not found: $ImagePath" }
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $ImagePath))
$b64 = [System.Convert]::ToBase64String($bytes)
$mediaType = "image/png"
# Read PNG width/height from the IHDR chunk (same source-image dimensions the app passes to Parse).
$imgW = 0; $imgH = 0
if ($bytes.Length -ge 24 -and $bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50) {
    $imgW = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 16))
    $imgH = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 20))
}
Write-Host "[1/5] source image: $ImagePath ($([math]::Round($bytes.Length/1KB,0)) KB, ${imgW}x${imgH})"

# --- 2. System prompt (verbatim copy of BuildSystemMessage) ----------------------
$system = @'
You are a vision-grounded compiler for Qwen Image Edit. Inspect the supplied source image and compile the user's request into one concise edit instruction.

Observe only visible facts needed to satisfy the request. Identify targets with visible locators such as clothing, position, laterality, or nearby objects. Do not invent names, relationships, hidden anatomy, unseen details, or story facts.

The user's request is authoritative. If they ask to add, remove, or alter a specific visible thing — clothing, an accessory such as glasses, an object (including moving or repositioning it), pose (looking another way, standing, lowering the head, opening the mouth), framing or zoom, or facial expression — compile that change directly. Never reject a request merely because it changes a category named in the preservation list.

Preserve only what the request did not ask to change: the location and surroundings, the subject's identity, and any unaffected people. When a request changes framing or moves an object, keep the surrounding location and identity intact while applying the change.

Return clarification_required only when the target is ambiguous (more than one visible candidate) or a visible detail is uncertain. Return invalid only when the request is genuinely impossible or self-contradictory (for example, two mutually exclusive outcomes), the thing to change is not visible in the source, or the content is clearly harmful or illegal. This editor is used for private, consensual adult fictional scenes; do not refuse an edit merely because it is sexual or adult in nature when the target and change are visible and feasible. Never guess a ready edit.

Ready instructions must be direct and feasible, describe only the requested change, and state the specific things to keep unchanged (usually the setting and identity). Return only JSON matching the supplied schema. Do not use markdown fences or explanatory text.
'@
if ($AppendSystemGuidance) { $system = $system + "`n`n" + $AppendSystemGuidance }

# --- 3. User message (verbatim copy of BuildMessages format) ----------------------
$user = "Raw edit intent:`n" + $Intent.Trim()

# --- 4. Response schema (verbatim copy of CreateResponseSchema) -------------------
$schema = @{
    type = "object"
    additionalProperties = $false
    required = @("schemaVersion","status","sourceSummary","targets","requestedChanges","preserve","clarificationQuestion","invalidReason","compiledPrompt")
    properties = @{
        schemaVersion = @{ const = "scene-image-edit-compiler-v1" }
        status        = @{ enum = @("ready","clarification_required","invalid") }
        sourceSummary = @{ type = "string"; minLength = 1 }
        targets = @{
            type = "array"
            items = @{
                type = "object"
                additionalProperties = $false
                required = @("key","visibleLocator","region")
                properties = @{
                    key            = @{ type = "string"; minLength = 1 }
                    visibleLocator = @{ type = "string"; minLength = 1 }
                    region = @{
                        anyOf = @(
                            @{ type = "null" },
                            @{
                                type = "object"
                                additionalProperties = $false
                                required = @("x","y","width","height")
                                properties = @{
                                    x      = @{ type = "number"; minimum = 0; maximum = 1 }
                                    y      = @{ type = "number"; minimum = 0; maximum = 1 }
                                    width  = @{ type = "number"; exclusiveMinimum = 0; maximum = 1 }
                                    height = @{ type = "number"; exclusiveMinimum = 0; maximum = 1 }
                                }
                            }
                        )
                    }
                }
            }
        }
        requestedChanges = @{ type = "array"; items = @{ type = "string"; minLength = 1 } }
        preserve         = @{ type = "array"; items = @{ type = "string"; minLength = 1 } }
        clarificationQuestion = @{ type = @("string","null") }
        invalidReason         = @{ type = @("string","null") }
        compiledPrompt        = @{ type = @("string","null") }
    }
}

$body = @{
    model    = $Model
    messages = @(
        @{ role = "system"; content = $system },
        @{ role = "user";   content = @(
            @{ type = "text"; text = $user },
            @{ type = "image_url"; image_url = @{ url = "data:$mediaType;base64,$b64" } }
        ) }
    )
    temperature = $Temperature
    top_p       = $TopP
    max_tokens  = $MaxTokens
    response_format = @{
        type = "json_schema"
        json_schema = @{
            name   = "scene_image_edit_compilation"
            strict = $true
            schema = $schema
        }
    }
} | ConvertTo-Json -Depth 30

# Validate the serialized body is valid JSON before sending (PS 5.1 ConvertTo-Json can be finicky).
try { $null = $body | ConvertFrom-Json } catch { throw "Serialized request body is NOT valid JSON: $($_.Exception.Message)" }

$url = ($BaseUrl.TrimEnd('/')) + "/" + ($ChatPath.TrimStart('/'))
Write-Host "[2/5] POST $url  model=$Model"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$resp = Invoke-RestMethod -Uri $url -Method Post -ContentType "application/json" -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 300
$sw.Stop()
$raw = $resp.choices[0].message.content
Write-Host "[3/5] HTTP 200 in $([math]::Round($sw.Elapsed.TotalSeconds,1))s; echoed model: $($resp.model)"

if ($resp.model -ne $Model) { throw "Model echo mismatch: '$($resp.model)' != '$Model'" }
Write-Host "[4/5] model echo OK"
Write-Host "`n=== RAW MODEL RESPONSE ==="
$raw

# --- 5. Strict parse (mirrors QwenSceneImageEditPromptCompiler.Parse) -----------
$root = $raw | ConvertFrom-Json
$rootFields = @($root.psobject.Properties.Name)
$expected = @("schemaVersion","status","sourceSummary","targets","requestedChanges","preserve","clarificationQuestion","invalidReason","compiledPrompt")
if (($rootFields.Count -ne $expected.Count) -or (Compare-Object $rootFields $expected)) {
    throw "Parse FAIL: unknown/missing/duplicate root fields: $($rootFields -join ',')"
}
if ($root.schemaVersion -ne "scene-image-edit-compiler-v1") { throw "Parse FAIL: schemaVersion '$($root.schemaVersion)'" }
if ($root.status -notin @("ready","clarification_required","invalid")) { throw "Parse FAIL: status '$($root.status)'" }

switch ($root.status) {
    "ready" {
        if (($root.targets.Count -eq 0) -or ($root.requestedChanges.Count -eq 0) -or ($root.preserve.Count -eq 0) -or [string]::IsNullOrWhiteSpace($root.compiledPrompt)) {
            throw "Parse FAIL: ready result missing targets/changes/preserve/compiledPrompt"
        }
        foreach ($t in $root.targets) {
            if (($t.psobject.Properties.Name.Count -ne 3) -or -not $t.key -or -not $t.visibleLocator) { throw "Parse FAIL: bad target $($t | ConvertTo-Json -Compress)" }
            if ($t.region) {
                # Mirror the app's ParseRegion fix: normalize pixel-scale regions using the source
                # image dimensions, then clamp into [0,1] before the strict containment check.
                $px = ($t.region.x -gt 1) -or ($t.region.y -gt 1) -or ($t.region.width -gt 1) -or ($t.region.height -gt 1)
                if ($px) {
                    if ($imgW -le 0 -or $imgH -le 0) { throw "Parse FAIL: pixel-scale region but no image dimensions" }
                    $t.region.x      = [math]::Min([math]::Max([double]$t.region.x / $imgW, 0.0), 1.0)
                    $t.region.y      = [math]::Min([math]::Max([double]$t.region.y / $imgH, 0.0), 1.0)
                    $t.region.width  = [math]::Min([math]::Max([double]$t.region.width / $imgW, 0.0), 1.0 - $t.region.x)
                    $t.region.height = [math]::Min([math]::Max([double]$t.region.height / $imgH, 0.0), 1.0 - $t.region.y)
                }
                if (($t.region.x -lt 0) -or ($t.region.y -lt 0) -or ($t.region.width -le 0) -or ($t.region.height -le 0) -or (($t.region.x + $t.region.width) -gt 1.000000001) -or (($t.region.y + $t.region.height) -gt 1.000000001)) {
                    throw "Parse FAIL: target region out of bounds after normalization: $($t.region | ConvertTo-Json -Compress)"
                }
            }
        }
        Write-Host "[5/5] PARSE OK (status=ready)"
    }
    "clarification_required" {
        if ([string]::IsNullOrWhiteSpace($root.clarificationQuestion)) { throw "Parse FAIL: clarification_required missing question" }
        Write-Host "[5/5] PARSE OK (status=clarification_required)"
    }
    "invalid" {
        if ([string]::IsNullOrWhiteSpace($root.invalidReason)) { throw "Parse FAIL: invalid missing reason" }
        Write-Host "[5/5] PARSE OK (status=invalid)"
    }
}

Write-Host "`n=== COMPILED RESULT ==="
$raw | ConvertFrom-Json | ConvertTo-Json -Depth 10
