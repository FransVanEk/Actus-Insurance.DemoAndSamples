# GPU / CPU Engine Guide

This document explains the calculation engine abstraction, the current (simulated) GPU implementation, and how to replace it with a real GPU backend.

---

## Interface

```csharp
public interface ICalculationEngine
{
    /// <summary>Human-readable label returned in run status and result responses.</summary>
    string Label { get; }

    /// <summary>
    /// Execute the calculation and report progress via <paramref name="progress"/>.
    /// </summary>
    Task<CalculationResult> ExecuteAsync(
        CalculationInputs inputs,
        IProgress<ProgressInfo> progress,
        CancellationToken ct);
}
```

### `CalculationInputs`

| Property | Description |
|----------|-------------|
| `ScenarioJson` | Raw JSON content of the scenario file |
| `RiskJson` | Raw JSON content of the risk file |
| `PortfolioJson` | Raw JSON content of the portfolio file |
| `SinkJson` | Raw JSON definition of the sink |
| `Parameters` | Arbitrary key/value parameters from the run request |

### `CalculationResult`

| Property | Description |
|----------|-------------|
| `EngineLabel` | Which engine ran (`"CPU"` / `"GPU (simulated)"`) |
| `DurationMs` | Wall-clock time in milliseconds |
| `MeanPv` | Mean present value across all scenarios |
| `StdPv` | Standard deviation of PV |
| `P05Pv` | 5th percentile PV |
| `P95Pv` | 95th percentile PV |
| `PortfolioPvByScenario` | PV for each scenario path (array) |

### `ProgressInfo`

| Property | Description |
|----------|-------------|
| `Stage` | Human-readable stage name (e.g. `"Load inputs"`, `"Compute"`) |
| `Percent` | 0–100 |

---

## CPU engine (`CpuCalculationEngine`)

**Label:** `"CPU"`  
**Availability:** Always  
**Simulated latency:** ~300 ms total

The CPU engine runs a simplified Monte-Carlo simulation:

1. **Load inputs** (10%) — parse scenario/risk/portfolio JSON
2. **Validate** (20%) — basic sanity checks
3. **Compute** (70%) — for each scenario path, compute a discounted PV
4. **Aggregate** (90%) — compute mean, std, percentiles across paths
5. **Store result** (100%) — serialise to JSON

The random seed is deterministic: it is derived from the combined length of all input strings, so the same inputs always produce the same outputs regardless of wall-clock time.

---

## GPU engine (`GpuCalculationEngine`)

**Label:** `"GPU (simulated)"`  
**Physical GPU required:** **No** — this is a placeholder  
**Simulated latency:** ~80 ms total

The GPU engine runs the **identical algorithm** as the CPU engine but:

- Introduces a shorter simulated latency (mimicking GPU parallelism speedup)
- Labels itself `"GPU (simulated)"` in all responses so it is immediately obvious
- Is explicitly documented as a **demo / placeholder** in code comments

The result is identical to the CPU engine (same seed, same algorithm).

---

## Toggling engines

### Global default (appsettings.json)

```json
{
  "Calculation": {
    "PreferGpu": false
  }
}
```

Set `true` to make every new run default to the GPU engine.

### Environment variable

```bash
CALCULATION__PREFERGPU=true dotnet run
```

### Per-run override

Include `enginePreference` in the `POST /runs` body:

```json
{
  "scenarioArtifactId": "...",
  "enginePreference": "GPU"
}
```

Values: `"CPU"` | `"GPU"` (case-insensitive is fine; the worker normalises to the keyed service).

### Demo endpoints

`POST /runs/demo/gpu-showcase` always sets `EnginePreference = "GPU"`, so it uses the GPU engine regardless of global config.

---

## GPU passthrough in Docker (NVIDIA)

The `docker-compose.yml` includes an `actus-sample-gpu` service that passes the host's physical NVIDIA GPU into the container. This uses the **NVIDIA Container Toolkit**.

### Host prerequisites

1. **NVIDIA GPU driver** installed on the host (any recent driver ≥ 525).
2. **NVIDIA Container Toolkit** installed:
   ```bash
   # Ubuntu / Debian
   curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg
   curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | \
     sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | \
     sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
   sudo apt-get update && sudo apt-get install -y nvidia-container-toolkit
   sudo nvidia-ctk runtime configure --runtime=docker
   sudo systemctl restart docker
   ```
   Full guide: https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html

### Start with GPU passthrough

```bash
cd samples/ActusInsurance.FastEndpointsSqliteGpuSample

# Build the GPU image and start the GPU-enabled service
docker compose --profile gpu up --build
```

Or manually:
```bash
docker build -t actus-sample-gpu -f Dockerfile.gpu .

docker run -d \
  --name actus-sample-gpu \
  -p 8080:8080 \
  -v actus-data:/app/data \
  --gpus all \
  -e Calculation__PreferGpu=true \
  actus-sample-gpu
```

