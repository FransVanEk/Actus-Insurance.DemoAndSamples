# ActusInsurance FastEndpoints + SQLite + GPU Sample

## Overview

This sample demonstrates an **async insurance calculation web API** built with:

- **[FastEndpoints](https://fast-endpoints.com/) 8.x** — minimal, high-performance endpoint routing
- **SQLite (EF Core 9)** — zero-config embedded database for metadata and run records
- **CPU & GPU calculation engines** — a CPU engine (always available) and a simulated GPU engine (demo/placeholder for real CUDA/ILGPU integration)
- **File blob storage** — scenario, risk, and portfolio input files stored on disk
- **Async run queue** — runs are submitted and processed asynchronously; clients poll for status and results

The project is intentionally self-contained: no authentication, no external services, no Docker required.

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

That's it. SQLite is embedded and the database is created automatically on first run.

---

## Running Locally

```bash
cd samples/ActusInsurance.FastEndpointsSqliteGpuSample
dotnet run
```

The API starts on `http://localhost:5000` (or the port shown in the console). Swagger UI is available at:

```
http://localhost:5000/swagger
```

The SQLite database is created at `./data/app.db` and uploaded file blobs are stored under `./data/blobs/`.

To enable the GPU (simulated) engine instead of the CPU engine:

```bash
dotnet run -- --Calculation:PreferGpu=true
# or set in appsettings.json / environment variable
```

---

## API Reference

### Files

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/files/scenarios` | Upload a scenario file (multipart/form-data) |
| `POST` | `/files/risks` | Upload a risk file |
| `POST` | `/files/portfolios` | Upload a portfolio file |
| `POST` | `/files/sinks` | Upload a sink definition file |
| `GET`  | `/files` | List uploaded files; optional `?type=Scenario\|Risk\|Portfolio\|Sink` filter |

### Sinks

Sink definitions describe how calculation outputs should be aggregated and formatted.

| Method | Path | Description |
|--------|------|-------------|
| `POST`   | `/sinks` | Create a sink definition (JSON body) |
| `GET`    | `/sinks` | List all sink definitions |
| `GET`    | `/sinks/{id}` | Get a single sink by ID |
| `PUT`    | `/sinks/{id}` | Update a sink definition |
| `DELETE` | `/sinks/{id}` | Delete a sink definition |

### Runs

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/runs` | Start an async calculation run |
| `GET`  | `/runs/{runId}/status` | Poll run status and progress (0–100) |
| `GET`  | `/runs/{runId}/result` | Get the final calculation result |
| `POST` | `/runs/{runId}/cancel` | Request cancellation |
| `POST` | `/runs/demo/{demoName}` | Start a predefined demo run (`basic` or `gpu-showcase`) |

### Samples

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/samples/create-default` | Create default sample datasets (idempotent); returns artifact IDs ready for use in `/runs` |

---

## Async Run Flow

```
1. POST /runs  →  202 Accepted  { runId, statusUrl, resultUrl, state: "Queued" }
2. GET  /runs/{runId}/status  →  { state: "Running", progress0To100: 50, engine: "CPU" }
   (poll until state is "Completed" or "Failed")
3. GET  /runs/{runId}/result  →  200 OK  { result: { meanPv, stdPv, p05, p95, ... } }
```

- Runs are persisted in SQLite immediately; the in-memory channel queue dispatches them to the background worker.
- The worker reports progress updates to the database as it executes.
- If the application restarts, queued runs are **not** re-enqueued automatically (in-memory queue is ephemeral). For production, replace `RunQueue` with a durable queue (Redis Streams, Azure Service Bus, etc.).

---

## GPU vs CPU Engine

| | CPU Engine | GPU Engine (simulated) |
|-|-----------|----------------------|
| **Class** | `CpuCalculationEngine` | `GpuCalculationEngine` |
| **Label** | `"CPU"` | `"GPU (simulated)"` |
| **Physical GPU required** | No | No (simulation only) |
| **Simulated latency** | ~300 ms | ~80 ms |
| **Production use** | ✅ Ready | ❌ Replace with CUDA/ILGPU |

Both engines produce **identical deterministic results** (same random seed from input lengths) for easy comparability.

To use a real GPU in production, replace `GpuCalculationEngine.ExecuteAsync` with a real CUDA kernel via [ILGPU](https://ilgpu.net/), [CUDAfy.NET](https://github.com/lepoco/cuda), or P/Invoke to a native CUDA library.

---

## Configuration

`appsettings.json`:

```json
{
  "Calculation": {
    "PreferGpu": false
  }
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `Calculation:PreferGpu` | `false` | `true` to use `GpuCalculationEngine` instead of `CpuCalculationEngine` |

Override at startup:
```bash
dotnet run -- --Calculation:PreferGpu=true
CALCULATION__PREFERGPU=true dotnet run   # environment variable (double underscore)
```

---

## Curl Examples

### 1. Create default sample datasets (idempotent)

```bash
curl -s -X POST http://localhost:5000/samples/create-default | jq .
```

Returns:
```json
{
  "scenarioArtifactId": "...",
  "riskArtifactId": "...",
  "portfolioArtifactId": "...",
  "sinkDefinitionId": "...",
  "message": "Default sample data ready. Use the returned IDs with POST /runs to start a calculation."
}
```

### 2. Start a calculation run

```bash
SCENARIO_ID="<scenarioArtifactId from above>"
RISK_ID="<riskArtifactId>"
PORTFOLIO_ID="<portfolioArtifactId>"
SINK_ID="<sinkDefinitionId>"

curl -s -X POST http://localhost:5000/runs \
  -H "Content-Type: application/json" \
  -d "{
    \"scenarioArtifactId\": \"$SCENARIO_ID\",
    \"riskArtifactId\": \"$RISK_ID\",
    \"portfolioArtifactId\": \"$PORTFOLIO_ID\",
    \"sinkDefinitionId\": \"$SINK_ID\"
  }" | jq .
```

### 3. Poll run status

```bash
RUN_ID="<runId from above>"
curl -s http://localhost:5000/runs/$RUN_ID/status | jq .
```

### 4. Get result

```bash
curl -s http://localhost:5000/runs/$RUN_ID/result | jq .
```

### 5. One-shot demo run

```bash
# basic demo (CPU)
curl -s -X POST http://localhost:5000/runs/demo/basic | jq .

# GPU showcase demo
curl -s -X POST http://localhost:5000/runs/demo/gpu-showcase | jq .
```

### 6. Upload a custom scenario file

```bash
curl -s -X POST http://localhost:5000/files/scenarios \
  -F "File=@/path/to/my-scenario.json" | jq .
```

### 7. Create a sink definition

```bash
curl -s -X POST http://localhost:5000/sinks \
  -H "Content-Type: application/json" \
  -d '{
    "name": "My Sink",
    "version": "1.0",
    "jsonDefinition": "{\"outputFormat\":\"csv\",\"aggregations\":[\"portfolio_pv_by_scenario\"]}"
  }' | jq .
```

### 8. Cancel a run

```bash
curl -s -X POST http://localhost:5000/runs/$RUN_ID/cancel
```

---

## Sample Data

Packaged sample files under `samples-data/`:

| File | Description |
|------|-------------|
| `scenario.json` | 3-scenario Vasicek rate scenario set (48 months) |
| `risk.json` | Vasicek interest-rate risk model parameters |
| `portfolio.json` | 5-contract PAM portfolio |
| `sink-definition.json` | CSV output sink with portfolio-level aggregates |

These are automatically used by `POST /samples/create-default` and `POST /runs/demo/*`.
