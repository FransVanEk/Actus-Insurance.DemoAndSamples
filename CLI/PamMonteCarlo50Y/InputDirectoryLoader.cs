/*
 * PamMonteCarlo50Y Demo — input directory loader.
 *
 * Parses the stable on-disk input contract from:
 *   <inputDir>/portfolio.csv           — contract terms for valuation
 *   <inputDir>/contract_metadata.csv   — optional descriptive metadata (path returned)
 *   <inputDir>/scenarios/scenario_set.json
 *                        + riskfactors/interest_rate_after.csv
 *                        + riskfactors/interest_rate_prior.csv  (optional)
 *   <inputDir>/runs.json               — optional run requests
 *
 * See CLI/PamMonteCarlo50Y/samples/input/ for example files and README.
 * See docs/input-output-contract.md for full schema documentation.
 */
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using ActusInsurance.Core.Models;
using ActusInsurance.Core.Types;

namespace PamMonteCarlo50Y;

/// <summary>
/// Loads a portfolio, scenario set, and run requests from the stable on-disk
/// input-directory contract.
/// </summary>
public static class InputDirectoryLoader
{
    // ── Public result bundle ──────────────────────────────────────────────

    /// <summary>Everything loaded from the input directory.</summary>
    public sealed class InputBundle
    {
        /// <summary>Parsed portfolio contracts ready for valuation.</summary>
        public List<PamContractTerms> Portfolio { get; init; } = new();

        /// <summary>Pre-built Vasicek rate generator (from CSV arrays).</summary>
        public VasicekRateGenerator Rates { get; init; } = null!;

        /// <summary>Vasicek model parameters extracted from scenario_set.json.</summary>
        public VasicekParams VasicekParams { get; init; } = new();

        /// <summary>Run requests (from runs.json or a single default run).</summary>
        public List<RunRequest> Runs { get; init; } = new();

        /// <summary>
        /// Absolute path to <c>contract_metadata.csv</c> if present, otherwise empty.
        /// Pass to <see cref="ExportConfig.MetadataPath"/>.
        /// </summary>
        public string MetadataPath { get; init; } = string.Empty;

        /// <summary>
        /// Earliest <c>InitialExchangeDate</c> across all contracts —
        /// used as the simulation base date (t = 0).
        /// </summary>
        public DateTime BaseDate { get; init; }
    }

    // ── Entry point ───────────────────────────────────────────────────────

    /// <summary>
    /// Load and validate all inputs from <paramref name="inputDir"/>.
    /// Throws <see cref="FileNotFoundException"/> or
    /// <see cref="FormatException"/> on invalid input.
    /// </summary>
    public static InputBundle Load(string inputDir)
    {
        inputDir = Path.GetFullPath(inputDir);
        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException($"Input directory not found: {inputDir}");

        // 1. Portfolio ────────────────────────────────────────────────────
        string portfolioPath = Path.Combine(inputDir, "portfolio.csv");
        if (!File.Exists(portfolioPath))
            throw new FileNotFoundException(
                $"Required file not found: {portfolioPath}");
        var portfolio = LoadPortfolio(portfolioPath);
        if (portfolio.Count == 0)
            throw new FormatException($"portfolio.csv contains no data rows: {portfolioPath}");

        var baseDate = portfolio.Min(c => c.InitialExchangeDate);

        // 2. Scenario set ─────────────────────────────────────────────────
        string scenarioDir     = Path.Combine(inputDir, "scenarios");
        string scenarioSetPath = Path.Combine(scenarioDir, "scenario_set.json");
        if (!File.Exists(scenarioSetPath))
            throw new FileNotFoundException(
                $"Required file not found: {scenarioSetPath}");

        var scenarioSet = LoadScenarioSet(scenarioSetPath);

        var vasicekParams = new VasicekParams
        {
            Kappa = scenarioSet.Model?.Kappa ?? 0.15,
            Theta = scenarioSet.Model?.Theta ?? 0.04,
            Sigma = scenarioSet.Model?.Sigma ?? 0.02,
            R0    = scenarioSet.Model?.R0    ?? 0.03,
        };

        // 3. Risk-factor arrays ──────────────────────────────────────────
        var rfDef    = scenarioSet.RiskFactors?.FirstOrDefault();
        string afterFile = rfDef?.AfterFile != null
            ? Path.Combine(scenarioDir, rfDef.AfterFile)
            : Path.Combine(scenarioDir, "riskfactors", "interest_rate_after.csv");

        if (!File.Exists(afterFile))
            throw new FileNotFoundException(
                $"Required risk-factor file not found: {afterFile}");

        int ns = scenarioSet.NumScenarios;
        int nm = scenarioSet.NumMonths;
        if (ns <= 0 || nm <= 0)
            throw new FormatException(
                $"scenario_set.json: numScenarios={ns} and numMonths={nm} must both be > 0");

        var (shortRates, discountFactors) = LoadRateArrays(afterFile, ns, nm);

        // 4. Optional prior rates (for calcDateIndex > 0 runs) ─────────
        string priorFile = rfDef?.PriorFile != null
            ? Path.Combine(scenarioDir, rfDef.PriorFile)
            : Path.Combine(scenarioDir, "riskfactors", "interest_rate_prior.csv");

        if (File.Exists(priorFile))
            MergePriorRates(priorFile, shortRates, discountFactors, nm);

        var rates = VasicekRateGenerator.FromArrays(shortRates, discountFactors, ns, nm);

        // 5. Optional runs.json ──────────────────────────────────────────
        string runsPath = Path.Combine(inputDir, "runs.json");
        var runs = File.Exists(runsPath)
            ? new List<RunRequest>(RunRequest.LoadFromJson(runsPath))
            : new List<RunRequest> { RunRequest.Default() };

        // 6. Optional contract_metadata.csv ──────────────────────────────
        string metaPath = Path.Combine(inputDir, "contract_metadata.csv");

        return new InputBundle
        {
            Portfolio     = portfolio,
            Rates         = rates,
            VasicekParams = vasicekParams,
            Runs          = runs,
            MetadataPath  = File.Exists(metaPath) ? metaPath : string.Empty,
            BaseDate      = baseDate,
        };
    }

