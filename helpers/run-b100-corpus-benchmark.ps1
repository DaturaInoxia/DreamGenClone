[CmdletBinding()]
param(
    [ValidateRange(1, [int]::MaxValue)]
    [int]$Iterations = 1,

    [string]$Corpus,

    [string]$ConfigDb,

    [string]$Output,

    [string]$Case,

    [ValidateSet('Catalogue', 'BeatProduction', 'MomentDiscovery', 'MomentEnrichment')]
    [string]$Stage,

    [switch]$KeepWorkingDb
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'DreamGenClone.CorpusRunner\DreamGenClone.CorpusRunner.csproj'
$runnerArguments = @('--iterations', $Iterations.ToString())

if ($Corpus) {
    $runnerArguments += @('--corpus', $Corpus)
}
if ($ConfigDb) {
    $runnerArguments += @('--config-db', $ConfigDb)
}
if ($Output) {
    $runnerArguments += @('--output', $Output)
}
if ($Case) {
    $runnerArguments += @('--case', $Case)
}
if ($Stage) {
    $runnerArguments += @('--stage', $Stage)
}
if ($KeepWorkingDb) {
    $runnerArguments += '--keep-working-db'
}

& dotnet run --project $projectPath -- @runnerArguments
exit $LASTEXITCODE
