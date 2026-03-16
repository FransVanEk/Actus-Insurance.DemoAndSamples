/*
 * PamMonteCarlo50Y Demo — result export transformer.
 *
 * Produces Excel-friendly CSV outputs keyed by ContractId so users can
 * JOIN with their own external contract metadata in Excel / PowerQuery.
 *
 * Files written (controlled by ExportConfig):
 *   contract_summary.csv      — per-contract aggregated stats (always)
 *   portfolio_by_scenario.csv — per-scenario portfolio PV     (always)
 *   runs.csv                  — run dimension                  (always)
 *   fact_results_long.csv     — long-format joinable fact table (optional)
 *   _README.txt               — explains files and join workflow
 *
 * CONTRACT METADATA NOTE
 * ──────────────────────
 * The transformer does NOT export contract details.  Only ContractId
 * (the engine-side key) is written.  Users join against their own
 * external "contracts_metadata.csv" (see samples/metadata/).
 */
using System.Globalization;
using System.Text;

namespace PamMonteCarlo50Y.Reporting;

/// <summary>
/// Transforms raw PV results into Excel-friendly CSV files.
///
/// Call <see cref="Write"/> once per run after the PV matrix has been
/// fetched from the engine.
/// </summary>
public static class ResultExportTransformer
{
    // ── Public entry point ────────────────────────────────────────────────

    /// <summary>
    /// Write reporting CSV files for one run.
    /// </summary>
    /// <param name="outputDir">Directory to write files into.</param>
    /// <param name="runId">Unique run identifier (used in RunId column and file names).</param>
    /// <param name="contractIds">
    ///   ContractId strings, one per contract, in the same order as the
    ///   <paramref name="pvMatrix"/> first dimension.
    /// </param>
    /// <param name="pvMatrix">
    ///   Flat PV array indexed as <c>[contractIndex * numScenarios + scenarioIndex]</c>.
    /// </param>
    /// <param name="numContracts">Number of contracts in this run slice.</param>
    /// <param name="numScenarios">Number of scenarios in this run slice.</param>
    /// <param name="scenarioStart">Absolute scenario start index (for ScenarioId column).</param>
    /// <param name="calcDateIndex">CalcDate month index (written to runs.csv).</param>
    /// <param name="backend">Backend name (cpu / gpu:...) written to runs.csv.</param>
    /// <param name="config">Export configuration.</param>
    public static void Write(
        string     outputDir,
        string     runId,
        string[]   contractIds,
        double[]   pvMatrix,
        int        numContracts,
        int        numScenarios,
        int        scenarioStart,
        int        calcDateIndex,
        string     backend,
        ExportConfig config)
    {
        if (!config.Enabled) return;

        Directory.CreateDirectory(outputDir);

        // 1. Always: portfolio_by_scenario.csv
        WritePortfolioByScenario(outputDir, runId, pvMatrix,
                                 numContracts, numScenarios, scenarioStart);

        // 2. Always: contract_summary.csv
        WriteContractSummary(outputDir, runId, contractIds,
                              pvMatrix, numContracts, numScenarios);

        // 3. Always: runs.csv (append if exists)
        WriteRunsRow(outputDir, runId, calcDateIndex,
                      numContracts, numScenarios, backend);

        // 4. Optional: fact_results_long.csv
        if (config.ExportFact && !config.AggregationOnly)
        {
            WriteFactResultsLong(outputDir, runId, contractIds,
                                  pvMatrix, numContracts, numScenarios,
                                  scenarioStart, config);
        }

        // 5. Optional: metadata-keyed grouped summaries
        if (!string.IsNullOrEmpty(config.MetadataPath) &&
            File.Exists(config.MetadataPath))
        {
            WriteMetadataGroupedSummary(outputDir, runId, contractIds,
                                         pvMatrix, numContracts, numScenarios,
                                         config.MetadataPath);
        }

        // 6. README (once per output dir)
        WriteReadme(outputDir);
    }

    // ── portfolio_by_scenario.csv ─────────────────────────────────────────

