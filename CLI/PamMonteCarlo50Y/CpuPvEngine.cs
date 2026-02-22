using ActusInsurance.Core.CPU.Contracts;
using ActusInsurance.Core.Events;
using ActusInsurance.Core.Externals;
using ActusInsurance.Core.Models;
using ActusInsurance.Core.Types;
using System.Globalization;

namespace PamMonteCarlo50Y;

/// <summary>
/// Result of a single (contract, scenario) PV computation.
/// </summary>
public readonly struct McPvResult
{
    /// <summary>Discounted present value of all cash flows.</summary>
    public double PV { get; init; }
}

/// <summary>
/// A single cashflow event captured during the valuation walk.
/// Used to produce the per-event (contract × scenario × time) output.
/// </summary>
public sealed class CashflowEventDetail
{
    public string   ContractId           { get; init; } = string.Empty;
    public int      ScenarioId           { get; init; }
    public DateTime EventDate            { get; init; }
    public int      TimeIndex            { get; init; }
    public string   EventType            { get; init; } = string.Empty;
    public double   UndiscountedCashflow { get; init; }
    public double   DiscountFactor       { get; init; }
    public double   DiscountedCashflow   { get; init; }
}

/// <summary>
/// CPU discounted-PV engine for Monte Carlo scenario analysis.
///
/// <b>Algorithm</b>:
/// <list type="number">
///   <item>Generate PAM schedule via <c>PrincipalAtMaturity.Schedule</c>.</item>
///   <item>For each event, compute the undiscounted cash flow (same formulas as
///         the GPU ScenarioBatchKernel).</item>
///   <item>Map the event's schedule time to a month index on the Vasicek grid.</item>
///   <item>Multiply cash flow by the per-scenario discount factor at that month.</item>
///   <item>Sum to obtain the contract PV for that scenario.</item>
/// </list>
///
/// <b>Output layout</b>:
/// <c>results[contractIndex * numScenarios + scenarioIndex]</c>
/// </summary>
public static class CpuPvEngine
{
    private const double TicksPerDay  = 864_000_000_000.0;
    private const double DaysPerMonth = 365.25 / 12.0;

    /// <summary>
    /// Evaluate a batch of contracts under all scenarios using the CPU.
    /// </summary>
    /// <param name="contracts">Slice of portfolio contracts.</param>
    /// <param name="riskFactors">Base risk-factor model (for floating rate RR events).</param>
    /// <param name="rates">Pre-generated Vasicek short-rate + DF arrays.</param>
    /// <param name="baseDate">Simulation t=0 date (defines month-index grid).</param>
    /// <param name="calcDateIndex">Month index acting as prior/after boundary.</param>
    /// <param name="scenarioStart">First scenario index to evaluate.</param>
    /// <param name="numScenarios">Number of scenarios to evaluate.</param>
    /// <param name="maturityHorizon">Schedule generation horizon (upper bound).</param>
    /// <returns>Flat <see cref="McPvResult"/> array of size contracts.Count × numScenarios.</returns>
    public static McPvResult[] Evaluate(
        IReadOnlyList<PamContractTerms> contracts,
        RiskFactorModel                 riskFactors,
        VasicekRateGenerator            rates,
        DateTime                        baseDate,
        int                             calcDateIndex,
        int                             scenarioStart,
        int                             numScenarios,
        DateTime                        maturityHorizon)
    {
        int numContracts = contracts.Count;
        var results      = new McPvResult[numContracts * numScenarios];
        long baseDateTicks = baseDate.Ticks;

        // Pre-build schedule for each contract once (schedule is scenario-independent)
        var schedules = new List<ContractEvent>[numContracts];
        for (int c = 0; c < numContracts; c++)
            schedules[c] = PrincipalAtMaturity.Schedule(maturityHorizon, contracts[c]);

        // Parallel over scenarios is valid because each scenario writes to a
        // disjoint region of results[].
        System.Threading.Tasks.Parallel.For(0, numScenarios, s =>
        {
            int absScenario = scenarioStart + s;
            int baseIdx     = absScenario * rates.NumMonths;

            for (int c = 0; c < numContracts; c++)
            {
                var terms    = contracts[c];
                var schedule = schedules[c];
                double pv    = ComputePv(terms, schedule, rates.DiscountFactors,
                                         baseIdx, rates.NumMonths, baseDateTicks,
                                         calcDateIndex, riskFactors, absScenario,
                                         rates.ShortRates);
                results[c * numScenarios + s] = new McPvResult { PV = pv };
            }
        });

        return results;
    }

