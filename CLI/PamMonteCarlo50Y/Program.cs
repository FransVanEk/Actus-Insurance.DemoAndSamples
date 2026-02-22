/*
 * PamMonteCarlo50Y — entry point.
 *
 * Usage:
 *   dotnet run -- [options]
 *
 * Key options:
 *   --backend   cpu|gpu|both       (default: both)
 *   --contracts 10000              (portfolio size)
 *   --scenarios 1000               (MC scenarios)
 *   --months    600                (50 years)
 *   --seed      12345              (deterministic seed)
 *   --calcDateIndex 0              (month index for prior/after boundary)
 *   --contractRange 0:10000        (contract slice)
 *   --scenarioRange 0:1000         (scenario slice)
 *   --runs      runs.json          (optional multi-run JSON file)
 *   --out       ./out              (output directory)
 *   --input     <dir>              (load portfolio+scenarios from input directory)
 *   --export-portfolio true        (write portfolio.csv to output dir for re-use)
 *   --help
 *
 * Run with: dotnet run --project demos/PamMonteCarlo50Y -- --help
 */
using System.Diagnostics;
using ActusInsurance.Core.Externals;
using ActusInsurance.Core.Models;
using PamMonteCarlo50Y.Reporting;
using PamMonteCarlo50Y.Sinks;

namespace PamMonteCarlo50Y;

internal static class Program
{
    static int Main(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts == null) return 1;

        // ── Input-directory mode vs. synthetic generator mode ────────────
        bool fromInputDir = !string.IsNullOrEmpty(opts.InputDir);

        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║      PAM Monte Carlo 50-Year Demo  —  ActusCoreCsharp               ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var totalSw = Stopwatch.StartNew();

        List<PamContractTerms> portfolio;
        VasicekRateGenerator   rates;
        VasicekParams          vasicek;
        RiskFactorModel        rf;
        List<RunRequest>       runs;
        DateTime               baseDate;
        int                    numMonths;

