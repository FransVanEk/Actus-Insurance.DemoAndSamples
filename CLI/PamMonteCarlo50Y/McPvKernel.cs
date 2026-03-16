/*
 * PamMonteCarlo50Y Demo — GPU PV kernel and supporting blittable structs.
 *
 * McPamEventGpu extends the concept of PamEventGpu with a MonthIndex field
 * so the GPU kernel can directly index into the pre-computed discount-factor array.
 *
 * McPvKernel is a 2-D ILGPU kernel: grid = (numContracts, numScenarios).
 * Thread (c, s) computes the discounted PV for contract c under scenario s.
 */
using System.Runtime.InteropServices;
using ActusInsurance.GPU.Models;
using ILGPU;
using ILGPU.Runtime;

namespace PamMonteCarlo50Y;

// ────────────────────────────────────────────────────────────────────────
// Blittable event struct (adds MonthIndex to the base event fields)
// ────────────────────────────────────────────────────────────────────────

/// <summary>
/// Blittable GPU event descriptor for the Monte Carlo PV kernel.
/// Identical to <c>PamEventGpu</c> but adds <see cref="MonthIndex"/> so
/// the kernel can look up <c>discountFactors[s * numMonths + MonthIndex]</c>
/// without recomputing the date arithmetic on the GPU.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct McPamEventGpu
{
    /// <summary>Original (unshifted) schedule time in ticks.</summary>
    public long ScheduleTimeTicks;

    /// <summary>Business-day-calc-shifted schedule time in ticks.</summary>
    public long CalcTimeTicks;

    /// <summary>Month index on the Vasicek grid (0-based from simulation start).</summary>
    public int MonthIndex;

    /// <summary>Event type code (see <c>GpuEventType</c> constants).</summary>
    public int EventType;

    /// <summary>–1 = no rate lookup; ≥0 = index for floating-rate scenario rate.</summary>
    public int RateIndex;

    /// <summary>Padding for 8-byte alignment.</summary>
    public int _pad;
}

// ────────────────────────────────────────────────────────────────────────
// Per-(contract, scenario) PV result
// ────────────────────────────────────────────────────────────────────────

/// <summary>
/// Blittable result produced by <see cref="McPvKernel"/> for each
/// (contract, scenario) pair.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct McPvGpuResult
{
    /// <summary>Discounted present value.</summary>
    public double PV;
}

// ────────────────────────────────────────────────────────────────────────
// Contract descriptor (subset used by the MC PV kernel)
// ────────────────────────────────────────────────────────────────────────

/// <summary>
/// Blittable contract descriptor for the MC PV kernel.
/// Mirrors <c>PamContractGpu</c> but is kept self-contained in the demo
/// project to avoid coupling the kernel signature to the library struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct McContractGpu
{
    public double NotionalPrincipal;
    public double NominalInterestRate;
    public double AccruedInterest;
    public double PremiumDiscountAtIED;
    public double PriceAtPurchaseDate;
    public double PriceAtTerminationDate;
    public double RateSpread;
    public double RateMultiplier;
    public double NextResetRate;
    public double FeeRate;
    public double FeeAccrued;
    public int    RoleSign;
    public int    HasNextResetRate;
    public int    FeeBasisN;
    public int    EventOffset;
    public int    EventCount;
    public double InitialStateNotionalPrincipal;
    public double InitialStateNominalInterestRate;
    public double InitialStateAccruedInterest;
    /// <summary>Business-day-adjusted StatusDate ticks (initial prevCalcTimeTicks).</summary>
    public long   InitialCalcTimeTicks;
}

// ────────────────────────────────────────────────────────────────────────
// ILGPU kernel
// ────────────────────────────────────────────────────────────────────────