    /// <summary>
    /// Evaluate a sampled subset of contracts × scenarios, capturing the full
    /// per-event cashflow detail (contract × scenario × time × event type).
    ///
    /// Intended for small subsets; runs single-threaded so capture is safe.
    /// </summary>
    /// <param name="contractIndices">Indices into <paramref name="contracts"/> to capture.</param>
    /// <param name="scenarioCount">Number of scenarios to capture (from scenarioStart).</param>
    /// <returns>List of <see cref="CashflowEventDetail"/> ordered by ContractId, ScenarioId, EventDate.</returns>
    public static List<CashflowEventDetail> EvaluateCashflows(
        IReadOnlyList<PamContractTerms> contracts,
        int[]                           contractIndices,
        string[]                        contractIds,
        RiskFactorModel                 riskFactors,
        VasicekRateGenerator            rates,
        DateTime                        baseDate,
        int                             calcDateIndex,
        int                             scenarioStart,
        int                             scenarioCount,
        DateTime                        maturityHorizon)
    {
        var result     = new List<CashflowEventDetail>();
        long baseTicks = baseDate.Ticks;

        foreach (int c in contractIndices)
        {
            var terms    = contracts[c];
            var schedule = PrincipalAtMaturity.Schedule(maturityHorizon, terms);
            string cid   = c < contractIds.Length ? contractIds[c] : c.ToString(CultureInfo.InvariantCulture);

            for (int sRel = 0; sRel < scenarioCount; sRel++)
            {
                int absS    = scenarioStart + sRel;
                int baseIdx = absS * rates.NumMonths;

                CollectCashflows(
                    terms, schedule, rates.DiscountFactors,
                    rates.ShortRates, baseIdx, rates.NumMonths,
                    baseTicks, calcDateIndex, cid, absS, result);
            }
        }

        return result;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Per-contract, per-scenario PV calculation
    // ──────────────────────────────────────────────────────────────────────

    private static double ComputePv(
        PamContractTerms    terms,
        List<ContractEvent> schedule,
        double[]            discountFactors,
        int                 scenarioBaseIdx,    // absScenario * numMonths
        int                 numMonths,
        long                baseDateTicks,
        int                 calcDateIndex,
        RiskFactorModel     riskFactors,
        int                 absScenario,
        double[]            shortRates)
    {
        double pv                 = 0.0;
        double notional           = terms.RoleSign * terms.NotionalPrincipal;
        double nominalRate        = terms.NominalInterestRate;
        double accruedInterest    = terms.AccruedInterest;
        DateTime prevScheduleTime = terms.StatusDate;

        foreach (var ev in schedule)
        {
            // Map event schedule time → month index on Vasicek grid
            int monthIdx = ScheduleTimeToMonthIndex(ev.ScheduleTime, baseDateTicks);
            monthIdx     = Math.Max(0, Math.Min(monthIdx, numMonths - 1));

            // Prior/after rate selection: events before calcDateIndex use base rate;
            // events at or after use the scenario short rate as the "after" rate.
            // For floating-rate (RR) events, use scenario rate as the reset rate.
            double scenarioRate = shortRates[scenarioBaseIdx + monthIdx];

            // Year fraction for this period
            double yf = (ev.ScheduleTime - prevScheduleTime).TotalDays / 365.25;
            yf = Math.Max(0.0, yf);

            // Compute cash flow (undiscounted) — matches ScenarioBatchKernel logic
            double cashFlow = ComputeCashFlow(ev.Type, terms, notional, nominalRate,
                                               accruedInterest, yf, scenarioRate,
                                               monthIdx, calcDateIndex);

            // Discount factor for this event's month
            double df = discountFactors[scenarioBaseIdx + monthIdx];

            pv += cashFlow * df;

            // State transitions (mirrors kernel logic)
            ApplyStateTransition(ev.Type, ref notional, ref nominalRate,
                                  ref accruedInterest, yf, terms, scenarioRate,
                                  monthIdx, calcDateIndex);

            prevScheduleTime = ev.ScheduleTime;
        }

        return pv;
    }

    /// <summary>
    /// Walk the event schedule for one contract × scenario, collecting per-event
    /// cashflow detail into <paramref name="result"/>.
    /// </summary>
    private static void CollectCashflows(
        PamContractTerms          terms,
        List<ContractEvent>       schedule,
        double[]                  discountFactors,
        double[]                  shortRates,
        int                       scenarioBaseIdx,
        int                       numMonths,
        long                      baseDateTicks,
        int                       calcDateIndex,
        string                    contractId,
        int                       absScenario,
        List<CashflowEventDetail> result)
    {
        double notional        = terms.RoleSign * terms.NotionalPrincipal;
        double nominalRate     = terms.NominalInterestRate;
        double accruedInterest = terms.AccruedInterest;
        DateTime prevTime      = terms.StatusDate;

        foreach (var ev in schedule)
        {
            int monthIdx  = ScheduleTimeToMonthIndex(ev.ScheduleTime, baseDateTicks);
            monthIdx      = Math.Max(0, Math.Min(monthIdx, numMonths - 1));
            double scenRate = shortRates[scenarioBaseIdx + monthIdx];
            double yf       = Math.Max(0.0, (ev.ScheduleTime - prevTime).TotalDays / 365.25);
            double cf       = ComputeCashFlow(ev.Type, terms, notional, nominalRate,
                                              accruedInterest, yf, scenRate, monthIdx, calcDateIndex);
            double df       = discountFactors[scenarioBaseIdx + monthIdx];

            // Only emit rows with a non-zero cashflow (skips pure-state-change events
            // like RR, IPCI unless the user wants all events).
            if (cf != 0.0 || ev.Type is EventType.IED or EventType.MD or EventType.IP)
            {
                result.Add(new CashflowEventDetail
                {
                    ContractId           = contractId,
                    ScenarioId           = absScenario,
                    EventDate            = ev.ScheduleTime,
                    TimeIndex            = monthIdx,
                    EventType            = ev.Type.ToString(),
                    UndiscountedCashflow = cf,
                    DiscountFactor       = df,
                    DiscountedCashflow   = cf * df,
                });
            }

            ApplyStateTransition(ev.Type, ref notional, ref nominalRate,
                                  ref accruedInterest, yf, terms, scenRate,
                                  monthIdx, calcDateIndex);
            prevTime = ev.ScheduleTime;
        }
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
        // Accrue interest for events that require it (before applying transition)
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
                // Use scenario short rate as the new rate + spread
                double newRate  = scenarioRate * terms.RateMultiplier + terms.RateSpread;
                nominalRate     = newRate;
                break;

            case EventType.RRF:
                if (terms.NextResetRate.HasValue)
                    nominalRate = terms.NextResetRate.Value;
                break;

            case EventType.FP:
                break;
        }
    }

