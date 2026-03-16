# Architecture Overview

This document describes the internal design of the **ActusInsurance.FastEndpointsSqliteGpuSample** web API.

---

## High-level structure

```
┌──────────────────────────────────────────────────────────────────┐
│                         HTTP Client                              │
└─────────────────────────────┬────────────────────────────────────┘
                              │ HTTP/1.1
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                     FastEndpoints router                         │
│  /files/*    /sinks/*    /runs/*    /samples/*                   │
└───┬──────────────┬─────────────┬──────────────┬──────────────────┘
    │              │             │              │
    ▼              ▼             ▼              ▼
 File           Sink           Run           Sample
 endpoints     endpoints      endpoints     endpoints
    │              │             │
    ▼              ▼             │
┌──────────────────────────┐    │ enqueue run ID
│     AppDbContext          │◄───┘
│  (EF Core + SQLite)       │
│                           │
│  FileArtifacts   ─────────┼── blobs on disk (./data/blobs/)
│  SinkDefinitions          │
│  Runs             ────────┼── ResultJson / MetricsJson stored inline
└───────────┬───────────────┘
            │
            │  RunRecord persisted, then ID pushed to channel
            ▼
┌──────────────────────────┐
│       RunQueue            │
│  (Channel<Guid>)          │
│  in-memory, bounded 512   │
└───────────┬───────────────┘
            │ dequeued by worker
            ▼
┌──────────────────────────────────────────────────────────────────┐
│                    RunWorkerService                               │
│  (BackgroundService – runs on dedicated thread)                  │
│                                                                  │
│  1. Load file artifacts from disk                                │
│  2. Select engine (CPU or GPU based on run.EnginePreference       │
│     or global Calculation:PreferGpu config)                      │
│  3. Execute engine → IProgress<ProgressInfo> callbacks           │
│     → each callback persists progress% + MetricsJson to DB       │
│  4. Persist ResultJson + final MetricsJson to Runs table         │
└────────────────────────┬─────────────────────────────────────────┘
                         │
             ┌───────────┴───────────┐
             ▼                       ▼
  CpuCalculationEngine     GpuCalculationEngine
  (always available)        (simulated; labels = "GPU (simulated)")
```

---

## Data model

### `FileArtifact`

| Column | Type | Notes |
|--------|------|-------|
| `Id` | GUID | Primary key |
| `Type` | string | `Scenario`, `Risk`, `Portfolio`, `Sink` |
| `FileName` | string | Original filename from upload |
| `ContentType` | string | MIME type |
| `Size` | long | Bytes |
| `Sha256` | string | Hex-encoded SHA-256 of the raw bytes |
| `StoragePath` | string | Absolute path on disk under `./data/blobs/` |
| `CreatedAt` | DateTime | UTC |

File bytes are stored **on disk** under `./data/blobs/` (not in SQLite BLOB columns) to keep the database small and support streaming downloads. The `StoragePath` column holds the path.

### `SinkDefinition`

| Column | Type | Notes |
|--------|------|-------|
| `Id` | GUID | Primary key |
| `Name` | string | Human-readable label |
| `Version` | string | Semantic version string |
| `JsonDefinition` | string | Raw JSON describing output format |
| `CreatedAt` | DateTime | UTC |
| `UpdatedAt` | DateTime | UTC, updated on PUT |

### `RunRecord`

| Column | Type | Notes |
|--------|------|-------|
| `Id` | GUID | Primary key |
| `State` | string | `Queued` → `Running` → `Completed` / `Failed` / `Canceled` |
| `Progress` | int | 0–100 |
| `EngineUsed` | string | `CPU` or `GPU (simulated)` – set once the worker picks up the run |
| `EnginePreference` | string? | Per-run override (`CPU` / `GPU`); overrides global config |
| `ScenarioArtifactId` | GUID? | FK to `FileArtifacts` |
| `RiskArtifactId` | GUID? | FK to `FileArtifacts` |
| `PortfolioArtifactId` | GUID? | FK to `FileArtifacts` |
| `SinkDefinitionId` | GUID? | FK to `SinkDefinitions` |
| `ParametersJson` | string? | Additional key/value parameters as JSON |
| `ResultJson` | string? | Full calculation result (JSON) after completion |
| `MetricsJson` | string? | Runtime metrics (stage, percent, elapsed_ms, engine, etc.) |
| `ErrorMessage` | string? | Populated when `State = Failed` |
| `CreatedAt` | DateTime | UTC |
| `StartedAt` | DateTime? | UTC, set when worker starts processing |
| `CompletedAt` | DateTime? | UTC, set when state reaches terminal |
| `UpdatedAt` | DateTime | UTC, updated on every state change |

---

## Async run pipeline

