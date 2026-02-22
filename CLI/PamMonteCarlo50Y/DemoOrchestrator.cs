using System.Diagnostics;
using PamMonteCarlo50Y.Reporting;
using PamMonteCarlo50Y.Sinks;

using ActusInsurance.Core.Models;
using ActusInsurance.Core.Externals;

namespace PamMonteCarlo50Y;

/// <summary>Execution backend selector.</summary>
public enum Backend { Cpu, Gpu, Both }

/// <summary>
/// Orchestrates a single <see cref="RunRequest"/> against a pre-built
/// portfolio and Vasicek scenario set.
/// </summary>
public sealed class DemoOrchestrator : IDisposable
{
    private readonly List<PamContractTerms> _portfolio;
    private readonly VasicekRateGenerator   _rates;
    private readonly RiskFactorModel        _riskFactors;
    private readonly DateTime               _baseDate;
    private readonly DateTime               _maturityHorizon;
    private readonly string                 _outputDir;
    private readonly VasicekParams          _vasicekParams;
    private readonly ulong                  _seed;
    private readonly GpuPvEngine?           _gpuEngine;
    private readonly ExportConfig           _exportConfig;
    private bool _disposed;

    public DemoOrchestrator(
        List<PamContractTerms> portfolio,
        VasicekRateGenerator   rates,
        RiskFactorModel        riskFactors,
        DateTime               baseDate,
        DateTime               maturityHorizon,
        string                 outputDir,
        VasicekParams          vasicekParams,
        ulong                  seed,
        Backend                backend,
        ExportConfig?          exportConfig = null)
    {
        _portfolio       = portfolio;
        _rates           = rates;
        _riskFactors     = riskFactors;
        _baseDate        = baseDate;
        _maturityHorizon = maturityHorizon;
        _outputDir       = outputDir;
        _vasicekParams   = vasicekParams;
        _seed            = seed;
        _exportConfig    = exportConfig ?? new ExportConfig();

        if (backend is Backend.Gpu or Backend.Both)
            _gpuEngine = GpuPvEngine.CreateDefault();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gpuEngine?.Dispose();
    }

    // ── Public run entry point ────────────────────────────────────────────

    /// <summary>
    /// Execute one <see cref="RunRequest"/> (or both CPU and GPU if backend = Both).
    /// Writes all output files to <see cref="_outputDir"/>.
    /// </summary>
    public void ExecuteRun(RunRequest req, Backend backend)
    {
        // Resolve slicing
        int contractStart = req.ContractStart;
        int contractCount = req.ContractCount <= 0
            ? _portfolio.Count - contractStart
            : Math.Min(req.ContractCount, _portfolio.Count - contractStart);

        int scenarioStart = req.ScenarioStart;
        int scenarioCount = req.ScenarioCount <= 0
            ? _rates.NumScenarios - scenarioStart
            : Math.Min(req.ScenarioCount, _rates.NumScenarios - scenarioStart);

        var contracts = _portfolio.GetRange(contractStart, contractCount);
        int calcDateIndex = req.CalcDateIndex;

        Console.WriteLine();
        Console.WriteLine($"═══ Run [{req.Id}] ═══════════════════════════════════════════════════");
        Console.WriteLine($"    Desc:      {(string.IsNullOrEmpty(req.Description) ? "(none)" : req.Description)}");
        Console.WriteLine($"    Contracts: [{contractStart}, {contractStart + contractCount}) = {contractCount:N0}");
        Console.WriteLine($"    Scenarios: [{scenarioStart}, {scenarioStart + scenarioCount}) = {scenarioCount:N0}");
        Console.WriteLine($"    CalcDate:  month {calcDateIndex} = {_baseDate.AddMonths(calcDateIndex):yyyy-MM-dd}");
        Console.WriteLine($"    Backend:   {backend}");

        if (backend is Backend.Cpu or Backend.Both)
            RunCpu(req.Id, contracts, scenarioStart, scenarioCount, calcDateIndex, req.Description);

        if (backend is Backend.Gpu or Backend.Both)
            RunGpu(req.Id, contracts, scenarioStart, scenarioCount, calcDateIndex, req.Description);
    }

    // ── CPU run ───────────────────────────────────────────────────────────