    // ── portfolio.csv parser ─────────────────────────────────────────────

    private static List<PamContractTerms> LoadPortfolio(string path)
    {
        var contracts = new List<PamContractTerms>();
        using var r = new System.IO.StreamReader(path, System.Text.Encoding.UTF8);

        string? header = r.ReadLine();
        if (header == null) return contracts;

        var cols = SplitCsvLine(header);
        int Idx(string name) =>
            Array.FindIndex(cols, c => c.Equals(name, StringComparison.OrdinalIgnoreCase));

        int iId     = Idx("ContractId");
        int iIed    = Idx("InitialExchangeDate");
        int iMat    = Idx("MaturityDate");
        int iNp     = Idx("NotionalPrincipal");
        int iRate   = Idx("NominalInterestRate");
        int iCycle  = Idx("CycleOfInterestPayment");
        int iSpread = Idx("RateSpread");
        int iMoc    = Idx("MarketObjectCodeOfRateReset");
        int iRrCyc  = Idx("CycleOfRateReset");

        if (iId < 0 || iIed < 0 || iMat < 0 || iNp < 0 || iRate < 0 || iCycle < 0)
            throw new FormatException(
                $"portfolio.csv is missing one or more required columns " +
                $"(ContractId, InitialExchangeDate, MaturityDate, NotionalPrincipal, " +
                $"NominalInterestRate, CycleOfInterestPayment). Header: {header}");

        string? line;
        int lineNum = 1;
        while ((line = r.ReadLine()) != null)
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var vals = SplitCsvLine(line);

            string contractId = GetVal(vals, iId);
            DateTime ied      = ParseDate(GetVal(vals, iIed), $"row {lineNum} InitialExchangeDate");
            DateTime mat      = ParseDate(GetVal(vals, iMat), $"row {lineNum} MaturityDate");
            double notional   = ParseDouble(GetVal(vals, iNp),   $"row {lineNum} NotionalPrincipal");
            double rate       = ParseDouble(GetVal(vals, iRate),  $"row {lineNum} NominalInterestRate");
            string cycle      = GetVal(vals, iCycle);
            double spread     = iSpread >= 0 ? ParseDouble(GetVal(vals, iSpread), $"row {lineNum} RateSpread", defaultVal: 0.0) : 0.0;
            string moc        = iMoc   >= 0 ? GetVal(vals, iMoc)   : string.Empty;
            string rrCycle    = iRrCyc >= 0 ? GetVal(vals, iRrCyc) : string.Empty;
            bool isFloating   = !string.IsNullOrEmpty(moc);

            var contract = new PamContractTerms
            {
                ContractID                           = contractId,
                Currency                             = "USD",
                ContractRole                         = ContractRole.RPA,
                StatusDate                           = ied,
                InitialExchangeDate                  = ied,
                MaturityDate                         = mat,
                NotionalPrincipal                    = notional,
                NominalInterestRate                  = rate,
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

            if (isFloating)
            {
                contract.MarketObjectCodeOfRateReset = moc;
                contract.CycleOfRateReset            = string.IsNullOrEmpty(rrCycle) ? "P3ML1" : rrCycle;
                contract.CycleAnchorDateOfRateReset  = ied;
            }

            contracts.Add(contract);
        }

        return contracts;
    }

