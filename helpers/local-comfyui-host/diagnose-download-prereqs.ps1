"PS version: $($PSVersionTable.PSVersion)"
$c = Get-Command curl.exe -ErrorAction SilentlyContinue
if ($c) { "curl: " + $c.Source } else { "curl: MISSING" }
"target dir exists: " + (Test-Path 'D:\ComfyUI\models\checkpoints')

$parseErrors = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile('C:\Users\kenac\download-qwen-aio-checkpoint.ps1', [ref]$null, [ref]$parseErrors)
"parse errors: " + ($parseErrors | Measure-Object).Count
$parseErrors | ForEach-Object { "  " + $_.Message }
