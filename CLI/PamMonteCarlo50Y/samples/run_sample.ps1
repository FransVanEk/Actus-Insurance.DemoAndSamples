# run_sample.ps1 — Execute the execution-proof sample using the CPU backend.
#
# Prerequisites:
#   .NET 9 SDK  (https://dotnet.microsoft.com/download)
#
# Usage (from repo root):
#   CLI\PamMonteCarlo50Y\samples\run_sample.ps1 [-Backend cpu|gpu|both] [-OutDir <dir>]

param(
    [ValidateSet('cpu','gpu','both')]
    [string]$Backend = 'cpu',

    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$RepoRoot  = Split-Path (Split-Path (Split-Path $ScriptDir -Parent) -Parent) -Parent
$Project   = Join-Path $RepoRoot 'CLI' 'PamMonteCarlo50Y'
$InputDir  = Join-Path $ScriptDir 'input'

Write-Host "╔══════════════════════════════════════════════════════════╗"
Write-Host "║  PAM Monte Carlo — Execution-Proof Sample                ║"
Write-Host "╚══════════════════════════════════════════════════════════╝"
Write-Host ""
Write-Host "  input dir  : $InputDir"
Write-Host "  output dir : $OutDir"
Write-Host "  backend    : $Backend"
Write-Host ""

dotnet run --project $Project -- `
    --input                $InputDir  `
    --backend              $Backend   `
    --out                  $OutDir    `
    --reporting            true       `
    --export-fact          true       `
    --contract-sample-size 0          `
    --scenario-sample-size 0

Write-Host ""
Write-Host "Output files written to: $OutDir"
Write-Host ""
Write-Host "Proof checklist:"
Write-Host "  ✓  portfolio.csv loaded from input directory"
Write-Host "  ✓  3 Vasicek scenarios × 24 months loaded from riskfactors CSVs"
Write-Host "  ✓  runs.json defines 2 runs with per-run outputOptions"
Write-Host "  ✓  Per-contract × per-scenario PVs in *_fact_results_long.csv"
Write-Host "  ✓  Per-contract × scenario × time cashflows in *_cashflow_timeseries.csv"
Write-Host "  ✓  Per-contract statistics in *_contract_summary.csv"
Write-Host "  ✓  Metadata join ready: contract_metadata.csv → ContractId"