/// <summary>
/// ILGPU kernel that computes discounted present values for PAM contracts
/// under Monte Carlo interest-rate scenarios.
///
/// <b>Grid</b>: 2-D launch <c>Index2D(numContracts, numScenarios)</c>.
/// Thread <c>(c, s)</c> writes to <c>results[c * numScenarios + s]</c>.
///
/// <b>Discounting</b>:
/// <code>
///   DF[s,t] = discountFactors[s * numMonths + monthIndex]
///   PV      = sum_events( cashFlow[event] * DF[s, event.MonthIndex] )
/// </code>
///
/// <b>Prior/after rate selection</b> for floating contracts:
/// <code>
///   rate = monthIndex &lt; calcDateIndex
///          ? baseRate (from McContractGpu.NominalInterestRate)
///          : shortRates[s * numMonths + monthIndex] * multiplier + spread
/// </code>
/// </summary>
public static class McPvKernel
{
    public static void Kernel(
        Index2D                                            index,
        ArrayView1D<McContractGpu,    Stride1D.Dense>     contracts,
        ArrayView1D<McPamEventGpu,    Stride1D.Dense>     events,
        ArrayView1D<double,           Stride1D.Dense>     discountFactors,
        ArrayView1D<double,           Stride1D.Dense>     shortRates,
        int  numMonths,
        int  calcDateIndex,
        int  numScenarios,
        ArrayView1D<McPvGpuResult,    Stride1D.Dense>     results)
    {
        int c = index.X;
        int s = index.Y;

        if (c >= contracts.Length || s >= numScenarios) return;

        McContractGpu contract = contracts[c];

        double notional        = contract.InitialStateNotionalPrincipal;
        double nominalRate     = contract.InitialStateNominalInterestRate;
        double accruedInterest = contract.InitialStateAccruedInterest;
        long   prevCalcTicks   = contract.InitialCalcTimeTicks;

        double pv = 0.0;

        int evOffset = contract.EventOffset;
        int evCount  = contract.EventCount;
        int baseIdx  = s * numMonths;

        for (int i = 0; i < evCount; i++)
        {
            McPamEventGpu ev  = events[evOffset + i];
            int           mIdx = ev.MonthIndex;
            if (mIdx < 0)       mIdx = 0;
            if (mIdx >= numMonths) mIdx = numMonths - 1;

            // Year fraction: ACT/365.25 from previous to current CalcTimeTicks
            double yf = 0.0;
            bool needsAccrual = ev.EventType == GpuEventType.IP   ||
                                ev.EventType == GpuEventType.IPCI ||
                                ev.EventType == GpuEventType.RR   ||
                                ev.EventType == GpuEventType.RRF  ||
                                ev.EventType == GpuEventType.FP   ||
                                ev.EventType == GpuEventType.SC;
            if (needsAccrual && prevCalcTicks < ev.CalcTimeTicks)
            {
                const long   ticksPerDay = 864_000_000_000L;
                const double daysPerYear = 365.25;
                yf = (double)(ev.CalcTimeTicks - prevCalcTicks) / ticksPerDay / daysPerYear;
                if (yf < 0.0) yf = 0.0;
            }

            // Accrual (before payoff, after yf is computed)
            if (needsAccrual && yf > 0.0 && notional != 0.0 && nominalRate != 0.0)
                accruedInterest += nominalRate * notional * yf;

            // Scenario rate at this month
            double scenRate = shortRates[baseIdx + mIdx];

            // Cash flow
            double cashFlow;
            switch (ev.EventType)
            {
                case GpuEventType.IED:
                    cashFlow = (double)contract.RoleSign * -1.0 *
                               (contract.NotionalPrincipal + contract.PremiumDiscountAtIED);
                    break;
                case GpuEventType.MD:
                    cashFlow = notional;
                    break;
                case GpuEventType.PRD:
                    cashFlow = (double)contract.RoleSign * -1.0 * contract.PriceAtPurchaseDate;
                    break;
                case GpuEventType.TD:
                    cashFlow = (double)contract.RoleSign * contract.PriceAtTerminationDate;
                    break;
                case GpuEventType.IP:
                    cashFlow = accruedInterest;   // accrual already done above
                    break;
                case GpuEventType.IPCI:
                    cashFlow = 0.0;
                    break;
                case GpuEventType.RR:
                case GpuEventType.RRF:
                    cashFlow = 0.0;
                    break;
                case GpuEventType.FP:
                    cashFlow = contract.FeeBasisN == 1
                        ? contract.FeeAccrued + notional * contract.FeeRate * yf
                        : (double)contract.RoleSign * contract.FeeRate;
                    break;
                default:
                    cashFlow = 0.0;
                    break;
            }

            // Discount factor
            double df = discountFactors[baseIdx + mIdx];
            pv += cashFlow * df;

            // State transitions
            switch (ev.EventType)
            {
                case GpuEventType.IED:
                    notional    = (double)contract.RoleSign * contract.NotionalPrincipal;
                    nominalRate = contract.NominalInterestRate;
                    break;
                case GpuEventType.MD:
                case GpuEventType.TD:
                    notional        = 0.0;
                    accruedInterest = 0.0;
                    break;
                case GpuEventType.IP:
                    accruedInterest = 0.0;
                    break;
                case GpuEventType.IPCI:
                    notional       += accruedInterest;
                    accruedInterest = 0.0;
                    break;
                case GpuEventType.RR:
                    {
                        double newRate = scenRate * contract.RateMultiplier + contract.RateSpread;
                        nominalRate = newRate;
                    }
                    break;
                case GpuEventType.RRF:
                    if (contract.HasNextResetRate == 1)
                        nominalRate = contract.NextResetRate;
                    break;
                case GpuEventType.FP:
                    break;
            }

            // Advance calc time for next event's yf computation
            prevCalcTicks = ev.CalcTimeTicks;
        }

        results[c * numScenarios + s] = new McPvGpuResult { PV = pv };
    }
}