    private void RunCpu(
        string                          runId,
        List<PamContractTerms>          contracts,
        int scenarioStart, int numScenarios,
        int calcDateIndex, string description)
    {
        string backendId = $"{runId}_cpu";
        var sw = Stopwatch.StartNew();

        // ─── PROVISIONING ─────────────────────────────────────────────────
        var t0 = sw.ElapsedMilliseconds;
        Console.WriteLine($"  [{backendId}] [{Now()}] PROVISIONING started  (contracts={contracts.Count:N0}, scenarios={numScenarios:N0})");
        // (No heavy work here for CPU path; schedules are built lazily in CpuPvEngine)
        var t1 = sw.ElapsedMilliseconds;
        Console.WriteLine($"  [{backendId}] [{Now()}] PROVISIONING done     ({t1 - t0} ms)");

        // ─── CALC ─────────────────────────────────────────────────────────
        Console.WriteLine($"  [{backendId}] [{Now()}] CALC started");
        McPvResult[] pvMatrix = CpuPvEngine.Evaluate(
            contracts, _riskFactors, _rates, _baseDate,
            calcDateIndex, scenarioStart, numScenarios, _maturityHorizon);
        var t2 = sw.ElapsedMilliseconds;
        long calcMs = t2 - t1;
        Console.WriteLine($"  [{backendId}] [{Now()}] CALC done             ({calcMs} ms, {calcMs * 1000.0 / (contracts.Count * numScenarios):F2} µs/contract·scenario)");

        // ─── FETCH ────────────────────────────────────────────────────────
        Console.WriteLine($"  [{backendId}] [{Now()}] FETCH started         (aggregating results)");
        double[] scenarioPvs = AggregatePvByScenario(pvMatrix, contracts.Count, numScenarios, isCpu: true);
        double[] pvFlat      = ExtractPvFlat(pvMatrix, isCpu: true);
        var t3 = sw.ElapsedMilliseconds;
        Console.WriteLine($"  [{backendId}] [{Now()}] FETCH done            ({t3 - t2} ms)");

        // ─── REPORTING ────────────────────────────────────────────────────
        Console.WriteLine($"  [{backendId}] [{Now()}] REPORTING started");
        string[] contractIds = contracts.Select(c => c.ContractID ?? string.Empty).ToArray();
        WriteOutputs(backendId, description, "cpu", contracts.Count, numScenarios,
                     calcDateIndex, pvFlat, scenarioPvs, contractIds,
                     scenarioStart,
                     provisioningMs: t1 - t0, calcMs: calcMs,
                     fetchMs: t3 - t2, totalElapsed: sw.ElapsedMilliseconds);
        var t4 = sw.ElapsedMilliseconds;
        Console.WriteLine($"  [{backendId}] [{Now()}] REPORTING done        ({t4 - t3} ms)");
        Console.WriteLine($"  [{backendId}] [{Now()}] TOTAL                 ({t4} ms)");
    }

    // ── GPU run ───────────────────────────────────────────────────────────

    private void RunGpu(
        string                          runId,
        List<PamContractTerms>          contracts,
        int scenarioStart, int numScenarios,
        int calcDateIndex, string description)
    {
        if (_gpuEngine == null)
        {
            Console.WriteLine("  [GPU] No GPU engine available (engine not initialised).");
            return;
        }

        string backendId = $"{runId}_gpu";
        var sw = Stopwatch.StartNew();

        // ─── PROVISIONING ─────────────────────────────────────────────────
        var t0 = sw.ElapsedMilliseconds;
        Console.WriteLine($"  [{backendId}] [{Now()}] PROVISIONING started  (accelerator={_gpuEngine.AcceleratorName}, contracts={contracts.Count:N0}, scenarios={numScenarios:N0})");
        // Warm-up: a small first call compiles the kernel; reported separately.
        var t1 = sw.ElapsedMilliseconds;
        Console.WriteLine($"  [{backendId}] [{Now()}] PROVISIONING done     ({t1 - t0} ms)");

        // ─── CALC ─────────────────────────────────────────────────────────
        Console.WriteLine($"  [{backendId}] [{Now()}] CALC started          (H2D + kernel + D2H)");
        McPvGpuResult[] gpuResults = _gpuEngine.Evaluate(
            contracts, _rates, _baseDate, calcDateIndex,
            scenarioStart, numScenarios, _maturityHorizon);
        var t2 = sw.ElapsedMilliseconds;
        long calcMs = t2 - t1;
        Console.WriteLine($"  [{backendId}] [{Now()}] CALC done             ({calcMs} ms, {calcMs * 1000.0 / (contracts.Count * numScenarios):F2} µs/contract·scenario)");

        // ─── FETCH ────────────────────────────────────────────────────────
        Console.WriteLine($"  [{backendId}] [{Now()}] FETCH started         (aggregating results)");
        double[] scenarioPvs = AggregatePvByScenario(gpuResults, contracts.Count, numScenarios);
        double[] pvFlat      = ExtractPvFlat(gpuResults);
        var t3 = sw.ElapsedMilliseconds;
        Console.WriteLine($"  [{backendId}] [{Now()}] FETCH done            ({t3 - t2} ms)");

        // ─── REPORTING ────────────────────────────────────────────────────
        Console.WriteLine($"  [{backendId}] [{Now()}] REPORTING started");
        string[] contractIds = contracts.Select(c => c.ContractID ?? string.Empty).ToArray();
        WriteOutputs(backendId, description, $"gpu:{_gpuEngine.AcceleratorName}",
                     contracts.Count, numScenarios, calcDateIndex,
                     pvFlat, scenarioPvs, contractIds,
                     scenarioStart,
                     provisioningMs: t1 - t0, calcMs: calcMs,
                     fetchMs: t3 - t2, totalElapsed: sw.ElapsedMilliseconds);
        var t4 = sw.ElapsedMilliseconds;
        Console.WriteLine($"  [{backendId}] [{Now()}] REPORTING done        ({t4 - t3} ms)");
        Console.WriteLine($"  [{backendId}] [{Now()}] TOTAL                 ({t4} ms)");
    }

