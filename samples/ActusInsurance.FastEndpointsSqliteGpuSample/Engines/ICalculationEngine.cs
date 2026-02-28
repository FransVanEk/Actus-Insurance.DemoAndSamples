namespace ActusInsurance.FastEndpointsSqliteGpuSample.Engines;

public record CalculationInputs(
    string ScenarioContent,
    string RiskContent,
    string PortfolioContent,
    string SinkDefinitionJson,
    Dictionary<string, string> Parameters);

public record ProgressInfo(int Percent, string Stage);

public record CalculationResult(
    string EngineLabel,
    double[] PortfolioPvByScenario,
    double MeanPv,
    double StdPv,
    double P05,
    double P95,
    long DurationMs,
    Dictionary<string, object> Metrics);

public interface ICalculationEngine
{
    string Label { get; }
    Task<CalculationResult> ExecuteAsync(
        CalculationInputs inputs,
        IProgress<ProgressInfo> progress,
        CancellationToken ct);
}