        if (fromInputDir)
        {
            // ── INPUT-DIRECTORY MODE ──────────────────────────────────────
            Console.WriteLine($"[{Now()}] ─── LOADING FROM INPUT DIRECTORY ──────────────────────");
            Console.WriteLine($"  input dir    : {opts.InputDir}");
            Console.WriteLine($"  backend      : {opts.Backend}");
            Console.WriteLine($"  output dir   : {opts.OutDir}");
            Console.WriteLine();

            var bundle = InputDirectoryLoader.Load(opts.InputDir);

            portfolio  = bundle.Portfolio;
            rates      = bundle.Rates;
            vasicek    = bundle.VasicekParams;
            baseDate   = bundle.BaseDate;
            numMonths  = rates.NumMonths;
            runs       = bundle.Runs;

            // Propagate metadata path from input bundle unless overridden via --metadata
            if (string.IsNullOrEmpty(opts.Export.MetadataPath) &&
                !string.IsNullOrEmpty(bundle.MetadataPath))
                opts.Export.MetadataPath = bundle.MetadataPath;

            Console.WriteLine($"[{Now()}]   Loaded {portfolio.Count:N0} contracts from portfolio.csv");
            Console.WriteLine($"[{Now()}]   Loaded {rates.NumScenarios:N0} scenarios × {rates.NumMonths} months from riskfactors");
            Console.WriteLine($"[{Now()}]   Loaded {runs.Count} run(s)");

            rf = new RiskFactorModel();
            rf.AddConstantRate("USD_LIBOR_3M", vasicek.R0);
        }
        else
        {
            // ── SYNTHETIC GENERATOR MODE (existing behaviour) ─────────────
            Console.WriteLine($"  contracts    : {opts.Contracts:N0}");
            Console.WriteLine($"  scenarios    : {opts.Scenarios:N0}");
            Console.WriteLine($"  months       : {opts.Months}  (= {opts.Months / 12} years)");
            Console.WriteLine($"  seed         : {opts.Seed}");
            Console.WriteLine($"  backend      : {opts.Backend}");
            Console.WriteLine($"  calcDateIndex: {opts.CalcDateIndex}");
            Console.WriteLine($"  output dir   : {opts.OutDir}");
            Console.WriteLine();

            // ── 1. Portfolio generation ───────────────────────────────────
            Console.WriteLine($"[{Now()}] ─── PORTFOLIO GENERATION ────────────────────────────────");
            var portParams = new PortfolioParams
            {
                NumContracts = opts.Contracts,
                BaseDate     = new DateTime(2020, 1, 1),
                Seed         = opts.Seed,
            };
            var portfolioSw = Stopwatch.StartNew();
            portfolio = PortfolioGenerator.Generate(portParams);
            Console.WriteLine($"[{Now()}]   Generated {portfolio.Count:N0} contracts in {portfolioSw.ElapsedMilliseconds} ms");

            baseDate  = portParams.BaseDate;
            numMonths = opts.Months;

            // ── 2. MC rate scenario generation (Vasicek) ─────────────────
            Console.WriteLine($"[{Now()}] ─── VASICEK SCENARIO GENERATION ─────────────────────────");
            vasicek = new VasicekParams
            {
                Kappa = 0.15,
                Theta = 0.04,
                Sigma = 0.02,
                R0    = 0.03,
            };
            var ratesSw = Stopwatch.StartNew();
            rates = VasicekRateGenerator.Generate(vasicek, opts.Scenarios, opts.Months, opts.Seed + 1UL);
            Console.WriteLine($"[{Now()}]   Generated {opts.Scenarios:N0} scenarios × {opts.Months} months in {ratesSw.ElapsedMilliseconds} ms");
            Console.WriteLine($"[{Now()}]   Mean short rate at t=0: {rates.MeanRateAtMonth(0):P2}");
            Console.WriteLine($"[{Now()}]   Mean short rate at t=300 (25y): {rates.MeanRateAtMonth(Math.Min(300, opts.Months - 1)):P2}");
            Console.WriteLine($"[{Now()}]   Mean DF at t=120 (10y): {rates.MeanDfAtMonth(Math.Min(120, opts.Months - 1)):F4}");

            // ── 3. Build risk-factor model ────────────────────────────────
            rf = new RiskFactorModel();
            rf.AddConstantRate("USD_LIBOR_3M", vasicek.R0);

            // ── 4. Resolve run requests ───────────────────────────────────
            if (!string.IsNullOrEmpty(opts.RunsFile) && File.Exists(opts.RunsFile))
            {
                runs = new List<RunRequest>(RunRequest.LoadFromJson(opts.RunsFile));
                Console.WriteLine($"[{Now()}]   Loaded {runs.Count} run(s) from {opts.RunsFile}");
            }
            else
            {
                runs = new List<RunRequest>();
                runs.Add(new RunRequest
                {
                    Id            = "run0",
                    Description   = "Full portfolio run",
                    ContractStart = opts.ContractStart,
                    ContractCount = opts.ContractCount,
                    ScenarioStart = opts.ScenarioStart,
                    ScenarioCount = opts.ScenarioCount,
                    CalcDateIndex = opts.CalcDateIndex,
                });
                if (opts.CalcDateIndex == 0 && opts.Months > 60)
                {
                    const int BacktestMaxContracts = 500;
                    const int BacktestMaxScenarios = 100;
                    const int BacktestCalcDateIdx  = 60;
                    runs.Add(new RunRequest
                    {
                        Id            = "run1_backtest",
                        Description   = $"Backtest: calcDateIndex = {BacktestCalcDateIdx} (5y into simulation)",
                        ContractStart = opts.ContractStart,
                        ContractCount = Math.Min(opts.ContractCount > 0 ? opts.ContractCount : opts.Contracts, BacktestMaxContracts),
                        ScenarioStart = opts.ScenarioStart,
                        ScenarioCount = Math.Min(opts.ScenarioCount > 0 ? opts.ScenarioCount : opts.Scenarios, BacktestMaxScenarios),
                        CalcDateIndex = BacktestCalcDateIdx,
                    });
                }
            }
        }

        // ── Portfolio export (opt-in) ─────────────────────────────────────
        if (opts.ExportPortfolio)
        {
            Console.WriteLine($"[{Now()}] ─── EXPORTING PORTFOLIO ──────────────────────────────────");
            Directory.CreateDirectory(opts.OutDir);
            PortfolioExportSink.Write(opts.OutDir, portfolio);
            Console.WriteLine($"[{Now()}]   Written {portfolio.Count:N0} contracts → {Path.GetFullPath(Path.Combine(opts.OutDir, "portfolio.csv"))}");
        }

        // ── Execute all runs ──────────────────────────────────────────────
        Console.WriteLine($"[{Now()}] ─── EXECUTING {runs.Count} RUN(S) ──────────────────────────────");
        var maturityHorizon = baseDate.AddMonths(numMonths + 1);

