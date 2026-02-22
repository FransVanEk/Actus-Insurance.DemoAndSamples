using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PamMonteCarlo50Y.Sinks;

// ── Portfolio-PV by scenario ─────────────────────────────────────────────

/// <summary>Writes <c>portfolio_pv_by_scenario.csv</c>.</summary>
public static class PortfolioPvCsvSink
{
    public static void Write(string outputDir, string runId, double[] scenarioPvs)
    {
        string path = Path.Combine(outputDir, $"{runId}_portfolio_pv_by_scenario.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("scenarioIndex,portfolioPV");
        for (int s = 0; s < scenarioPvs.Length; s++)
            w.WriteLine($"{s},{scenarioPvs[s].ToString("F4", CultureInfo.InvariantCulture)}");
    }
}

// ── Contract PV sample ───────────────────────────────────────────────────

/// <summary>
/// Writes <c>contract_pv_sample.csv</c> for a small subset of contracts
/// (useful for inspection / debugging).
/// </summary>
public static class ContractPvSampleCsvSink
{
    public static void Write(
        string   outputDir,
        string   runId,
        double[] pvMatrix,          // [c * numScenarios + s]
        int      numContracts,
        int      numScenarios,
        int      sampleContracts = 20,
        int      sampleScenarios = 10)
    {
        string path = Path.Combine(outputDir, $"{runId}_contract_pv_sample.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("contractIndex,scenarioIndex,pv");

        int nc = Math.Min(sampleContracts, numContracts);
        int ns = Math.Min(sampleScenarios, numScenarios);

        for (int c = 0; c < nc; c++)
            for (int s = 0; s < ns; s++)
                w.WriteLine($"{c},{s},{pvMatrix[c * numScenarios + s].ToString("F4", CultureInfo.InvariantCulture)}");
    }
}

// ── Summary JSON ─────────────────────────────────────────────────────────

/// <summary>Summary metrics + config snapshot written to <c>summary.json</c>.</summary>
public sealed class RunSummaryRecord
{
    [JsonPropertyName("runId")]         public string RunId        { get; set; } = string.Empty;
    [JsonPropertyName("description")]   public string Description  { get; set; } = string.Empty;
    [JsonPropertyName("backend")]       public string Backend      { get; set; } = "cpu";
    [JsonPropertyName("numContracts")]  public int    NumContracts  { get; set; }
    [JsonPropertyName("numScenarios")]  public int    NumScenarios  { get; set; }
    [JsonPropertyName("numMonths")]     public int    NumMonths     { get; set; }
    [JsonPropertyName("calcDateIndex")] public int    CalcDateIndex { get; set; }
    [JsonPropertyName("seed")]          public ulong  Seed          { get; set; }
    [JsonPropertyName("vasicek")]       public VasicekParamsRecord? Vasicek { get; set; }

    // Timing
    [JsonPropertyName("provisioningMs")]  public long ProvisioningMs  { get; set; }
    [JsonPropertyName("calcMs")]          public long CalcMs           { get; set; }
    [JsonPropertyName("fetchMs")]         public long FetchMs          { get; set; }
    [JsonPropertyName("reportingMs")]     public long ReportingMs      { get; set; }
    [JsonPropertyName("totalMs")]         public long TotalMs          { get; set; }
    [JsonPropertyName("calcMsPerContract")] public double CalcMsPerContract { get; set; }

    // Portfolio PV statistics
    [JsonPropertyName("pvMean")]   public double PvMean   { get; set; }
    [JsonPropertyName("pvStdev")]  public double PvStdev  { get; set; }
    [JsonPropertyName("pvMin")]    public double PvMin    { get; set; }
    [JsonPropertyName("pvMax")]    public double PvMax    { get; set; }
    [JsonPropertyName("pvP05")]    public double PvP05    { get; set; }
    [JsonPropertyName("pvP25")]    public double PvP25    { get; set; }
    [JsonPropertyName("pvP50")]    public double PvP50    { get; set; }
    [JsonPropertyName("pvP75")]    public double PvP75    { get; set; }
    [JsonPropertyName("pvP95")]    public double PvP95    { get; set; }
    [JsonPropertyName("pvP99")]    public double PvP99    { get; set; }
    [JsonPropertyName("var99")]    public double Var99    { get; set; }
    [JsonPropertyName("es99")]     public double Es99     { get; set; }
}

public sealed class VasicekParamsRecord
{
    [JsonPropertyName("kappa")] public double Kappa { get; set; }
    [JsonPropertyName("theta")] public double Theta { get; set; }
    [JsonPropertyName("sigma")] public double Sigma { get; set; }
    [JsonPropertyName("r0")]    public double R0    { get; set; }
}

/// <summary>Computes summary statistics and writes <c>summary.json</c>.</summary>
public static class JsonSummarySink
{
    /// <summary>Compute statistics over the per-scenario portfolio PV array and write JSON.</summary>
    public static void Write(string outputDir, RunSummaryRecord summary)
    {
        string path = Path.Combine(outputDir, $"{summary.RunId}_summary.json");
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(summary, opts));
    }

    /// <summary>
    /// Fill the PV statistics on <paramref name="summary"/> from the
    /// per-scenario portfolio PV array.
    /// </summary>
    public static void ComputeStats(RunSummaryRecord summary, double[] scenarioPvs)
    {
        if (scenarioPvs.Length == 0) return;

        const double Var99Threshold = 0.01;   // worst 1% of outcomes

        double sum = 0.0, sum2 = 0.0;
        foreach (var v in scenarioPvs) { sum += v; sum2 += v * v; }
        double n    = scenarioPvs.Length;
        double mean = sum / n;
        double var  = sum2 / n - mean * mean;

        var sorted = (double[])scenarioPvs.Clone();
        Array.Sort(sorted);

        summary.PvMean  = mean;
        summary.PvStdev = Math.Sqrt(Math.Max(0.0, var));
        summary.PvMin   = sorted[0];
        summary.PvMax   = sorted[sorted.Length - 1];
        summary.PvP05   = Percentile(sorted, 0.05);
        summary.PvP25   = Percentile(sorted, 0.25);
        summary.PvP50   = Percentile(sorted, 0.50);
        summary.PvP75   = Percentile(sorted, 0.75);
        summary.PvP95   = Percentile(sorted, 0.95);
        summary.PvP99   = Percentile(sorted, 1.0 - Var99Threshold);

        // VaR99 = loss exceeded with Var99Threshold probability (worst 1%)
        double p01    = Percentile(sorted, Var99Threshold);
        summary.Var99 = -p01;

        // ES99 = expected shortfall = conditional mean of worst Var99Threshold fraction
        int    cutoff = (int)Math.Max(1, Math.Floor(Var99Threshold * n));
        double esum   = 0.0;
        for (int i = 0; i < cutoff; i++) esum += sorted[i];
        summary.Es99 = -(esum / cutoff);
    }

    private static double Percentile(double[] sorted, double p)
    {
        double idx = p * (sorted.Length - 1);
        int    lo  = (int)Math.Floor(idx);
        int    hi  = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
    }
}
