# Input / Output Directory Contract

This document specifies the **stable on-disk format** for providing inputs to and
reading outputs from the PAM Monte Carlo CLI demo.

---

## Table of Contents

1. [Input Directory Structure](#input-directory-structure)
2. [portfolio.csv Schema](#portfoliocsv-schema)
3. [contract_metadata.csv Schema](#contract_metadatacsv-schema)
4. [scenario_set.json Schema](#scenario_setjson-schema)
5. [Risk Factor Arrays (CSV)](#risk-factor-arrays-csv)
6. [runs.json Schema](#runsjson-schema)
7. [Output Directory Structure](#output-directory-structure)
8. [Output File Schemas](#output-file-schemas)
9. [How to Run the CLI](#how-to-run-the-cli)
10. [Opening Outputs in Excel](#opening-outputs-in-excel)
11. [Validating Individual Contract Evaluation](#validating-individual-contract-evaluation)

---

## Input Directory Structure

```
<input-dir>/
├── portfolio.csv                          ← valuation engine fields only
├── contract_metadata.csv                  ← descriptive fields (NOT fed into valuation)
├── runs.json                              ← one or more run requests
└── scenarios/
    ├── scenario_set.json                  ← scenario metadata + model parameters
    └── riskfactors/
        ├── interest_rate_prior.csv        ← rates for t < calcDateIndex
        └── interest_rate_after.csv        ← rates for t ≥ calcDateIndex
```

See the working example in `samples/execution-proof/input/`.

---

## portfolio.csv Schema

Contains **only the fields required by the valuation kernel**.
Additional descriptive fields belong in `contract_metadata.csv`.

### Required Columns

| Column | Type | Example | Description |
|--------|------|---------|-------------|
| `ContractId` | string | `PAM_000000` | Unique contract identifier (primary key) |
| `InitialExchangeDate` | date | `2020-01-01` | Contract start date (ISO 8601 `YYYY-MM-DD`) |
| `MaturityDate` | date | `2025-01-01` | Maturity date (ISO 8601 `YYYY-MM-DD`) |
| `NotionalPrincipal` | decimal | `500000.00` | Face value (positive) |
| `NominalInterestRate` | decimal | `0.0500` | Annual rate, decimal (5% = `0.0500`) |
| `CycleOfInterestPayment` | string | `P1YL1` | Payment frequency (`P1ML1`/`P3ML1`/`P1YL1`) |

### Optional Columns

| Column | Type | Default | Description |
|--------|------|---------|-------------|
| `RateSpread` | decimal | `0.0` | Spread added to floating index |
| `MarketObjectCodeOfRateReset` | string | _(empty = fixed)_ | Risk-factor id for floating-rate resets (e.g., `USD_LIBOR_3M`) |
| `CycleOfRateReset` | string | _(empty)_ | Rate-reset frequency (e.g., `P3ML1`) |

### Loader Defaults (fields not in CSV)

| Field | Default value |
|-------|--------------|
| `Currency` | `USD` |
| `ContractRole` | `RPA` |
| `StatusDate` | = `InitialExchangeDate` |
| `CycleAnchorDateOfInterestPayment` | = `InitialExchangeDate` |
| `CycleAnchorDateOfRateReset` | = `InitialExchangeDate` |
| `AccruedInterest` | `0.0` |
| `RateMultiplier` | `1.0` |
| `DayCountConvention` | `A_365` |
| `BusinessDayConvention` | `NOS` |
| `Calendar` | `NC` |
| `NotionalScalingMultiplier` | `1.0` |
| `InterestScalingMultiplier` | `1.0` |

### Example

```csv
ContractId,InitialExchangeDate,MaturityDate,NotionalPrincipal,NominalInterestRate,CycleOfInterestPayment,RateSpread,MarketObjectCodeOfRateReset,CycleOfRateReset
PAM_000000,2020-01-01,2022-01-01,500000.00,0.0500,P1YL1,0.0000,,
PAM_000001,2020-01-01,2022-01-01,250000.00,0.0430,P3ML1,0.0000,,
PAM_000002,2020-01-01,2021-01-01,1000000.00,0.0000,P1YL1,0.0100,USD_LIBOR_3M,P3ML1
```

---

## contract_metadata.csv Schema

Contains **descriptive fields** that are **NOT** fed into the valuation engine.
Used exclusively as an analysis join target in Excel / PowerQuery.

### Required Columns

| Column | Type | Description |
|--------|------|-------------|
| `ContractId` | string | Join key → must match `portfolio.csv` |

### Recommended Columns (user-defined)

| Column | Type | Example | Description |
|--------|------|---------|-------------|
| `Segment` | string | `Corporate` | Business segment |
| `Region` | string | `Americas` | Geographic region |
| `ProductLine` | string | `TermLoan` | Product category |
| `Currency` | string | `USD` | Reporting currency |
| `Broker` | string | `BrokerAlpha` | Originating broker |
| `Underwriter` | string | `UW_West` | Underwriter team |

Any additional columns are passed through to grouped-summary outputs.

### Example

```csv
ContractId,Segment,Region,ProductLine,Currency,Broker,Underwriter
PAM_000000,Corporate,Americas,TermLoan,USD,BrokerAlpha,UW_West
PAM_000001,Retail,EMEA,PersonalLoan,USD,BrokerBeta,UW_East
PAM_000002,Corporate,APAC,FloatingLoan,USD,BrokerGamma,UW_East
```

---

## scenario_set.json Schema

Specifies scenario metadata and points to the risk-factor CSV arrays.

```json
{
  "scenarioSetId": "vasicek_3s_24m_seed12345",
  "description": "3 Vasicek scenarios × 24 monthly steps (2-year horizon)",
  "numScenarios": 3,
  "numMonths": 24,
  "seed": 12346,
  "model": {
    "type": "Vasicek",
    "kappa": 0.15,
    "theta": 0.04,
    "sigma": 0.02,
    "r0": 0.03
  },
  "riskFactors": [
    {
      "id": "USD_LIBOR_3M",
      "type": "InterestRate",
      "priorFile": "riskfactors/interest_rate_prior.csv",
      "afterFile": "riskfactors/interest_rate_after.csv"
    }
  ]
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `scenarioSetId` | string | Unique scenario set identifier |
| `numScenarios` | int | Number of Monte Carlo paths |
| `numMonths` | int | Horizon in monthly time steps |
| `seed` | ulong | XorShift64 seed for the rate generator |
| `model.type` | string | Interest-rate model (`Vasicek`) |
| `model.kappa` | double | Mean-reversion speed |
| `model.theta` | double | Long-run mean rate |
| `model.sigma` | double | Volatility |
| `model.r0` | double | Initial short rate |
| `riskFactors[].id` | string | Market object code referenced in `portfolio.csv` |
| `riskFactors[].priorFile` | string | Relative path to prior-rates CSV |
| `riskFactors[].afterFile` | string | Relative path to after-rates CSV |

---

## Risk Factor Arrays (CSV)

### `interest_rate_after.csv`

Full scenario × time rate matrix for `t ≥ calcDateIndex`.

```
scenarioIndex,timeIndex,shortRate,discountFactor
0,0,0.03000000,1.00000000
0,1,0.01568774,0.99869354
...
```

| Column | Type | Description |
|--------|------|-------------|
| `scenarioIndex` | int | 0-based scenario index (0 to `numScenarios-1`) |
| `timeIndex` | int | 0-based month index (0 to `numMonths-1`) |
| `shortRate` | double | Annualised short rate `r(s,t)` |
| `discountFactor` | double | `DF(s,t) = exp(−∑ r(s,i)·dt)` cumulative discount factor |

Rows must be sorted by `(scenarioIndex, timeIndex)`.
Total rows = `numScenarios × numMonths`.

### `interest_rate_prior.csv`

Same format as `interest_rate_after.csv` but only for `t < calcDateIndex`.
**Empty (header only) when `calcDateIndex = 0`** (pure forward simulation).

---

## runs.json Schema

Array of `RunRequest` objects.

```json
[
  {
    "id": "proof_fwd",
    "description": "Full portfolio run — proves all contracts evaluated individually",
    "contractStart": 0,
    "contractCount": 0,
    "scenarioStart": 0,
    "scenarioCount": 0,
    "calcDateIndex": 0
  },
  {
    "id": "proof_slice",
    "description": "Contract subset [0,3) — proves per-contract granularity",
    "contractStart": 0,
    "contractCount": 3,
    "scenarioStart": 0,
    "scenarioCount": 0,
    "calcDateIndex": 0
  }
]
```

| Field | Default | Description |
|-------|---------|-------------|
| `id` | `run0` | Run identifier (used in output file names) |
| `description` | `""` | Human-readable description |
| `contractStart` | `0` | First contract index (0-based) |
| `contractCount` | `0` | Number of contracts (0 = all from `contractStart`) |
| `scenarioStart` | `0` | First scenario index (0-based) |
| `scenarioCount` | `0` | Number of scenarios (0 = all from `scenarioStart`) |
| `calcDateIndex` | `0` | Month index for prior/after boundary (0 = pure forward) |

---

## Output Directory Structure

```
<output-dir>/
├── runs.csv                                 ← run dimension table
├── {runId}_cpu_summary.json                 ← timing + PV statistics per run/backend
├── {runId}_cpu_portfolio_pv_by_scenario.csv ← per-scenario portfolio PV (legacy)
├── {runId}_cpu_contract_pv_sample.csv       ← small contract×scenario sample (legacy)
│
│   (when --reporting true:)
├── {runId}_cpu_portfolio_by_scenario.csv    ← RunId, ScenarioId, PortfolioPV
├── {runId}_cpu_contract_summary.csv         ← per-contract statistics across scenarios
├── {runId}_cpu_fact_results_long.csv        ← long-format fact table (when --export-fact true)
├── {runId}_cpu_grouped_by_segment.csv       ← grouped summary (when --metadata supplied)
├── {runId}_cpu_grouped_by_region.csv        ← grouped summary (when --metadata supplied)
└── _README.txt                              ← auto-generated join guide
```

---

## Output File Schemas

### `runs.csv`

Run dimension table. Appended each run.

```csv
RunId,CalcDateIndex,ContractCount,ScenarioCount,Backend,Timestamp
proof_fwd_cpu,0,5,3,cpu,2024-01-15T10:30:00.000Z
```

### `{runId}_contract_summary.csv`

Per-contract statistics across all scenarios. **Primary key: `(RunId, ContractId)`.**

```csv
RunId,ContractId,MeanPV,StdPV,P05,P50,P95,VaR99,ES99
proof_fwd_cpu,PAM_000000,487234.12,3421.55,480100.00,487000.00,493500.00,...
proof_fwd_cpu,PAM_000001,241890.34,2100.22,...
```

**JOIN** this file with `contract_metadata.csv` on `ContractId` to analyse results
by Segment, Region, ProductLine, etc.

### `{runId}_portfolio_by_scenario.csv`

Per-scenario portfolio PV (sum of all contracts). **Primary key: `(RunId, ScenarioId)`.**

```csv
RunId,ScenarioId,PortfolioPV
proof_fwd_cpu,0,1934567.89
proof_fwd_cpu,1,1921345.67
proof_fwd_cpu,2,1841234.56
```

### `{runId}_fact_results_long.csv`

Long-format fact table with individual PV per `(RunId, ContractId, ScenarioId)`.
**This table proves every contract is evaluated individually per scenario.**

```csv
RunId,ContractId,ScenarioId,Measure,Value
proof_fwd_cpu,PAM_000000,0,PV,489234.123456
proof_fwd_cpu,PAM_000000,1,PV,483456.789012
proof_fwd_cpu,PAM_000000,2,PV,472345.678901
proof_fwd_cpu,PAM_000001,0,PV,243210.456789
...
```

**JOIN** on `ContractId` → `contract_metadata.csv` for full drill-down.
**JOIN** on `RunId` → `runs.csv` for run metadata.

---

## How to Run the CLI

### Using the sample runner (recommended)

```bash
# Linux / macOS
cd samples/execution-proof
chmod +x run_sample.sh
./run_sample.sh

# Windows PowerShell
cd samples\execution-proof
.\run_sample.ps1
```

### Using the CLI directly with `--input`

```bash
dotnet run --project CLI/PamMonteCarlo50Y -- \
  --input   samples/execution-proof/input  \
  --backend cpu                            \
  --out     ./my_output                    \
  --reporting  true                        \
  --export-fact true                       \
  --metadata samples/execution-proof/input/contract_metadata.csv
```

### CLI options for input-directory mode

| Option | Description |
|--------|-------------|
| `--input <dir>` | Input directory containing `portfolio.csv`, `scenarios/`, and optionally `runs.json` |
| `--backend cpu\|gpu\|both` | Execution backend (default: `both`) |
| `--out <dir>` | Output directory (default: `./out`) |
| `--reporting true` | Enable Excel-friendly CSV exports |
| `--export-fact true` | Also write `fact_results_long.csv` (per-contract × per-scenario) |
| `--metadata <path>` | Path to `contract_metadata.csv` for grouped summaries |

**Backwards compatibility**: All existing flags still work when `--input` is omitted;
the synthetic portfolio generator is used as before.

---

## Opening Outputs in Excel

### Step 1 — Load result files

1. Open Excel → **Data → Get Data → From Text/CSV**
2. Load `{runId}_cpu_contract_summary.csv`
3. Load `contract_metadata.csv` (from the input directory)
4. Load `{runId}_cpu_fact_results_long.csv` (optional, for scenario drill-down)
5. Load `runs.csv` (optional, for run metadata)

### Step 2 — Merge on ContractId

In **Power Query Editor**:

1. Select the `contract_summary` query
2. **Home → Merge Queries** (or `Merge Queries as New`)
3. Left table: `contract_summary` key = `ContractId`
4. Right table: `contract_metadata` key = `ContractId`
5. Join kind: **Left Outer**
6. Click the **expand** icon → select `Segment`, `Region`, `ProductLine`, `Broker`, etc.
7. **Close & Load**

### Step 3 — Build PivotTables

Go to **Insert → PivotTable** (from the merged query):

| Analysis goal | Rows | Values |
|---------------|------|--------|
| Mean PV by Region | `Region` | `MeanPV` (Average) |
| VaR99 by Segment | `Segment` | `VaR99` (Sum) |
| Portfolio P95 by Run | `RunId` | `P95` (Average) |
| Contract count by Broker | `Broker` | `ContractId` (Count) |

### Step 4 — Scenario drill-down

1. Load `fact_results_long.csv`
2. Merge with `contract_metadata` on `ContractId`
3. Merge with `runs.csv` on `RunId`
4. PivotTable with:
   - Rows: `Segment` + `ContractId`
   - Columns: `ScenarioId`
   - Values: `Value` (PV)
5. Add slicers for `Region`, `ProductLine`, `RunId`

---

## Validating Individual Contract Evaluation

The `fact_results_long.csv` file is the primary validation artifact.
Use the following checks:

### Check 1 — Every contract appears

```
Expected rows = numContracts × numScenarios × 1 (Measure = "PV")
```

In Excel:
1. Load `fact_results_long.csv`
2. **Data → PivotTable**
3. Rows: `ContractId`, Values: `ScenarioId` (Count)
4. Verify each `ContractId` has exactly `numScenarios` rows

### Check 2 — PVs differ across scenarios

Sort `fact_results_long.csv` by `(ContractId, ScenarioId)`.
The `Value` column should vary across scenarios, proving the engine uses per-scenario
discount factors and rate paths (not a single deterministic PV).

### Check 3 — Contract-level granularity via contract_summary

In `contract_summary.csv`:
- `StdPV > 0` for every contract confirms cross-scenario variance
- `P05 < P50 < P95` confirms the distribution is properly ordered

### Check 4 — Sliced run matches full run

Compare `proof_slice` (contracts 0–2) against the corresponding rows in `proof_fwd`:
- `proof_slice_cpu_contract_summary.csv` rows for `PAM_000000`, `PAM_000001`, `PAM_000002`
  must match the same rows in `proof_fwd_cpu_contract_summary.csv` (within floating-point tolerance)
- This proves that slicing does not affect individual contract PVs

### Check 5 — Determinism

Run the sample twice and diff the output files:

```bash
./run_sample.sh --out ./out1
./run_sample.sh --out ./out2
diff out1/proof_fwd_cpu_fact_results_long.csv out2/proof_fwd_cpu_fact_results_long.csv
# Expected: no differences
```

---

## Determinism Guarantee

- **Same input directory** → **identical outputs** across runs and platforms.
- Portfolio seed controls contract generation.
- Scenario seed (`scenario_set.json:seed`) controls all rate paths via XorShift64 + Box-Muller.
- No thread-level non-determinism in generators.
- Floating-point operations are deterministic on IEEE 754 compliant hardware.
