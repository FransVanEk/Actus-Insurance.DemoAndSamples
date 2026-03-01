# GitHub Copilot Instructions — Actus Insurance API

This repository contains a demo REST API for ACTUS-based insurance calculations.

## API overview

The API (`samples/ActusInsurance.FastEndpointsSqliteGpuSample`) exposes:
- **Files** (`/files`) — upload/download scenario, risk, portfolio and sink files
- **Sinks** (`/sinks`) — manage calculation output definitions
- **Runs** (`/runs`) — start async calculation runs, poll status, retrieve results
- **Samples** (`/samples/create-default`) — seed the database with bundled sample data

When generating API client code or HTTP requests, always refer to `docs/client-prompt.md`
for the complete system prompt and `docs/endpoints.md` for the full endpoint reference.

## Quick-start for the API

```
POST /samples/create-default        → get artifact IDs
POST /runs  { scenarioArtifactId, riskArtifactId }  → start run, get runId
GET  /runs/{runId}/status           → poll until state = "Completed"
GET  /runs/{runId}/result           → retrieve result
```

Base URL (local): `http://localhost:8080`  
Swagger UI: `http://localhost:8080/swagger`

## Project conventions

- Framework: **FastEndpoints 8.x** (not MVC/minimal-api)
- `SendAsync` / `SendNotFoundAsync` are **extension methods on `HttpContext.Response`** in FastEndpoints 8.x
- `CancellationToken` must be passed as the named parameter `cancellation:` in those calls
- DB: SQLite via Entity Framework Core; connection string in `appsettings.json`
- Calculation engines are registered as keyed services (`"cpu"` / `"gpu"`)
- Toggle GPU by setting `Calculation__PreferGpu=true` or passing `enginePreference: "GPU"` in POST /runs
