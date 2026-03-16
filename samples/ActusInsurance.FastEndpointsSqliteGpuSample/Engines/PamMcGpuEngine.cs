/*
 * GPU PV engine for PAM Monte Carlo.
 * Ported from CLI/PamMonteCarlo50Y/GpuPvEngine.cs.
 *
 * Accelerator fallback chain: CUDA → OpenCL → CPU (ILGPU software backend).
 * Buffers grow on demand and are never reallocated unless needed.
 */
using ActusInsurance.Core.CPU.Contracts;
using ActusInsurance.Core.Models;
using ActusInsurance.Core.Types;
using ActusInsurance.GPU.Models;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;

namespace ActusInsurance.FastEndpointsSqliteGpuSample.Engines;

internal sealed class PamMcGpuEngine : IDisposable
{
    private readonly Context     _context;
    private readonly Accelerator _accelerator;
    private bool _disposed;

    private Action<Index2D,
                    ArrayView1D<PamMcContractGpu, Stride1D.Dense>,
                    ArrayView1D<PamMcEventGpu,    Stride1D.Dense>,
                    ArrayView1D<double,           Stride1D.Dense>,
                    ArrayView1D<double,           Stride1D.Dense>,
                    int, int, int,
                    ArrayView1D<PamMcPvGpuResult, Stride1D.Dense>>? _kernel;

    private MemoryBuffer1D<PamMcContractGpu, Stride1D.Dense>? _contractsBuf;
    private MemoryBuffer1D<PamMcEventGpu,    Stride1D.Dense>? _eventsBuf;
    private MemoryBuffer1D<double,           Stride1D.Dense>? _dfBuf;
    private MemoryBuffer1D<double,           Stride1D.Dense>? _ratesBuf;
    private MemoryBuffer1D<PamMcPvGpuResult, Stride1D.Dense>? _resultsBuf;

    private long _contractsCap, _eventsCap, _dfCap, _ratesCap, _resultsCap;

    public string AcceleratorName => _accelerator.Name;

    private PamMcGpuEngine(Context context, Accelerator accelerator)
    {
        _context     = context;
        _accelerator = accelerator;
    }