        using var orchestrator = new DemoOrchestrator(
            portfolio, rates, rf,
            baseDate, maturityHorizon,
            opts.OutDir, vasicek, opts.Seed, opts.Backend, opts.Export);

        foreach (var run in runs)
            orchestrator.ExecuteRun(run, opts.Backend);

        // ── Final summary ─────────────────────────────────────────────────
        totalSw.Stop();
        Console.WriteLine();
        Console.WriteLine($"[{Now()}] ═══ ALL DONE — total wall time: {totalSw.ElapsedMilliseconds:N0} ms ═══");
        Console.WriteLine($"           Output files written to: {Path.GetFullPath(opts.OutDir)}");
        return 0;
    }

    // ── CLI parsing ───────────────────────────────────────────────────────

    private sealed class CliOptions
    {
        public Backend Backend       { get; set; } = Backend.Both;
        public int     Contracts     { get; set; } = 10_000;
        public int     Scenarios     { get; set; } = 1_000;
        public int     Months        { get; set; } = 600;
        public ulong   Seed          { get; set; } = 12345UL;
        public int     CalcDateIndex { get; set; } = 0;
        public int     ContractStart { get; set; } = 0;
        public int     ContractCount { get; set; } = 0;
        public int     ScenarioStart { get; set; } = 0;
        public int     ScenarioCount { get; set; } = 0;
        public string  RunsFile      { get; set; } = string.Empty;
        public string  OutDir        { get; set; } = "./out";

        /// <summary>
        /// Input directory containing portfolio.csv, scenarios/, and optionally
        /// runs.json and contract_metadata.csv.  When set, the synthetic portfolio
        /// and scenario generator are bypassed.
        /// </summary>
        public string  InputDir      { get; set; } = string.Empty;

        /// <summary>
        /// Write the generated (or loaded) portfolio to portfolio.csv in the
        /// output directory so it can be re-used with --input in future runs.
        /// </summary>
        public bool    ExportPortfolio { get; set; } = false;

        // Reporting / export options
        public ExportConfig Export { get; set; } = new ExportConfig();
    }

    private static CliOptions? ParseArgs(string[] args)
    {
        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h" || args[0] == "/?"))
        {
            PrintHelp();
            return null;
        }

        var opts = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--backend":
                    opts.Backend = (args[++i].ToLowerInvariant()) switch
                    {
                        "cpu"  => Backend.Cpu,
                        "gpu"  => Backend.Gpu,
                        "both" => Backend.Both,
                        _ => throw new ArgumentException($"Unknown backend: {args[i]}")
                    };
                    break;
                case "--contracts":    opts.Contracts     = int.Parse(args[++i]); break;
                case "--scenarios":    opts.Scenarios     = int.Parse(args[++i]); break;
                case "--months":       opts.Months        = int.Parse(args[++i]); break;
                case "--seed":         opts.Seed          = ulong.Parse(args[++i]); break;
                case "--calcDateIndex": opts.CalcDateIndex = int.Parse(args[++i]); break;
                case "--out":          opts.OutDir        = args[++i]; break;
                case "--runs":         opts.RunsFile      = args[++i]; break;
                case "--input":        opts.InputDir      = args[++i]; break;
                case "--export-portfolio":
                    opts.ExportPortfolio = ParseBool(args[++i]);
                    break;
                case "--contractRange":
                {
                    var parts = args[++i].Split(':');
                    opts.ContractStart = int.Parse(parts[0]);
                    opts.ContractCount = parts.Length > 1 ? int.Parse(parts[1]) - opts.ContractStart : 0;
                    break;
                }
                case "--scenarioRange":
                {
                    var parts = args[++i].Split(':');
                    opts.ScenarioStart = int.Parse(parts[0]);
                    opts.ScenarioCount = parts.Length > 1 ? int.Parse(parts[1]) - opts.ScenarioStart : 0;
                    break;
                }
                // Reporting flags
                case "--reporting":
                    opts.Export.Enabled = ParseBool(args[++i]);
                    break;
                case "--export-fact":
                    opts.Export.Enabled  = true;
                    opts.Export.ExportFact = ParseBool(args[++i]);
                    break;
                case "--aggregation-only":
                    opts.Export.Enabled        = true;
                    opts.Export.AggregationOnly = ParseBool(args[++i]);
                    break;
                case "--contract-sample-size":
                    opts.Export.Enabled             = true;
                    opts.Export.ContractSampleSize   = int.Parse(args[++i]);
                    break;
                case "--contract-sample-seed":
                    opts.Export.Enabled             = true;
                    opts.Export.ContractSampleSeed   = ulong.Parse(args[++i]);
                    break;
                case "--scenario-sample-size":
                    opts.Export.Enabled             = true;
                    opts.Export.ScenarioSampleSize   = int.Parse(args[++i]);
                    break;
                case "--metadata":
                    opts.Export.Enabled      = true;
                    opts.Export.MetadataPath = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintHelp();
                    return null;
            }
        }
        return opts;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
