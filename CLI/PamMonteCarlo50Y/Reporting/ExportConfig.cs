/*
 * PamMonteCarlo50Y Demo — export configuration for the reporting transformer.
 *
 * Controls which CSV outputs are produced and how large datasets are sampled
 * to keep file sizes manageable.
 */
namespace PamMonteCarlo50Y.Reporting;

/// <summary>
/// Controls which files the <see cref="ResultExportTransformer"/> produces
/// and how it samples large result sets.
/// </summary>
public sealed class ExportConfig
{
    // ── Master switches ──────────────────────────────────────────────────

    /// <summary>
    /// Enable the full reporting pipeline (contract_summary.csv,
    /// portfolio_by_scenario.csv, runs.csv, _README.txt, and optionally
    /// fact_results_long.csv).
    /// When <c>false</c>, no reporting CSV files are written.
    /// Default: <c>false</c> (opt-in).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, skip fact_results_long.csv entirely and only
    /// write the aggregated summaries (contract_summary.csv +
    /// portfolio_by_scenario.csv).
    /// Default: <c>false</c>.
    /// </summary>
    public bool AggregationOnly { get; set; } = false;

    // ── Fact table (fact_results_long.csv) ──────────────────────────────

    /// <summary>
    /// Export <c>fact_results_long.csv</c>.
    /// Ignored when <see cref="AggregationOnly"/> is <c>true</c>.
    /// Default: <c>false</c>.
    /// </summary>
    public bool ExportFact { get; set; } = false;

    /// <summary>
    /// Maximum number of contracts to include in fact_results_long.csv.
    /// 0 = all contracts in the run.
    /// Default: 200.
    /// </summary>
    public int ContractSampleSize { get; set; } = 200;

    /// <summary>
    /// Seed for random contract sampling (reproducible samples).
    /// 0 = take the first <see cref="ContractSampleSize"/> contracts (no random sampling).
    /// Default: 0.
    /// </summary>
    public ulong ContractSampleSeed { get; set; } = 0UL;

    /// <summary>
    /// Maximum number of scenarios to include in fact_results_long.csv.
    /// 0 = all scenarios in the run.
    /// Default: 200.
    /// </summary>
    public int ScenarioSampleSize { get; set; } = 200;

    // ── Optional metadata file ───────────────────────────────────────────

    /// <summary>
    /// Path to an external contracts metadata CSV file
    /// (ContractId, Segment, Region, ...).
    /// When set, the transformer validates that all exported ContractIds
    /// exist in the metadata and optionally appends grouped summaries.
    /// The metadata file is NOT required for valuation.
    /// Default: empty (no metadata join).
    /// </summary>
    public string MetadataPath { get; set; } = string.Empty;
}
