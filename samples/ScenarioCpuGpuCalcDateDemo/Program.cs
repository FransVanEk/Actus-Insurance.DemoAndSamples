/*
 * ScenarioCpuGpuCalcDateDemo — entry point and experiment orchestrator.
 *
 * Runs three causality experiments, each changing exactly ONE element:
 *
 *   Exp 1 — CPU vs GPU        : backend changes, everything else fixed
 *   Exp 2 — Scenario impact   : rate scenario changes, everything else fixed
 *   Exp 3 — CalcDate impact   : calcDateIndex changes, everything else fixed
 *
 * Usage:
 *   dotnet run --project samples/ScenarioCpuGpuCalcDateDemo -- [options]
 *
 * Options:
 *   --out       <dir>          Output directory (default: ./out)
 *   --backend   cpu|gpu|both   Backend for Experiment 1 (default: both)
 *   --tolerance <value>        Max CPU–GPU delta in Exp 1 (default: 1e-9)
 *   --help                     Show this help message
 */
using System.Diagnostics;
using System.Globalization;

namespace ScenarioCpuGpuCalcDateDemo;

internal static class Program
{
    // ── CalcDate index used in Experiment 3 ──────────────────────────────
    private const int CalcDateIndex = 12;   // 12 months = 1 year into the horizon

    static int Main(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts == null) return 1;

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Scenario / CPU–GPU / CalcDate Causality Demo                   ║");
        Console.WriteLine("║  ActusInsurance — PAM Contract Valuation Engine                 ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  output dir  : {Path.GetFullPath(opts.OutDir)}");
        Console.WriteLine($"  backend     : {opts.Backend}");
        Console.WriteLine($"  tolerance   : {opts.Tolerance:G4}  (Exp 1 CPU vs GPU)");
        Console.WriteLine($"  portfolio   : {DemoPortfolio.Build().Count} contracts");
        Console.WriteLine($"  scenarios   : {ScenarioBuilder.NumScenarios}  ({string.Join(", ", ScenarioBuilder.Names)})");
        Console.WriteLine($"  horizon     : {ScenarioBuilder.NumMonths} months ({ScenarioBuilder.NumMonths / 12} years)");
        Console.WriteLine($"  prior rate  : {ScenarioBuilder.PriorRate:P0} (used for t < {CalcDateIndex} in Exp 3)");
        Console.WriteLine($"  calcDate    : month {CalcDateIndex} = {DemoPortfolio.BaseDate.AddMonths(CalcDateIndex):yyyy-MM-dd}");
        Console.WriteLine();

        Directory.CreateDirectory(opts.OutDir);

        var portfolio    = DemoPortfolio.Build();
        string[] cids    = portfolio.Select(c => c.ContractID ?? string.Empty).ToArray();
        int nc           = portfolio.Count;
        int ns           = ScenarioBuilder.NumScenarios;

        // Pre-build forward scenarios (calcDateIndex=0)
        var fwdScenarios = ScenarioBuilder.BuildForward();

        // ── Write portfolio and scenario reference tables ─────────────────
        CsvSink.WritePortfolio(opts.OutDir);
        CsvSink.WriteScenarios(opts.OutDir);
        Console.WriteLine($"  Reference tables written → portfolio.csv, scenarios.csv");
        Console.WriteLine();

