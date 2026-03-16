using System.Diagnostics;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Engines;

/// <summary>
/// GPU-simulated calculation engine.
/// This is a DEMO IMPLEMENTATION that simulates GPU execution.
/// In production, replace this with a real CUDA/OpenCL/ILGPU implementation.
/// Produces deterministic results identical to the CPU engine for comparability.
/// Toggle via appsettings: "Calculation:PreferGpu": true
/// </summary>
public class GpuCalculationEngine : ICalculationEngine
{
    public string Label => "GPU (simulated)";

    public async Task<CalculationResult> ExecuteAsync(
        CalculationInputs inputs,
        IProgress<ProgressInfo> progress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        progress.Report(new ProgressInfo(5, "Initializing GPU context [SIMULATED]"));
        await Task.Delay(20, ct);   // GPU init is typically fast after first use

        progress.Report(new ProgressInfo(25, "Transferring data to device"));
        await Task.Delay(20, ct);

        progress.Report(new ProgressInfo(50, "Executing GPU kernel [SIMULATED]"));
        // Same deterministic computation as CPU for comparability
        int seed = (inputs.ScenarioContent.Length + inputs.RiskContent.Length + inputs.PortfolioContent.Length) % 9999;
        var rng = new Random(seed);
        int numScenarios = 10;
        var pvs = new double[numScenarios];
        for (int i = 0; i < numScenarios; i++)
            pvs[i] = 900_000 + rng.NextDouble() * 200_000;

        await Task.Delay(30, ct);   // GPU typically faster than CPU

        progress.Report(new ProgressInfo(85, "Retrieving results from device"));
        await Task.Delay(10, ct);

        progress.Report(new ProgressInfo(100, "Done"));

        double mean = pvs.Average();
        double variance = pvs.Select(v => (v - mean) * (v - mean)).Average();
        double std = Math.Sqrt(variance);
        var sorted = pvs.OrderBy(v => v).ToArray();
        double p05 = sorted[(int)(numScenarios * 0.05)];
        double p95 = sorted[(int)(numScenarios * 0.95)];

        sw.Stop();
        return new CalculationResult(
            Label,
            pvs,
            mean, std, p05, p95,
            sw.ElapsedMilliseconds,
            new Dictionary<string, object>
            {
                ["numScenarios"]  = numScenarios,
                ["engine"]        = "GPU (simulated)",
                ["gpuDevice"]     = "DEMO - no physical GPU required",
                ["note"]          = "This is a simulated GPU engine. Replace GpuCalculationEngine with a real CUDA/ILGPU implementation for production use."
            });
    }
}
