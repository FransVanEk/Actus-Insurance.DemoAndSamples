using System.Diagnostics;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Engines;

/// <summary>CPU-based calculation engine — always available.</summary>
public class CpuCalculationEngine : ICalculationEngine
{
    public string Label => "CPU";

    public async Task<CalculationResult> ExecuteAsync(
        CalculationInputs inputs,
        IProgress<ProgressInfo> progress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        progress.Report(new ProgressInfo(10, "Loading inputs"));
        await Task.Delay(50, ct);

        progress.Report(new ProgressInfo(30, "Validating"));
        await Task.Delay(50, ct);

        progress.Report(new ProgressInfo(50, "Computing"));
        // Deterministic pseudo-computation based on input lengths
        int seed = (inputs.ScenarioContent.Length + inputs.RiskContent.Length + inputs.PortfolioContent.Length) % 9999;
        var rng = new Random(seed);
        int numScenarios = 10;
        var pvs = new double[numScenarios];
        for (int i = 0; i < numScenarios; i++)
            pvs[i] = 900_000 + rng.NextDouble() * 200_000;

        await Task.Delay(100, ct);

        progress.Report(new ProgressInfo(80, "Aggregating"));
        double mean = pvs.Average();
        double variance = pvs.Select(v => (v - mean) * (v - mean)).Average();
        double std = Math.Sqrt(variance);
        var sorted = pvs.OrderBy(v => v).ToArray();
        double p05 = sorted[(int)(numScenarios * 0.05)];
        double p95 = sorted[(int)(numScenarios * 0.95)];

        await Task.Delay(50, ct);
        progress.Report(new ProgressInfo(100, "Done"));

        sw.Stop();
        return new CalculationResult(
            Label,
            pvs,
            mean, std, p05, p95,
            sw.ElapsedMilliseconds,
            new Dictionary<string, object>
            {
                ["numScenarios"] = numScenarios,
                ["engine"]       = "CPU",
                ["note"]         = "Deterministic CPU calculation engine"
            });
    }
}