    public static PamMcGpuEngine CreateDefault()
    {
        var context = Context.CreateDefault();
        Accelerator? acc = TryCreate(context, AcceleratorType.Cuda)
                        ?? TryCreate(context, AcceleratorType.OpenCL)
                        ?? context.CreateCPUAccelerator(0);
        var eng = new PamMcGpuEngine(context, acc!);
        eng.EnsureKernel();
        return eng;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns flat PamMcPvGpuResult[c * numScenarios + s].
    /// </summary>
    public PamMcPvGpuResult[] Evaluate(
        IReadOnlyList<PamContractTerms> contracts,
        PamMcVasicekRateGenerator       rates,
        DateTime                        baseDate,
        int                             calcDateIndex,
        int                             scenarioStart,
        int                             numScenarios,
        DateTime                        maturityHorizon)
    {
        if (contracts.Count == 0 || numScenarios == 0)
            return Array.Empty<PamMcPvGpuResult>();

        EnsureKernel();

        int  numContracts  = contracts.Count;
        int  numMonths     = rates.NumMonths;
        long baseDateTicks = baseDate.Ticks;

        // 1. Build GPU descriptors on the CPU
        BuildGpuArrays(contracts, maturityHorizon, baseDateTicks, numMonths,
                       out var gpuContracts, out var gpuEvents);

        // 2. Slice discount-factor and rate arrays for the selected scenario range
        int scenarioRateLen = numScenarios * numMonths;
        var dfSlice         = new double[scenarioRateLen];
        var rateSlice       = new double[scenarioRateLen];

        for (int s = 0; s < numScenarios; s++)
        {
            int srcBase = (scenarioStart + s) * numMonths;
            int dstBase = s * numMonths;
            Array.Copy(rates.DiscountFactors, srcBase, dfSlice,    dstBase, numMonths);
            Array.Copy(rates.ShortRates,      srcBase, rateSlice,  dstBase, numMonths);
        }

        int eventCount   = gpuEvents.Length;
        int outcomeCount = numContracts * numScenarios;

        // 3. Allocate / grow device buffers
        EnsureBuffers(numContracts, Math.Max(eventCount, 1), scenarioRateLen, outcomeCount);

        // 4. H2D copies
        _contractsBuf!.View.SubView(0, numContracts).CopyFromCPU(gpuContracts);
        if (eventCount > 0)
            _eventsBuf!.View.SubView(0, eventCount).CopyFromCPU(gpuEvents);
        _dfBuf!.View.SubView(0, scenarioRateLen).CopyFromCPU(dfSlice);
        _ratesBuf!.View.SubView(0, scenarioRateLen).CopyFromCPU(rateSlice);

        // 5. Kernel launch
        _kernel!(new Index2D(numContracts, numScenarios),
                 _contractsBuf!.View.SubView(0, numContracts),
                 _eventsBuf!.View.SubView(0, Math.Max(eventCount, 1)),
                 _dfBuf!.View.SubView(0, scenarioRateLen),
                 _ratesBuf!.View.SubView(0, scenarioRateLen),
                 numMonths, calcDateIndex, numScenarios,
                 _resultsBuf!.View.SubView(0, outcomeCount));

        _accelerator.Synchronize();

        // 6. D2H copy
        var results = new PamMcPvGpuResult[outcomeCount];
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
        out PamMcContractGpu[]          gpuContracts,
        out PamMcEventGpu[]             gpuEvents)
    {
        gpuContracts = new PamMcContractGpu[contracts.Count];
        var evList   = new List<PamMcEventGpu>(contracts.Count * 20);

        for (int c = 0; c < contracts.Count; c++)
        {
            var terms    = contracts[c];
            var schedule = PrincipalAtMaturity.Schedule(maturityHorizon, terms);
            int evOffset = evList.Count;

            double initNotional = terms.InitialExchangeDate > terms.StatusDate
                ? 0.0 : terms.RoleSign * terms.NotionalPrincipal;
            double initRate = terms.InitialExchangeDate > terms.StatusDate
                ? 0.0 : terms.NominalInterestRate;

            gpuContracts[c] = new PamMcContractGpu
            {
                NotionalPrincipal               = terms.NotionalPrincipal,
                NominalInterestRate             = terms.NominalInterestRate,
                AccruedInterest                 = terms.AccruedInterest,
                PremiumDiscountAtIED            = terms.PremiumDiscountAtIED,
                PriceAtPurchaseDate             = terms.PriceAtPurchaseDate,
                PriceAtTerminationDate          = terms.PriceAtTerminationDate,
                RateSpread                      = terms.RateSpread,
                RateMultiplier                  = terms.RateMultiplier,
                NextResetRate                   = terms.NextResetRate ?? 0.0,
                FeeRate                         = terms.FeeRate,
                FeeAccrued                      = terms.FeeAccrued,
                RoleSign                        = terms.RoleSign,
                HasNextResetRate                = terms.NextResetRate.HasValue ? 1 : 0,
                FeeBasisN                       = terms.FeeBasis == "N" ? 1 : 0,
                EventOffset                     = evOffset,
                EventCount                      = schedule.Count,
                InitialStateNotionalPrincipal   = initNotional,
                InitialStateNominalInterestRate = initRate,
                InitialStateAccruedInterest     = terms.AccruedInterest,
                InitialCalcTimeTicks            = terms.StatusDate.Ticks,
            };

            foreach (var ev in schedule)
            {
                int monthIdx = (int)Math.Round(
                    (ev.ScheduleTime.Ticks - baseDateTicks) / TicksPerDay / DaysPerMonth);
                monthIdx = Math.Max(0, Math.Min(monthIdx, numMonths - 1));

                bool isRr = ev.Type is EventType.RR or EventType.RRF;
                evList.Add(new PamMcEventGpu
                {
                    ScheduleTimeTicks = ev.ScheduleTime.Ticks,
                    CalcTimeTicks     = ev.ScheduleTime.Ticks,
                    MonthIndex        = monthIdx,
                    EventType         = ConvertEventType(ev.Type),
                    RateIndex         = isRr ? monthIdx : -1,
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
            _contractsBuf = _accelerator.Allocate1D<PamMcContractGpu>(_contractsCap);
        }
        if (events > _eventsCap)
        {
            _eventsBuf?.Dispose();
            _eventsCap = Grow(_eventsCap, events);
            _eventsBuf = _accelerator.Allocate1D<PamMcEventGpu>(_eventsCap);
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
            _resultsBuf = _accelerator.Allocate1D<PamMcPvGpuResult>(_resultsCap);
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
            ArrayView1D<PamMcContractGpu, Stride1D.Dense>,
            ArrayView1D<PamMcEventGpu,    Stride1D.Dense>,
            ArrayView1D<double,           Stride1D.Dense>,
            ArrayView1D<double,           Stride1D.Dense>,
            int, int, int,
            ArrayView1D<PamMcPvGpuResult, Stride1D.Dense>>(PamMcKernel.Kernel);
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
