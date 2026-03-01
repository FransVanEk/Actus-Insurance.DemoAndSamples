# Endpoint Reference

Base URL: `http://localhost:5000`  
Interactive docs: `http://localhost:5000/swagger`

All endpoints are **unauthenticated**. All request/response bodies are JSON unless noted.

---

## Files

### `POST /files/scenarios`

Upload a scenario file.

**Request** — `multipart/form-data`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `File` | file | ✓ | The file to upload |

**Response** `200 OK`

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "Scenario",
  "fileName": "my-scenario.json",
  "contentType": "application/json",
  "size": 1234,
  "sha256": "abc123...",
  "createdAt": "2025-01-01T12:00:00Z"
}
```

**Other upload endpoints** — identical shape, different type tag:

| Endpoint | Type tag |
|----------|----------|
| `POST /files/risks` | `Risk` |
| `POST /files/portfolios` | `Portfolio` |
| `POST /files/sinks` | `Sink` |

---

### `POST /files/scenarios/batch`

Upload multiple scenario files in a single request.

**Request** — `multipart/form-data`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Files` | file[] | ✓ | One or more files |

**Response** `200 OK`

```json
{
  "uploaded": [
    { "id": "...", "fileName": "a.json", "size": 100, ... },
    { "id": "...", "fileName": "b.json", "size": 200, ... }
  ],
  "errors": []
}
```

Partial success is supported: if some files fail validation they appear in `errors` while the valid ones are returned in `uploaded`. Batch endpoints exist for `risks` and `portfolios` too.

---

### `GET /files`

List stored file artifacts.

**Query parameters**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `type` | string | — | Filter by type: `Scenario`, `Risk`, `Portfolio`, `Sink` |

**Response** `200 OK` — array of file artifact objects (same shape as upload response).

---

### `GET /files/{id}`

Get metadata for a single file artifact.

**Response** `200 OK` — file artifact object.  
**Response** `404 Not Found` — artifact not found.

---

### `GET /files/{id}/content`

Download the raw file bytes.

**Response** `200 OK`  
Headers: `Content-Disposition: attachment; filename=<original-name>`, `Content-Type: <stored-mime>`, `X-Content-Sha256: <hex>`  
Body: raw file bytes.

**Response** `404 Not Found` — artifact or blob file not found.

---

### `DELETE /files/{id}`

Delete a file artifact and its blob from disk.

**Response** `204 No Content`  
**Response** `404 Not Found`

---

## Sinks

Sink definitions describe how calculation outputs are formatted and where they are sent.

### `POST /sinks`

Create a new sink definition.

**Request body**

```json
{
  "name": "My Sink",
  "version": "1.0",
  "jsonDefinition": "{\"outputFormat\":\"csv\",\"aggregations\":[\"portfolio_pv\"]}"
}
```

| Field | Required | Notes |
|-------|----------|-------|
| `name` | ✓ | 1–200 chars |
| `version` | ✓ | 1–50 chars |
| `jsonDefinition` | ✓ | Any valid JSON string |

**Response** `201 Created` — sink object with `id`, `name`, `version`, `jsonDefinition`, `createdAt`, `updatedAt`.

---

### `GET /sinks`

List all sink definitions.

**Response** `200 OK` — array of sink objects.

---

### `GET /sinks/{id}`

Get a single sink definition.

**Response** `200 OK` — sink object.  
**Response** `404 Not Found`

---

### `PUT /sinks/{id}`

Update a sink definition.

**Request body** — same shape as `POST /sinks` (all fields optional; only provided fields are updated).

**Response** `200 OK` — updated sink object.  
**Response** `404 Not Found`

---

### `DELETE /sinks/{id}`

Delete a sink definition.

**Response** `204 No Content`  
**Response** `404 Not Found`

---

## Runs

### `POST /runs`

Start an async calculation run.

**Request body**

```json
{
  "scenarioArtifactId": "guid",
  "riskArtifactId": "guid",
  "portfolioArtifactId": "guid",
  "sinkDefinitionId": "guid",
  "enginePreference": "CPU",
  "parameters": { "key": "value" }
}
```

| Field | Required | Notes |
|-------|----------|-------|
| `scenarioArtifactId` | * | At least one of scenario/risk/portfolio is required |
| `riskArtifactId` | * | — |
| `portfolioArtifactId` | — | — |
| `sinkDefinitionId` | — | — |
| `enginePreference` | — | `"CPU"` or `"GPU"` (overrides global config) |
| `parameters` | — | Arbitrary string key/value map |

