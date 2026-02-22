# PAM Monte Carlo 50-Year Demo

Self-contained demo that values a portfolio of **10,000 PAM contracts** under
**Monte Carlo interest-rate scenarios** for a **50-year horizon (600 months)**
using both CPU and GPU paths.

---

## What it does

| Feature | Details |
|---------|---------|
| **Portfolio** | 10,000 synthetic PAM contracts; heterogeneous notionals (100k–10M), maturities (1–50y), payment frequencies (monthly/quarterly/annual), fixed + floating (quarterly rate resets) |
| **Rate model** | Vasicek (Ornstein-Uhlenbeck): `dr = κ(θ−r)dt + σ√dt·Z` |
| **Horizon** | 50 years = 600 monthly steps (configurable) |
| **Scenarios** | Default 1,000 MC scenarios; configurable to 10,000+ |
| **Discounting** | `DF[t] = exp(−∑ r[i]·dt)` pre-computed per scenario on CPU, then used in PV kernel |
| **CPU path** | Parallel C# PV engine using `Parallel.For` over scenarios |
| **GPU path** | ILGPU 2-D kernel `(contracts × scenarios)`; falls back: CUDA → OpenCL → CPU |
| **Determinism** | Fully seeded (XorShift64 + Box-Muller); same seed → bit-for-bit identical runs |
| **Runs** | `RunRequest` concept: multiple valuations on the same portfolio with different `calcDateIndex`, contract/scenario slices |

---

## How to run

### Quick start (CPU-only, 100 contracts × 100 scenarios)
```bash
cd <repo-root>
dotnet run --project demos/PamMonteCarlo50Y -- \
  --backend cpu --contracts 100 --scenarios 100 --months 120
```

### Full demo (both CPU and GPU, default 10k × 1k)
```bash
dotnet run --project demos/PamMonteCarlo50Y -- --backend both
```

### GPU only
```bash
dotnet run --project demos/PamMonteCarlo50Y -- --backend gpu
```

### Custom seed and output directory
```bash
dotnet run --project demos/PamMonteCarlo50Y -- \
  --seed 99999 --scenarios 5000 --out ./results
```

### Multiple runs from a JSON file
```bash
dotnet run --project demos/PamMonteCarlo50Y -- --runs runs.json
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

| Option | Default | Description |
|--------|---------|-------------|
| `--backend cpu\|gpu\|both` | `both` | Execution backend |
| `--contracts N` | `10000` | Portfolio size |
| `--scenarios N` | `1000` | MC scenarios |
| `--months N` | `600` | Horizon in months (50y = 600) |
| `--seed N` | `12345` | Deterministic seed |
| `--calcDateIndex N` | `0` | Month index for prior/after boundary |
| `--contractRange S:E` | all | Contract slice \[S, E) |
| `--scenarioRange S:E` | all | Scenario slice \[S, E) |
| `--runs file.json` | — | Load multiple `RunRequest` objects |
| `--out dir` | `./out` | Output directory |

---

## Output files (per run `{id}`)

| File | Contents |
|------|----------|
| `{id}_cpu_portfolio_pv_by_scenario.csv` | scenarioIndex, portfolioPV |
| `{id}_cpu_contract_pv_sample.csv` | contractIndex, scenarioIndex, pv (sample) |
| `{id}_cpu_summary.json` | metrics + config snapshot + timing |
| `{id}_gpu_*` | Same files for GPU path (when `--backend gpu\|both`) |

### Summary JSON fields

```json
{
  "runId": "run0_cpu",
  "backend": "cpu",
  "numContracts": 10000,
  "numScenarios": 1000,
  "calcDateIndex": 0,
  "seed": 12345,
  "vasicek": { "kappa": 0.15, "theta": 0.04, "sigma": 0.02, "r0": 0.03 },
  "provisioningMs": 120,
  "calcMs": 4500,
  "fetchMs": 30,
  "reportingMs": 80,
  "totalMs": 4730,
  "calcMsPerContract": 0.45,
  "pvMean": 1234567.89,
  "pvStdev": 98765.43,
  "pvMin": -12345.0,
  "pvMax": 2345678.0,
  "pvP05": 850000.0,
  "pvP50": 1230000.0,
  "pvP95": 1650000.0,
  "pvP99": 1900000.0,
  "var99": 400000.0,
  "es99": 600000.0
}
```

---

## Timeline log

Each phase is logged with UTC timestamp and duration:

```
[14:35:01.234] PROVISIONING started  (contracts=10000, scenarios=1000)
[14:35:01.356] PROVISIONING done     (122 ms)
[14:35:01.356] CALC started
[14:35:05.890] CALC done             (4534 ms, 0.45 µs/contract·scenario)
[14:35:05.890] FETCH started         (aggregating results)
[14:35:05.921] FETCH done            (31 ms)
[14:35:05.921] REPORTING started
[14:35:05.999] REPORTING done        (78 ms)
[14:35:05.999] TOTAL                 (4765 ms)
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

---

## Tests

```bash
dotnet test demos/PamMonteCarlo50Y.Tests/
```

Tests validate:
- Vasicek rate determinism (same seed → same rates)
- Discount factor shape (DF[0] = 1, non-increasing)
- Portfolio determinism
- CPU PV produces finite results
- CPU vs GPU PV parity on 20 contracts × 20 scenarios (tolerance: 1e-4)

---

## Architecture

```
demos/PamMonteCarlo50Y/
├── Program.cs                   CLI entry point
├── PortfolioGenerator.cs        Seeded PAM portfolio generation
├── VasicekRateGenerator.cs      Vasicek MC short rates + discount factors
├── RunRequest.cs                Multi-run slicing model
├── CpuPvEngine.cs               CPU discounted PV (Parallel.For over scenarios)
├── McPvKernel.cs                ILGPU kernel + blittable structs
├── GpuPvEngine.cs               GPU executor (ILGPU, buffer pooling)
├── DemoOrchestrator.cs          Run orchestration + timeline logging
└── Sinks/
    └── OutputSinks.cs           CSV + JSON writers

demos/PamMonteCarlo50Y.Tests/
└── DemoTests.cs                 Determinism + CPU/GPU parity tests
```

Reused from existing engine:
- `ActusGPU` — ILGPU infrastructure, `PamV3Adapter`, `ScenarioBatchExecutor`
- `ActusCPU` — `PamContractTerms`, `PrincipalAtMaturity.Schedule`, `RiskFactorModel`
- `Actus.Core` — `GpuEventType`, `GpuDayCountCode`, shared types
