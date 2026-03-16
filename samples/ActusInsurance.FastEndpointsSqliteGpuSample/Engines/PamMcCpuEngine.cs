/*
 * CPU discounted-PV engine for PAM Monte Carlo.
 * Ported from CLI/PamMonteCarlo50Y/CpuPvEngine.cs.
 *
 * Output layout: results[contractIndex * numScenarios + scenarioIndex].
 */
using ActusInsurance.Core.CPU.Contracts;
using ActusInsurance.Core.Events;
using ActusInsurance.Core.Externals;
using ActusInsurance.Core.Models;
using ActusInsurance.Core.Types;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Engines;

internal readonly struct PamMcPvResult
{
    public double PV { get; init; }
}

internal static class PamMcCpuEngine
{
    private const double TicksPerDay  = 864_000_000_000.0;
    private const double DaysPerMonth = 365.25 / 12.0;

    /// <summary>
    /// Evaluate all contracts × scenarios in parallel.
    /// Returns flat array results[c * numScenarios + s].
    /// </summary>
    public static PamMcPvResult[] Evaluate(
        IReadOnlyList<PamContractTerms> contracts,
        RiskFactorModel                 riskFactors,
        PamMcVasicekRateGenerator       rates,
        DateTime                        baseDate,
        int                             calcDateIndex,
        int                             scenarioStart,
        int                             numScenarios,
        DateTime                        maturityHorizon)
    {
        int  numContracts  = contracts.Count;
        var  results       = new PamMcPvResult[numContracts * numScenarios];
        long baseDateTicks = baseDate.Ticks;

        // Pre-build schedules once (scenario-independent)
        var schedules = new List<ContractEvent>[numContracts];
        for (int c = 0; c < numContracts; c++)
            schedules[c] = PrincipalAtMaturity.Schedule(maturityHorizon, contracts[c]);

        // Parallel over scenarios — each scenario writes to a disjoint region
        System.Threading.Tasks.Parallel.For(0, numScenarios, s =>
        {
            int absScenario = scenarioStart + s;
            int baseIdx     = absScenario * rates.NumMonths;

            for (int c = 0; c < numContracts; c++)
            {
                double pv = ComputePv(
                    contracts[c], schedules[c],
                    rates.DiscountFactors, baseIdx, rates.NumMonths,
                    baseDateTicks, calcDateIndex,
                    rates.ShortRates, absScenario);

                results[c * numScenarios + s] = new PamMcPvResult { PV = pv };
            }
        });

        return results;
    }

    // ── Per-contract, per-scenario walk ──────────────────────────────────

    private static double ComputePv(
        PamContractTerms    terms,
        List<ContractEvent> schedule,
        double[]            discountFactors,
        int                 scenarioBaseIdx,
        int                 numMonths,
        long                baseDateTicks,
        int                 calcDateIndex,
        double[]            shortRates,
        int                 absScenario)
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

            double scenarioRate = shortRates[scenarioBaseIdx + monthIdx];
            double yf           = Math.Max(0.0, (ev.ScheduleTime - prevTime).TotalDays / 365.25);

            double cashFlow = ComputeCashFlow(ev.Type, terms, notional, nominalRate,
                                              accruedInterest, yf, scenarioRate,
                                              monthIdx, calcDateIndex);

            double df = discountFactors[scenarioBaseIdx + monthIdx];
            pv += cashFlow * df;

            ApplyStateTransition(ev.Type, ref notional, ref nominalRate,
                                 ref accruedInterest, yf, terms, scenarioRate,
                                 monthIdx, calcDateIndex);

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
        double           yf,
        double           scenarioRate,
        int              monthIdx,
        int              calcDateIndex)
    {
        return evType switch
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
    }

    private static void ApplyStateTransition(
        EventType        evType,
        ref double       notional,
        ref double       nominalRate,
        ref double       accruedInterest,
        double           yf,
        PamContractTerms terms,
        double           scenarioRate,
        int              monthIdx,
        int              calcDateIndex)
    {
        bool needsAccrual = evType is EventType.IP   or EventType.IPCI
                                     or EventType.RR  or EventType.RRF
                                     or EventType.FP  or EventType.SC;
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
                notional = 0.0; accruedInterest = 0.0; break;
            case EventType.IP:
                accruedInterest = 0.0; break;
            case EventType.IPCI:
                notional += accruedInterest; accruedInterest = 0.0; break;
            case EventType.RR:
                nominalRate = scenarioRate * terms.RateMultiplier + terms.RateSpread;
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
