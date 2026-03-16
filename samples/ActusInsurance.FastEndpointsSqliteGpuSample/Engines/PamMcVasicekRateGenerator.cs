namespace ActusInsurance.FastEndpointsSqliteGpuSample.Engines;

/// <summary>Parameters for the Vasicek interest-rate model.</summary>
internal sealed class PamMcVasicekParams
{
    public double Kappa { get; init; } = 0.15;
    public double Theta { get; init; } = 0.04;
    public double Sigma { get; init; } = 0.02;
    public double R0    { get; init; } = 0.03;
}

/// <summary>
/// Generates Vasicek monthly short rates and discount factors for Monte Carlo scenarios.
/// Ported from CLI/PamMonteCarlo50Y/VasicekRateGenerator.cs.
/// Output layout (flat arrays indexed [scenario * numMonths + month]).
/// </summary>
internal sealed class PamMcVasicekRateGenerator
{
    public double[] ShortRates      { get; }
    public double[] DiscountFactors { get; }
    public int      NumScenarios    { get; }
    public int      NumMonths       { get; }

    private PamMcVasicekRateGenerator(double[] rates, double[] dfs, int numScenarios, int numMonths)
    {
        ShortRates      = rates;
        DiscountFactors = dfs;
        NumScenarios    = numScenarios;
        NumMonths       = numMonths;
    }

    public static PamMcVasicekRateGenerator FromArrays(
        double[] shortRates, double[] discountFactors, int numScenarios, int numMonths)
    {
        if (shortRates.Length      != numScenarios * numMonths) throw new ArgumentException("shortRates length mismatch");
        if (discountFactors.Length != numScenarios * numMonths) throw new ArgumentException("discountFactors length mismatch");
        return new PamMcVasicekRateGenerator(shortRates, discountFactors, numScenarios, numMonths);
    }

    /// <summary>
    /// Generate Vasicek scenarios using Euler-Maruyama discretization with XorShift64 PRNG.
    /// dr = kappa*(theta - r)*dt + sigma*sqrt(dt)*Z,  dt = 1/12.
    /// DF[t=0] = 1;  DF[t] = exp(-sum_{i=0..t-1} r[i]*dt).
    /// </summary>
    public static PamMcVasicekRateGenerator Generate(
        PamMcVasicekParams p,
        int                numScenarios,
        int                numMonths = 600,
        ulong              seed      = 12345UL)
    {
        if (numScenarios <= 0) throw new ArgumentOutOfRangeException(nameof(numScenarios));
        if (numMonths    <= 0) throw new ArgumentOutOfRangeException(nameof(numMonths));

        const double dt    = 1.0 / 12.0;
        double       sqrtDt = Math.Sqrt(dt);

        long total = (long)numScenarios * numMonths;
        var  rates = new double[total];
        var  dfs   = new double[total];

        ulong state = seed == 0UL ? 1UL : seed;

        for (int s = 0; s < numScenarios; s++)
        {
            double r       = p.R0;
            double cumNegR = 0.0;
            int    baseIdx = s * numMonths;

            for (int t = 0; t < numMonths; t++)
            {
                rates[baseIdx + t] = r;
                dfs  [baseIdx + t] = Math.Exp(cumNegR);

                double z  = NextNormal(ref state);
                double dr = p.Kappa * (p.Theta - r) * dt + p.Sigma * sqrtDt * z;
                r = Math.Max(r + dr, 0.0);

                cumNegR -= r * dt;
            }
        }

        return new PamMcVasicekRateGenerator(rates, dfs, numScenarios, numMonths);
    }

    // ── XorShift64 PRNG ───────────────────────────────────────────────────

    private static ulong NextUInt64(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    private static double NextUniform(ref ulong state)
        => (NextUInt64(ref state) >> 11) * (1.0 / 9007199254740992.0);

    private static double NextNormal(ref ulong state)
    {
        double u1 = NextUniform(ref state);
        double u2 = NextUniform(ref state);
        if (u1 < 1e-300) u1 = 1e-300;
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