    // ── scenario_set.json DTOs ────────────────────────────────────────────

    private sealed class ScenarioSetDto
    {
        [JsonPropertyName("numScenarios")] public int  NumScenarios { get; set; } = 0;
        [JsonPropertyName("numMonths")]    public int  NumMonths    { get; set; } = 0;
        [JsonPropertyName("seed")]         public ulong Seed        { get; set; } = 0;
        [JsonPropertyName("model")]        public VasicekModelDto? Model { get; set; }
        [JsonPropertyName("riskFactors")]  public List<RiskFactorRefDto>? RiskFactors { get; set; }
    }

    private sealed class VasicekModelDto
    {
        [JsonPropertyName("kappa")] public double Kappa { get; set; } = 0.15;
        [JsonPropertyName("theta")] public double Theta { get; set; } = 0.04;
        [JsonPropertyName("sigma")] public double Sigma { get; set; } = 0.02;
        [JsonPropertyName("r0")]    public double R0    { get; set; } = 0.03;
    }

    private sealed class RiskFactorRefDto
    {
        [JsonPropertyName("id")]        public string? Id        { get; set; }
        [JsonPropertyName("afterFile")] public string? AfterFile { get; set; }
        [JsonPropertyName("priorFile")] public string? PriorFile { get; set; }
    }

    private static ScenarioSetDto LoadScenarioSet(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ScenarioSetDto>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new FormatException($"scenario_set.json is empty or invalid: {path}");
    }

    // ── Rate array loader ─────────────────────────────────────────────────

    /// <summary>
    /// Parse interest_rate_after.csv (or prior) into flat shortRates and
    /// discountFactors arrays shaped [scenarioIndex * numMonths + timeIndex].
    /// </summary>
    private static (double[] shortRates, double[] discountFactors) LoadRateArrays(
        string path, int numScenarios, int numMonths)
    {
        long total        = (long)numScenarios * numMonths;
        var shortRates    = new double[total];
        var discountFactors = new double[total];

        // Default DFs to 1.0 (neutral; will be overwritten by file rows)
        Array.Fill(discountFactors, 1.0);

        using var r = new System.IO.StreamReader(path, System.Text.Encoding.UTF8);
        string? header = r.ReadLine();
        if (header == null) return (shortRates, discountFactors);

        var cols = SplitCsvLine(header);
        int iS  = Array.FindIndex(cols, c => c.Equals("scenarioIndex", StringComparison.OrdinalIgnoreCase));
        int iT  = Array.FindIndex(cols, c => c.Equals("timeIndex",     StringComparison.OrdinalIgnoreCase));
        int iR  = Array.FindIndex(cols, c => c.Equals("shortRate",     StringComparison.OrdinalIgnoreCase));
        int iDF = Array.FindIndex(cols, c => c.Equals("discountFactor",StringComparison.OrdinalIgnoreCase));

        if (iS < 0 || iT < 0 || iR < 0)
            throw new FormatException(
                $"Rate CSV must have columns scenarioIndex, timeIndex, shortRate. " +
                $"Header: {header}\nFile: {path}");

        string? line;
        int lineNum = 1;
        while ((line = r.ReadLine()) != null)
        {
            lineNum++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var vals = SplitCsvLine(line);

            int    s = int.Parse(GetVal(vals, iS), CultureInfo.InvariantCulture);
            int    t = int.Parse(GetVal(vals, iT), CultureInfo.InvariantCulture);
            double rv = double.Parse(GetVal(vals, iR), CultureInfo.InvariantCulture);

            if (s < 0 || s >= numScenarios || t < 0 || t >= numMonths)
                throw new FormatException(
                    $"Rate CSV row {lineNum}: scenarioIndex={s} or timeIndex={t} " +
                    $"out of range (numScenarios={numScenarios}, numMonths={numMonths}). " +
                    $"File: {path}");

            int idx = s * numMonths + t;
            shortRates[idx] = rv;
            if (iDF >= 0)
                discountFactors[idx] = double.Parse(GetVal(vals, iDF), CultureInfo.InvariantCulture);
        }

        // If no discountFactor column was present, compute from shortRates
        if (iDF < 0)
            ComputeDiscountFactors(shortRates, discountFactors, numScenarios, numMonths);

        return (shortRates, discountFactors);
    }

