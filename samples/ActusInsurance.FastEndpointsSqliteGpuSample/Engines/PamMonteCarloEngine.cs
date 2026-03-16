using System.Diagnostics;
using ActusInsurance.Core.Externals;
using ActusInsurance.Core.Models;
using ActusInsurance.Core.Types;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Engines;

/// <summary>
/// PAM Monte Carlo calculation engine using real ActusInsurance.GPU valuation.
/// Mirrors CLI/PamMonteCarlo50Y — synthetic-portfolio mode and file-input mode.
/// </summary>
public class PamMonteCarloEngine : ICalculationEngine
{
    private readonly bool _preferGpu;

    public PamMonteCarloEngine(bool preferGpu = false)
    {
        _preferGpu = preferGpu;
    }

    public string Label => _preferGpu ? "PAM Monte Carlo (GPU)" : "PAM Monte Carlo (CPU)";

    public async Task<CalculationResult> ExecuteAsync(
        CalculationInputs inputs,
        IProgress<ProgressInfo> progress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        progress.Report(new ProgressInfo(5, "Parsing inputs"));

        bool hasFileInput = GetBoolParameter(inputs.Parameters, "hasFileInput", false);

        List<PamContractTerms> portfolio;
        PamParameters parameters;

        if (hasFileInput)
        {
            progress.Report(new ProgressInfo(15, "Processing uploaded portfolio data"));
            portfolio  = await ParsePortfolioCsvAsync(inputs.Parameters, ct);
            parameters = DerivePamParametersFromPortfolio(inputs.Parameters, portfolio);
        }
        else
        {
            progress.Report(new ProgressInfo(15, "Generating synthetic portfolio"));
            parameters = ParsePamParameters(inputs.Parameters);
            portfolio  = GenerateSyntheticPortfolio(parameters);
        }

        progress.Report(new ProgressInfo(25, "Generating Vasicek rate scenarios"));

        // Generate Vasicek scenarios — identical algorithm to CLI
        var vasicek = new PamMcVasicekParams
        {
            Kappa = GetDoubleParameter(inputs.Parameters, "kappa", 0.15),
            Theta = GetDoubleParameter(inputs.Parameters, "theta", 0.04),
            Sigma = GetDoubleParameter(inputs.Parameters, "sigma", 0.02),
            R0    = GetDoubleParameter(inputs.Parameters, "r0",    0.03),
        };
        var rates = await Task.Run(() =>
            PamMcVasicekRateGenerator.Generate(
                vasicek,
                parameters.NumScenarios,
                parameters.MonthsToMaturity,
                parameters.Seed + 1UL), ct);

        progress.Report(new ProgressInfo(40, "Building risk-factor model"));

        var riskFactors = new RiskFactorModel();
        riskFactors.AddConstantRate("USD_LIBOR_3M", vasicek.R0);

        DateTime maturityHorizon = parameters.BaseDate.AddMonths(parameters.MonthsToMaturity + 1);

        progress.Report(new ProgressInfo(55, "Computing present values"));

        double[] pvMatrix;
        string   engineLabel;

        if (_preferGpu)
        {
            progress.Report(new ProgressInfo(58, "Initializing GPU accelerator"));
            using var gpuEngine = await Task.Run(() => PamMcGpuEngine.CreateDefault(), ct);
            engineLabel = $"GPU:{gpuEngine.AcceleratorName}";

            progress.Report(new ProgressInfo(62, $"Executing GPU kernel ({engineLabel})"));
            var gpuResults = await Task.Run(() =>
                gpuEngine.Evaluate(
                    portfolio, rates, parameters.BaseDate, parameters.CalcDateIndex,
                    0, parameters.NumScenarios, maturityHorizon), ct);

            pvMatrix = new double[gpuResults.Length];
            for (int i = 0; i < gpuResults.Length; i++)
                pvMatrix[i] = gpuResults[i].PV;
        }
        else
        {
            engineLabel = "CPU";
            progress.Report(new ProgressInfo(62, "Executing CPU engine"));
            var cpuResults = await Task.Run(() =>
                PamMcCpuEngine.Evaluate(
                    portfolio, riskFactors, rates, parameters.BaseDate,
                    parameters.CalcDateIndex, 0, parameters.NumScenarios,
                    maturityHorizon), ct);

            pvMatrix = new double[cpuResults.Length];
            for (int i = 0; i < cpuResults.Length; i++)
                pvMatrix[i] = cpuResults[i].PV;
        }

        progress.Report(new ProgressInfo(82, "Aggregating results"));

        // Sum per-contract PVs into per-scenario portfolio PVs
        // Layout: pvMatrix[c * numScenarios + s]
        int nc = portfolio.Count;
        int ns = parameters.NumScenarios;
        var scenarioPvs = new double[ns];
        for (int s = 0; s < ns; s++)
            for (int c = 0; c < nc; c++)
                scenarioPvs[s] += pvMatrix[c * ns + s];

        progress.Report(new ProgressInfo(92, "Computing statistics"));

        double mean     = scenarioPvs.Average();
        double variance = scenarioPvs.Select(v => (v - mean) * (v - mean)).Average();
        double std      = Math.Sqrt(variance);
        var    sorted   = scenarioPvs.OrderBy(v => v).ToArray();
        double p05      = sorted[Math.Max(0, (int)(ns * 0.05))];
        double p95      = sorted[Math.Min(ns - 1, (int)(ns * 0.95))];

        progress.Report(new ProgressInfo(100, "Complete"));
        sw.Stop();

        return new CalculationResult(
            Label,
            scenarioPvs,
            mean, std, p05, p95,
            sw.ElapsedMilliseconds,
            new Dictionary<string, object>
            {
                ["numContracts"]     = portfolio.Count,
                ["numScenarios"]     = parameters.NumScenarios,
                ["engine"]           = engineLabel,
                ["baseDate"]         = parameters.BaseDate.ToString("yyyy-MM-dd"),
                ["monthsToMaturity"] = parameters.MonthsToMaturity,
                ["calcDateIndex"]    = parameters.CalcDateIndex,
                ["vasicekKappa"]     = vasicek.Kappa,
                ["vasicekTheta"]     = vasicek.Theta,
                ["vasicekSigma"]     = vasicek.Sigma,
                ["vasicekR0"]        = vasicek.R0,
                ["dataSource"]       = hasFileInput ? "Custom CSV" : "Generated",
            });
    }