    /// <summary>
    /// Map a schedule date to the nearest month index on the Vasicek grid.
    /// monthIndex = round( (scheduleDate - baseDate).TotalDays / (365.25/12) )
    /// </summary>
    private static int ScheduleTimeToMonthIndex(DateTime scheduleDate, long baseDateTicks)
    {
        double days = (scheduleDate.Ticks - baseDateTicks) / TicksPerDay;
        return (int)Math.Round(days / DaysPerMonth);
    }
}


/// <summary>
/// CPU discounted-PV engine for Monte Carlo scenario analysis.
///
/// <b>Algorithm</b>:
/// <list type="number">
///   <item>Generate PAM schedule via <c>PrincipalAtMaturity.Schedule</c>.</item>
///   <item>For each event, compute the undiscounted cash flow (same formulas as
///         the GPU ScenarioBatchKernel).</item>
///   <item>Map the event's schedule time to a month index on the Vasicek grid.</item>
///   <item>Multiply cash flow by the per-scenario discount factor at that month.</item>
///   <item>Sum to obtain the contract PV for that scenario.</item>
/// </list>
///
/// <b>Output layout</b>:
/// <c>results[contractIndex * numScenarios + scenarioIndex]</c>
/// </summary>
public static class CpuPvEngine
{
    private const double TicksPerDay  = 864_000_000_000.0;
    private const double DaysPerMonth = 365.25 / 12.0;

