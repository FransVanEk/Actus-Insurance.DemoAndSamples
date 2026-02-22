/*
 * PamMonteCarlo50Y Demo — Vasicek rate generator.
 * Generates monthly short rates over a 50-year horizon using the
 * Vasicek (Ornstein-Uhlenbeck) model with Euler-Maruyama discretization:
 *   r[t+1] = r[t] + kappa*(theta - r[t])*dt + sigma*sqrt(dt)*Z
 * where Z ~ N(0,1) and dt = 1/12 (monthly steps).
 *
 * Discount factors are computed as:
 *   DF[0] = 1
 *   DF[t] = exp(-sum_{i=1..t} r[i] * dt)
 *
 * PRNG: XorShift64 with Box-Muller transform (identical to existing
 * MonteCarloScenarioGenerator in ActusGPU for consistency).
 */
namespace PamMonteCarlo50Y;

/// <summary>
/// Parameters for the Vasicek interest-rate model.
/// dr = kappa*(theta - r)*dt + sigma*sqrt(dt)*Z
/// </summary>
public sealed class VasicekParams
{
    /// <summary>Mean-reversion speed (typical: 0.1 – 0.5).</summary>
    public double Kappa  { get; init; } = 0.15;

    /// <summary>Long-run mean (e.g., 0.04 = 4%).</summary>
    public double Theta  { get; init; } = 0.04;

    /// <summary>Volatility of the short rate (e.g., 0.02).</summary>
    public double Sigma  { get; init; } = 0.02;

    /// <summary>Initial short rate r[0].</summary>
    public double R0     { get; init; } = 0.03;
}

/// <summary>
/// Generates Vasicek monthly short rates and pre-computes discount factors
/// for a specified number of Monte Carlo scenarios.
///
/// Output layout (all flat arrays indexed [scenario * numMonths + month]):
/// <list type="bullet">
///   <item><see cref="ShortRates"/>   – monthly short rate r[s,t]</item>
///   <item><see cref="DiscountFactors"/> – cumulative DF[s,t]</item>
/// </list>
///
/// DF[s,0] = 1; DF[s,t] = exp(-sum_{i=1..t} r[s,i] * dt).
/// </summary>
public sealed class VasicekRateGenerator
{
    /// <summary>Monthly short rates [scenario × months], row-major.</summary>
    public double[] ShortRates     { get; }

    /// <summary>Discount factors [scenario × months], row-major.  DF[t=0] = 1.</summary>
    public double[] DiscountFactors { get; }

    /// <summary>Number of scenarios.</summary>
    public int NumScenarios { get; }

    /// <summary>Number of monthly steps (e.g., 600 for 50 years).</summary>
    public int NumMonths { get; }

    private VasicekRateGenerator(double[] rates, double[] dfs, int numScenarios, int numMonths)
    {
        ShortRates      = rates;
        DiscountFactors = dfs;
        NumScenarios    = numScenarios;
        NumMonths       = numMonths;
    }

    /// <summary>
    /// Generate scenarios using the Vasicek model.
    /// </summary>
    /// <param name="p">Vasicek parameters.</param>
    /// <param name="numScenarios">Number of MC scenarios.</param>
    /// <param name="numMonths">Horizon in months (default 600 = 50 years).</param>
    /// <param name="seed">Deterministic seed (XorShift64; 0 → replaced with 1).</param>
    public static VasicekRateGenerator Generate(
        VasicekParams p,
        int           numScenarios,
        int           numMonths = 600,
        ulong         seed      = 12345UL)
    {
        if (numScenarios <= 0) throw new ArgumentOutOfRangeException(nameof(numScenarios));
        if (numMonths    <= 0) throw new ArgumentOutOfRangeException(nameof(numMonths));

        const double dt = 1.0 / 12.0;
        double sqrtDt = Math.Sqrt(dt);

        long total   = (long)numScenarios * numMonths;
        var  rates   = new double[total];
        var  dfs     = new double[total];

        ulong state = seed == 0UL ? 1UL : seed;

        for (int s = 0; s < numScenarios; s++)
        {
            double r       = p.R0;
            double cumNegR = 0.0;           // running sum of -r[i]*dt for DF
            int    baseIdx = s * numMonths;

            for (int t = 0; t < numMonths; t++)
            {
                rates[baseIdx + t] = r;
                dfs  [baseIdx + t] = Math.Exp(cumNegR);

                // Euler-Maruyama step (applied after recording state at t)
                double z  = NextNormal(ref state);
                double dr = p.Kappa * (p.Theta - r) * dt + p.Sigma * sqrtDt * z;
                r = Math.Max(r + dr, 0.0);  // floor at 0 (Vasicek can go negative)

                // Accumulate for next DF: DF[t+1] = exp(-sum_{i=0..t} r[i]*dt)
                // We use r at current step t for the [t, t+1) interval.
                cumNegR -= r * dt;
            }
        }

        return new VasicekRateGenerator(rates, dfs, numScenarios, numMonths);
    }

    // ── XorShift64 PRNG (identical to MonteCarloScenarioGenerator) ──────

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

    private static double NextNormal(ref ulong state)
    {
        double u1 = NextUniform(ref state);
        double u2 = NextUniform(ref state);
        if (u1 < 1e-300) u1 = 1e-300;
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>
    /// Return the scenario-average (across all scenarios) short rate at month t.
    /// Useful for diagnostics.
    /// </summary>
    public double MeanRateAtMonth(int t)
    {
        double sum = 0.0;
        for (int s = 0; s < NumScenarios; s++)
            sum += ShortRates[s * NumMonths + t];
        return sum / NumScenarios;
    }

    /// <summary>
    /// Return the mean discount factor (across all scenarios) at month t.
    /// </summary>
    public double MeanDfAtMonth(int t)
    {
        double sum = 0.0;
        for (int s = 0; s < NumScenarios; s++)
            sum += DiscountFactors[s * NumMonths + t];
        return sum / NumScenarios;
    }
}