    // ── Parameter parsing ─────────────────────────────────────────────────

    private static PamParameters ParsePamParameters(Dictionary<string, string> parameters)
    {
        return new PamParameters
        {
            NumContracts     = GetIntParameter(parameters, "contracts",      1_000),
            NumScenarios     = GetIntParameter(parameters, "scenarios",        100),
            MonthsToMaturity = GetIntParameter(parameters, "months",           600),
            CalcDateIndex    = GetIntParameter(parameters, "calcDateIndex",      0),
            Seed             = GetULongParameter(parameters, "seed",        12345ul),
            BaseDate         = GetDateParameter(parameters, "baseDate", new DateTime(2020, 1, 1)),
        };
    }

    private static PamParameters DerivePamParametersFromPortfolio(
        Dictionary<string, string> parameters,
        List<PamContractTerms>     portfolio)
    {
        var baseDate    = portfolio.Count > 0 ? portfolio.Min(c => c.InitialExchangeDate) : new DateTime(2020, 1, 1);
        var maxMaturity = portfolio.Count > 0 ? portfolio.Max(c => c.MaturityDate) : baseDate.AddMonths(600);
        int monthsToMat = Math.Max(1, (int)Math.Round((maxMaturity - baseDate).Days / 30.44));

        return new PamParameters
        {
            NumContracts     = portfolio.Count,
            NumScenarios     = GetIntParameter(parameters, "scenarios",      100),
            MonthsToMaturity = monthsToMat,
            CalcDateIndex    = GetIntParameter(parameters, "calcDateIndex",    0),
            Seed             = GetULongParameter(parameters, "seed",       12345ul),
            BaseDate         = baseDate,
        };
    }

    // ── Synthetic portfolio generation ─────────────────────────────────────
    //    Mirrors CLI PortfolioGenerator.cs — same XorShift64 PRNG and heterogeneity