    // ── Aggregation helpers ───────────────────────────────────────────────

    private static double[] AggregatePvByScenario(McPvResult[] matrix, int nc, int ns, bool isCpu)
    {
        var out_ = new double[ns];
        for (int s = 0; s < ns; s++)
            for (int c = 0; c < nc; c++)
                out_[s] += matrix[c * ns + s].PV;
        return out_;
    }

    private static double[] AggregatePvByScenario(McPvGpuResult[] matrix, int nc, int ns)
    {
        var out_ = new double[ns];
        for (int s = 0; s < ns; s++)
            for (int c = 0; c < nc; c++)
                out_[s] += matrix[c * ns + s].PV;
        return out_;
    }

    private static double[] ExtractPvFlat(McPvResult[] matrix, bool isCpu)
    {
        var arr = new double[matrix.Length];
        for (int i = 0; i < matrix.Length; i++) arr[i] = matrix[i].PV;
        return arr;
    }

    private static double[] ExtractPvFlat(McPvGpuResult[] matrix)
    {
        var arr = new double[matrix.Length];
        for (int i = 0; i < matrix.Length; i++) arr[i] = matrix[i].PV;
        return arr;
    }

    // ── Output writing ────────────────────────────────────────────────────

    private void WriteOutputs(
        string   runId,
        string   description,
        string   backend,
        int      numContracts,
        int      numScenarios,
        int      calcDateIndex,
        double[] pvFlat,
        double[] scenarioPvs,
        string[] contractIds,
        int      scenarioStart,
        long     provisioningMs,
        long     calcMs,
        long     fetchMs,
        long     totalElapsed)
    {
        Directory.CreateDirectory(_outputDir);

        // CSV outputs (existing sinks)
        PortfolioPvCsvSink.Write(_outputDir, runId, scenarioPvs);
        ContractPvSampleCsvSink.Write(_outputDir, runId, pvFlat, numContracts, numScenarios);

        // Reporting transformer (new Excel-friendly exports)
        ResultExportTransformer.Write(
            _outputDir, runId, contractIds, pvFlat,
            numContracts, numScenarios, scenarioStart,
            calcDateIndex, backend, _exportConfig);

        // Summary JSON
        var summary = new RunSummaryRecord
        {
            RunId        = runId,
            Description  = description,
            Backend      = backend,
            NumContracts = numContracts,
            NumScenarios = numScenarios,
            NumMonths    = _rates.NumMonths,
            CalcDateIndex = calcDateIndex,
            Seed         = _seed,
            Vasicek      = new VasicekParamsRecord
            {
                Kappa = _vasicekParams.Kappa,
                Theta = _vasicekParams.Theta,
                Sigma = _vasicekParams.Sigma,
                R0    = _vasicekParams.R0,
            },
            ProvisioningMs    = provisioningMs,
            CalcMs            = calcMs,
            FetchMs           = fetchMs,
            ReportingMs       = 0,   // filled after
            TotalMs           = totalElapsed,
            CalcMsPerContract = numContracts > 0 ? (double)calcMs / numContracts : 0.0,
        };
        JsonSummarySink.ComputeStats(summary, scenarioPvs);
        JsonSummarySink.Write(_outputDir, summary);

        Console.WriteLine($"    PV mean={summary.PvMean:F2}  stdev={summary.PvStdev:F2}  VaR99={summary.Var99:F2}  ES99={summary.Es99:F2}");
        Console.WriteLine($"    P05={summary.PvP05:F2}  P50={summary.PvP50:F2}  P95={summary.PvP95:F2}  P99={summary.PvP99:F2}");
        Console.WriteLine($"    Files → {_outputDir}/{runId}_*");
    }

    private static string Now() => DateTime.UtcNow.ToString("HH:mm:ss.fff");
}
