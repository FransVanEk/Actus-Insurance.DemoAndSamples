# Scenario / CPU–GPU / CalcDate Causality Demo

A minimal, self-contained sample that proves **causality** by changing exactly
**one element at a time** and showing the resulting output delta.

---

## What it demonstrates

| Experiment | Changed element | All else fixed | Expected observation |
|---|---|---|---|
| **1 — CPU vs GPU** | Execution backend (CPU → GPU) | Same portfolio, same scenarios, `calcDateIndex=0` | `|CPU_PV − GPU_PV|` < 1e-9 for every (contract, scenario) pair |
| **2 — Scenario impact** | Interest-rate scenario (Low 1.5% → High 6.5%) | Same portfolio, CPU backend, `calcDateIndex=0` | Fixed-rate PV decreases as rates rise; floating-rate PV may rise |
| **3 — CalcDate impact** | `calcDateIndex` (0 → 12) | Same portfolio, same scenarios, CPU backend | PV shifts because months [0, 12) switch from scenario rates to a flat 5% prior rate |

The outputs are written as **per-contract × per-scenario** CSV files so every
result can be individually verified in Excel or any spreadsheet tool.

---

## Quick start

```bash
# CPU only (fastest, no GPU required)
dotnet run --project samples/ScenarioCpuGpuCalcDateDemo -- --backend cpu --out ./demo-out

# CPU + GPU comparison
dotnet run --project samples/ScenarioCpuGpuCalcDateDemo -- --backend both --out ./demo-out
```

---

## CLI options

| Option | Default | Description |
|--------|---------|-------------|
| `--out <dir>` | `./out` | Output directory for CSV files |
| `--backend cpu\|gpu\|both` | `both` | Execution backend for Experiment 1 |
| `--tolerance <value>` | `1e-9` | Maximum allowed `|CPU_PV − GPU_PV|` in Experiment 1 |
| `--help` | — | Show help message |

---

## Output files

All files are written to `--out` (default: `./out`).

| File | Contents |
|------|----------|
| `portfolio.csv` | The 5 demo contracts (ContractId, dates, notional, rate, cycle, spread) |
| `scenarios.csv` | The 3 scenario definitions (rate level, prior rate) |
| `exp1_cpu_vs_gpu.csv` | Per (ContractId, ScenarioId): CPU_PV, GPU_PV, AbsDelta, WithinTolerance |
| `exp2_scenario_impact.csv` | Per ContractId: PV under each scenario, Delta(Low→High), DeltaPct, Attribution |
| `exp3_calcdate_impact.csv` | Per (ContractId, ScenarioId): PV(calcDate=0), PV(calcDate=12), Delta, DeltaPct, Attribution |

---

## Demo portfolio

Five PAM contracts with intentionally different features:

| ContractId | Notional | Maturity | Payment cycle | Type | Start month |
|---|---|---|---|---|---|
| PAM_C001 | $1 000 000 | 48 months | Annual (P1YL1) | Fixed 4.0% | 0 |
| PAM_C002 | $500 000 | 36 months | Quarterly (P3ML1) | Fixed 5.0% | 0 |
| PAM_C003 | $2 000 000 | 24 months | Monthly (P1ML1) | Fixed 3.0% | 0 |
| PAM_C004 | $750 000 | 45 months | Quarterly (P3ML1) | Floating, spread 1% | 3 |
| PAM_C005 | $1 500 000 | 36 months | Annual (P1YL1) | Fixed 4.5% | 6 |

All contracts use:
- `ContractRole = RPA` (lend principal, receive coupons + principal)
- `DayCount = A/365`, no calendar adjustment

---

## Scenarios

Three deterministic **constant-rate** scenarios — chosen for interpretability:

| ScenarioId | Name | Short rate | Expected PV direction |
|---|---|---|---|
| 0 | Low (1.5%) | 1.5% flat | Highest PV (low discounting) |
| 1 | Base (3.0%) | 3.0% flat | Medium PV |
| 2 | High (6.5%) | 6.5% flat | Lowest PV (heavy discounting) |

**Prior rate (Experiment 3)**: 5.0% flat, used for months [0, 12) when `calcDateIndex=12`.

Constant-rate discount factors:
```
DF[t] = exp(−r × t / 12)
```

