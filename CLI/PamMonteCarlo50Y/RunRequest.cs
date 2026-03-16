/*
 * PamMonteCarlo50Y Demo — RunRequest model.
 * Represents a single valuation run with slicing of the portfolio,
 * scenario set, and date window.
 */
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PamMonteCarlo50Y;

/// <summary>
/// Per-run output configuration.  When present on a <see cref="RunRequest"/>,
/// these settings override the global CLI reporting flags for that run only.
/// </summary>
public sealed class RunOutputOptions
{
    /// <summary>
    /// Enable the reporting transformer (contract_summary, portfolio_by_scenario,
    /// runs.csv, grouped summaries).  Default: <c>true</c>.
    /// </summary>
    [JsonPropertyName("reporting")]
    public bool Reporting { get; set; } = true;

    /// <summary>
    /// Export <c>fact_results_long.csv</c> — one row per
    /// (RunId, ContractId, ScenarioId) with Measure=PV and the discounted PV.
    /// Default: <c>false</c>.
    /// </summary>
    [JsonPropertyName("exportPvFact")]
    public bool ExportPvFact { get; set; } = false;

    /// <summary>
    /// Export <c>cashflow_timeseries.csv</c> — one row per
    /// (RunId, ContractId, ScenarioId, EventDate, TimeIndex, EventType)
    /// with UndiscountedCashflow, DiscountFactor, and DiscountedCashflow.
    /// Produces the full contract × scenario × time cashflow detail.
    /// Default: <c>false</c>.
    /// </summary>
    [JsonPropertyName("exportCashflowTimeSeries")]
    public bool ExportCashflowTimeSeries { get; set; } = false;

    /// <summary>
    /// Maximum number of contracts to include in the fact table and cashflow
    /// time-series export.  <c>0</c> = all contracts.  Default: <c>0</c>.
    /// </summary>
    [JsonPropertyName("contractSampleSize")]
    public int ContractSampleSize { get; set; } = 0;

    /// <summary>
    /// Maximum number of scenarios to include in the fact table and cashflow
    /// time-series export.  <c>0</c> = all scenarios.  Default: <c>0</c>.
    /// </summary>
    [JsonPropertyName("scenarioSampleSize")]
    public int ScenarioSampleSize { get; set; } = 0;
}

/// <summary>
/// A single valuation run request.  Multiple requests can be executed against
/// the same pre-built portfolio without rebuilding it.
///
/// <b>Slicing semantics</b>:
/// <list type="bullet">
///   <item><see cref="ContractStart"/>/<see cref="ContractCount"/> — subset of the
///         full portfolio (default: 0 / all contracts).</item>
///   <item><see cref="ScenarioStart"/>/<see cref="ScenarioCount"/> — subset of the
///         scenario set (default: 0 / all scenarios).</item>
///   <item><see cref="CalcDateIndex"/> — month index (0..numMonths-1) that acts as
///         the prior/after boundary.  Events at t &lt; CalcDateIndex use prior rates;
///         events at t &gt;= CalcDateIndex use after (projected) rates.</item>
/// </list>
///
/// <b>Per-run output</b>:
/// Set <see cref="OutputOptions"/> to control exactly which output files are
/// written for this run (overrides the global CLI reporting flags).
/// </summary>
public sealed class RunRequest
{
    /// <summary>Unique identifier for this run (used in output file names).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "run0";

    /// <summary>Zero-based start index into the portfolio.</summary>
    [JsonPropertyName("contractStart")]
    public int ContractStart { get; set; } = 0;

    /// <summary>
    /// Number of contracts to evaluate.  0 = all contracts from <see cref="ContractStart"/>.
    /// </summary>
    [JsonPropertyName("contractCount")]
    public int ContractCount { get; set; } = 0;

    /// <summary>Zero-based start index into the scenario set.</summary>
    [JsonPropertyName("scenarioStart")]
    public int ScenarioStart { get; set; } = 0;

    /// <summary>
    /// Number of scenarios to evaluate.  0 = all scenarios from <see cref="ScenarioStart"/>.
    /// </summary>
    [JsonPropertyName("scenarioCount")]
    public int ScenarioCount { get; set; } = 0;

    /// <summary>
    /// Month index that defines the calculation date (prior/after boundary).
    /// 0 = pure forward simulation (no historical prior).
    /// </summary>
    [JsonPropertyName("calcDateIndex")]
    public int CalcDateIndex { get; set; } = 0;

    /// <summary>
    /// Optional description for this run (written to summary.json).
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Per-run output options.  When set, these override the global CLI
    /// <c>--reporting</c> / <c>--export-fact</c> flags for this run only.
    /// When <c>null</c>, the global CLI flags are used.
    /// </summary>
    [JsonPropertyName("outputOptions")]
    public RunOutputOptions? OutputOptions { get; set; }

    // ── Factory helpers ──────────────────────────────────────────────

    /// <summary>Parse a list of RunRequests from a JSON file.</summary>
    public static RunRequest[] LoadFromJson(string path)
    {
        var json = System.IO.File.ReadAllText(path);
        return JsonSerializer.Deserialize<RunRequest[]>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? Array.Empty<RunRequest>();
    }

    /// <summary>Build a default single-run request (all contracts, all scenarios).</summary>
    public static RunRequest Default(int calcDateIndex = 0) => new()
    {
        Id            = "run0",
        ContractStart = 0,
        ContractCount = 0,
        ScenarioStart = 0,
        ScenarioCount = 0,
        CalcDateIndex = calcDateIndex,
        Description   = "Default full-portfolio run",
    };
}