### What `Dockerfile.gpu` adds

The GPU Dockerfile uses `nvidia/cuda:12.6.3-runtime-ubuntu24.04` as the runtime base instead of the standard `aspnet:9.0` image. This ships the CUDA 12 runtime libraries (`libcuda`, `libcudart`, cuBLAS, cuDNN) inside the container. Combined with the toolkit's host driver injection, any CUDA library (including ILGPU) can discover and use the physical GPU.

```
Host:      NVIDIA driver + NVIDIA Container Toolkit
           ↓ injects /dev/nvidia* + libcuda.so at runtime
Container: nvidia/cuda:12.6.3-runtime-ubuntu24.04
              + aspnetcore-runtime-9.0
              + your ILGPU / CUDA P/Invoke code
```

The two environment variables set in `Dockerfile.gpu` tell the NVIDIA runtime which GPU devices and driver capabilities to expose:

```dockerfile
ENV NVIDIA_VISIBLE_DEVICES=all        # expose all GPUs (change to "0" to pin to GPU 0)
ENV NVIDIA_DRIVER_CAPABILITIES=compute,utility
```

### Verify GPU is visible inside the container

```bash
docker run --rm --gpus all nvidia/cuda:12.6.3-runtime-ubuntu24.04 nvidia-smi
```

You should see your GPU listed in the `nvidia-smi` output before starting the application.

> **Note:** The current `GpuCalculationEngine` is still a **simulated** engine — it does not actually invoke CUDA kernels. The Docker GPU passthrough infrastructure is in place so that when you replace the engine body with real ILGPU/CUDA code (see the section below), it will have access to the hardware without further Docker changes.

---

## Replacing the simulated GPU engine with a real one

The `GpuCalculationEngine` class is the only file you need to change.

### Option 1 — ILGPU (cross-platform CUDA/OpenCL)

```xml
<!-- Add to .csproj -->
<PackageReference Include="ILGPU" Version="1.5.1" />
<PackageReference Include="ILGPU.Algorithms" Version="1.5.1" />
```

```csharp
public class GpuCalculationEngine : ICalculationEngine
{
    public string Label => "GPU (ILGPU/CUDA)";

    public async Task<CalculationResult> ExecuteAsync(
        CalculationInputs inputs, IProgress<ProgressInfo> progress, CancellationToken ct)
    {
        using var context = Context.CreateDefault();
        using var accelerator = context.GetPreferredDevice(preferCPU: false)
                                       .CreateAccelerator(context);

        // Load scenario paths into GPU memory
        var scenarioPaths = ParseScenarioPaths(inputs.ScenarioJson);
        using var gpuPaths = accelerator.Allocate1D(scenarioPaths);

        progress.Report(new("Loaded inputs to GPU", 20));

        // Launch kernel
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<double>, ArrayView<double>>(DiscountKernel);
        using var pvBuffer = accelerator.Allocate1D<double>(scenarioPaths.Length);

        kernel(scenarioPaths.Length, gpuPaths.View, pvBuffer.View);
        accelerator.Synchronize();

        progress.Report(new("GPU kernel complete", 80));

        var pvs = pvBuffer.GetAsArray1D();
        // ... aggregate and return result
    }

    static void DiscountKernel(Index1D i, ArrayView<double> paths, ArrayView<double> pvs)
    {
        pvs[i] = paths[i] * 0.95; // replace with real discounting
    }
}
```

### Option 2 — Native CUDA via P/Invoke

If you have an existing `.so` / `.dll` CUDA library:

```csharp
[DllImport("libactuscalc.so", EntryPoint = "run_monte_carlo")]
static extern void RunMonteCarlo(double[] paths, int n, double[] output);
```

### Option 3 — REST call to a Python GPU service

If your GPU code lives in Python (e.g. PyTorch, CuPy):

```csharp
var response = await httpClient.PostAsJsonAsync("http://gpu-service/calc", inputs);
var result = await response.Content.ReadFromJsonAsync<GpuResult>();
```

---

## Engine selection in the worker

```csharp
// RunWorkerService.cs
private ICalculationEngine SelectEngine(string? preference)
{
    if (preference == "GPU")
        return scopeFactory.GetRequiredKeyedService<ICalculationEngine>("gpu");
    if (preference == "CPU")
        return scopeFactory.GetRequiredKeyedService<ICalculationEngine>("cpu");

    // Fall back to global default
    return scopeFactory.GetRequiredService<ICalculationEngine>();
}
```

The `run.EngineUsed` field is set to `engine.Label` before execution begins, so it is visible in `GET /runs/{id}/status` immediately after the worker picks up the run.

---

## Result traceability

Every run response includes `engine` (from `run.EngineUsed`) so callers always know exactly which engine ran:

```json
{
  "state": "Completed",
  "engine": "GPU (simulated)",
  "result": { "engineLabel": "GPU (simulated)", ... }
}
```
