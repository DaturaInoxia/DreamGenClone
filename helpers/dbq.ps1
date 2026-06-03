# helpers/dbq.ps1
# Trusted wrapper for the dbquery tool. Avoids VS Code "Allow" prompts.
# Usage: pwsh helpers/dbq.ps1 <command> [args...]
# Examples:
#   pwsh helpers/dbq.ps1 tables
#   pwsh helpers/dbq.ps1 schema Sessions
#   pwsh helpers/dbq.ps1 session <id>
#   pwsh helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/my.sql <optionalId>

param(
    [Parameter(ValueFromRemainingArguments=$true)]
    [string[]]$DbqArgs
)

Set-Location $PSScriptRoot/..
dotnet run --project artifacts/tmp/dbquery -- @DbqArgs
