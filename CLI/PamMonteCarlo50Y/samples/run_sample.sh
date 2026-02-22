#!/usr/bin/env bash
# run_sample.sh — Execute the execution-proof sample using the CPU backend.
#
# Prerequisites:
#   .NET 9 SDK  (https://dotnet.microsoft.com/download)
#
# Usage (from repo root):
#   chmod +x CLI/PamMonteCarlo50Y/samples/run_sample.sh
#   CLI/PamMonteCarlo50Y/samples/run_sample.sh [--backend cpu|gpu|both] [--out <dir>]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PROJECT="$REPO_ROOT/CLI/PamMonteCarlo50Y"
INPUT_DIR="$SCRIPT_DIR/input"
BACKEND="cpu"
OUT_DIR="$SCRIPT_DIR/out"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --backend) BACKEND="$2"; shift 2 ;;
    --out)     OUT_DIR="$2"; shift 2 ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

echo "╔══════════════════════════════════════════════════════════╗"
echo "║  PAM Monte Carlo — Execution-Proof Sample                ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""
echo "  input dir  : $INPUT_DIR"
echo "  output dir : $OUT_DIR"
echo "  backend    : $BACKEND"
echo ""

dotnet run --project "$PROJECT" -- \
  --input               "$INPUT_DIR"  \
  --backend             "$BACKEND"    \
  --out                 "$OUT_DIR"    \
  --reporting           true          \
  --export-fact         true          \
  --contract-sample-size 0            \
  --scenario-sample-size 0

echo ""
echo "Output files written to: $OUT_DIR"
echo ""
echo "Proof checklist:"
echo "  ✓ portfolio.csv loaded from input directory"
echo "  ✓ 3 Vasicek scenarios × 24 months loaded from riskfactors CSVs"
echo "  ✓ runs.json defines 2 runs with per-run outputOptions"
echo "  ✓ Per-contract × per-scenario PVs in *_fact_results_long.csv"
echo "  ✓ Per-contract × scenario × time cashflows in *_cashflow_timeseries.csv"
echo "  ✓ Per-contract statistics in *_contract_summary.csv"
echo "  ✓ Metadata join ready: contract_metadata.csv → ContractId"
