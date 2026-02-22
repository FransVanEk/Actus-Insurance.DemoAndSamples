# PAM Monte Carlo 50-Year Demo

Self-contained demo that values a portfolio of PAM contracts under Monte Carlo
interest-rate scenarios using both CPU and GPU paths.

The **execution-proof sample** (`samples/execution-proof/`) ships ready-made
input files (portfolio, scenarios, metadata) and produces Excel-ready CSV
outputs in a single command — see [Quick start: execution-proof sample](#quick-start-execution-proof-sample) below.

---

## What it does

| Feature | Details |
|---------|---------|
| **Portfolio** | Synthetic PAM contracts; heterogeneous notionals (100k–10M), maturities (1–50y), payment frequencies (monthly/quarterly/annual), fixed + floating (quarterly rate resets) |
| **Rate model** | Vasicek (Ornstein-Uhlenbeck): `dr = κ(θ−r)dt + σ√dt·Z` |
| **Horizon** | Configurable (default 50 years = 600 monthly steps) |
| **Scenarios** | Default 1,000 MC scenarios; configurable |
| **Discounting** | `DF[t] = exp(−∑ r[i]·dt)` pre-computed per scenario, used in PV kernel |
| **CPU path** | Parallel C# PV engine using `Parallel.For` over scenarios |
| **GPU path** | ILGPU 2-D kernel `(contracts × scenarios)`; falls back: CUDA → OpenCL → CPU |
| **Determinism** | Fully seeded (XorShift64 + Box-Muller); same seed → bit-for-bit identical runs |
| **Runs** | `RunRequest` concept: multiple valuations on the same portfolio with different `calcDateIndex`, contract/scenario slices |
| **Input directory** | Load portfolio + scenarios from stable on-disk CSV/JSON files with `--input` |
| **Excel outputs** | Per-contract statistics, portfolio PV by scenario, long-format fact table, metadata-grouped summaries |

---

## Quick start: execution-proof sample

This is the **recommended first run**. It uses the ready-made sample inputs and
produces all Excel-friendly CSV files in one command.

```
dotnet run --project CLI/PamMonteCarlo50Y -- --input samples/execution-proof/input --backend cpu --out samples/execution-proof/out --reporting true --export-fact true
```

### What gets produced

After the run, `samples/execution-proof/out/` contains:

| File | Use in Excel |
|------|-------------|
| `proof_fwd_cpu_contract_summary.csv` | Per-contract PV statistics (MeanPV, StdPV, P05–ES99) — **JOIN with metadata on ContractId** |
| `proof_fwd_cpu_portfolio_by_scenario.csv` | Portfolio PV per scenario — pivot by ScenarioId |
| `proof_fwd_cpu_fact_results_long.csv` | Long-format PV per contract × scenario — drill-down analysis |
| `proof_fwd_cpu_grouped_by_segment.csv` | Mean PV aggregated by business segment |
| `proof_fwd_cpu_grouped_by_region.csv` | Mean PV aggregated by region |
| `proof_fwd_cpu_grouped_by_productline.csv` | Mean PV aggregated by product line |
| `proof_slice_cpu_contract_summary.csv` | Same for the 3-contract slice run |
| `runs.csv` | Run dimension table — join to all other files on RunId |
| `_README.txt` | Auto-generated Excel join guide |

> **Metadata is auto-loaded** from `samples/execution-proof/input/contract_metadata.csv`
> because `--input` picks it up automatically.
> The metadata adds `Segment`, `Region`, `ProductLine`, `Broker`, `Underwriter` to the analysis.

### Analysing the results in Excel

1. **Open Excel** → Data → Get Data → From Text/CSV
2. Load `proof_fwd_cpu_contract_summary.csv` (or open any other CSV directly)
3. Load `samples/execution-proof/input/contract_metadata.csv`
4. **PowerQuery → Merge Queries** → Left table: `contract_summary`, Right table: `contract_metadata`, Join key: `ContractId`
5. Expand columns: `Segment`, `Region`, `ProductLine`, `Broker`
6. **Insert → PivotTable** and use the merged query as the data source

Example pivot analyses:

| Pivot question | Rows | Values |
|----------------|------|--------|
| Mean PV by Region | Region | MeanPV (Average) |
| Risk by Segment (VaR99) | Segment | VaR99 (Sum) |
| Spread of outcomes per contract | ContractId | P05, P50, P95 (Average) |
| Per-scenario portfolio PV | ScenarioId | PortfolioPV (Sum) |

---

## Generating and re-using a larger portfolio

Use `--export-portfolio true` to capture any generated portfolio to `portfolio.csv`
in the output directory, then re-run it with `--input`:

```
dotnet run --project CLI/PamMonteCarlo50Y -- --backend cpu --contracts 500 --scenarios 200 --months 120 --seed 42 --export-portfolio true --out ./my-portfolio
```

```
dotnet run --project CLI/PamMonteCarlo50Y -- --input ./my-portfolio --backend cpu --out ./my-portfolio/out --reporting true --export-fact true
```

---

## How to run (all modes)

### Quick CPU-only demo (synthetic, 100 contracts × 100 scenarios)
```
dotnet run --project CLI/PamMonteCarlo50Y -- --backend cpu --contracts 100 --scenarios 100 --months 120
```

### Full demo (both CPU and GPU, default 10k × 1k)
```bash
dotnet run --project CLI/PamMonteCarlo50Y -- --backend both
```

### GPU only
```bash
dotnet run --project CLI/PamMonteCarlo50Y -- --backend gpu
```

### Custom seed and output directory
```
dotnet run --project CLI/PamMonteCarlo50Y -- --seed 99999 --scenarios 5000 --out ./results
```

### Multiple runs from a JSON file
```bash
dotnet run --project CLI/PamMonteCarlo50Y -- --runs runs.json
```

Example `runs.json`:
```json
[
  { "id": "fwd",      "description": "Pure forward", "calcDateIndex": 0 },
  { "id": "backtest", "description": "5-year backtest", "calcDateIndex": 60,
    "contractCount": 500, "scenarioCount": 200 }
]
```

---

## CLI options

### Core options

| Option | Default | Description |
|--------|---------|-------------|
| `--backend cpu\|gpu\|both` | `both` | Execution backend |
| `--contracts N` | `10000` | Portfolio size (synthetic mode) |
| `--scenarios N` | `1000` | MC scenarios (synthetic mode) |
| `--months N` | `600` | Horizon in months (50y = 600) |
| `--seed N` | `12345` | Deterministic seed |
| `--calcDateIndex N` | `0` | Month index for prior/after boundary |
| `--contractRange S:E` | all | Contract slice \[S, E) |
| `--scenarioRange S:E` | all | Scenario slice \[S, E) |
| `--runs file.json` | — | Load multiple `RunRequest` objects |
| `--out dir` | `./out` | Output directory |

### Input-directory mode

| Option | Default | Description |
|--------|---------|-------------|
| `--input dir` | — | Load portfolio + scenarios from disk. Bypasses synthetic generation. Expected layout: `portfolio.csv`, `scenarios/scenario_set.json`, `scenarios/riskfactors/interest_rate_after.csv`, optional `runs.json` and `contract_metadata.csv`. See `samples/execution-proof/input/`. |

### Portfolio export

| Option | Default | Description |
|--------|---------|-------------|
| `--export-portfolio true\|false` | `false` | Write the current portfolio to `portfolio.csv` in the output dir in the stable input format (re-usable with `--input`) |

### Reporting / Excel outputs

| Option | Default | Description |
|--------|---------|-------------|
| `--reporting true\|false` | `false` | Enable Excel-friendly CSV exports |
| `--export-fact true\|false` | `false` | Also write `fact_results_long.csv` (per-contract × per-scenario) |
| `--aggregation-only true\|false` | `false` | Skip fact table; write only aggregates |
| `--contract-sample-size N` | `200` | Max contracts in fact table (0 = all) |
| `--contract-sample-seed N` | `0` | Seed for random contract sample; 0 = first N |
| `--scenario-sample-size N` | `200` | Max scenarios in fact table (0 = all) |
| `--metadata path/to/meta.csv` | — | External contract metadata CSV for grouped summaries (auto-loaded from `--input` dir if present) |

---

## Output files

### Core outputs (always written, per run)

| File | Contents |
|------|----------|
| `{runId}_portfolio_pv_by_scenario.csv` | scenarioIndex, portfolioPV |
| `{runId}_contract_pv_sample.csv` | contractIndex, scenarioIndex, pv (small sample) |
| `{runId}_summary.json` | metrics + config snapshot + timing |

### Reporting outputs (when `--reporting true`)

| File | Contents |
|------|----------|
| `{runId}_portfolio_by_scenario.csv` | RunId, ScenarioId, PortfolioPV |
| `{runId}_contract_summary.csv` | RunId, ContractId, MeanPV, StdPV, P05, P50, P95, VaR99, ES99 |
| `runs.csv` | RunId, CalcDateIndex, ContractCount, ScenarioCount, Backend, Timestamp |
| `{runId}_fact_results_long.csv` | RunId, ContractId, ScenarioId, Measure, Value (when `--export-fact true`) |
| `{runId}_grouped_by_{dim}.csv` | RunId, {dim}, MeanPV, ContractCount — one file per metadata column (when `--metadata` set) |
| `_README.txt` | Auto-generated guide to files + Excel join workflow |

### Portfolio export (when `--export-portfolio true`)

| File | Contents |
|------|----------|
| `portfolio.csv` | All contracts in stable input-directory format — re-usable with `--input` |

### Summary JSON fields

```json
{
  "runId": "proof_fwd_cpu",
  "backend": "cpu",
  "numContracts": 5,
  "numScenarios": 3,
  "calcDateIndex": 0,
  "seed": 12345,
  "vasicek": { "kappa": 0.15, "theta": 0.04, "sigma": 0.02, "r0": 0.03 },
  "provisioningMs": 2,
  "calcMs": 8,
  "fetchMs": 1,
  "reportingMs": 5,
  "totalMs": 16,
  "pvMean": 1245678.90,
  "pvStdev": 12345.00,
  "pvP05": 1190000.0,
  "pvP50": 1245000.0,
  "pvP95": 1310000.0,
  "var99": 80000.0,
  "es99": 95000.0
}
```

---

## Input directory format

The `--input` mode reads a stable on-disk directory contract:

```
<input-dir>/
├── portfolio.csv                          ← valuation engine fields only
├── contract_metadata.csv                  ← descriptive fields (NOT fed into valuation)
├── runs.json                              ← run requests (optional)
└── scenarios/
    ├── scenario_set.json                  ← Vasicek params + file references
    └── riskfactors/
        ├── interest_rate_after.csv        ← scenarioIndex, timeIndex, shortRate, discountFactor
        └── interest_rate_prior.csv        ← same format, for t < calcDateIndex (optional)
```

See `samples/execution-proof/input/` for a complete working example and
`docs/input-output-contract.md` for the full schema.

---

## Timeline log

Each phase is logged with UTC timestamp and duration:

```
[14:35:01.234] PROVISIONING started  (contracts=5, scenarios=3)
[14:35:01.236] PROVISIONING done     (2 ms)
[14:35:01.236] CALC started
[14:35:01.244] CALC done             (8 ms, 0.53 µs/contract·scenario)
[14:35:01.244] FETCH started         (aggregating results)
[14:35:01.245] FETCH done            (1 ms)
[14:35:01.245] REPORTING started
[14:35:01.250] REPORTING done        (5 ms)
[14:35:01.250] TOTAL                 (16 ms)
```

---

## Multiple runs (RunRequest)

A `RunRequest` allows repeated valuations on the **same pre-built portfolio** without
regenerating contracts or scenarios. Use cases:

- **Backtesting**: run the same portfolio with `calcDateIndex` set to a historical
  month index (e.g., 60 = 5 years in) to see how PVs would have looked at that date.
- **Scenario subsets**: compare PV distributions from different scenario ranges.
- **Contract slices**: value a sub-portfolio independently.

The `calcDateIndex` splits rates into *prior* (historical, used for t < index) and
*after* (forward-looking, used for t ≥ index). For a pure forward simulation, set
`calcDateIndex = 0`.

---

## Vasicek model

```
dr = κ(θ − r) dt + σ√dt · Z,   Z ~ N(0,1)
```

Default parameters:

| Parameter | Value | Meaning |
|-----------|-------|---------|
| κ (kappa) | 0.15 | Mean-reversion speed |
| θ (theta) | 0.04 | Long-run mean (4%) |
| σ (sigma) | 0.02 | Volatility |
| r₀ | 0.03 | Initial short rate (3%) |

Rates are floored at 0 to avoid negative rates in the simpler Vasicek version.

---

## Determinism guarantees

- **Same seed** → same portfolio and same scenario set, bit-for-bit across runs and platforms.
- Portfolio generator: XorShift64 seeded from `--seed`.
- Vasicek generator: XorShift64 seeded from `--seed + 1` (separate stream).
- Box-Muller transform for normal variates (deterministic given the PRNG state).
- No concurrency in the generators (sequential, deterministic order).
- Input-directory mode: determinism is controlled by the CSV files themselves.

---

## Architecture

```
CLI/PamMonteCarlo50Y/
├── Program.cs                   CLI entry point (--input, --export-portfolio, --reporting, ...)
├── InputDirectoryLoader.cs      Parses portfolio.csv, scenario_set.json, riskfactor CSVs
├── PortfolioGenerator.cs        Seeded PAM portfolio generation (synthetic mode)
├── VasicekRateGenerator.cs      Vasicek MC short rates + discount factors
├── RunRequest.cs                Multi-run slicing model
├── CpuPvEngine.cs               CPU discounted PV (Parallel.For over scenarios)
├── McPvKernel.cs                ILGPU kernel + blittable structs
├── GpuPvEngine.cs               GPU executor (ILGPU, buffer pooling)
├── DemoOrchestrator.cs          Run orchestration + timeline logging
├── Reporting/
│   ├── ExportConfig.cs          Controls which CSV files are written
│   └── ResultExportTransformer.cs  Excel-friendly CSV outputs + metadata grouping
└── Sinks/
    └── OutputSinks.cs           CSV + JSON writers (including PortfolioExportSink)

samples/execution-proof/
├── input/                       Ready-made sample inputs (5 contracts, 3 scenarios)
│   ├── portfolio.csv
│   ├── contract_metadata.csv
│   ├── runs.json
│   └── scenarios/
│       ├── scenario_set.json
│       └── riskfactors/
│           ├── interest_rate_after.csv
│           └── interest_rate_prior.csv
├── run_sample.sh                Linux/macOS runner script
└── run_sample.ps1               Windows PowerShell runner script

docs/
└── input-output-contract.md     Full schema reference for input/output formats
```