    /// <summary>
    /// Writes <c>portfolio_by_scenario.csv</c>: RunId, ScenarioId, PortfolioPV.
    /// PortfolioPV = sum of all contract PVs for each scenario.
    /// </summary>
    public static void WritePortfolioByScenario(
        string   outputDir,
        string   runId,
        double[] pvMatrix,
        int      numContracts,
        int      numScenarios,
        int      scenarioStart)
    {
        string path = Path.Combine(outputDir, $"{runId}_portfolio_by_scenario.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("RunId,ScenarioId,PortfolioPV");
        for (int s = 0; s < numScenarios; s++)
        {
            double sum = 0.0;
            for (int c = 0; c < numContracts; c++)
                sum += pvMatrix[c * numScenarios + s];
            w.WriteLine(string.Join(",",
                EscapeCsv(runId),
                (scenarioStart + s).ToString(CultureInfo.InvariantCulture),
                sum.ToString("F6", CultureInfo.InvariantCulture)));
        }
    }

    // ── contract_summary.csv ─────────────────────────────────────────────

    /// <summary>
    /// Writes <c>contract_summary.csv</c>:
    /// RunId, ContractId, MeanPV, StdPV, P05, P50, P95, VaR99, ES99.
    /// Statistics are computed across all scenarios for each contract.
    /// </summary>
    public static void WriteContractSummary(
        string   outputDir,
        string   runId,
        string[] contractIds,
        double[] pvMatrix,
        int      numContracts,
        int      numScenarios)
    {
        string path = Path.Combine(outputDir, $"{runId}_contract_summary.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("RunId,ContractId,MeanPV,StdPV,P05,P50,P95,VaR99,ES99");

        var buf = new double[numScenarios];
        for (int c = 0; c < numContracts; c++)
        {
            for (int s = 0; s < numScenarios; s++)
                buf[s] = pvMatrix[c * numScenarios + s];

            ComputeStats(buf, out double mean, out double std,
                          out double p05, out double p50, out double p95,
                          out double var99, out double es99);

            string cid = c < contractIds.Length ? contractIds[c] : c.ToString(CultureInfo.InvariantCulture);
            w.WriteLine(string.Join(",",
                EscapeCsv(runId),
                EscapeCsv(cid),
                mean.ToString("F6",  CultureInfo.InvariantCulture),
                std.ToString("F6",   CultureInfo.InvariantCulture),
                p05.ToString("F6",   CultureInfo.InvariantCulture),
                p50.ToString("F6",   CultureInfo.InvariantCulture),
                p95.ToString("F6",   CultureInfo.InvariantCulture),
                var99.ToString("F6", CultureInfo.InvariantCulture),
                es99.ToString("F6",  CultureInfo.InvariantCulture)));
        }
    }

    // ── runs.csv ─────────────────────────────────────────────────────────

    /// <summary>
    /// Appends one row to <c>runs.csv</c> (creates with header if missing):
    /// RunId, CalcDateIndex, ContractCount, ScenarioCount, Backend, Timestamp.
    /// </summary>
    public static void WriteRunsRow(
        string outputDir,
        string runId,
        int    calcDateIndex,
        int    numContracts,
        int    numScenarios,
        string backend)
    {
        string path = Path.Combine(outputDir, "runs.csv");
        bool exists = File.Exists(path);
        using var w = new StreamWriter(path, append: true, Encoding.UTF8);
        if (!exists)
            w.WriteLine("RunId,CalcDateIndex,ContractCount,ScenarioCount,Backend,Timestamp");
        w.WriteLine(string.Join(",",
            EscapeCsv(runId),
            calcDateIndex.ToString(CultureInfo.InvariantCulture),
            numContracts.ToString(CultureInfo.InvariantCulture),
            numScenarios.ToString(CultureInfo.InvariantCulture),
            EscapeCsv(backend),
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
    }

    // ── fact_results_long.csv ─────────────────────────────────────────────

    /// <summary>
    /// Writes <c>fact_results_long.csv</c> (long-format, joinable):
    /// RunId, ContractId, ScenarioId, Measure, Value.
    ///
    /// Applies contract and scenario sampling from <see cref="ExportConfig"/>
    /// to keep file size manageable.
    /// </summary>
    public static void WriteFactResultsLong(
        string       outputDir,
        string       runId,
        string[]     contractIds,
        double[]     pvMatrix,
        int          numContracts,
        int          numScenarios,
        int          scenarioStart,
        ExportConfig config)
    {
        // Resolve contract indices to export
        int[] contractIndices = SampleContractIndices(
            numContracts, config.ContractSampleSize, config.ContractSampleSeed);

        // Resolve scenario indices to export
        int scenarioLimit = config.ScenarioSampleSize > 0
            ? Math.Min(config.ScenarioSampleSize, numScenarios)
            : numScenarios;

        string path = Path.Combine(outputDir, $"{runId}_fact_results_long.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("RunId,ContractId,ScenarioId,Measure,Value");

        foreach (int c in contractIndices)
        {
            string cid = c < contractIds.Length
                ? contractIds[c]
                : c.ToString(CultureInfo.InvariantCulture);

            for (int s = 0; s < scenarioLimit; s++)
            {
                double pv = pvMatrix[c * numScenarios + s];
                w.WriteLine(string.Join(",",
                    EscapeCsv(runId),
                    EscapeCsv(cid),
                    (scenarioStart + s).ToString(CultureInfo.InvariantCulture),
                    "PV",
                    pv.ToString("F6", CultureInfo.InvariantCulture)));
            }
        }
    }

    // ── Metadata-keyed grouped summary ───────────────────────────────────

    /// <summary>
    /// Reads the external metadata CSV and writes
    /// <c>{runId}_grouped_by_segment.csv</c> and
    /// <c>{runId}_grouped_by_region.csv</c> if those columns exist.
    /// </summary>
    public static void WriteMetadataGroupedSummary(
        string   outputDir,
        string   runId,
        string[] contractIds,
        double[] pvMatrix,
        int      numContracts,
        int      numScenarios,
        string   metadataPath)
    {
        // Load metadata → Dictionary<ContractId, row>
        var meta = LoadMetadata(metadataPath);
        if (meta.Count == 0) return;

        // Build per-contract mean PV
        var meanPvByContractId = new Dictionary<string, double>(numContracts);
        var buf = new double[numScenarios];
        for (int c = 0; c < numContracts; c++)
        {
            string cid = c < contractIds.Length ? contractIds[c] : c.ToString(CultureInfo.InvariantCulture);
            for (int s = 0; s < numScenarios; s++)
                buf[s] = pvMatrix[c * numScenarios + s];
            meanPvByContractId[cid] = buf.Average();
        }

        // Identify dimension columns (all except ContractId)
        string[] dimCols = meta.Count > 0
            ? meta.First().Value.Keys
                  .Where(k => !k.Equals("ContractId", StringComparison.OrdinalIgnoreCase))
                  .ToArray()
            : Array.Empty<string>();

        foreach (string dim in dimCols)
        {
            var grouped = new Dictionary<string, (double sumPv, int count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in meanPvByContractId)
            {
                if (!meta.TryGetValue(kv.Key, out var row)) continue;
                if (!row.TryGetValue(dim, out string? dimVal) || dimVal == null) continue;
                if (!grouped.TryGetValue(dimVal, out var acc))
                    acc = (0.0, 0);
                grouped[dimVal] = (acc.sumPv + kv.Value, acc.count + 1);
            }
            if (grouped.Count == 0) continue;

            string dimFile = Path.Combine(outputDir,
                $"{runId}_grouped_by_{dim.ToLowerInvariant()}.csv");
            using var w = new StreamWriter(dimFile, false, Encoding.UTF8);
            w.WriteLine($"RunId,{EscapeCsv(dim)},MeanPV,ContractCount");
            foreach (var kv in grouped.OrderBy(x => x.Key))
            {
                double groupMean = kv.Value.count > 0
                    ? kv.Value.sumPv / kv.Value.count : 0.0;
                w.WriteLine(string.Join(",",
                    EscapeCsv(runId),
                    EscapeCsv(kv.Key),
                    groupMean.ToString("F6", CultureInfo.InvariantCulture),
                    kv.Value.count.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    // ── _README.txt ───────────────────────────────────────────────────────

    /// <summary>
    /// Writes a <c>_README.txt</c> in the output directory explaining the
    /// files and how to join them in Excel / PowerQuery.
    /// Overwrites existing file so it stays up-to-date.
    /// </summary>
    public static void WriteReadme(string outputDir)
    {
        string path = Path.Combine(outputDir, "_README.txt");
        File.WriteAllText(path, ReadmeContent, Encoding.UTF8);
    }

    private const string ReadmeContent = @"
PAM Monte Carlo — Reporting Output Files
=========================================

Files produced by the Result Export Transformer
------------------------------------------------

  *_portfolio_by_scenario.csv
      Columns: RunId, ScenarioId, PortfolioPV
      One row per scenario.  Use as a fact table for portfolio-level analysis.

  *_contract_summary.csv
      Columns: RunId, ContractId, MeanPV, StdPV, P05, P50, P95, VaR99, ES99
      One row per contract.  Statistics computed across all scenarios.
      JOIN KEY → ContractId links to your external contracts_metadata file.

  *_fact_results_long.csv  (optional, written when --export-fact is set)
      Columns: RunId, ContractId, ScenarioId, Measure, Value
      Long-format fact table.  JOIN KEY → ContractId.
      May be a sampled subset; see --contract-sample-size / --scenario-range.

  runs.csv
      Columns: RunId, CalcDateIndex, ContractCount, ScenarioCount, Backend, Timestamp
      One row per run.  Dimension table for slicing by run in pivot tables.

  *_grouped_by_<dimension>.csv  (optional, written when --metadata is supplied)
      Columns: RunId, <dimension>, MeanPV, ContractCount
      Pre-aggregated summaries grouped by each metadata dimension.


How to JOIN in Excel / PowerQuery
-----------------------------------

Step 1 — Load the engine outputs
  In Excel: Data → Get Data → From Text/CSV
  Load:  contract_summary.csv
  Load:  your external contracts_metadata.csv
         (columns: ContractId, Segment, Region, ProductLine, ...)

Step 2 — Merge on ContractId
  In PowerQuery:
    Home → Merge Queries
    Left table:  contract_summary   key = ContractId
    Right table: contracts_metadata  key = ContractId
    Join kind:   Left Outer

Step 3 — Expand metadata columns
  Click the expand icon on the merged column.
  Select: Segment, Region, ProductLine, Currency, Broker, etc.

Step 4 — Build pivot tables
  Insert → PivotTable from the merged query.
  Examples:
    Mean PV by Region      → Rows: Region,    Values: MeanPV (Average)
    VaR99 by Segment       → Rows: Segment,   Values: VaR99  (Sum)
    Portfolio P95 by Run   → Rows: RunId,      Values: P95    (Average)

Step 5 — Load the fact table (for scenario drill-down)
  Load:  fact_results_long.csv
  Merge: ContractId → contracts_metadata (as above)
  Merge: RunId      → runs.csv
  PivotTable: filter by Scenario + Dimension for full drill-down.


Keeping exports small
----------------------
  --aggregation-only          Skip fact_results_long.csv entirely.
  --contract-sample-size N    Limit fact table to N contracts (default 200).
  --contract-sample-seed N    Reproducible random contract sample (0 = first N).
  --scenario-sample-size N    Limit fact table to N scenarios (default 200).
  (contract_summary.csv and portfolio_by_scenario.csv always contain all data.)


Sample external metadata
-------------------------
  See: samples/metadata/contracts_metadata_sample.csv
  Columns: ContractId, Segment, Region, ProductLine, Broker, Underwriter, Currency
  Keep ContractId values exactly as generated by the engine (e.g. PAM_000000).
";

    // ── Statistics helpers ────────────────────────────────────────────────

    /// <summary>
    /// Compute descriptive statistics over a PV sample (in-place sort of
    /// <paramref name="values"/> for efficiency — do not pass the original array).
    /// </summary>
    public static void ComputeStats(
        double[]   values,
        out double mean,
        out double std,
        out double p05,
        out double p50,
        out double p95,
        out double var99,
        out double es99)
    {
        if (values.Length == 0)
        {
            mean = std = p05 = p50 = p95 = var99 = es99 = double.NaN;
            return;
        }

        double sum = 0.0, sum2 = 0.0;
        foreach (double v in values) { sum += v; sum2 += v * v; }
        double n = values.Length;
        mean = sum / n;
        double variance = sum2 / n - mean * mean;
        std = Math.Sqrt(Math.Max(0.0, variance));

        var sorted = (double[])values.Clone();
        Array.Sort(sorted);

        p05  = Percentile(sorted, 0.05);
        p50  = Percentile(sorted, 0.50);
        p95  = Percentile(sorted, 0.95);

        // VaR99 = loss exceeded with 1% probability (worst 1%)
        double p01 = Percentile(sorted, 0.01);
        var99 = -p01;

        // ES99 = expected shortfall (mean of worst 1%)
        int cutoff = (int)Math.Max(1, Math.Floor(0.01 * n));
        double esum = 0.0;
        for (int i = 0; i < cutoff; i++) esum += sorted[i];
        es99 = -(esum / cutoff);
    }

    private static double Percentile(double[] sorted, double p)
    {
        double idx = p * (sorted.Length - 1);
        int    lo  = (int)Math.Floor(idx);
        int    hi  = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (idx - lo) * (sorted[hi] - sorted[lo]);
    }

    // ── Contract sampling ────────────────────────────────────────────────

    /// <summary>
    /// Return the contract indices to include in the fact table.
    /// </summary>
    public static int[] SampleContractIndices(
        int   numContracts,
        int   sampleSize,
        ulong seed)
    {
        int limit = sampleSize <= 0
            ? numContracts
            : Math.Min(sampleSize, numContracts);

        if (limit >= numContracts)
            return Enumerable.Range(0, numContracts).ToArray();

        if (seed == 0UL)
        {
            // Sequential (first N)
            return Enumerable.Range(0, limit).ToArray();
        }

        // Random sample without replacement using Fisher-Yates partial shuffle
        var indices = Enumerable.Range(0, numContracts).ToArray();
        ulong state = seed;
        for (int i = 0; i < limit; i++)
        {
            int j = i + (int)(XorShift64(ref state) % (ulong)(numContracts - i));
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }
        var result = new int[limit];
        Array.Copy(indices, result, limit);
        Array.Sort(result);   // sort for deterministic CSV row order
        return result;
    }

    private static ulong XorShift64(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    // ── Metadata loader ──────────────────────────────────────────────────

    /// <summary>
    /// Loads a CSV file into a dictionary keyed by the ContractId column value.
    /// Each value is a dictionary of column name → raw string value.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> LoadMetadata(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            using var r = new StreamReader(path, Encoding.UTF8);
            string? header = r.ReadLine();
            if (header == null) return result;

            string[] cols = SplitCsvLine(header);
            int cidCol = Array.FindIndex(cols,
                c => c.Equals("ContractId", StringComparison.OrdinalIgnoreCase));
            if (cidCol < 0) return result;

            string? line;
            while ((line = r.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] vals = SplitCsvLine(line);
                if (cidCol >= vals.Length) continue;
                string cid = vals[cidCol].Trim('"');
                if (string.IsNullOrEmpty(cid)) continue;

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cols.Length; i++)
                    row[cols[i].Trim('"')] = i < vals.Length ? vals[i].Trim('"') : string.Empty;
                result[cid] = row;
            }
        }
        catch (IOException) { /* metadata is optional; ignore read errors */ }
        return result;
    }

    // ── CSV helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// RFC 4180-compliant CSV field quoting: wrap in double-quotes if the
    /// value contains comma, double-quote, or newline; escape inner quotes.
    /// </summary>
    public static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Naïve single-line CSV splitter (no embedded newlines).</summary>
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;
        while (i <= line.Length)
        {
            if (i < line.Length && line[i] == '"')
            {
                i++; // skip opening quote
                var sb = new StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        i++;
                        if (i < line.Length && line[i] == '"') { sb.Append('"'); i++; }
                        else break;
                    }
                    else { sb.Append(line[i++]); }
                }
                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                int comma = line.IndexOf(',', i);
                if (comma < 0)
                {
                    fields.Add(line.Substring(i));
                    break;
                }
                fields.Add(line.Substring(i, comma - i));
                i = comma + 1;
            }
        }
        return fields.ToArray();
    }
}
