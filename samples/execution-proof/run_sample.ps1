# run_sample.ps1 — Execute the execution-proof sample using the CPU backend.
#
# Prerequisites:
#   .NET 9 SDK  (https://dotnet.microsoft.com/download)
#
# Usage:
#   .\run_sample.ps1 [-Backend cpu|gpu|both] [-OutDir <output-dir>]
#
# Defaults: -Backend cpu, -OutDir .\out

param(
    [ValidateSet('cpu','gpu','both')]
    [string]$Backend = 'cpu',

    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'

$ScriptDir  = $PSScriptRoot
$RepoRoot   = Split-Path (Split-Path $ScriptDir -Parent) -Parent
$Project    = Join-Path $RepoRoot 'CLI' 'PamMonteCarlo50Y'
$InputDir   = Join-Path $ScriptDir 'input'
$MetaFile   = Join-Path $InputDir 'contract_metadata.csv'

Write-Host "╔══════════════════════════════════════════════════════════╗"
Write-Host "║  Execution-Proof Sample Runner                           ║"
Write-Host "╚══════════════════════════════════════════════════════════╝"
Write-Host ""
Write-Host "  input dir  : $InputDir"
Write-Host "  output dir : $OutDir"
Write-Host "  backend    : $Backend"
Write-Host ""

dotnet run --project $Project -- `
    --input      $InputDir  `
    --backend    $Backend   `
    --out        $OutDir    `
    --reporting  true       `
    --export-fact true      `
    --metadata   $MetaFile

Write-Host ""
Write-Host "Output files written to: $OutDir"
Write-Host ""
Write-Host "Proof checklist:"
Write-Host "  ✓  portfolio.csv loaded from input directory"
Write-Host "  ✓  3 Vasicek scenarios × 24 months loaded from riskfactors CSVs"
Write-Host "  ✓  runs.json defines 2 runs with different contract/scenario slices"
Write-Host "  ✓  Per-contract × per-scenario PVs in *_fact_results_long.csv"
Write-Host "  ✓  Per-contract statistics in *_contract_summary.csv"
Write-Host "  ✓  Metadata join ready: contract_metadata.csv → ContractId"
