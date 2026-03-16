/*
 * ScenarioCpuGpuCalcDateDemo — CSV output sinks.
 *
 * Writes one CSV file per experiment plus portfolio and scenario tables.
 * All files use invariant-culture formatting and UTF-8 encoding.
 */
using System.Globalization;
using System.Text;

namespace ScenarioCpuGpuCalcDateDemo;

/// <summary>
/// Writes all demo experiment outputs to CSV files.
/// </summary>
public static class CsvSink
{
    // ── Portfolio ─────────────────────────────────────────────────────────

    /// <summary>
    /// Writes <c>portfolio.csv</c>: one row per contract describing the key
    /// terms used in the valuation.
    /// </summary>
    public static void WritePortfolio(string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "portfolio.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine(
            "ContractId,InitialExchangeDate,MaturityDate,NotionalPrincipal," +
            "NominalInterestRate,PaymentCycle,RateSpread,IsFloating,StartMonth");

        var baseDate   = DemoPortfolio.BaseDate;
        var contracts  = DemoPortfolio.Build();
        foreach (var c in contracts)
        {
            int startMonth = (int)Math.Round(
                (c.InitialExchangeDate - baseDate).TotalDays / (365.25 / 12.0));
            bool isFloating = !string.IsNullOrEmpty(c.MarketObjectCodeOfRateReset);

            w.WriteLine(string.Join(",",
                c.ContractID ?? string.Empty,
                c.InitialExchangeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                c.MaturityDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                c.NotionalPrincipal.ToString("F2", CultureInfo.InvariantCulture),
                c.NominalInterestRate.ToString("F4", CultureInfo.InvariantCulture),
                c.CycleOfInterestPayment ?? string.Empty,
                c.RateSpread.ToString("F4", CultureInfo.InvariantCulture),
                isFloating ? "true" : "false",
                startMonth.ToString(CultureInfo.InvariantCulture)));
        }
    }

    // ── Scenarios ─────────────────────────────────────────────────────────

    /// <summary>
    /// Writes <c>scenarios.csv</c>: one row per scenario describing the
    /// rate level and the prior rate used in Experiment 3.
    /// </summary>
    public static void WriteScenarios(string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "scenarios.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine("ScenarioId,Name,AfterRate,PriorRate,NumMonths");

        for (int s = 0; s < ScenarioBuilder.NumScenarios; s++)
        {
            w.WriteLine(string.Join(",",
                s.ToString(CultureInfo.InvariantCulture),
                ScenarioBuilder.Names[s],
                ScenarioBuilder.AfterRates[s].ToString("F4", CultureInfo.InvariantCulture),
                ScenarioBuilder.PriorRate.ToString("F4", CultureInfo.InvariantCulture),
                ScenarioBuilder.NumMonths.ToString(CultureInfo.InvariantCulture)));
        }
    }

    // ── Experiment 1: CPU vs GPU ──────────────────────────────────────────

    /// <summary>
    /// Writes <c>exp1_cpu_vs_gpu.csv</c>: per (contract, scenario) pair with
    /// CPU PV, GPU PV, absolute delta, and a tolerance flag.
    /// </summary>
    public static void WriteExp1(
        string       dir,
        string[]     contractIds,
        double[]     cpuPvFlat,      // [c * numScenarios + s]
        double[]     gpuPvFlat,
        int          numContracts,
        int          numScenarios,
        double       tolerance)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "exp1_cpu_vs_gpu.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine(
            "ContractId,ScenarioId,ScenarioName,CPU_PV,GPU_PV,AbsDelta,WithinTolerance");

        for (int c = 0; c < numContracts; c++)
        for (int s = 0; s < numScenarios; s++)
        {
            int    idx   = c * numScenarios + s;
            double cpuPv = cpuPvFlat[idx];
            double gpuPv = gpuPvFlat[idx];
            double delta = Math.Abs(cpuPv - gpuPv);
            bool   ok    = delta <= tolerance;
            w.WriteLine(string.Join(",",
                contractIds[c],
                s.ToString(CultureInfo.InvariantCulture),
                ScenarioBuilder.Names[s],
                cpuPv.ToString("F6", CultureInfo.InvariantCulture),
                gpuPv.ToString("F6", CultureInfo.InvariantCulture),
                delta.ToString("G4", CultureInfo.InvariantCulture),
                ok ? "true" : "false"));
        }
    }

    // ── Experiment 2: Scenario impact ─────────────────────────────────────

    /// <summary>
    /// Writes <c>exp2_scenario_impact.csv</c>: one row per contract with the
    /// PV under each scenario and the delta between Low and High scenarios.
    /// </summary>
    public static void WriteExp2(
        string   dir,
        string[] contractIds,
        double[] pvFlat,        // [c * numScenarios + s]
        int      numContracts,
        int      numScenarios)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "exp2_scenario_impact.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);

        // Header: per-scenario PV columns + delta (Low vs High)
        var headerParts = new List<string> { "ContractId" };
        for (int s = 0; s < numScenarios; s++)
            headerParts.Add($"PV_{ScenarioBuilder.Names[s].Replace(" ", "_").Replace("(", "").Replace(")", "").Replace("%", "pct")}");
        headerParts.AddRange(new[] { "Delta_Low_to_High", "DeltaPct_Low_to_High", "Attribution" });
        w.WriteLine(string.Join(",", headerParts));

        for (int c = 0; c < numContracts; c++)
        {
            var row = new List<string> { contractIds[c] };
            double pvLow  = pvFlat[c * numScenarios + 0];
            double pvHigh = pvFlat[c * numScenarios + (numScenarios - 1)];
            for (int s = 0; s < numScenarios; s++)
                row.Add(pvFlat[c * numScenarios + s].ToString("F6", CultureInfo.InvariantCulture));

            double delta    = pvHigh - pvLow;
            double deltaPct = pvLow != 0.0 ? delta / Math.Abs(pvLow) * 100.0 : 0.0;
            string dir_     = delta < 0.0
                ? "Higher rates → lower discount factors → lower PV"
                : "Higher rates → higher interest income → higher PV (floating)";
            row.Add(delta.ToString("F6", CultureInfo.InvariantCulture));
            row.Add(deltaPct.ToString("F2", CultureInfo.InvariantCulture));
            row.Add($"\"{dir_}\"");
            w.WriteLine(string.Join(",", row));
        }
    }

    // ── Experiment 3: CalcDate impact ─────────────────────────────────────

    /// <summary>
    /// Writes <c>exp3_calcdate_impact.csv</c>: per (contract, scenario) with
    /// PV at calcDateIndex=0 vs calcDateIndex=12, and the delta attributed
    /// to the prior/after boundary shift.
    /// </summary>
    public static void WriteExp3(
        string   dir,
        string[] contractIds,
        double[] pvFlatCd0,     // calcDateIndex = 0  [c * numScenarios + s]
        double[] pvFlatCd12,    // calcDateIndex = 12
        int      numContracts,
        int      numScenarios,
        int      calcDateIndex)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "exp3_calcdate_impact.csv");
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine(
            "ContractId,ScenarioId,ScenarioName," +
            "PV_CalcDate0,PV_CalcDate12," +
            "Delta,DeltaPct,Attribution");

        for (int c = 0; c < numContracts; c++)
        for (int s = 0; s < numScenarios; s++)
        {
            int    idx    = c * numScenarios + s;
            double pv0    = pvFlatCd0[idx];
            double pv12   = pvFlatCd12[idx];
            double delta  = pv12 - pv0;
            double dpct   = pv0 != 0.0 ? delta / Math.Abs(pv0) * 100.0 : 0.0;

            double afterRate = ScenarioBuilder.AfterRates[s];
            string direction = afterRate < ScenarioBuilder.PriorRate
                ? $"Prior {ScenarioBuilder.PriorRate:P0} > After {afterRate:P1}: heavier discounting in [0,{calcDateIndex}] → lower PV"
                : $"Prior {ScenarioBuilder.PriorRate:P0} < After {afterRate:P1}: lighter discounting in [0,{calcDateIndex}] → higher PV";

            w.WriteLine(string.Join(",",
                contractIds[c],
                s.ToString(CultureInfo.InvariantCulture),
                ScenarioBuilder.Names[s],
                pv0.ToString("F6", CultureInfo.InvariantCulture),
                pv12.ToString("F6", CultureInfo.InvariantCulture),
                delta.ToString("F6", CultureInfo.InvariantCulture),
                dpct.ToString("F2", CultureInfo.InvariantCulture),
                $"\"{direction}\""));
        }
    }
}
