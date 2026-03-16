/*
 * PamMonteCarlo50Y Demo — synthetic PAM portfolio generator.
 * Produces a deterministic, heterogeneous portfolio of PAM contracts
 * suitable for Monte Carlo valuation over a 50-year horizon.
 */
using ActusInsurance.Core.Models;
using ActusInsurance.Core.Types;

namespace PamMonteCarlo50Y;

/// <summary>
/// Portfolio-generation parameters.
/// </summary>
public sealed class PortfolioParams
{
    public int      NumContracts      { get; init; } = 10_000;
    public DateTime BaseDate          { get; init; } = new DateTime(2020, 1, 1);
    public ulong    Seed              { get; init; } = 42UL;

    // Range of notional values
    public double MinNotional         { get; init; } = 100_000.0;
    public double MaxNotional         { get; init; } = 10_000_000.0;

    // Range of contract term in months
    public int    MinTermMonths       { get; init; } = 12;
    public int    MaxTermMonths       { get; init; } = 600;   // 50 years

    // Base nominal interest rate
    public double BaseRate            { get; init; } = 0.04;

    // Max spread over base rate
    public double MaxSpread           { get; init; } = 0.03;

    // Fraction of contracts with rate resets (floating-rate)
    public double FloatingFraction    { get; init; } = 0.3;

    /// <summary>Market object code used for floating-rate resets.</summary>
    public string FloatingMOC         { get; init; } = "USD_LIBOR_3M";
}

/// <summary>
/// Generates a synthetic, deterministic portfolio of PAM contracts.
///
/// Portfolio heterogeneity:
/// <list type="bullet">
///   <item>Varied notional (log-uniform in [MinNotional, MaxNotional]).</item>
///   <item>Varied maturity (1–50 years, uniform in months).</item>
///   <item>Payment frequencies: monthly (P1ML1), quarterly (P3ML1), annual (P1YL1).</item>
///   <item>Varied spread over reference rate.</item>
///   <item>Mix of fixed and floating (rate-reset) contracts.</item>
/// </list>
/// All randomness is driven by XorShift64 seeded from <see cref="PortfolioParams.Seed"/>.
/// </summary>
public static class PortfolioGenerator
{
    private static readonly string[] IpCycles = { "P1ML1", "P3ML1", "P1YL1" };
    /// <summary>Contracts are spread over the first 5 years (60 months) of the simulation.</summary>
    private const int MaxStartOffsetMonths = 60;

    public static List<PamContractTerms> Generate(PortfolioParams p)
    {
        ulong state = p.Seed == 0UL ? 1UL : p.Seed;
        var contracts = new List<PamContractTerms>(p.NumContracts);

        for (int i = 0; i < p.NumContracts; i++)
        {
            // ---- Notional: log-uniform ----
            double logMin = Math.Log(p.MinNotional);
            double logMax = Math.Log(p.MaxNotional);
            double notional = Math.Round(
                Math.Exp(logMin + NextUniform(ref state) * (logMax - logMin)), 2);

            // ---- Term: uniform integer months ----
            int termMonths = p.MinTermMonths +
                (int)(NextUniform(ref state) * (p.MaxTermMonths - p.MinTermMonths + 1));
            termMonths = Math.Min(termMonths, p.MaxTermMonths);

            // ---- Start offset: spread contracts over first MaxStartOffsetMonths of simulation ----
            int startOffsetMonths = (int)(NextUniform(ref state) * MaxStartOffsetMonths);

            DateTime ied      = p.BaseDate.AddMonths(startOffsetMonths);
            DateTime maturity = ied.AddMonths(termMonths);

            // ---- Payment frequency: 33% monthly, 33% quarterly, 33% annual ----
            double freqRoll = NextUniform(ref state);
            string ipCycle  = freqRoll < 0.33 ? "P1ML1"
                            : freqRoll < 0.66 ? "P3ML1"
                            : "P1YL1";

            // ---- Rate spread: uniform [0, MaxSpread] ----
            double spread = NextUniform(ref state) * p.MaxSpread;

            // ---- Fixed vs. floating ----
            bool isFloating = NextUniform(ref state) < p.FloatingFraction;

            // ---- Nominal rate = base rate + spread ----
            double nominalRate = p.BaseRate + spread;

            var t = new PamContractTerms
            {
                ContractID             = $"PAM_{i:D6}",
                Currency               = "USD",
                ContractRole           = ContractRole.RPA,
                StatusDate             = ied,
                InitialExchangeDate    = ied,
                MaturityDate           = maturity,
                NotionalPrincipal      = notional,
                NominalInterestRate    = nominalRate,
                AccruedInterest        = 0.0,
                CycleOfInterestPayment = ipCycle,
                CycleAnchorDateOfInterestPayment = ied,
                RateSpread             = spread,
                RateMultiplier         = 1.0,
                DayCountConvention     = DayCountConvention.A_365,
                BusinessDayConvention  = BusinessDayConventionEnum.NOS,
                Calendar               = Calendar.NC,
                NotionalScalingMultiplier  = 1.0,
                InterestScalingMultiplier  = 1.0,
            };

            if (isFloating)
            {
                // Quarterly rate reset
                t.MarketObjectCodeOfRateReset     = p.FloatingMOC;
                t.CycleOfRateReset                = "P3ML1";
                t.CycleAnchorDateOfRateReset      = ied;
            }

            contracts.Add(t);
        }

        return contracts;
    }

    // ── XorShift64 PRNG (same as rest of codebase) ─────────────────────

    private static ulong NextUInt64(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    private static double NextUniform(ref ulong state)
    {
        ulong raw = NextUInt64(ref state);
        return (raw >> 11) * (1.0 / 9007199254740992.0);
    }
}
