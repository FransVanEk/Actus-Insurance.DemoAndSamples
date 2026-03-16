/*
 * ScenarioCpuGpuCalcDateDemo — scenario builder.
 *
 * Builds three deterministic, constant-rate scenarios so that the
 * economic direction of every PV change is immediately explainable:
 *
 *   Scenario 0 — "Low"  : r = 1.5 % (flat across all months)
 *   Scenario 1 — "Base" : r = 3.0 % (flat across all months)
 *   Scenario 2 — "High" : r = 6.5 % (flat across all months)
 *
 * A separate "prior" flat rate (5 %) is used for Experiment 3:
 *   When calcDateIndex > 0, months  [0, calcDateIndex) use priorRate
 *   and months [calcDateIndex, NumMonths) use the scenario rate.
 *   Discount factors are computed as a cumulative product so the
 *   prior/after boundary is correctly reflected in DF[t].
 *
 * Constant-rate DF formula:
 *   DF[t] = exp( -rate * t * dt ),   dt = 1/12
 *
 * Mixed prior/after DF formula:
 *   DF[t < cdi] = exp( -priorRate * t * dt )
 *   DF[t >= cdi] = exp( -priorRate * cdi * dt - afterRate * (t-cdi) * dt )
 */
namespace ScenarioCpuGpuCalcDateDemo;

/// <summary>
/// Builds deterministic constant-rate <see cref="RateScenarios"/> instances
/// used by the three demo experiments.
/// </summary>
public static class ScenarioBuilder
{
    /// <summary>Number of scenarios in every built set.</summary>
    public const int NumScenarios = 3;

    /// <summary>Horizon in monthly steps (4 years).</summary>
    public const int NumMonths = 48;

    /// <summary>Human-readable scenario labels.</summary>
    public static readonly string[] Names = { "Low (1.5%)", "Base (3.0%)", "High (6.5%)" };

    /// <summary>Flat short-rate per scenario (used for all months when calcDateIndex=0).</summary>
    public static readonly double[] AfterRates = { 0.015, 0.030, 0.065 };

    /// <summary>
    /// Flat prior rate used for months [0, calcDateIndex) in Experiment 3.
    /// Chosen to be clearly different from all after-rates so the effect is visible.
    /// </summary>
    public const double PriorRate = 0.05;   // 5 %

    private const double Dt = 1.0 / 12.0;

    // ── Factory methods ────────────────────────────────────────────────────

    /// <summary>
    /// Build the standard scenario set with <paramref name="calcDateIndex"/> = 0
    /// (pure forward: all months use scenario short rates).
    /// </summary>
    public static RateScenarios BuildForward() => Build(0);

    /// <summary>
    /// Build a scenario set where months [0, <paramref name="calcDateIndex"/>)
    /// use <see cref="PriorRate"/> and months [calcDateIndex, NumMonths) use
    /// the scenario's own short rate.  Discount factors are computed on the
    /// merged path so the full prior/after effect propagates into DFs.
    /// </summary>
    public static RateScenarios Build(int calcDateIndex)
    {
        if (calcDateIndex < 0 || calcDateIndex > NumMonths)
            throw new ArgumentOutOfRangeException(nameof(calcDateIndex),
                $"calcDateIndex must be in [0, {NumMonths}]");

        long total  = (long)NumScenarios * NumMonths;
        var  sr     = new double[total];
        var  df     = new double[total];

        for (int s = 0; s < NumScenarios; s++)
        {
            double afterRate = AfterRates[s];
            double cumNegR   = 0.0;

            for (int t = 0; t < NumMonths; t++)
            {
                double rateAtT = t < calcDateIndex ? PriorRate : afterRate;
                int    idx     = s * NumMonths + t;
                sr[idx] = rateAtT;
                df[idx] = Math.Exp(cumNegR);

                // Accumulate for the NEXT step's DF
                cumNegR -= rateAtT * Dt;
            }
        }

        return new RateScenarios(sr, df, NumScenarios, NumMonths);
    }
}
