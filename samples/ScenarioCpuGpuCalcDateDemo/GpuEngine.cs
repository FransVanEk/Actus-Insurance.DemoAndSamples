/*
 * ScenarioCpuGpuCalcDateDemo — GPU present-value engine and ILGPU kernel.
 *
 * Adapted from CLI/PamMonteCarlo50Y/GpuPvEngine.cs and McPvKernel.cs.
 * Key changes:
 *   - Uses RateScenarios instead of VasicekRateGenerator.
 *   - Namespace changed to ScenarioCpuGpuCalcDateDemo.
 *   - Both the host executor (GpuEngine) and the ILGPU kernel (McPvKernel)
 *     are combined in this single file for self-containedness.
 *
 * Accelerator fallback: CUDA → OpenCL → ILGPU CPU simulator.
 * In environments without a physical GPU (e.g., CI) the ILGPU CPU
 * simulator is used, which produces bit-for-bit identical results to
 * the C# CpuEngine and therefore confirms CPU ≡ GPU within tolerance.
 */
using System.Runtime.InteropServices;
using ActusInsurance.Core.CPU.Contracts;
using ActusInsurance.Core.Models;
using ActusInsurance.Core.Types;
using ActusInsurance.GPU.Models;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;

namespace ScenarioCpuGpuCalcDateDemo;

// ── Blittable structs for the ILGPU kernel ────────────────────────────────

/// <summary>Blittable GPU event descriptor (mirrors McPamEventGpu).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DemoEventGpu
{
    public long   ScheduleTimeTicks;
    public long   CalcTimeTicks;
    public int    MonthIndex;
    public int    EventType;
    public int    RateIndex;
    public int    _pad;
}

/// <summary>Blittable contract descriptor for the demo GPU kernel.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DemoContractGpu
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

/// <summary>Per-(contract, scenario) PV result from the GPU kernel.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DemoPvGpuResult
{
    public double PV;
}

// ── ILGPU kernel ──────────────────────────────────────────────────────────

/// <summary>
/// 2-D ILGPU kernel: grid = (numContracts, numScenarios).
/// Thread (c, s) computes the discounted PV for contract c under scenario s
/// and writes to results[c * numScenarios + s].
/// </summary>
public static class DemoKernel
{
    public static void Kernel(
        Index2D                                              index,
        ArrayView1D<DemoContractGpu,   Stride1D.Dense>      contracts,
        ArrayView1D<DemoEventGpu,      Stride1D.Dense>      events,
        ArrayView1D<double,            Stride1D.Dense>      discountFactors,
        ArrayView1D<double,            Stride1D.Dense>      shortRates,
        int numMonths,
        int numScenarios,
        ArrayView1D<DemoPvGpuResult,   Stride1D.Dense>      results)
    {
        int c = index.X;
        int s = index.Y;
        if (c >= contracts.Length || s >= numScenarios) return;

        DemoContractGpu ct  = contracts[c];
        double notional        = ct.InitialStateNotionalPrincipal;
        double nominalRate     = ct.InitialStateNominalInterestRate;
        double accruedInterest = ct.InitialStateAccruedInterest;
        long   prevCalcTicks   = ct.InitialCalcTimeTicks;
        double pv              = 0.0;
        int    evOffset        = ct.EventOffset;
        int    evCount         = ct.EventCount;
        int    baseIdx         = s * numMonths;

        for (int i = 0; i < evCount; i++)
        {
            DemoEventGpu ev   = events[evOffset + i];
            int          mIdx = ev.MonthIndex;
            if (mIdx < 0)          mIdx = 0;
            if (mIdx >= numMonths) mIdx = numMonths - 1;

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

            if (needsAccrual && yf > 0.0 && notional != 0.0 && nominalRate != 0.0)
                accruedInterest += nominalRate * notional * yf;

            double scenRate = shortRates[baseIdx + mIdx];

            double cashFlow;
            switch (ev.EventType)
            {
                case GpuEventType.IED:
                    cashFlow = (double)ct.RoleSign * -1.0 *
                               (ct.NotionalPrincipal + ct.PremiumDiscountAtIED);
                    break;
                case GpuEventType.MD:
                    cashFlow = notional;
                    break;
                case GpuEventType.PRD:
                    cashFlow = (double)ct.RoleSign * -1.0 * ct.PriceAtPurchaseDate;
                    break;
                case GpuEventType.TD:
                    cashFlow = (double)ct.RoleSign * ct.PriceAtTerminationDate;
                    break;
                case GpuEventType.IP:
                    cashFlow = accruedInterest;
                    break;
                case GpuEventType.IPCI:
                    cashFlow = 0.0;
                    break;
                case GpuEventType.RR:
                case GpuEventType.RRF:
                    cashFlow = 0.0;
                    break;
                case GpuEventType.FP:
                    cashFlow = ct.FeeBasisN == 1
                        ? ct.FeeAccrued + notional * ct.FeeRate * yf
                        : (double)ct.RoleSign * ct.FeeRate;
                    break;
                default:
                    cashFlow = 0.0;
                    break;
            }

            double df = discountFactors[baseIdx + mIdx];
            pv += cashFlow * df;

            switch (ev.EventType)
            {
                case GpuEventType.IED:
                    notional    = (double)ct.RoleSign * ct.NotionalPrincipal;
                    nominalRate = ct.NominalInterestRate;
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
                    nominalRate = scenRate * ct.RateMultiplier + ct.RateSpread;
                    break;
                case GpuEventType.RRF:
                    if (ct.HasNextResetRate == 1)
                        nominalRate = ct.NextResetRate;
                    break;
            }

            prevCalcTicks = ev.CalcTimeTicks;
        }

        results[c * numScenarios + s] = new DemoPvGpuResult { PV = pv };
    }
}

