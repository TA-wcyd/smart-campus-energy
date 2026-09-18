using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using MyApp.Tests.TestData;

namespace MyApp.Tests;

/// <summary>
/// Unit tests for ScheduleValidator — validates all 10 replay rules.
/// </summary>
public class ValidatorTests
{
    private static ScheduleValidator CreateSut() => new();

    // ── Helper: build a correct plan from the optimizer ───────────────────────

    private static (OptimizeRequest Req, List<HourlyPlanEntry> Plan) BuildValid(
        List<DirectiveInterpretation>? directives = null)
    {
        var req  = SampleRequests.SimpleScenario();
        var plan = new OptimizerService().Solve(req, directives ?? []);
        return (req, plan);
    }

    // ── 1. Valid plan passes ──────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidPlan_Passes()
    {
        var (req, plan) = BuildValid();
        var (isValid, reason) = CreateSut().Validate(req, plan, []);
        Assert.True(isValid, reason);
    }

    // ── 2. Corrupt grid_kwh → energy balance fails ────────────────────────────

    [Fact]
    public void Validate_CorruptGridKwh_FailsEnergyBalance()
    {
        var (req, plan) = BuildValid();
        plan[5] = plan[5] with { GridKwh = plan[5].GridKwh + 50.0 };
        var (isValid, reason) = CreateSut().Validate(req, plan, []);
        Assert.False(isValid);
        Assert.Contains("balance", reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── 3. Corrupt battery transition → fails ─────────────────────────────────

    [Fact]
    public void Validate_CorruptBatteryTransition_Fails()
    {
        var (req, plan) = BuildValid();
        // Shift BatteryEnergyAfterKwh by a large amount so replay diverges
        plan[3] = plan[3] with { BatteryEnergyAfterKwh = plan[3].BatteryEnergyAfterKwh + 100.0 };
        var (isValid, reason) = CreateSut().Validate(req, plan, []);
        Assert.False(isValid);
    }

    // ── 4. Solar > effective solar → fails ───────────────────────────────────

    [Fact]
    public void Validate_SolarOverEffective_Fails()
    {
        var (req, plan) = BuildValid();
        // Force solar used beyond available solar in a high-solar hour
        int solarHour = 12;
        plan[solarHour] = plan[solarHour] with
        {
            SolarUsedKwh = req.Hours[solarHour].SolarKwh + 50.0
        };
        var (isValid, reason) = CreateSut().Validate(req, plan, []);
        Assert.False(isValid);
    }

    // ── 5. Charge in no_charge_window → fails ────────────────────────────────

    [Fact]
    public void Validate_ChargeInNoChargeWindow_Fails()
    {
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0, Applies = true, DirectiveType = "no_charge_window",
                Explanation = "Test",
                StructuredAdjustment = new StructuredAdjustment { Hours = [10, 11, 12] }
            }
        };

        var (req, plan) = BuildValid([]);

        // Manually force a charge action in the restricted window
        // (doesn't need to be a solver plan — validator checks the raw plan)
        var bat = req.Battery;
        double socBefore = plan[9].BatteryEnergyAfterKwh;
        double chgAmt    = Math.Min(50.0, bat.CapacityKwh - socBefore);
        double demand10  = req.Hours[10].DemandKwh;
        double solar10   = Math.Min(req.Hours[10].SolarKwh, demand10);
        double grid10    = Math.Max(0, demand10 + chgAmt - solar10);

        plan[10] = new HourlyPlanEntry
        {
            Hour                  = 10,
            BatteryAction         = "charge",
            BatteryKwh            = chgAmt,
            SolarUsedKwh          = solar10,
            GridKwh               = grid10,
            BatteryEnergyAfterKwh = socBefore + chgAmt
        };

        var (isValid, reason) = CreateSut().Validate(req, plan, directives);
        Assert.False(isValid);
        Assert.Contains("no_charge_window", reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── 6. Discharge in no_discharge_window → fails ───────────────────────────

    [Fact]
    public void Validate_DischargeInNoDischargeWindow_Fails()
    {
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0, Applies = true, DirectiveType = "no_discharge_window",
                Explanation = "Test",
                StructuredAdjustment = new StructuredAdjustment { Hours = [19, 20] }
            }
        };

        var (req, plan) = BuildValid([]);
        double socBefore = plan[18].BatteryEnergyAfterKwh;
        double dchgAmt   = Math.Min(30.0, socBefore - req.Battery.MinimumEnergyKwh);
        if (dchgAmt <= 0) dchgAmt = 1.0; // force even if borderline

        double demand19 = req.Hours[19].DemandKwh;
        double solar19  = Math.Min(req.Hours[19].SolarKwh, demand19 - dchgAmt);
        solar19 = Math.Max(0, solar19);
        double grid19   = Math.Max(0, demand19 - dchgAmt - solar19);

        plan[19] = new HourlyPlanEntry
        {
            Hour                  = 19,
            BatteryAction         = "discharge",
            BatteryKwh            = dchgAmt,
            SolarUsedKwh          = solar19,
            GridKwh               = grid19,
            BatteryEnergyAfterKwh = socBefore - dchgAmt
        };

        var (isValid, reason) = CreateSut().Validate(req, plan, directives);
        Assert.False(isValid);
        Assert.Contains("no_discharge_window", reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── 7. Grid over max_grid_window cap → fails ──────────────────────────────

    [Fact]
    public void Validate_GridOverMaxGridCap_Fails()
    {
        const double cap = 20.0;
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0, Applies = true, DirectiveType = "max_grid_window",
                Explanation = "Test",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = [1], MaxGridKwh = cap
                }
            }
        };

        var (req, plan) = BuildValid([]);
        // Artificially set grid > cap (don't worry about balance — we're testing the cap check)
        plan[1] = plan[1] with { GridKwh = cap + 10.0 };

        var (isValid, reason) = CreateSut().Validate(req, plan, directives);
        Assert.False(isValid);
        Assert.Contains("max_grid_window", reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── 8. Battery below reserve → fails ─────────────────────────────────────

    [Fact]
    public void Validate_BatteryBelowReserve_Fails()
    {
        var req  = SampleRequests.SimpleScenario();
        var plan = new OptimizerService().Solve(req, []);

        // Manually corrupt SoC below minimum reserve
        plan[5] = plan[5] with
        {
            BatteryEnergyAfterKwh = req.Battery.MinimumEnergyKwh - 20.0
        };

        var (isValid, reason) = CreateSut().Validate(req, plan, []);
        Assert.False(isValid);
    }

    // ── 9. Final energy != initial → fails ───────────────────────────────────

    [Fact]
    public void Validate_FinalEnergyNotInitial_Fails()
    {
        var (req, plan) = BuildValid();
        plan[23] = plan[23] with
        {
            BatteryEnergyAfterKwh = req.Battery.InitialEnergyKwh + 50.0
        };
        var (isValid, reason) = CreateSut().Validate(req, plan, []);
        Assert.False(isValid);
    }

    // ── 10. Wrong plan length → fails ─────────────────────────────────────────

    [Fact]
    public void Validate_WrongPlanLength_Fails()
    {
        var (req, plan) = BuildValid();
        plan.RemoveAt(0); // 23 entries
        var (isValid, reason) = CreateSut().Validate(req, plan, []);
        Assert.False(isValid);
        Assert.Contains("24", reason);
    }
}
