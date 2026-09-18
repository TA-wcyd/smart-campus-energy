using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using MyApp.Tests.TestData;
using Xunit;

namespace MyApp.Tests;

public class ValidatorTests
{
    private readonly OptimizerService _optimizer = new();
    private readonly ScheduleValidator _validator = new();

    private (OptimizeRequest req, List<HourlyPlanEntry> plan, IReadOnlyList<DirectiveInterpretation> directives) GetValidSetup()
    {
        var req = SampleRequests.SimpleScenario();
        var directives = Array.Empty<DirectiveInterpretation>();
        var plan = _optimizer.Solve(req, directives);
        return (req, plan, directives);
    }

    [Fact]
    public void Validate_ValidPlan_ReturnsTrue()
    {
        var (req, plan, directives) = GetValidSetup();

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.True(isValid);
        Assert.Null(reason);
    }

    [Fact]
    public void Validate_CorruptGridKwh_FailsEnergyBalance()
    {
        var (req, plan, directives) = GetValidSetup();
        // Corrupt grid kWh at hour 5
        plan[5].GridKwh += 25.0;

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.False(isValid);
        Assert.Contains("Energy balance violation", reason);
    }

    [Fact]
    public void Validate_CorruptBatteryTransition_Fails()
    {
        var (req, plan, directives) = GetValidSetup();
        // Corrupt battery SoC at hour 3
        plan[3].BatteryEnergyAfterKwh += 40.0;

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.False(isValid);
        Assert.Contains("Battery transition mismatch", reason);
    }

    [Fact]
    public void Validate_SolarOverEffectiveSolar_Fails()
    {
        var (req, plan, directives) = GetValidSetup();
        // Over-report solar generation beyond actual available solar
        plan[12].SolarUsedKwh = req.Hours.First(h => h.Hour == 12).SolarKwh + 50.0;

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.False(isValid);
        Assert.Contains("exceeds effective solar generation", reason);
    }

    [Fact]
    public void Validate_ChargeInNoChargeWindow_Fails()
    {
        var req = SampleRequests.SimpleScenario();
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = "no_charge_window",
                StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 10 } }
            }
        };

        var plan = _optimizer.Solve(req, Array.Empty<DirectiveInterpretation>());
        // Force charge action into prohibited window
        plan[10].BatteryAction = "charge";
        plan[10].BatteryKwh = 15.0;

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.False(isValid);
        Assert.Contains("no_charge_window", reason);
    }

    [Fact]
    public void Validate_DischargeInNoDischargeWindow_Fails()
    {
        var req = SampleRequests.SimpleScenario();
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = "no_discharge_window",
                StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 19 } }
            }
        };

        var plan = _optimizer.Solve(req, Array.Empty<DirectiveInterpretation>());
        // Force discharge action into prohibited window
        plan[19].BatteryAction = "discharge";
        plan[19].BatteryKwh = 20.0;

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.False(isValid);
        Assert.Contains("no_discharge_window", reason);
    }

    [Fact]
    public void Validate_GridOverMaxGridCap_Fails()
    {
        var req = SampleRequests.SimpleScenario();
        double cap = 40.0;
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = "max_grid_window",
                StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 18 }, MaxGridKwh = cap }
            }
        };

        var plan = _optimizer.Solve(req, directives);
        // Exceed max grid cap
        plan[18].GridKwh = cap + 15.0;

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.False(isValid);
        Assert.Contains("exceeded max grid cap", reason);
    }

    [Fact]
    public void Validate_BatteryBelowReserve_Fails()
    {
        var (req, plan, directives) = GetValidSetup();
        // Drop SoC below minimum limit
        plan[8].BatteryEnergyAfterKwh = req.Battery.MinimumEnergyKwh - 20.0;

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.False(isValid);
        Assert.Contains("dropped below required reserve floor", reason);
    }

    [Fact]
    public void Validate_FinalEnergyMismatch_FailsEndOfDayNeutrality()
    {
        var (req, plan, directives) = GetValidSetup();
        
        // Construct a plan where every hourly energy balance and transition is valid,
        // but net charge is +10 kWh at end of day (210 vs 200 initial)
        for (int h = 0; h < 24; h++)
        {
            if (h == 0)
            {
                plan[h].BatteryAction = "charge";
                plan[h].BatteryKwh = 10.0;
                plan[h].GridKwh = req.Hours[h].DemandKwh + 10.0;
                plan[h].SolarUsedKwh = 0.0;
                plan[h].BatteryEnergyAfterKwh = req.Battery.InitialEnergyKwh + 10.0;
            }
            else
            {
                plan[h].BatteryAction = "idle";
                plan[h].BatteryKwh = 0.0;
                plan[h].GridKwh = req.Hours[h].DemandKwh;
                plan[h].SolarUsedKwh = 0.0;
                plan[h].BatteryEnergyAfterKwh = req.Battery.InitialEnergyKwh + 10.0;
            }
        }

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.False(isValid);
        Assert.Contains("End-of-day neutrality violation", reason);
    }

    [Fact]
    public void Validate_WrongPlanLength_Fails()
    {
        var (req, plan, directives) = GetValidSetup();
        plan.RemoveAt(23); // Now 23 entries instead of 24

        var (isValid, reason) = _validator.Validate(req, plan, directives);

        Assert.False(isValid);
        Assert.Contains("must contain exactly 24 entries", reason);
    }
}
