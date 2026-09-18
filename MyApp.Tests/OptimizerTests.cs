using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using MyApp.Tests.TestData;
using Xunit;

namespace MyApp.Tests;

public class OptimizerTests
{
    private readonly OptimizerService _optimizer = new();
    private readonly ScheduleValidator _validator = new();

    [Fact]
    public void Solve_FeasibleScenario_NoDirectives_ReturnsValid24HourPlan()
    {
        var req = SampleRequests.SimpleScenario();
        var directives = Array.Empty<DirectiveInterpretation>();

        var plan = _optimizer.Solve(req, directives);

        Assert.NotNull(plan);
        Assert.Equal(24, plan.Count);

        var (isValid, reason) = _validator.Validate(req, plan, directives);
        Assert.True(isValid, $"Plan failed validation: {reason}");

        // End-of-day neutrality check
        Assert.Equal(req.Battery.InitialEnergyKwh, plan[23].BatteryEnergyAfterKwh, precision: 3);
    }

    [Fact]
    public void Solve_SolarReductionDirective_ReducesSolarUsedInTargetHours()
    {
        var req = SampleRequests.SimpleScenario();
        var targetHours = new List<int> { 12, 13, 14 };
        double factor = 0.3;

        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = "solar_reduction",
                Explanation = "Solar output dropped due to overcast sky.",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = targetHours,
                    Factor = factor
                }
            }
        };

        var plan = _optimizer.Solve(req, directives);

        var (isValid, reason) = _validator.Validate(req, plan, directives);
        Assert.True(isValid, $"Plan failed validation: {reason}");

        foreach (var h in targetHours)
        {
            var rawSolar = req.Hours.First(x => x.Hour == h).SolarKwh;
            var maxAllowedSolar = rawSolar * factor;
            Assert.True(plan[h].SolarUsedKwh <= maxAllowedSolar + 1e-4,
                $"Hour {h} solar used {plan[h].SolarUsedKwh} exceeds effective solar {maxAllowedSolar}");
        }
    }

    [Fact]
    public void Solve_NoChargeWindowDirective_ProhibitsChargingInWindow()
    {
        var req = SampleRequests.SimpleScenario();
        var windowHours = new List<int> { 11, 12, 13 };

        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = "no_charge_window",
                Explanation = "Network congestion prevents charging.",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = windowHours
                }
            }
        };

        var plan = _optimizer.Solve(req, directives);

        var (isValid, reason) = _validator.Validate(req, plan, directives);
        Assert.True(isValid, $"Plan failed validation: {reason}");

        foreach (var h in windowHours)
        {
            Assert.NotEqual("charge", plan[h].BatteryAction, StringComparer.OrdinalIgnoreCase);
            Assert.True(plan[h].BatteryKwh < 1e-4, $"Hour {h} battery kWh was {plan[h].BatteryKwh} during no_charge_window");
        }
    }

    [Fact]
    public void Solve_NoDischargeWindowDirective_ProhibitsDischargingInWindow()
    {
        var req = SampleRequests.SimpleScenario();
        var windowHours = new List<int> { 18, 19 };

        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = "no_discharge_window",
                Explanation = "Hold battery power for emergency backup reserve.",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = windowHours
                }
            }
        };

        var plan = _optimizer.Solve(req, directives);

        var (isValid, reason) = _validator.Validate(req, plan, directives);
        Assert.True(isValid, $"Plan failed validation: {reason}");

        foreach (var h in windowHours)
        {
            Assert.NotEqual("discharge", plan[h].BatteryAction, StringComparer.OrdinalIgnoreCase);
            Assert.True(plan[h].BatteryKwh < 1e-4, $"Hour {h} battery kWh was {plan[h].BatteryKwh} during no_discharge_window");
        }
    }

    [Fact]
    public void Solve_MaxGridWindowDirective_CapsGridImportInWindow()
    {
        var req = SampleRequests.SimpleScenario();
        var windowHours = new List<int> { 18, 19, 20 };
        double maxGridCap = 80.0;

        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = "max_grid_window",
                Explanation = "Transformer capacity restriction during peak hours.",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = windowHours,
                    MaxGridKwh = maxGridCap
                }
            }
        };

        var plan = _optimizer.Solve(req, directives);

        var (isValid, reason) = _validator.Validate(req, plan, directives);
        Assert.True(isValid, $"Plan failed validation: {reason}");

        foreach (var h in windowHours)
        {
            Assert.True(plan[h].GridKwh <= maxGridCap + 1e-3,
                $"Hour {h} grid import ({plan[h].GridKwh}) exceeded cap ({maxGridCap})");
        }
    }

    [Fact]
    public void Solve_MinimumBatteryReserveDirective_EnforcesHigherReserveInWindow()
    {
        var req = SampleRequests.SimpleScenario();
        var windowHours = new List<int> { 14, 15, 16 };
        double higherReserve = 250.0;

        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0,
                Applies = true,
                DirectiveType = "minimum_battery_reserve",
                Explanation = "Higher reserve floor required for evening storm contingency.",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = windowHours,
                    MinimumEnergyKwh = higherReserve
                }
            }
        };

        var plan = _optimizer.Solve(req, directives);

        var (isValid, reason) = _validator.Validate(req, plan, directives);
        Assert.True(isValid, $"Plan failed validation: {reason}");

        foreach (var h in windowHours)
        {
            Assert.True(plan[h].BatteryEnergyAfterKwh >= higherReserve - 1e-3,
                $"Hour {h} battery SoC ({plan[h].BatteryEnergyAfterKwh}) dropped below directive reserve ({higherReserve})");
        }
    }

    [Fact]
    public void Solve_ZeroSolarScenario_SuccessfullySolvesWithEndOfDayNeutrality()
    {
        var req = SampleRequests.SimpleScenario();
        foreach (var h in req.Hours)
        {
            h.SolarKwh = 0.0;
        }

        var plan = _optimizer.Solve(req, Array.Empty<DirectiveInterpretation>());

        Assert.NotNull(plan);
        Assert.Equal(24, plan.Count);
        Assert.All(plan, p => Assert.Equal(0.0, p.SolarUsedKwh));
        Assert.Equal(req.Battery.InitialEnergyKwh, plan[23].BatteryEnergyAfterKwh, precision: 3);

        var (isValid, reason) = _validator.Validate(req, plan, Array.Empty<DirectiveInterpretation>());
        Assert.True(isValid, $"Plan failed validation: {reason}");
    }
}