// ── Host-side GPU executor ────────────────────────────────────────────────

/// <summary>
/// Host-side executor for <see cref="DemoKernel"/>.
///
/// Converts PAM contracts to blittable GPU descriptors, uploads them to
/// device buffers, launches the kernel, and returns per-(contract, scenario)
/// PV results.
///
/// Fallback chain: CUDA → OpenCL → ILGPU CPU simulator.
/// </summary>
public sealed class GpuEngine : IDisposable
{
    private readonly Context     _context;
    private readonly Accelerator _accelerator;
    private bool _disposed;

    private Action<Index2D,
                    ArrayView1D<DemoContractGpu,  Stride1D.Dense>,
                    ArrayView1D<DemoEventGpu,     Stride1D.Dense>,
                    ArrayView1D<double,           Stride1D.Dense>,
                    ArrayView1D<double,           Stride1D.Dense>,
                    int, int,
                    ArrayView1D<DemoPvGpuResult,  Stride1D.Dense>>? _kernel;

    private MemoryBuffer1D<DemoContractGpu,  Stride1D.Dense>? _contractsBuf;
    private MemoryBuffer1D<DemoEventGpu,     Stride1D.Dense>? _eventsBuf;
    private MemoryBuffer1D<double,           Stride1D.Dense>? _dfBuf;
    private MemoryBuffer1D<double,           Stride1D.Dense>? _ratesBuf;
    private MemoryBuffer1D<DemoPvGpuResult,  Stride1D.Dense>? _resultsBuf;
    private long _contractsCap, _eventsCap, _dfCap, _ratesCap, _resultsCap;

    /// <summary>Name reported by the underlying ILGPU accelerator.</summary>
    public string AcceleratorName => _accelerator.Name;

    private GpuEngine(Context context, Accelerator accelerator)
    {
        _context     = context;
        _accelerator = accelerator;
    }