PAM Monte Carlo 50-Year Demo
============================
Generates a synthetic portfolio of PAM contracts and values them under
Monte Carlo Vasicek interest-rate scenarios.

Usage:
  dotnet run --project demos/PamMonteCarlo50Y -- [options]

Options:
  --backend   cpu|gpu|both    Execution backend (default: both)
  --contracts N               Portfolio size (default: 10000)
  --scenarios N               Number of MC scenarios (default: 1000)
  --months    N               Horizon in months (default: 600 = 50y)
  --seed      N               Deterministic seed (default: 12345)
  --calcDateIndex N           Month index for prior/after boundary (default: 0)
  --contractRange S:E         Contract slice [S, E) (default: all)
  --scenarioRange S:E         Scenario slice [S, E) (default: all)
  --runs      file.json       JSON file with multiple RunRequest objects
  --out       dir             Output directory (default: ./out)

Input-directory mode (bypasses synthetic generator):
  --input     dir             Load portfolio + scenarios from an input directory.
                              Expected layout:
                                <dir>/portfolio.csv
                                <dir>/scenarios/scenario_set.json
                                <dir>/scenarios/riskfactors/interest_rate_after.csv
                                <dir>/runs.json              (optional)
                                <dir>/contract_metadata.csv  (optional)
                              See samples/execution-proof/input/ for an example.
                              See docs/input-output-contract.md for full schema.

Portfolio export (works in both modes):
  --export-portfolio true     Write generated/loaded portfolio to portfolio.csv
                              in the output directory.  Use this to capture a
                              synthetic portfolio so you can re-run it with --input.

Reporting / export options (Excel-friendly CSV outputs):
  --reporting true|false          Enable reporting transformer (default: false)
  --export-fact true|false        Also write fact_results_long.csv (default: false)
  --aggregation-only true|false   Skip fact table; write only aggregates (default: false)
  --contract-sample-size N        Max contracts in fact table (default: 200)
  --contract-sample-seed N        Seed for random contract sample; 0=first N (default: 0)
  --scenario-sample-size N        Max scenarios in fact table (default: 200)
  --metadata path/to/meta.csv     External contracts metadata CSV for grouped summaries

  --help                      Show this help message

Output files (per run):
  {runId}_portfolio_pv_by_scenario.csv   — per-scenario portfolio PV (legacy)
  {runId}_contract_pv_sample.csv         — sample of per-contract PVs (legacy)
  {runId}_summary.json                   — metrics + config snapshot

Reporting output files (when --reporting true):
  {runId}_portfolio_by_scenario.csv      — RunId, ScenarioId, PortfolioPV
  {runId}_contract_summary.csv           — RunId, ContractId, MeanPV, StdPV, P05..ES99
  runs.csv                               — run dimension table
  {runId}_fact_results_long.csv          — long-format fact table (when --export-fact true)
  _README.txt                            — explains files and Excel join workflow

Portfolio export output (when --export-portfolio true):
  portfolio.csv                          — all contracts in stable input-directory format
                                           (re-usable with --input in future runs)

Examples:
  # Quick CPU-only demo with 100 contracts, 100 scenarios
  dotnet run -- --backend cpu --contracts 100 --scenarios 100

  # Generate a portfolio and export it (capture for re-use)
  dotnet run -- --backend cpu --contracts 500 --scenarios 100 --export-portfolio true --out ./generated

  # Re-run using the exported portfolio from an input directory
  dotnet run -- --input ./generated --backend cpu --reporting true

  # Run the execution-proof sample
  dotnet run -- --input samples/execution-proof/input --backend cpu --out ./out/proof --reporting true --export-fact true

  See: samples/execution-proof/  for ready-to-run sample scripts.
  See: docs/input-output-contract.md for full schema documentation.
");
    }

    private static string Now() => DateTime.UtcNow.ToString("HH:mm:ss.fff");

    private static bool ParseBool(string s) =>
        s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("1", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase);
}