For the mixed prior/after scenario:
```
DF[t < 12]  = exp(−5% × t / 12)
DF[t ≥ 12]  = exp(−5% × 12/12) × exp(−r_scenario × (t−12) / 12)
```

---

## How the experiments prove causality

### Experiment 1 — CPU vs GPU

```
Unchanged: portfolio, scenarios (3 × 48 months), calcDateIndex=0
Changed:   execution backend (CPU → GPU)
```

Both engines implement identical arithmetic.  In the absence of a physical
GPU the ILGPU library falls back to its CPU simulator, which produces
**bit-for-bit identical** results (`AbsDelta = 0`).  On a real GPU, IEEE 754
floating-point differences may appear but must remain below the tolerance
threshold (`1e-9` by default).

### Experiment 2 — Scenario impact

```
Unchanged: portfolio, execution backend (CPU), calcDateIndex=0
Changed:   interest-rate scenario (Low 1.5% → Base 3.0% → High 6.5%)
```

For a fixed-rate contract (PAM_C001–C003, PAM_C005):
- Higher rates → lower discount factors → lower present value of future cash flows
- The delta is entirely attributable to the rate change

For the floating-rate contract (PAM_C004, spread=1%):
- Higher rates also increase the coupon cash flows via rate resets
- The net PV direction depends on the balance of higher coupons vs heavier discounting

### Experiment 3 — CalcDate impact

```
Unchanged: portfolio, execution backend (CPU), scenarios
Changed:   calcDateIndex (0 → 12)
```

When `calcDateIndex = 12`:
- Months [0, 12): rate = 5% (prior, flat)
- Months [12, 48): rate = scenario rate

**Low scenario** (1.5%): prior=5% > after=1.5% → heavier discounting in [0,12)
→ PV decreases relative to calcDateIndex=0

**Base scenario** (3.0%): prior=5% > after=3.0% → heavier discounting in [0,12)
→ PV decreases relative to calcDateIndex=0

**High scenario** (6.5%): prior=5% < after=6.5% → lighter discounting in [0,12)
→ PV increases relative to calcDateIndex=0

The direction of the delta precisely identifies whether the prior rate is above
or below the scenario after-rate.

---

## Architecture

```
samples/ScenarioCpuGpuCalcDateDemo/
├── ScenarioCpuGpuCalcDateDemo.csproj  SDK-style exe; references ActusInsurance.GPU
├── Program.cs                         CLI entry point + experiment orchestrator
├── DemoPortfolio.cs                   5 hard-coded PAM contracts
├── ScenarioBuilder.cs                 Builds constant-rate RateScenarios with prior/after merge
├── RateScenarios.cs                   Lightweight rate-array holder (replaces VasicekRateGenerator)
├── CpuEngine.cs                       CPU PV engine (parallel over scenarios)
├── GpuEngine.cs                       ILGPU kernel + host executor (GPU or CPU simulator)
└── CsvSink.cs                         CSV writers for portfolio, scenarios, and 3 experiments
```

The project references only the `ActusInsurance.GPU` NuGet package, which
provides `PrincipalAtMaturity.Schedule()`, `PamContractTerms`, and the
`GpuEventType` constants needed by the ILGPU kernel.

---

## Analysing results in Excel

1. Open Excel → **Data → Get Data → From Text/CSV**
2. Load `exp2_scenario_impact.csv`
3. Insert a bar chart: X axis = ContractId, series = PV per scenario column
4. The chart shows the monotone PV decrease from Low to High for fixed-rate contracts

For Experiment 3:
5. Load `exp3_calcdate_impact.csv`
6. Add a column: `=PV_CalcDate12 - PV_CalcDate0` (already provided as `Delta`)
7. Filter by scenario to see how the delta sign flips between Low/Base (prior > after) and High (prior < after)

---

## Running from the repo root

```bash
# CPU only
dotnet run --project samples/ScenarioCpuGpuCalcDateDemo/ScenarioCpuGpuCalcDateDemo.csproj \
  -- --backend cpu --out samples/ScenarioCpuGpuCalcDateDemo/out

# With GPU (falls back to ILGPU CPU simulator if no GPU present)
dotnet run --project samples/ScenarioCpuGpuCalcDateDemo/ScenarioCpuGpuCalcDateDemo.csproj \
  -- --backend both --out samples/ScenarioCpuGpuCalcDateDemo/out
```
