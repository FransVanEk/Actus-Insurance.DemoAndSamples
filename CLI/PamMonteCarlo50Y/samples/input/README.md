# Execution-Proof Sample — Input Directory

This directory contains a ready-made input dataset for the PAM Monte Carlo demo.

## Contents

```
input/
├── portfolio.csv                   ← 5 PAM contracts (fixed + floating)
├── contract_metadata.csv           ← descriptive fields NOT used in valuation
├── runs.json                       ← 2 run requests with per-run outputOptions
└── scenarios/
    ├── scenario_set.json           ← Vasicek params + file references
    └── riskfactors/
        ├── interest_rate_after.csv ← 3 scenarios × 24 months
        └── interest_rate_prior.csv ← header-only (calcDateIndex=0)
```

## How to run

From the repository root:

```
dotnet run --project CLI/PamMonteCarlo50Y -- --input CLI/PamMonteCarlo50Y/samples/input --backend cpu --out CLI/PamMonteCarlo50Y/samples/out
```

## Output files produced

After the run, `samples/out/` contains:

| File | Description |
|------|-------------|
| `proof_fwd_cpu_contract_summary.csv` | Per-contract PV statistics (MeanPV, StdPV, P05–ES99) |
| `proof_fwd_cpu_portfolio_by_scenario.csv` | Portfolio PV per scenario |
| `proof_fwd_cpu_fact_results_long.csv` | PV per contract × scenario |
| `proof_fwd_cpu_cashflow_timeseries.csv` | Full cashflow detail: contract × scenario × time × eventType |
| `proof_fwd_cpu_grouped_by_*.csv` | Mean PV grouped by Segment, Region, ProductLine |
| `proof_slice_cpu_contract_summary.csv` | Same for 3-contract slice |
| `runs.csv` | Run dimension table |
| `_README.txt` | Auto-generated Excel join guide |

## `runs.json` — outputOptions schema

Each run request can carry an `outputOptions` block to control exactly what
output files are written for that run (overrides the global CLI flags):

```json
{
  "outputOptions": {
    "reporting": true,
    "exportPvFact": true,
    "exportCashflowTimeSeries": true,
    "contractSampleSize": 0,
    "scenarioSampleSize": 0
  }
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `reporting` | `true` | Write contract_summary, portfolio_by_scenario, grouped summaries |
| `exportPvFact` | `false` | Also write `fact_results_long.csv` (PV per contract × scenario) |
| `exportCashflowTimeSeries` | `false` | Write `cashflow_timeseries.csv` (full event-level detail) |
| `contractSampleSize` | `0` | Max contracts in exports (0 = all) |
| `scenarioSampleSize` | `0` | Max scenarios in exports (0 = all) |

## `cashflow_timeseries.csv` — column reference

The cashflow time-series export is the most granular output: one row per
(contract, scenario, cashflow event). Primary key: `RunId + ContractId + ScenarioId + EventDate + EventType`.

| Column | Description |
|--------|-------------|
| `RunId` | Run identifier (joins to `runs.csv`) |
| `ContractId` | Contract identifier (joins to `contract_metadata.csv`) |
| `ScenarioId` | Scenario index (0-based absolute) |
| `EventDate` | Calendar date of the cashflow event (yyyy-MM-dd) |
| `TimeIndex` | Month index on the Vasicek grid (0 = base date) |
| `EventType` | ACTUS event type: IED, IP, MD, RR, etc. |
| `UndiscountedCashflow` | Raw cashflow amount before discounting |
| `DiscountFactor` | Vasicek DF at (ScenarioId, TimeIndex) |
| `DiscountedCashflow` | UndiscountedCashflow × DiscountFactor (contribution to PV) |

## Excel analysis

Load `cashflow_timeseries.csv` and pivot by:
- Rows: `EventDate` or `TimeIndex` → see cashflow profile over time
- Columns: `ScenarioId` → compare cashflows across rate scenarios
- Filter: `ContractId` → inspect a single contract's waterfall
- Join with `contract_metadata.csv` on `ContractId` → analyse by Segment/Region/ProductLine