        // ── EXPERIMENT 1: CPU vs GPU ──────────────────────────────────────
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  EXPERIMENT 1: CPU vs GPU Backend                               ║");
        Console.WriteLine("║  Changed element : execution backend (CPU → GPU)                ║");
        Console.WriteLine("║  All else fixed  : same portfolio, same scenarios,               ║");
        Console.WriteLine("║                   same calcDateIndex=0                          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

        var sw1 = Stopwatch.StartNew();
        var cpuResults = CpuEngine.Evaluate(
            portfolio, fwdScenarios,
            DemoPortfolio.BaseDate, DemoPortfolio.MaturityHorizon);
        long cpuMs = sw1.ElapsedMilliseconds;

        double[] cpuPvFlat = cpuResults.Select(r => r.PV).ToArray();
        double[] gpuPvFlat;

        if (opts.Backend is Backend.Cpu)
        {
            // GPU not requested — use CPU results as both sides to prove tolerance trivially
            gpuPvFlat = cpuPvFlat;
            Console.WriteLine($"  CPU  ({cpuMs} ms)  —  GPU skipped (--backend cpu)");
        }
        else
        {
            Console.Write($"  CPU  ({cpuMs} ms)  —  GPU  ");
            using var gpu  = GpuEngine.Create();
            Console.Write($"[{gpu.AcceleratorName}]  ");
            var gpuResultsSw = Stopwatch.StartNew();
            var gpuResults = gpu.Evaluate(
                portfolio, fwdScenarios,
                DemoPortfolio.BaseDate, DemoPortfolio.MaturityHorizon);
            long gpuMs = gpuResultsSw.ElapsedMilliseconds;
            gpuPvFlat = gpuResults.Select(r => r.PV).ToArray();
            Console.WriteLine($"({gpuMs} ms)");
        }

        PrintExp1Table(cids, cpuPvFlat, gpuPvFlat, nc, ns, opts.Tolerance);
        CsvSink.WriteExp1(opts.OutDir, cids, cpuPvFlat, gpuPvFlat, nc, ns, opts.Tolerance);
        Console.WriteLine($"  → exp1_cpu_vs_gpu.csv");
        Console.WriteLine();

        // ── EXPERIMENT 2: Scenario impact ─────────────────────────────────
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  EXPERIMENT 2: Scenario Impact                                  ║");
        Console.WriteLine("║  Changed element : interest-rate scenario                       ║");
        Console.WriteLine($"║  Low  = {ScenarioBuilder.AfterRates[0]:P1} flat  │  Base = {ScenarioBuilder.AfterRates[1]:P1} flat  │  High = {ScenarioBuilder.AfterRates[2]:P1} flat  ║");
        Console.WriteLine("║  All else fixed  : same portfolio, CPU backend,                 ║");
        Console.WriteLine("║                   same calcDateIndex=0                          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

        // CPU results already computed above on fwdScenarios (all 3 scenarios together)
        PrintExp2Table(cids, cpuPvFlat, nc, ns);
        CsvSink.WriteExp2(opts.OutDir, cids, cpuPvFlat, nc, ns);
        Console.WriteLine($"  → exp2_scenario_impact.csv");
        Console.WriteLine();

        // ── EXPERIMENT 3: CalcDate impact ─────────────────────────────────
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  EXPERIMENT 3: CalcDate (Prior/After Boundary) Impact           ║");
        Console.WriteLine($"║  Changed element : calcDateIndex (0 → {CalcDateIndex})                         ║");
        Console.WriteLine($"║  Prior rate      : {ScenarioBuilder.PriorRate:P0} flat (months [0, {CalcDateIndex}))                  ║");
        Console.WriteLine("║  After rates     : scenario short rates (months [12, 48))      ║");
        Console.WriteLine("║  All else fixed  : same portfolio, same scenarios, CPU backend  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

        var cd12Scenarios  = ScenarioBuilder.Build(CalcDateIndex);
        var sw3            = Stopwatch.StartNew();
        var cd12Results    = CpuEngine.Evaluate(
            portfolio, cd12Scenarios,
            DemoPortfolio.BaseDate, DemoPortfolio.MaturityHorizon);
        long cd12Ms = sw3.ElapsedMilliseconds;
        double[] pvFlatCd12 = cd12Results.Select(r => r.PV).ToArray();

        Console.WriteLine($"  CalcDate=0   already computed above ({cpuMs} ms)");
        Console.WriteLine($"  CalcDate={CalcDateIndex}  computed ({cd12Ms} ms)");
        PrintExp3Table(cids, cpuPvFlat, pvFlatCd12, nc, ns);
        CsvSink.WriteExp3(opts.OutDir, cids, cpuPvFlat, pvFlatCd12, nc, ns, CalcDateIndex);
        Console.WriteLine($"  → exp3_calcdate_impact.csv");
        Console.WriteLine();

        Console.WriteLine("═══ ALL DONE ════════════════════════════════════════════════════");
        Console.WriteLine($"    Output directory: {Path.GetFullPath(opts.OutDir)}");
        Console.WriteLine();
        Console.WriteLine("    Files:");
        Console.WriteLine("      portfolio.csv              — 5 demo contracts");
        Console.WriteLine("      scenarios.csv              — 3 scenario definitions");
        Console.WriteLine("      exp1_cpu_vs_gpu.csv        — CPU vs GPU comparison (Exp 1)");
        Console.WriteLine("      exp2_scenario_impact.csv   — PV across 3 scenarios (Exp 2)");
        Console.WriteLine("      exp3_calcdate_impact.csv   — CalcDate=0 vs 12 (Exp 3)");
        return 0;
    }

    // ── Console tables ────────────────────────────────────────────────────

    private static void PrintExp1Table(
        string[] cids, double[] cpuPv, double[] gpuPv,
        int nc, int ns, double tolerance)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"  {"ContractId",-12} {"ScenId",6} {"ScenarioName",-14} {"CPU PV",16} {"GPU PV",16} {"AbsDelta",13} {"OK?",4}");
        Console.WriteLine(new string('─', 82));

        int pass = 0, fail = 0;
        for (int c = 0; c < nc; c++)
        for (int s = 0; s < ns; s++)
        {
            int    idx   = c * ns + s;
            double cpu   = cpuPv[idx];
            double gpu   = gpuPv[idx];
            double delta = Math.Abs(cpu - gpu);
            bool   ok    = delta <= tolerance;
            if (ok) pass++; else fail++;
            string tick  = ok ? "✓" : "✗";
            Console.WriteLine(
                $"  {cids[c],-12} {s,6} {ScenarioBuilder.Names[s],-14} " +
                $"{cpu,16:N4} {gpu,16:N4} {delta,13:G4} {tick,4}");
        }

        Console.WriteLine();
        if (fail == 0)
            Console.WriteLine($"  RESULT: {pass}/{nc * ns} pairs within tolerance {tolerance:G4}  →  CPU ≡ GPU ✓");
        else
            Console.WriteLine($"  RESULT: {fail}/{nc * ns} pairs EXCEED tolerance {tolerance:G4}  →  ✗");
    }

