# Execution-Proof Sample — Input Contract

This directory contains a **stable, minimal set of inputs** that proves:

1. Every contract is evaluated **individually** (not aggregated-only).
2. Results are produced at **contract × scenario granularity**.
3. Inputs are provided from an **input directory** (portfolio + scenarios + run requests).
4. Outputs are written to an **output directory** in Excel-friendly CSV format.

---

## Directory Structure

```
input/
├── portfolio.csv                          ← valuation engine fields only
├── contract_metadata.csv                  ← descriptive fields (NOT fed into valuation)
├── runs.json                              ← one or more run requests
├── scenarios/
│   ├── scenario_set.json                  ← scenario metadata + model parameters
│   └── riskfactors/
│       ├── interest_rate_prior.csv        ← rates for t < calcDateIndex (empty here: calcDateIndex=0)
│       └── interest_rate_after.csv        ← rates for t ≥ calcDateIndex (full 3×24 matrix)
└── README.md                              ← this file
```

---

## Files

### `portfolio.csv`

Contains only the fields required by the valuation engine.
Primary key: `ContractId`.

| Column | Description |
|--------|-------------|
| `ContractId` | Unique contract identifier |
| `InitialExchangeDate` | Date the contract starts (ISO 8601: `YYYY-MM-DD`) |
| `MaturityDate` | Maturity / end date (ISO 8601) |
| `NotionalPrincipal` | Face value |
| `NominalInterestRate` | Annual interest rate (decimal, e.g., `0.0500` = 5%) |
| `CycleOfInterestPayment` | Payment frequency (`P1ML1`=monthly, `P3ML1`=quarterly, `P1YL1`=annual) |
| `RateSpread` | Spread added to rate-reset index for floating contracts |
| `MarketObjectCodeOfRateReset` | Risk-factor id for floating rate (blank = fixed) |
| `CycleOfRateReset` | Rate-reset frequency for floating contracts |

Default values applied by the loader (not in CSV):
`Currency=USD`, `ContractRole=RPA`, `RateMultiplier=1.0`, `DayCountConvention=A_365`,
`BusinessDayConvention=NOS`, `Calendar=NC`.

### `contract_metadata.csv`

Contains **descriptive fields** that are **NOT** used in the valuation.
Keyed by `ContractId` for joining against engine outputs in Excel / PowerQuery.

| Column | Description |
|--------|-------------|
| `ContractId` | Join key → matches `portfolio.csv` |
| `Segment` | Business segment |
| `Region` | Geographic region |
| `ProductLine` | Product category |
| `Currency` | Reporting currency (informational only) |
| `Broker` | Originating broker |
| `Underwriter` | Underwriter team |

### `scenarios/scenario_set.json`

Scenario metadata and model parameters. Fields:

| Field | Value | Description |
|-------|-------|-------------|
| `scenarioSetId` | `vasicek_3s_24m_seed12345` | Unique scenario set identifier |
| `numScenarios` | `3` | Number of Monte Carlo paths |
| `numMonths` | `24` | Horizon in monthly steps (2 years) |
| `seed` | `12346` | XorShift64 seed for rate generation (`portfolioSeed + 1`) |
| `model.type` | `Vasicek` | Interest-rate model |
| `model.kappa` | `0.15` | Mean-reversion speed |
| `model.theta` | `0.04` | Long-run mean (4%) |
| `model.sigma` | `0.02` | Volatility |
| `model.r0` | `0.03` | Initial short rate (3%) |
| `riskFactors[].afterFile` | `riskfactors/interest_rate_after.csv` | Path to rate array CSV |
| `riskFactors[].priorFile` | `riskfactors/interest_rate_prior.csv` | Path to prior rates (empty for `calcDateIndex=0`) |

### `scenarios/riskfactors/interest_rate_after.csv`

Flat rate matrix for all Monte Carlo paths. Shape: `numScenarios × numMonths` rows.

Columns:
- `scenarioIndex` — 0-based scenario index
- `timeIndex` — 0-based month index on the simulation grid
- `shortRate` — Vasicek short rate `r(s,t)` (annual, decimal)
- `discountFactor` — `DF(s,t) = exp(−∑ r(s,i)·dt)` cumulative discount factor

Generated deterministically by the XorShift64 + Box-Muller Vasicek path simulator
(seed = `scenario_set.json:seed`).

### `scenarios/riskfactors/interest_rate_prior.csv`

Rates for time steps `t < calcDateIndex`. Empty (header only) in this sample
because `runs.json` uses `calcDateIndex = 0` (pure forward simulation).

### `runs.json`

Two run requests:

| RunId | Description |
|-------|-------------|
| `proof_fwd` | Full portfolio (5 contracts × 3 scenarios) — proves individual contract evaluation |
| `proof_slice` | Contract subset [0, 3) × 3 scenarios — proves slicing granularity |

---

## How the inputs flow into the engine

```
portfolio.csv
    └─► PamContractTerms[]  ─────────────────────────────────┐
                                                              │
scenario_set.json + riskfactors/interest_rate_after.csv       ├─► CpuPvEngine.Evaluate()
    └─► VasicekRateGenerator (ShortRates[], DiscountFactors[]) ┤       → McPvResult[contracts × scenarios]
                                                              │
runs.json                                                     │
    └─► RunRequest[] (slicing: contractRange, scenarioRange) ─┘

contract_metadata.csv
    └─► ExportConfig.MetadataPath  ─► ResultExportTransformer (join in output)
```

---

## Determinism guarantee

Same `scenario_set.json` parameters + same seed → **bit-for-bit identical** rate arrays
and PV outputs across platforms and runs.

The `interest_rate_after.csv` in this directory was generated with seed `12346` and
matches the output of `VasicekRateGenerator.Generate(params, 3, 24, 12346UL)`.