**Response** `202 Accepted`

```json
{
  "runId": "3fa85f64-...",
  "statusUrl": "/runs/3fa85f64-.../status",
  "resultUrl": "/runs/3fa85f64-.../result",
  "state": "Queued"
}
```

---

### `POST /runs/demo/{demoName}`

Start a predefined demo run using packaged sample data.

**Route parameter** `demoName`: `basic` | `gpu-showcase`

- `basic` — uses CPU engine with the bundled sample files.
- `gpu-showcase` — forces the GPU engine (simulated) and the same sample files.

Both are **idempotent**: sample file artifacts are created in the database on first call and reused on subsequent calls.

**Response** `202 Accepted` — same shape as `POST /runs`.  
**Response** `404 Not Found` — unknown demo name.

---

### `GET /runs`

List runs.

**Query parameters**

| Parameter | Default | Description |
|-----------|---------|-------------|
| `state` | — | Filter: `Queued`, `Running`, `Completed`, `Failed`, `Canceled` |
| `limit` | `50` | Max results (1–200) |

**Response** `200 OK` — array of run summary objects:

```json
[
  {
    "id": "...",
    "state": "Completed",
    "progress": 100,
    "engine": "CPU",
    "enginePreference": null,
    "scenarioArtifactId": "...",
    "riskArtifactId": "...",
    "portfolioArtifactId": null,
    "sinkDefinitionId": null,
    "errorMessage": null,
    "createdAt": "...",
    "startedAt": "...",
    "completedAt": "...",
    "updatedAt": "..."
  }
]
```

---

### `GET /runs/{runId}`

Get full run detail (status + result in one call).

**Response** `200 OK` — run object including `result` (JSON) if completed.  
**Response** `404 Not Found`

---

### `GET /runs/{runId}/status`

Poll run status and progress.

**Response** `200 OK`

```json
{
  "runId": "...",
  "state": "Running",
  "progress0To100": 60,
  "message": null,
  "createdAt": "...",
  "startedAt": "...",
  "updatedAt": "...",
  "engine": "CPU",
  "metrics": {
    "stage": "Compute",
    "percent": 60,
    "elapsed_ms": 180
  }
}
```

States: `Queued` → `Running` → `Completed` | `Failed` | `Canceled`

---

### `GET /runs/{runId}/result`

Get the final calculation result.

**Response** `200 OK` (when completed)

```json
{
  "runId": "...",
  "state": "Completed",
  "engine": "CPU",
  "completedAt": "...",
  "result": {
    "engineLabel": "CPU",
    "durationMs": 298,
    "meanPv": 1013437.08,
    "stdPv": 45231.12,
    "p05Pv": 934210.50,
    "p95Pv": 1091043.20,
    "portfolioPvByScenario": [980000.0, 1010000.0, ...]
  }
}
```

**Response** `202 Accepted` — run not yet completed; `state` field indicates current state.  
**Response** `422 Unprocessable Entity` — run failed; `state = "Failed"`.

---

### `POST /runs/{runId}/cancel`

Request cancellation of a queued or running run.

**Response** `204 No Content` — cancellation recorded.  
**Response** `404 Not Found` — run not found.  
**Response** `409 Conflict` — run already in a terminal state (`Completed`, `Failed`, or `Canceled`).

> **Note:** The background worker checks for `Canceled` state before and after each stage, so already-running computations may complete a current stage before stopping.

---

## Samples

### `POST /samples/create-default`

Idempotent endpoint that ensures the four packaged sample files exist in the database.

**Response** `200 OK`

```json
{
  "scenarioArtifactId": "...",
  "riskArtifactId": "...",
  "portfolioArtifactId": "...",
  "sinkDefinitionId": "...",
  "message": "Default sample data ready. Use the returned IDs with POST /runs to start a calculation."
}
```

Call this once after starting the server to get IDs you can immediately pass to `POST /runs`.

---

## Common errors

| Status | Meaning |
|--------|---------|
| `400 Bad Request` | Validation failed (details in body) |
| `404 Not Found` | Resource not found |
| `409 Conflict` | Invalid state transition (e.g. cancel a completed run) |
| `422 Unprocessable Entity` | Run result requested for a failed run |
