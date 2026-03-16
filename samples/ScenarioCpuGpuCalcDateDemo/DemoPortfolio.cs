/*
 * ScenarioCpuGpuCalcDateDemo — tiny demo portfolio.
 *
 * Five PAM contracts with intentionally different features so every
 * dimension of heterogeneity (notional, maturity, payment frequency,
 * spread, floating/fixed, start offset) is represented:
 *
 *   C001  $1 000 000  48 m  annual         fixed   4.0%   start month  0
 *   C002  $  500 000  36 m  quarterly      fixed   5.0%   start month  0
 *   C003  $2 000 000  24 m  monthly        fixed   3.0%   start month  0
 *   C004  $  750 000  45 m  quarterly      floating, spread 1.0%  start month 3
 *   C005  $1 500 000  36 m  annual         fixed   4.5%   start month  6
 *
 * All contracts:
 *   ContractRole = RPA (lend principal, receive interest + principal back)
 *   DayCount = A/365
 *   Calendar = NC (no calendar adjustment)
 *   BaseDate = 2020-01-01
 */
using ActusInsurance.Core.Models;
using ActusInsurance.Core.Types;

namespace ScenarioCpuGpuCalcDateDemo;

/// <summary>
/// Hard-coded demo portfolio of five heterogeneous PAM contracts.
/// The portfolio is intentionally small so outputs are readable.
/// </summary>
public static class DemoPortfolio
{
    /// <summary>Simulation base date (t = 0 on the monthly grid).</summary>
    public static readonly DateTime BaseDate = new(2020, 1, 1);

    /// <summary>
    /// Maturity horizon for schedule generation.
    /// Must be at least as late as the latest contract maturity.
    /// </summary>
    public static readonly DateTime MaturityHorizon = BaseDate.AddMonths(ScenarioBuilder.NumMonths + 1);

    /// <summary>Returns the five demo PAM contracts.</summary>
    public static List<PamContractTerms> Build() =>
    [
        // C001: $1M, 4-year, annual, fixed 4%
        MakeFixed("PAM_C001",
            ied       : BaseDate,                       // month 0
            maturity  : BaseDate.AddMonths(48),
            notional  : 1_000_000.0,
            rate      : 0.040,
            ipCycle   : "P1YL1"),

        // C002: $500K, 3-year, quarterly, fixed 5%
        MakeFixed("PAM_C002",
            ied       : BaseDate,                       // month 0
            maturity  : BaseDate.AddMonths(36),
            notional  : 500_000.0,
            rate      : 0.050,
            ipCycle   : "P3ML1"),

        // C003: $2M, 2-year, monthly, fixed 3%
        MakeFixed("PAM_C003",
            ied       : BaseDate,                       // month 0
            maturity  : BaseDate.AddMonths(24),
            notional  : 2_000_000.0,
            rate      : 0.030,
            ipCycle   : "P1ML1"),

        // C004: $750K, 3-year 9-month, quarterly, floating (spread 1%, quarterly RR)
        // Starts 3 months into the simulation to demonstrate timeIndex offsets.
        MakeFloating("PAM_C004",
            ied       : BaseDate.AddMonths(3),          // month 3
            maturity  : BaseDate.AddMonths(48),
            notional  : 750_000.0,
            rate      : 0.040,                          // initial rate before first reset
            ipCycle   : "P3ML1",
            spread    : 0.010),

        // C005: $1.5M, 3-year, annual, fixed 4.5%
        // Starts 6 months into the simulation.
        MakeFixed("PAM_C005",
            ied       : BaseDate.AddMonths(6),          // month 6
            maturity  : BaseDate.AddMonths(42),
            notional  : 1_500_000.0,
            rate      : 0.045,
            ipCycle   : "P1YL1"),
    ];

    // ── Builders ──────────────────────────────────────────────────────────

    private static PamContractTerms MakeFixed(
        string   id,
        DateTime ied,
        DateTime maturity,
        double   notional,
        double   rate,
        string   ipCycle) =>
        new()
        {
            ContractID                           = id,
            Currency                             = "USD",
            ContractRole                         = ContractRole.RPA,
            StatusDate                           = ied,
            InitialExchangeDate                  = ied,
            MaturityDate                         = maturity,
            NotionalPrincipal                    = notional,
            NominalInterestRate                  = rate,
            AccruedInterest                      = 0.0,
            CycleOfInterestPayment               = ipCycle,
            CycleAnchorDateOfInterestPayment     = ied,
            RateSpread                           = 0.0,
            RateMultiplier                       = 1.0,
            DayCountConvention                   = DayCountConvention.A_365,
            BusinessDayConvention                = BusinessDayConventionEnum.NOS,
            Calendar                             = Calendar.NC,
            NotionalScalingMultiplier            = 1.0,
            InterestScalingMultiplier            = 1.0,
        };

    private static PamContractTerms MakeFloating(
        string   id,
        DateTime ied,
        DateTime maturity,
        double   notional,
        double   rate,
        string   ipCycle,
        double   spread) =>
        new()
        {
            ContractID                           = id,
            Currency                             = "USD",
            ContractRole                         = ContractRole.RPA,
            StatusDate                           = ied,
            InitialExchangeDate                  = ied,
            MaturityDate                         = maturity,
            NotionalPrincipal                    = notional,
            NominalInterestRate                  = rate,
            AccruedInterest                      = 0.0,
            CycleOfInterestPayment               = ipCycle,
            CycleAnchorDateOfInterestPayment     = ied,
            RateSpread                           = spread,
            RateMultiplier                       = 1.0,
            DayCountConvention                   = DayCountConvention.A_365,
            BusinessDayConvention                = BusinessDayConventionEnum.NOS,
            Calendar                             = Calendar.NC,
            NotionalScalingMultiplier            = 1.0,
            InterestScalingMultiplier            = 1.0,
            MarketObjectCodeOfRateReset          = "USD_LIBOR_3M",
            CycleOfRateReset                     = "P3ML1",
            CycleAnchorDateOfRateReset           = ied,
        };
}