    /// <summary>
    /// Merge prior-rate rows (t &lt; calcDateIndex) into the already-loaded
    /// after-rate arrays.  Overwrites only the covered (scenario, time) cells.
    /// </summary>
    private static void MergePriorRates(
        string   priorPath,
        double[] shortRates,
        double[] discountFactors,
        int      numMonths)
    {
        // Reuse LoadRateArrays with dummy dimensions; merge by (s,t)
        using var r = new System.IO.StreamReader(priorPath, System.Text.Encoding.UTF8);
        string? header = r.ReadLine();
        if (header == null) return;

        var cols = SplitCsvLine(header);
        int iS  = Array.FindIndex(cols, c => c.Equals("scenarioIndex", StringComparison.OrdinalIgnoreCase));
        int iT  = Array.FindIndex(cols, c => c.Equals("timeIndex",     StringComparison.OrdinalIgnoreCase));
        int iR  = Array.FindIndex(cols, c => c.Equals("shortRate",     StringComparison.OrdinalIgnoreCase));
        int iDF = Array.FindIndex(cols, c => c.Equals("discountFactor",StringComparison.OrdinalIgnoreCase));
        if (iS < 0 || iT < 0 || iR < 0) return;   // empty or wrong format — skip

        string? line;
        while ((line = r.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var vals = SplitCsvLine(line);
            if (!int.TryParse(GetVal(vals, iS), out int s)) continue;
            if (!int.TryParse(GetVal(vals, iT), out int t)) continue;
            if (!double.TryParse(GetVal(vals, iR), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double rv)) continue;

            int idx = s * numMonths + t;
            if (idx < 0 || idx >= shortRates.Length) continue;
            shortRates[idx] = rv;
            if (iDF >= 0 && double.TryParse(GetVal(vals, iDF), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double df))
                discountFactors[idx] = df;
        }
    }

    /// <summary>
    /// Compute discount factors from short rates when the CSV has no discountFactor column:
    /// DF[s,0] = 1; DF[s,t] = DF[s,t-1] * exp(-shortRates[s,t-1] * dt).
    /// </summary>
    private static void ComputeDiscountFactors(
        double[] shortRates,
        double[] discountFactors,
        int      numScenarios,
        int      numMonths)
    {
        const double dt = 1.0 / 12.0;
        for (int s = 0; s < numScenarios; s++)
        {
            int baseIdx = s * numMonths;
            discountFactors[baseIdx] = 1.0;
            for (int t = 1; t < numMonths; t++)
                discountFactors[baseIdx + t] =
                    discountFactors[baseIdx + t - 1] * Math.Exp(-shortRates[baseIdx + t - 1] * dt);
        }
    }

    // ── CSV / parse helpers ───────────────────────────────────────────────

    private static string GetVal(string[] vals, int idx) =>
        idx >= 0 && idx < vals.Length ? vals[idx].Trim() : string.Empty;

    private static DateTime ParseDate(string s, string context)
    {
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
            return dt;
        throw new FormatException($"Invalid date '{s}' at {context} (expected yyyy-MM-dd)");
    }

    private static double ParseDouble(string s, string context, double defaultVal = double.NaN)
    {
        if (string.IsNullOrEmpty(s)) return double.IsNaN(defaultVal) ? 0.0 : defaultVal;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return v;
        throw new FormatException($"Invalid number '{s}' at {context}");
    }

    /// <summary>Naïve RFC 4180 single-line CSV splitter.</summary>
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;
        while (i <= line.Length)
        {
            if (i < line.Length && line[i] == '"')
            {
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        i++;
                        if (i < line.Length && line[i] == '"') { sb.Append('"'); i++; }
                        else break;
                    }
                    else sb.Append(line[i++]);
                }
                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                int comma = line.IndexOf(',', i);
                if (comma < 0) { fields.Add(line.Substring(i)); break; }
                fields.Add(line.Substring(i, comma - i));
                i = comma + 1;
            }
        }
        return fields.ToArray();
    }
}