    /// <summary>
    /// Create an engine using the best available accelerator
    /// (CUDA → OpenCL → ILGPU CPU simulator).
    /// </summary>
    public static GpuEngine Create()
    {
        var ctx = Context.CreateDefault();
        Accelerator? acc = TryCreate(ctx, AcceleratorType.Cuda)
                        ?? TryCreate(ctx, AcceleratorType.OpenCL)
                        ?? ctx.CreateCPUAccelerator(0);
        var eng = new GpuEngine(ctx, acc!);
        eng.EnsureKernel();
        return eng;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate all contracts in <paramref name="contracts"/> against all
    /// scenarios in <paramref name="rates"/>.
    ///
    /// Returns a flat array of size contracts.Count × numScenarios,
    /// indexed as <c>[contractIndex * numScenarios + scenarioIndex]</c>.
    /// </summary>
    public DemoPvGpuResult[] Evaluate(
        IReadOnlyList<PamContractTerms> contracts,
        RateScenarios                   rates,
        DateTime                        baseDate,
        DateTime                        maturityHorizon)
    {
        if (contracts.Count == 0 || rates.NumScenarios == 0)
            return Array.Empty<DemoPvGpuResult>();

        EnsureKernel();

        int  numContracts  = contracts.Count;
        int  numScenarios  = rates.NumScenarios;
        int  numMonths     = rates.NumMonths;
        long baseDateTicks = baseDate.Ticks;

        // Build GPU descriptors on the CPU
        BuildGpuArrays(contracts, maturityHorizon, baseDateTicks, numMonths,
                       out var gpuContracts, out var gpuEvents);

        int scenarioRateLen = numScenarios * numMonths;
        int eventCount      = gpuEvents.Length;
        int outcomeCount    = numContracts * numScenarios;

        // Allocate / grow device buffers
        EnsureBuffers(numContracts, Math.Max(eventCount, 1), scenarioRateLen, outcomeCount);

        // H2D copies
        _contractsBuf!.View.SubView(0, numContracts).CopyFromCPU(gpuContracts);
        if (eventCount > 0)
            _eventsBuf!.View.SubView(0, eventCount).CopyFromCPU(gpuEvents);
        _dfBuf!.View.SubView(0, scenarioRateLen).CopyFromCPU(rates.DiscountFactors);
        _ratesBuf!.View.SubView(0, scenarioRateLen).CopyFromCPU(rates.ShortRates);

        // Launch
        _kernel!(new Index2D(numContracts, numScenarios),
                 _contractsBuf!.View.SubView(0, numContracts),
                 _eventsBuf!.View.SubView(0, Math.Max(eventCount, 1)),
                 _dfBuf!.View.SubView(0, scenarioRateLen),
                 _ratesBuf!.View.SubView(0, scenarioRateLen),
                 numMonths, numScenarios,
                 _resultsBuf!.View.SubView(0, outcomeCount));

        _accelerator.Synchronize();

        // D2H copy
        var results = new DemoPvGpuResult[outcomeCount];
        _resultsBuf!.View.SubView(0, outcomeCount).CopyToCPU(results);
        return results;
    }

    // ── GPU descriptor builder ────────────────────────────────────────────

    private const double TicksPerDay  = 864_000_000_000.0;
    private const double DaysPerMonth = 365.25 / 12.0;

    private static void BuildGpuArrays(
        IReadOnlyList<PamContractTerms> contracts,
        DateTime                        maturityHorizon,
        long                            baseDateTicks,
        int                             numMonths,
        out DemoContractGpu[]           gpuContracts,
        out DemoEventGpu[]              gpuEvents)
    {
        gpuContracts = new DemoContractGpu[contracts.Count];
        var evList   = new List<DemoEventGpu>(contracts.Count * 20);

        for (int c = 0; c < contracts.Count; c++)
        {
            var terms    = contracts[c];
            var schedule = PrincipalAtMaturity.Schedule(maturityHorizon, terms);
            int evOffset = evList.Count;

            double initNotional = terms.InitialExchangeDate > terms.StatusDate
                ? 0.0 : terms.RoleSign * terms.NotionalPrincipal;
            double initRate     = terms.InitialExchangeDate > terms.StatusDate
                ? 0.0 : terms.NominalInterestRate;

            gpuContracts[c] = new DemoContractGpu
            {
                NotionalPrincipal                = terms.NotionalPrincipal,
                NominalInterestRate              = terms.NominalInterestRate,
                AccruedInterest                  = terms.AccruedInterest,
                PremiumDiscountAtIED             = terms.PremiumDiscountAtIED,
                PriceAtPurchaseDate              = terms.PriceAtPurchaseDate,
                PriceAtTerminationDate           = terms.PriceAtTerminationDate,
                RateSpread                       = terms.RateSpread,
                RateMultiplier                   = terms.RateMultiplier,
                NextResetRate                    = terms.NextResetRate ?? 0.0,
                FeeRate                          = terms.FeeRate,
                FeeAccrued                       = terms.FeeAccrued,
                RoleSign                         = terms.RoleSign,
                HasNextResetRate                 = terms.NextResetRate.HasValue ? 1 : 0,
                FeeBasisN                        = terms.FeeBasis == "N" ? 1 : 0,
                EventOffset                      = evOffset,
                EventCount                       = schedule.Count,
                InitialStateNotionalPrincipal    = initNotional,
                InitialStateNominalInterestRate  = initRate,
                InitialStateAccruedInterest      = terms.AccruedInterest,
                InitialCalcTimeTicks             = terms.StatusDate.Ticks,
            };

            foreach (var ev in schedule)
            {
                int mIdx = (int)Math.Round(
                    (ev.ScheduleTime.Ticks - baseDateTicks) / TicksPerDay / DaysPerMonth);
                mIdx = Math.Max(0, Math.Min(mIdx, numMonths - 1));

                bool isRr = ev.Type is EventType.RR or EventType.RRF;
                evList.Add(new DemoEventGpu
                {
                    ScheduleTimeTicks = ev.ScheduleTime.Ticks,
                    CalcTimeTicks     = ev.ScheduleTime.Ticks,
                    MonthIndex        = mIdx,
                    EventType         = ConvertEventType(ev.Type),
                    RateIndex         = isRr ? mIdx : -1,
                    _pad              = 0,
                });
            }
        }

        gpuEvents = evList.ToArray();
    }

    private static int ConvertEventType(EventType type) => type switch
    {
        EventType.IED  => GpuEventType.IED,
        EventType.IP   => GpuEventType.IP,
        EventType.IPCI => GpuEventType.IPCI,
        EventType.PRD  => GpuEventType.PRD,
        EventType.TD   => GpuEventType.TD,
        EventType.RR   => GpuEventType.RR,
        EventType.RRF  => GpuEventType.RRF,
        EventType.FP   => GpuEventType.FP,
        EventType.SC   => GpuEventType.SC,
        EventType.MD   => GpuEventType.MD,
        _              => GpuEventType.AD,
    };

    // ── Buffer management ─────────────────────────────────────────────────

    private void EnsureBuffers(long contracts, long events, long dfAndRates, long results)
    {
        if (contracts > _contractsCap)
        {
            _contractsBuf?.Dispose();
            _contractsCap = Grow(_contractsCap, contracts);
            _contractsBuf = _accelerator.Allocate1D<DemoContractGpu>(_contractsCap);
        }
        if (events > _eventsCap)
        {
            _eventsBuf?.Dispose();
            _eventsCap = Grow(_eventsCap, events);
            _eventsBuf = _accelerator.Allocate1D<DemoEventGpu>(_eventsCap);
        }
        if (dfAndRates > _dfCap)
        {
            _dfBuf?.Dispose();
            _dfCap = Grow(_dfCap, dfAndRates);
            _dfBuf = _accelerator.Allocate1D<double>(_dfCap);
        }
        if (dfAndRates > _ratesCap)
        {
            _ratesBuf?.Dispose();
            _ratesCap = Grow(_ratesCap, dfAndRates);
            _ratesBuf = _accelerator.Allocate1D<double>(_ratesCap);
        }
        if (results > _resultsCap)
        {
            _resultsBuf?.Dispose();
            _resultsCap = Grow(_resultsCap, results);
            _resultsBuf = _accelerator.Allocate1D<DemoPvGpuResult>(_resultsCap);
        }
    }

    private static long Grow(long current, long needed)
        => Math.Max(needed, Math.Max(1L, current * 2));

    // ── Kernel loading ────────────────────────────────────────────────────

    private void EnsureKernel()
    {
        if (_kernel != null) return;
        _kernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index2D,
            ArrayView1D<DemoContractGpu,  Stride1D.Dense>,
            ArrayView1D<DemoEventGpu,     Stride1D.Dense>,
            ArrayView1D<double,           Stride1D.Dense>,
            ArrayView1D<double,           Stride1D.Dense>,
            int, int,
            ArrayView1D<DemoPvGpuResult,  Stride1D.Dense>>(DemoKernel.Kernel);
    }

    // ── Accelerator factory ───────────────────────────────────────────────

    private static Accelerator? TryCreate(Context ctx, AcceleratorType type)
    {
        try
        {
            switch (type)
            {
                case AcceleratorType.Cuda:
                {
                    var devs = ctx.GetCudaDevices();
                    if (devs.Count > 0) return ctx.CreateCudaAccelerator(0);
                    break;
                }
                case AcceleratorType.OpenCL:
                {
                    var devs = ctx.GetCLDevices();
                    if (devs.Count > 0) return ctx.CreateCLAccelerator(0);
                    break;
                }
            }
        }
        catch { /* backend not available */ }
        return null;
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _contractsBuf?.Dispose();
        _eventsBuf?.Dispose();
        _dfBuf?.Dispose();
        _ratesBuf?.Dispose();
        _resultsBuf?.Dispose();
        _accelerator.Dispose();
        _context.Dispose();
    }
}
