/*
 * ScenarioCpuGpuCalcDateDemo — CPU present-value engine.
 *
 * Adapted from CLI/PamMonteCarlo50Y/CpuPvEngine.cs.
 * Key changes:
 *   - Uses RateScenarios instead of VasicekRateGenerator.
 *   - RiskFactorModel removed (unused in actual PV computation).
 *   - Namespace changed to ScenarioCpuGpuCalcDateDemo.
 *
 * The prior/after boundary is expressed SOLELY through the pre-built
 * RateScenarios arrays: ScenarioBuilder.Build(calcDateIndex) already
 * sets shortRates[t] = priorRate for t < calcDateIndex.  The engine
 * itself needs no knowledge of calcDateIndex.
 */
using ActusInsurance.Core.CPU.Contracts;
using ActusInsurance.Core.Events;
using ActusInsurance.Core.Models;
using ActusInsurance.Core.Types;

namespace ScenarioCpuGpuCalcDateDemo;

/// <summary>
/// Result of a single (contract, scenario) present-value computation.
/// </summary>
public readonly struct PvResult
{
    /// <summary>Discounted present value of all cash flows.</summary>
    public double PV { get; init; }
}

/// <summary>
/// CPU present-value engine for the demo.
///
/// <b>Output layout</b>: <c>results[contractIndex * numScenarios + scenarioIndex]</c>
/// </summary>
public static class CpuEngine
{
    private const double TicksPerDay  = 864_000_000_000.0;
    private const double DaysPerMonth = 365.25 / 12.0;

    /// <summary>
    /// Evaluate all contracts against all scenarios in <paramref name="rates"/>.
    /// </summary>
    /// <param name="contracts">Portfolio slice to value.</param>
    /// <param name="rates">
    ///   Pre-built scenario rate arrays.  Prior/after merging must already be
    ///   reflected in these arrays (see <see cref="ScenarioBuilder.Build"/>).
    /// </param>
    /// <param name="baseDate">Simulation t=0 date (defines the monthly grid).</param>
    /// <param name="maturityHorizon">Upper bound for schedule generation.</param>
    /// <returns>
    ///   Flat <see cref="PvResult"/> array of size contracts.Count × numScenarios,
    ///   indexed as <c>[contractIndex * numScenarios + scenarioIndex]</c>.
    /// </returns>
    public static PvResult[] Evaluate(
        IReadOnlyList<PamContractTerms> contracts,
        RateScenarios                   rates,
        DateTime                        baseDate,
        DateTime                        maturityHorizon)
    {
        int numContracts   = contracts.Count;
        int numScenarios   = rates.NumScenarios;
        var results        = new PvResult[numContracts * numScenarios];
        long baseDateTicks = baseDate.Ticks;

        // Pre-build event schedules (scenario-independent) once per contract.
        var schedules = new List<ContractEvent>[numContracts];
        for (int c = 0; c < numContracts; c++)
            schedules[c] = PrincipalAtMaturity.Schedule(maturityHorizon, contracts[c]);

        // Parallel over scenarios — each scenario writes to a disjoint region.
        System.Threading.Tasks.Parallel.For(0, numScenarios, s =>
        {
            int baseIdx = s * rates.NumMonths;

            for (int c = 0; c < numContracts; c++)
            {
                double pv = ComputePv(
                    contracts[c], schedules[c],
                    rates.DiscountFactors, rates.ShortRates,
                    baseIdx, rates.NumMonths, baseDateTicks);

                results[c * numScenarios + s] = new PvResult { PV = pv };
            }
        });

        return results;
    }

    // ── Per-contract, per-scenario PV ─────────────────────────────────────

    private static double ComputePv(
        PamContractTerms    terms,
        List<ContractEvent> schedule,
        double[]            discountFactors,
        double[]            shortRates,
        int                 scenarioBaseIdx,
        int                 numMonths,
        long                baseDateTicks)
    {
        double pv              = 0.0;
        double notional        = terms.RoleSign * terms.NotionalPrincipal;
        double nominalRate     = terms.NominalInterestRate;
        double accruedInterest = terms.AccruedInterest;
        DateTime prevTime      = terms.StatusDate;

        foreach (var ev in schedule)
        {
            int monthIdx = ScheduleTimeToMonthIndex(ev.ScheduleTime, baseDateTicks);
            monthIdx     = Math.Max(0, Math.Min(monthIdx, numMonths - 1));

            double scenRate = shortRates[scenarioBaseIdx + monthIdx];
            double yf       = Math.Max(0.0, (ev.ScheduleTime - prevTime).TotalDays / 365.25);
            double df       = discountFactors[scenarioBaseIdx + monthIdx];

            double cashFlow = ComputeCashFlow(ev.Type, terms, notional, nominalRate,
                                              accruedInterest, yf);
            pv += cashFlow * df;

            ApplyStateTransition(ev.Type, ref notional, ref nominalRate,
                                 ref accruedInterest, yf, terms, scenRate);
            prevTime = ev.ScheduleTime;
        }

        return pv;
    }

    private static double ComputeCashFlow(
        EventType        evType,
        PamContractTerms terms,
        double           notional,
        double           nominalRate,
        double           accruedInterest,
        double           yf) =>
        evType switch
        {
            EventType.IED  => terms.RoleSign * -1.0 *
                               (terms.NotionalPrincipal + terms.PremiumDiscountAtIED),
            EventType.MD   => notional,
            EventType.PRD  => terms.RoleSign * -1.0 * terms.PriceAtPurchaseDate,
            EventType.TD   => terms.RoleSign * terms.PriceAtTerminationDate,
            EventType.IP   => accruedInterest + yf * nominalRate * notional,
            EventType.IPCI => 0.0,
            EventType.RR   => 0.0,
            EventType.RRF  => 0.0,
            EventType.FP   => terms.FeeBasis == "N"
                               ? terms.FeeAccrued + notional * terms.FeeRate * yf
                               : terms.RoleSign * terms.FeeRate,
            EventType.SC   => 0.0,
            _              => 0.0,
        };

    private static void ApplyStateTransition(
        EventType        evType,
        ref double       notional,
        ref double       nominalRate,
        ref double       accruedInterest,
        double           yf,
        PamContractTerms terms,
        double           scenRate)
    {
        bool needsAccrual = evType is EventType.IP   or EventType.IPCI
                                   or EventType.RR   or EventType.RRF
                                   or EventType.FP   or EventType.SC;
        if (needsAccrual && yf > 0.0 && notional != 0.0 && nominalRate != 0.0)
            accruedInterest += nominalRate * notional * yf;

        switch (evType)
        {
            case EventType.IED:
                notional    = terms.RoleSign * terms.NotionalPrincipal;
                nominalRate = terms.NominalInterestRate;
                break;
            case EventType.MD:
            case EventType.TD:
                notional        = 0.0;
                accruedInterest = 0.0;
                break;
            case EventType.IP:
                accruedInterest = 0.0;
                break;
            case EventType.IPCI:
                notional       += accruedInterest;
                accruedInterest = 0.0;
                break;
            case EventType.RR:
                nominalRate = scenRate * terms.RateMultiplier + terms.RateSpread;
                break;
            case EventType.RRF:
                if (terms.NextResetRate.HasValue)
                    nominalRate = terms.NextResetRate.Value;
                break;
        }
    }

    private static int ScheduleTimeToMonthIndex(DateTime scheduleDate, long baseDateTicks)
    {
        double days = (scheduleDate.Ticks - baseDateTicks) / TicksPerDay;
        return (int)Math.Round(days / DaysPerMonth);
    }
}