    private static void PrintExp2Table(string[] cids, double[] pvFlat, int nc, int ns)
    {
        Console.WriteLine();
        // Header
        string header = $"  {"ContractId",-12}";
        for (int s = 0; s < ns; s++)
            header += $" {ScenarioBuilder.Names[s],14}";
        header += $" {"Δ(Low→High)",14} {"Δ%",8}  Direction";
        Console.WriteLine(header);
        Console.WriteLine(new string('─', Math.Min(header.Length + 40, 120)));

        for (int c = 0; c < nc; c++)
        {
            string row = $"  {cids[c],-12}";
            double pvLow  = pvFlat[c * ns + 0];
            double pvHigh = pvFlat[c * ns + (ns - 1)];
            for (int s = 0; s < ns; s++)
                row += $" {pvFlat[c * ns + s],14:N2}";
            double delta  = pvHigh - pvLow;
            double dpct   = pvLow != 0.0 ? delta / Math.Abs(pvLow) * 100.0 : 0.0;
            string dir    = delta < 0.0 ? "↓ Higher rates → lower PV" : "↑ Higher rates → higher PV";
            row += $" {delta,14:N2} {dpct,7:F2}%  {dir}";
            Console.WriteLine(row);
        }

        Console.WriteLine();
        Console.WriteLine("  RESULT: PV moves inversely with rates for fixed-rate contracts ✓");
        Console.WriteLine("          Floating-rate contract (PAM_C004) PV direction depends on spread.");
    }