    private static List<PamContractTerms> GenerateSyntheticPortfolio(PamParameters p)
    {
        ulong state     = p.Seed == 0UL ? 1UL : p.Seed;
        var   contracts = new List<PamContractTerms>(p.NumContracts);

        const double minNotional      = 100_000.0;
        const double maxNotional      = 10_000_000.0;
        const int    minTermMonths    = 12;
        const int    maxTermMonths    = 600;
        const int    maxStartOffset   = 60;
        const double baseRate         = 0.04;
        const double maxSpread        = 0.03;
        const double floatingFraction = 0.3;

        double logMin = Math.Log(minNotional);
        double logMax = Math.Log(maxNotional);

        for (int i = 0; i < p.NumContracts; i++)
        {
            double   notional   = Math.Round(Math.Exp(logMin + NextUniform(ref state) * (logMax - logMin)), 2);
            int      termMonths = Math.Min(maxTermMonths,
                                      minTermMonths + (int)(NextUniform(ref state) * (maxTermMonths - minTermMonths + 1)));
            int      startOff   = (int)(NextUniform(ref state) * maxStartOffset);
            DateTime ied        = p.BaseDate.AddMonths(startOff);
            DateTime maturity   = ied.AddMonths(termMonths);

            double freqRoll = NextUniform(ref state);
            string ipCycle  = freqRoll < 0.33 ? "P1ML1"
                            : freqRoll < 0.66 ? "P3ML1"
                            :                   "P1YL1";

            double spread      = NextUniform(ref state) * maxSpread;
            bool   isFloating  = NextUniform(ref state) < floatingFraction;
            double nominalRate = baseRate + spread;

            var t = new PamContractTerms
            {
                ContractID                           = $"PAM_{i:D6}",
                Currency                             = "USD",
                ContractRole                         = ContractRole.RPA,
                StatusDate                           = ied,
                InitialExchangeDate                  = ied,
                MaturityDate                         = maturity,
                NotionalPrincipal                    = notional,
                NominalInterestRate                  = nominalRate,
                AccruedInterest                      = 0.0,
                CycleOfInterestPayment               = ipCycle,
                CycleAnchorDateOfInterestPayment     = ied,
                RateSpread                           = spread,
                RateMultiplier                       = 1.0,
                DayCountConvention                   = DayCountConvention.A_365,
                BusinessDayConvention                = BusinessDayConventionEnum.NOS,
                Calendar                             = Calendar.NC,
                NotionalScalingMultiplier            = 1.0,
                InterestScalingMultiplier            = 1.0,
            };

            if (isFloating)
            {
                t.MarketObjectCodeOfRateReset    = "USD_LIBOR_3M";
                t.CycleOfRateReset               = "P3ML1";
                t.CycleAnchorDateOfRateReset     = ied;
            }

            contracts.Add(t);
        }

        return contracts;
    }

    // ── Portfolio CSV parser ──────────────────────────────────────────────