    /// <summary>
    /// Evaluate a batch of contracts under all scenarios using the CPU.
    /// </summary>
    /// <param name="contracts">Slice of portfolio contracts.</param>
    /// <param name="riskFactors">Base risk-factor model (for floating rate RR events).</param>
    /// <param name="rates">Pre-generated Vasicek short-rate + DF arrays.</param>
    /// <param name="baseDate">Simulation t=0 date (defines month-index grid).</param>
    /// <param name="calcDateIndex">Month index acting as prior/after boundary.</param>
    /// <param name="scenarioStart">First scenario index to evaluate.</param>
    /// <param name="numScenarios">Number of scenarios to evaluate.</param>
    /// <param name="maturityHorizon">Schedule generation horizon (upper bound).</param>
    /// <returns>Flat <see cref="McPvResult"/> array of size contracts.Count × numScenarios.</returns>
    public static McPvResult[] Evaluate(
        IReadOnlyList<PamContractTerms> contracts,
        RiskFactorModel                 riskFactors,
        VasicekRateGenerator            rates,
        DateTime                        baseDate,
        int                             calcDateIndex,
        int                             scenarioStart,
        int                             numScenarios,
        DateTime                        maturityHorizon)
    {
        int numContracts = contracts.Count;
        var results      = new McPvResult[numContracts * numScenarios];
        long baseDateTicks = baseDate.Ticks;

        // Pre-build schedule for each contract once (schedule is scenario-independent)
        var schedules = new List<ContractEvent>[numContracts];
        for (int c = 0; c < numContracts; c++)
            schedules[c] = PrincipalAtMaturity.Schedule(maturityHorizon, contracts[c]);

        // Parallel over scenarios is valid because each scenario writes to a
        // disjoint region of results[].
        System.Threading.Tasks.Parallel.For(0, numScenarios, s =>
        {
            int absScenario = scenarioStart + s;
            int baseIdx     = absScenario * rates.NumMonths;

            for (int c = 0; c < numContracts; c++)
            {
                var terms    = contracts[c];
                var schedule = schedules[c];
                double pv    = ComputePv(terms, schedule, rates.DiscountFactors,
                                         baseIdx, rates.NumMonths, baseDateTicks,
                                         calcDateIndex, riskFactors, absScenario,
                                         rates.ShortRates);
                results[c * numScenarios + s] = new McPvResult { PV = pv };
            }
        });

        return results;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Per-contract, per-scenario PV calculation
    // ──────────────────────────────────────────────────────────────────────

    private static double ComputePv(
        PamContractTerms    terms,
        List<ContractEvent> schedule,
        double[]            discountFactors,
        int                 scenarioBaseIdx,    // absScenario * numMonths
        int                 numMonths,
        long                baseDateTicks,
        int                 calcDateIndex,
        RiskFactorModel     riskFactors,
        int                 absScenario,
        double[]            shortRates)
    {
        double pv                 = 0.0;
        double notional           = terms.RoleSign * terms.NotionalPrincipal;
        double nominalRate        = terms.NominalInterestRate;
        double accruedInterest    = terms.AccruedInterest;
        DateTime prevScheduleTime = terms.StatusDate;

        foreach (var ev in schedule)
        {
            // Map event schedule time → month index on Vasicek grid
            int monthIdx = ScheduleTimeToMonthIndex(ev.ScheduleTime, baseDateTicks);
            monthIdx     = Math.Max(0, Math.Min(monthIdx, numMonths - 1));

            // Prior/after rate selection: events before calcDateIndex use base rate;
            // events at or after use the scenario short rate as the "after" rate.
            // For floating-rate (RR) events, use scenario rate as the reset rate.
            double scenarioRate = shortRates[scenarioBaseIdx + monthIdx];

            // Year fraction for this period
            double yf = (ev.ScheduleTime - prevScheduleTime).TotalDays / 365.25;
            yf = Math.Max(0.0, yf);

            // Compute cash flow (undiscounted) — matches ScenarioBatchKernel logic
            double cashFlow = ComputeCashFlow(ev.Type, terms, notional, nominalRate,
                                               accruedInterest, yf, scenarioRate,
                                               monthIdx, calcDateIndex);

            // Discount factor for this event's month
            double df = discountFactors[scenarioBaseIdx + monthIdx];

            pv += cashFlow * df;

            // State transitions (mirrors kernel logic)
            ApplyStateTransition(ev.Type, ref notional, ref nominalRate,
                                  ref accruedInterest, yf, terms, scenarioRate,
                                  monthIdx, calcDateIndex);

            prevScheduleTime = ev.ScheduleTime;
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
        // Accrue interest for events that require it (before applying transition)
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
                // Use scenario short rate as the new rate + spread
                double newRate  = scenarioRate * terms.RateMultiplier + terms.RateSpread;
                nominalRate     = newRate;
                break;

            case EventType.RRF:
                if (terms.NextResetRate.HasValue)
                    nominalRate = terms.NextResetRate.Value;
                break;

            case EventType.FP:
                break;
        }
    }

    /// <summary>
    /// Map a schedule date to the nearest month index on the Vasicek grid.
    /// monthIndex = round( (scheduleDate - baseDate).TotalDays / (365.25/12) )
    /// </summary>
    private static int ScheduleTimeToMonthIndex(DateTime scheduleDate, long baseDateTicks)
    {
        double days = (scheduleDate.Ticks - baseDateTicks) / TicksPerDay;
        return (int)Math.Round(days / DaysPerMonth);
    }
}