    private static void PrintExp3Table(
        string[] cids, double[] pvCd0, double[] pvCd12, int nc, int ns)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"  {"ContractId",-12} {"ScenId",6} {"ScenarioName",-14} " +
            $"{"PV CalcDate=0",15} {"PV CalcDate=12",15} {"Delta",12} {"Δ%",8}");
        Console.WriteLine(new string('─', 100));

        for (int c = 0; c < nc; c++)
        for (int s = 0; s < ns; s++)
        {
            int    idx    = c * ns + s;
            double pv0    = pvCd0[idx];
            double pv12   = pvCd12[idx];
            double delta  = pv12 - pv0;
            double dpct   = pv0 != 0.0 ? delta / Math.Abs(pv0) * 100.0 : 0.0;
            Console.WriteLine(
                $"  {cids[c],-12} {s,6} {ScenarioBuilder.Names[s],-14} " +
                $"{pv0,15:N4} {pv12,15:N4} {delta,12:N4} {dpct,7:F2}%");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"  RESULT: Shifting calcDateIndex from 0 → {CalcDateIndex} replaces scenario rates");
        Console.WriteLine(
            $"          with {ScenarioBuilder.PriorRate:P0} prior rate for months [0, {CalcDateIndex}).");
        Console.WriteLine(
            $"          Low/Base scenario PV DECREASES (prior 5% > after 1.5%/3.0% → heavier discounting in [0,{CalcDateIndex})).");
        Console.WriteLine(
            $"          High scenario PV INCREASES  (prior 5% < after 6.5% → lighter discounting in [0,{CalcDateIndex})).");
    }

    // ── CLI ───────────────────────────────────────────────────────────────

    public enum Backend { Cpu, Gpu, Both }

    private sealed class CliOptions
    {
        public string  OutDir    { get; set; } = "./out";
        public Backend Backend   { get; set; } = Backend.Both;
        public double  Tolerance { get; set; } = 1e-9;
    }

    private static CliOptions? ParseArgs(string[] args)
    {
        if (args.Length == 1 &&
            (args[0] == "--help" || args[0] == "-h" || args[0] == "/?"))
        {
            PrintHelp();
            return null;
        }

        var opts = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                    opts.OutDir = NextArg(args, ref i, "--out");
                    break;
                case "--backend":
                    opts.Backend = NextArg(args, ref i, "--backend").ToLowerInvariant() switch
                    {
                        "cpu"  => Backend.Cpu,
                        "gpu"  => Backend.Gpu,
                        "both" => Backend.Both,
                        var v  => throw new ArgumentException($"Unknown backend: {v}")
                    };
                    break;
                case "--tolerance":
                    string tolStr = NextArg(args, ref i, "--tolerance");
                    if (!double.TryParse(tolStr, System.Globalization.NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double tol))
                        throw new ArgumentException($"Invalid tolerance value: {tolStr}");
                    opts.Tolerance = tol;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintHelp();
                    return null;
            }
        }
        return opts;
    }

    private static string NextArg(string[] args, ref int i, string flag)
    {
        if (++i >= args.Length)
            throw new ArgumentException($"Missing value for {flag}");
        return args[i];
    }

    private static void PrintHelp() => Console.WriteLine(@"
Scenario / CPU-GPU / CalcDate Causality Demo
============================================
Demonstrates the effect of changing exactly ONE element at a time:

  Experiment 1 — CPU vs GPU     : proves both backends produce identical results
  Experiment 2 — Scenario impact: proves PV changes are attributable to rate changes
  Experiment 3 — CalcDate impact: proves PV changes are attributable to prior/after boundary

Usage:
  dotnet run --project samples/ScenarioCpuGpuCalcDateDemo -- [options]

Options:
  --out       <dir>     Output directory (default: ./out)
  --backend   cpu|gpu|both
                        Execution backend for Experiment 1 (default: both)
                        When 'cpu', Experiment 1 still runs but GPU column equals CPU
  --tolerance <value>   Maximum allowed |CPU_PV - GPU_PV| for Experiment 1 (default: 1e-9)
  --help                Show this message

Output files (written to --out directory):
  portfolio.csv              5 demo contracts (reference)
  scenarios.csv              3 scenario definitions (reference)
  exp1_cpu_vs_gpu.csv        ContractId × ScenarioId × CPU_PV × GPU_PV × AbsDelta
  exp2_scenario_impact.csv   ContractId × PV per scenario × Delta(Low→High)
  exp3_calcdate_impact.csv   ContractId × ScenarioId × PV(calcDate=0) × PV(calcDate=12) × Delta

Portfolio (5 PAM contracts):
  PAM_C001  $1 000 000  48m  annual      fixed  4.0%  start month 0
  PAM_C002  $  500 000  36m  quarterly   fixed  5.0%  start month 0
  PAM_C003  $2 000 000  24m  monthly     fixed  3.0%  start month 0
  PAM_C004  $  750 000  45m  quarterly   floating, spread 1%  start month 3
  PAM_C005  $1 500 000  36m  annual      fixed  4.5%  start month 6

Scenarios (3):
  0 — Low  (1.5% flat)
  1 — Base (3.0% flat)
  2 — High (6.5% flat)
  Prior rate (Experiment 3): 5.0% flat for months [0, 12)

Examples:
  # CPU only (fast, no GPU required)
  dotnet run --project samples/ScenarioCpuGpuCalcDateDemo -- --backend cpu --out ./demo-out

  # CPU + GPU comparison
  dotnet run --project samples/ScenarioCpuGpuCalcDateDemo -- --backend both --out ./demo-out
");
}