    private static async Task<List<PamContractTerms>> ParsePortfolioCsvAsync(
        Dictionary<string, string> parameters,
        CancellationToken ct)
    {
        var portfolio = new List<PamContractTerms>();

        if (!parameters.TryGetValue("portfolioCsv", out var csvContent) || string.IsNullOrEmpty(csvContent))
            return portfolio;

        await Task.Run(() =>
        {
            var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return;

            var cols = SplitCsvLine(lines[0]);
            int Idx(string name) => Array.FindIndex(cols, c => c.Equals(name, StringComparison.OrdinalIgnoreCase));

            int iId     = Idx("ContractId");
            int iIed    = Idx("InitialExchangeDate");
            int iMat    = Idx("MaturityDate");
            int iNp     = Idx("NotionalPrincipal");
            int iRate   = Idx("NominalInterestRate");
            int iCycle  = Idx("CycleOfInterestPayment");
            int iSpread = Idx("RateSpread");
            int iMoc    = Idx("MarketObjectCodeOfRateReset");
            int iRrCyc  = Idx("CycleOfRateReset");

            if (iId < 0 || iIed < 0 || iMat < 0 || iNp < 0 || iRate < 0)
                throw new FormatException(
                    "Portfolio CSV must have columns: ContractId, InitialExchangeDate, " +
                    "MaturityDate, NotionalPrincipal, NominalInterestRate");

            for (int lineNum = 1; lineNum < lines.Length; lineNum++)
            {
                var line = lines[lineNum].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                var vals = SplitCsvLine(line);

                try
                {
                    DateTime ied    = ParseDate(GetVal(vals, iIed));
                    DateTime mat    = ParseDate(GetVal(vals, iMat));
                    string   cycle  = iCycle >= 0 ? GetVal(vals, iCycle) : string.Empty;
                    if (string.IsNullOrEmpty(cycle)) cycle = "P1YL1";
                    double   spread = iSpread >= 0 ? ParseDouble(GetVal(vals, iSpread)) : 0.0;
                    string   moc    = iMoc    >= 0 ? GetVal(vals, iMoc)   : string.Empty;
                    string   rrCyc  = iRrCyc  >= 0 ? GetVal(vals, iRrCyc) : string.Empty;

                    var contract = new PamContractTerms
                    {
                        ContractID                           = GetVal(vals, iId),
                        Currency                             = "USD",
                        ContractRole                         = ContractRole.RPA,
                        StatusDate                           = ied,
                        InitialExchangeDate                  = ied,
                        MaturityDate                         = mat,
                        NotionalPrincipal                    = ParseDouble(GetVal(vals, iNp)),
                        NominalInterestRate                  = ParseDouble(GetVal(vals, iRate)),
                        AccruedInterest                      = 0.0,
                        CycleOfInterestPayment               = cycle,
                        CycleAnchorDateOfInterestPayment     = ied,
                        RateSpread                           = spread,
                        RateMultiplier                       = 1.0,
                        DayCountConvention                   = DayCountConvention.A_365,
                        BusinessDayConvention                = BusinessDayConventionEnum.NOS,
                        Calendar                             = Calendar.NC,
                        NotionalScalingMultiplier            = 1.0,
                        InterestScalingMultiplier            = 1.0,
                    };

                    if (!string.IsNullOrEmpty(moc))
                    {
                        contract.MarketObjectCodeOfRateReset = moc;
                        contract.CycleOfRateReset            = string.IsNullOrEmpty(rrCyc) ? "P3ML1" : rrCyc;
                        contract.CycleAnchorDateOfRateReset  = ied;
                    }

                    portfolio.Add(contract);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping CSV row {lineNum}: {ex.Message}");
                }
            }
        }, ct);

        return portfolio;
    }

    // ── Parameter helpers ─────────────────────────────────────────────────

    private static int GetIntParameter(Dictionary<string, string> p, string key, int def)
        => p.TryGetValue(key, out var v) && int.TryParse(v, out var r) ? r : def;

    private static ulong GetULongParameter(Dictionary<string, string> p, string key, ulong def)
        => p.TryGetValue(key, out var v) && ulong.TryParse(v, out var r) ? r : def;

    private static double GetDoubleParameter(Dictionary<string, string> p, string key, double def)
        => p.TryGetValue(key, out var v) && double.TryParse(v,
               System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : def;

    private static DateTime GetDateParameter(Dictionary<string, string> p, string key, DateTime def)
        => p.TryGetValue(key, out var v) && DateTime.TryParse(v, out var r) ? r : def;

    private static bool GetBoolParameter(Dictionary<string, string> p, string key, bool def)
        => p.TryGetValue(key, out var v) && bool.TryParse(v, out var r) ? r : def;

    // ── CSV helpers ───────────────────────────────────────────────────────

    private static string[] SplitCsvLine(string line)
    {
        var  result   = new List<string>();
        bool inQuotes = false;
        var  current  = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if      (c == '"')              { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { result.Add(current.ToString().Trim()); current.Clear(); }
            else                            { current.Append(c); }
        }
        result.Add(current.ToString().Trim());
        return result.ToArray();
    }

    private static string GetVal(string[] vals, int index)
        => index >= 0 && index < vals.Length ? vals[index].Trim('"', ' ') : string.Empty;

    private static DateTime ParseDate(string s)
        => DateTime.TryParse(s, out var d) ? d : DateTime.Today;

    private static double ParseDouble(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0.0;

    // ── XorShift64 PRNG (same as CLI PortfolioGenerator) ─────────────────

    private static ulong NextUInt64(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    private static double NextUniform(ref ulong state)
        => (NextUInt64(ref state) >> 11) * (1.0 / 9007199254740992.0);

    // ── Internal parameter record ─────────────────────────────────────────

    private record PamParameters
    {
        public int      NumContracts     { get; init; }
        public int      NumScenarios     { get; init; }
        public int      MonthsToMaturity { get; init; }
        public int      CalcDateIndex    { get; init; }
        public ulong    Seed             { get; init; }
        public DateTime BaseDate         { get; init; }
    }
}
