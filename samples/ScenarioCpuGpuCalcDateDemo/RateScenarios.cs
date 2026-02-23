/*
 * ScenarioCpuGpuCalcDateDemo — lightweight rate scenario holder.
 *
 * RateScenarios is a plain data carrier for pre-built short-rate and
 * discount-factor arrays, independent of any generator implementation.
 * It replaces the VasicekRateGenerator that lives in PamMonteCarlo50Y.
 *
 * Layout (flat, row-major):
 *   ShortRates      [scenarioIndex * NumMonths + timeIndex]
 *   DiscountFactors [scenarioIndex * NumMonths + timeIndex]
 */
namespace ScenarioCpuGpuCalcDateDemo;

/// <summary>
/// Pre-built short-rate and discount-factor arrays for a fixed set of
/// scenarios and monthly time steps.
/// </summary>
public sealed class RateScenarios
{
    /// <summary>Monthly short rates [scenario × months], row-major.</summary>
    public double[] ShortRates      { get; }

    /// <summary>Discount factors [scenario × months], row-major.  DF[t=0] = 1.</summary>
    public double[] DiscountFactors { get; }

    /// <summary>Number of scenarios.</summary>
    public int NumScenarios { get; }

    /// <summary>Number of monthly time steps.</summary>
    public int NumMonths { get; }

    public RateScenarios(double[] shortRates, double[] discountFactors,
                         int numScenarios, int numMonths)
    {
        if (shortRates.Length != numScenarios * numMonths)
            throw new ArgumentException(
                $"shortRates length {shortRates.Length} ≠ {numScenarios}×{numMonths}");
        if (discountFactors.Length != numScenarios * numMonths)
            throw new ArgumentException(
                $"discountFactors length {discountFactors.Length} ≠ {numScenarios}×{numMonths}");

        ShortRates      = shortRates;
        DiscountFactors = discountFactors;
        NumScenarios    = numScenarios;
        NumMonths       = numMonths;
    }
}