```
POST /runs
  └─► validate request
  └─► INSERT RunRecord (State=Queued)
  └─► RunQueue.EnqueueAsync(runId)       ← in-memory channel push
  └─► 202 Accepted { runId, statusUrl, resultUrl }

BackgroundService (RunWorkerService)
  └─► await channel.ReadAsync()
  └─► UPDATE RunRecord (State=Running, EngineUsed=...)
  └─► for each progress stage → UPDATE RunRecord (Progress, MetricsJson)
  └─► ICalculationEngine.ExecuteAsync(inputs, progress, ct)
  └─► UPDATE RunRecord (State=Completed, ResultJson, MetricsJson)

GET /runs/{id}/status                    ← client polls
  └─► SELECT RunRecord → return state + progress + metrics

GET /runs/{id}/result                    ← when Completed
  └─► SELECT RunRecord → deserialize ResultJson
```

**Important:** The in-memory channel queue means that **queued runs are lost on process restart**. Runs that already have `State=Completed` or `State=Failed` survive restarts because their results are in SQLite. For production, replace `RunQueue` with a durable broker (e.g. Redis Streams, NATS, Azure Service Bus).

---

## Engine abstraction

```csharp
public interface ICalculationEngine
{
    string Label { get; }
    Task<CalculationResult> ExecuteAsync(
        CalculationInputs inputs,
        IProgress<ProgressInfo> progress,
        CancellationToken ct);
}
```

| Implementation | Label | Behaviour |
|----------------|-------|-----------|
| `CpuCalculationEngine` | `"CPU"` | Monte-Carlo simulation over scenario paths; ~300 ms |
| `GpuCalculationEngine` | `"GPU (simulated)"` | Same algorithm, annotated as GPU demo; ~80 ms simulated latency |

Both engines use the same random seed (derived from input lengths) to produce identical, deterministic results regardless of which engine is chosen.

### Selecting an engine

1. **Per-run override** (`RunRecord.EnginePreference = "GPU"`) — the worker checks this first.  
2. **Global config** (`Calculation:PreferGpu = true` in `appsettings.json`) — used when no per-run override is set.  
3. Default is CPU if neither is set.

The `POST /runs/demo/gpu-showcase` endpoint sets `EnginePreference = "GPU"` so the run always uses the GPU engine regardless of the global default.

---

## File storage

```
./data/
├── app.db               ← SQLite database (metadata + run records)
└── blobs/
    ├── <guid>_scenario.json
    ├── <guid>_risk.json
    └── ...
```

Uploaded files are saved to `./data/blobs/<random-guid>_<original-filename>`. The SQLite `FileArtifacts` table records the path. On `DELETE /files/{id}`, both the database row and the blob file are removed.

---

## Project structure

```
ActusInsurance.FastEndpointsSqliteGpuSample/
├── Program.cs                     ← DI wiring, middleware, startup
├── appsettings.json               ← Configuration (Calculation:PreferGpu)
├── Data/
│   ├── AppDbContext.cs            ← EF Core DbContext
│   └── Entities/
│       ├── FileArtifact.cs
│       ├── RunRecord.cs           ← includes RunState string constants
│       └── SinkDefinition.cs
├── Endpoints/
│   ├── Files/
│   │   ├── UploadFileEndpoint.cs  ← POST /files/{type}
│   │   ├── BatchUploadEndpoints.cs
│   │   ├── ListFilesEndpoint.cs
│   │   ├── GetFileEndpoint.cs
│   │   ├── DownloadFileEndpoint.cs
│   │   └── DeleteFileEndpoint.cs
│   ├── Sinks/
│   │   └── SinkEndpoints.cs       ← CRUD for /sinks
│   ├── Runs/
│   │   ├── StartRunEndpoint.cs    ← POST /runs
│   │   ├── StartDemoRunEndpoint.cs
│   │   ├── ListRunsEndpoint.cs
│   │   ├── GetRunEndpoint.cs
│   │   ├── RunStatusEndpoint.cs
│   │   ├── RunResultEndpoint.cs
│   │   └── CancelRunEndpoint.cs
│   └── Samples/
│       └── CreateDefaultSampleEndpoint.cs
├── Engines/
│   ├── ICalculationEngine.cs
│   ├── CpuCalculationEngine.cs
│   └── GpuCalculationEngine.cs
├── Services/
│   ├── RunQueue.cs                ← Channel<Guid> wrapper
│   └── RunWorkerService.cs        ← BackgroundService consumer
├── Validation/
│   └── Validators.cs              ← FluentValidation for uploads + runs
├── samples-data/
│   ├── scenario.json
│   ├── risk.json
│   ├── portfolio.json
│   └── sink-definition.json
└── docs/
    ├── architecture.md            ← (this file)
    ├── endpoints.md
    └── gpu-engine.md
```

---

## Dependency summary

| Package | Purpose |
|---------|---------|
| `FastEndpoints 8.x` | Endpoint routing, request/response DTOs, validation |
| `FastEndpoints.Swagger 8.x` | OpenAPI 3.0 spec + Swagger UI |
| `Microsoft.EntityFrameworkCore.Sqlite 9.x` | ORM + SQLite driver |

No external services (no Redis, no message broker, no cloud storage) are required.
