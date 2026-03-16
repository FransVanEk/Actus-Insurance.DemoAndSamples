/*
 * Blittable structs and ILGPU kernel for PAM Monte Carlo PV computation.
 * Ported from CLI/PamMonteCarlo50Y/McPvKernel.cs.
 *
 * Grid: 2-D launch Index2D(numContracts, numScenarios).
 * Thread (c,s) writes results[c * numScenarios + s].
 */
using System.Runtime.InteropServices;
using ActusInsurance.GPU.Models;
using ILGPU;
using ILGPU.Runtime;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Engines;

// ── Blittable event descriptor ────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
public struct PamMcEventGpu
{
    public long ScheduleTimeTicks;
    public long CalcTimeTicks;
    public int  MonthIndex;
    public int  EventType;
    public int  RateIndex;
    public int  _pad;
}

// ── Per-(contract, scenario) result ──────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
public struct PamMcPvGpuResult
{
    public double PV;
}

// ── Blittable contract descriptor ─────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
public struct PamMcContractGpu
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
    public long   InitialCalcTimeTicks;
}

// ── ILGPU kernel ──────────────────────────────────────────────────────────

public static class PamMcKernel
{
    public static void Kernel(
        Index2D                                              index,
        ArrayView1D<PamMcContractGpu, Stride1D.Dense>       contracts,
        ArrayView1D<PamMcEventGpu,    Stride1D.Dense>       events,
        ArrayView1D<double,           Stride1D.Dense>       discountFactors,
        ArrayView1D<double,           Stride1D.Dense>       shortRates,
        int numMonths,
        int calcDateIndex,
        int numScenarios,
        ArrayView1D<PamMcPvGpuResult, Stride1D.Dense>       results)
    {
        int c = index.X;
        int s = index.Y;
        if (c >= contracts.Length || s >= numScenarios) return;

        PamMcContractGpu contract = contracts[c];

        double notional        = contract.InitialStateNotionalPrincipal;
        double nominalRate     = contract.InitialStateNominalInterestRate;
        double accruedInterest = contract.InitialStateAccruedInterest;
        long   prevCalcTicks   = contract.InitialCalcTimeTicks;

        double pv      = 0.0;
        int    baseIdx = s * numMonths;

        for (int i = 0; i < contract.EventCount; i++)
        {
            PamMcEventGpu ev   = events[contract.EventOffset + i];
            int           mIdx = ev.MonthIndex;
            if (mIdx < 0)          mIdx = 0;
            if (mIdx >= numMonths) mIdx = numMonths - 1;

            // Year fraction (ACT/365.25)
            double yf          = 0.0;
            bool   needAccrual = ev.EventType == GpuEventType.IP   ||
                                 ev.EventType == GpuEventType.IPCI ||
                                 ev.EventType == GpuEventType.RR   ||
                                 ev.EventType == GpuEventType.RRF  ||
                                 ev.EventType == GpuEventType.FP   ||
                                 ev.EventType == GpuEventType.SC;
            if (needAccrual && prevCalcTicks < ev.CalcTimeTicks)
            {
                const long   ticksPerDay = 864_000_000_000L;
                const double daysPerYear = 365.25;
                yf = (double)(ev.CalcTimeTicks - prevCalcTicks) / ticksPerDay / daysPerYear;
                if (yf < 0.0) yf = 0.0;
            }

            if (needAccrual && yf > 0.0 && notional != 0.0 && nominalRate != 0.0)
                accruedInterest += nominalRate * notional * yf;

            double scenRate  = shortRates[baseIdx + mIdx];
            double cashFlow;

            switch (ev.EventType)
            {
                case GpuEventType.IED:
                    cashFlow = (double)contract.RoleSign * -1.0 *
                               (contract.NotionalPrincipal + contract.PremiumDiscountAtIED);
                    break;
                case GpuEventType.MD:
                    cashFlow = notional; break;
                case GpuEventType.PRD:
                    cashFlow = (double)contract.RoleSign * -1.0 * contract.PriceAtPurchaseDate;
                    break;
                case GpuEventType.TD:
                    cashFlow = (double)contract.RoleSign * contract.PriceAtTerminationDate;
                    break;
                case GpuEventType.IP:
                    cashFlow = accruedInterest; break;
                case GpuEventType.IPCI:
                case GpuEventType.RR:
                case GpuEventType.RRF:
                    cashFlow = 0.0; break;
                case GpuEventType.FP:
                    cashFlow = contract.FeeBasisN == 1
                        ? contract.FeeAccrued + notional * contract.FeeRate * yf
                        : (double)contract.RoleSign * contract.FeeRate;
                    break;
                default:
                    cashFlow = 0.0; break;
            }

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
                    notional = 0.0; accruedInterest = 0.0; break;
                case GpuEventType.IP:
                    accruedInterest = 0.0; break;
                case GpuEventType.IPCI:
                    notional += accruedInterest; accruedInterest = 0.0; break;
                case GpuEventType.RR:
                    nominalRate = scenRate * contract.RateMultiplier + contract.RateSpread;
                    break;
                case GpuEventType.RRF:
                    if (contract.HasNextResetRate == 1)
                        nominalRate = contract.NextResetRate;
                    break;
            }

            prevCalcTicks = ev.CalcTimeTicks;
        }

        results[c * numScenarios + s] = new PamMcPvGpuResult { PV = pv };
    }
}
