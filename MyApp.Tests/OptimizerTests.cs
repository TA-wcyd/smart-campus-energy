using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using MyApp.Tests.TestData;

namespace MyApp.Tests;

/// <summary>
/// Unit tests for OptimizerService (OR-Tools CBC solver).
/// </summary>
public class OptimizerTests
{
    private static OptimizerService CreateSut() => new();

    // ── 1. Feasible baseline ──────────────────────────────────────────────────

    [Fact]
    public void Solve_NoDirectives_Returns24ValidEntries()
    {
        var req  = SampleRequests.SimpleScenario();
        var plan = CreateSut().Solve(req, []);

        Assert.Equal(24, plan.Count);
        for (int h = 0; h < 24; h++)
            Assert.Equal(h, plan[h].Hour);
    }

    [Fact]
    public void Solve_NoDirectives_AllGridKwhNonNegative()
    {
        var req  = SampleRequests.SimpleScenario();
        var plan = CreateSut().Solve(req, []);

        Assert.All(plan, p => Assert.True(p.GridKwh >= -1e-4, $"Negative grid at h={p.Hour}"));
    }

    // ── 2. End-of-day neutrality ──────────────────────────────────────────────

    [Fact]
    public void Solve_NoDirectives_EndOfDayNeutrality()
    {
        var req  = SampleRequests.SimpleScenario();
        var plan = CreateSut().Solve(req, []);

        Assert.True(
            Math.Abs(plan[23].BatteryEnergyAfterKwh - req.Battery.InitialEnergyKwh) < 1e-3,
            $"EOD SoC={plan[23].BatteryEnergyAfterKwh} != initial={req.Battery.InitialEnergyKwh}");
    }

    // ── 3. Solar reduction ────────────────────────────────────────────────────

    [Fact]
    public void Solve_SolarReduction_ReducesSolarUsedInListedHours()
    {
        var req = SampleRequests.SimpleScenario();
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0, Applies = true, DirectiveType = "solar_reduction",
                Explanation = "Test",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = [10, 11, 12], Factor = 0.5
                }
            }
        };

        var plan = CreateSut().Solve(req, directives);

        // Solar used in hours 10-12 must not exceed 50% of original solar
        foreach (int h in new[] { 10, 11, 12 })
        {
            double maxAllowed = req.Hours[h].SolarKwh * 0.5 + 1e-3;
            Assert.True(plan[h].SolarUsedKwh <= maxAllowed,
                $"Hour {h}: SolarUsed {plan[h].SolarUsedKwh} > {maxAllowed}");
        }
    }

    // ── 4. no_charge_window ───────────────────────────────────────────────────

    [Fact]
    public void Solve_NoChargeWindow_NoChargeActionInWindow()
    {
        var req = SampleRequests.SimpleScenario();
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0, Applies = true, DirectiveType = "no_charge_window",
                Explanation = "Test",
                StructuredAdjustment = new StructuredAdjustment { Hours = [8, 9, 10, 11] }
            }
        };

        var plan = CreateSut().Solve(req, directives);

        foreach (int h in new[] { 8, 9, 10, 11 })
            Assert.NotEqual("charge", plan[h].BatteryAction,
                $"Hour {h} should not have 'charge' action");
    }

    // ── 5. no_discharge_window ────────────────────────────────────────────────

    [Fact]
    public void Solve_NoDischargeWindow_NoDischargeActionInWindow()
    {
        var req = SampleRequests.SimpleScenario();
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0, Applies = true, DirectiveType = "no_discharge_window",
                Explanation = "Test",
                StructuredAdjustment = new StructuredAdjustment { Hours = [18, 19, 20, 21] }
            }
        };

        var plan = CreateSut().Solve(req, directives);

        foreach (int h in new[] { 18, 19, 20, 21 })
            Assert.NotEqual("discharge", plan[h].BatteryAction,
                $"Hour {h} should not have 'discharge' action");
    }

    // ── 6. max_grid_window ────────────────────────────────────────────────────

    [Fact]
    public void Solve_MaxGridWindow_GridKwhRespectsCap()
    {
        var req = SampleRequests.SimpleScenario();
        const double cap = 50.0;
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0, Applies = true, DirectiveType = "max_grid_window",
                Explanation = "Test",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = [0, 1, 2], MaxGridKwh = cap
                }
            }
        };

        var plan = CreateSut().Solve(req, directives);

        foreach (int h in new[] { 0, 1, 2 })
            Assert.True(plan[h].GridKwh <= cap + 1e-3,
                $"Hour {h}: grid {plan[h].GridKwh} > cap {cap}");
    }

    // ── 7. minimum_battery_reserve ────────────────────────────────────────────

    [Fact]
    public void Solve_MinimumBatteryReserve_SoCRespectedInWindow()
    {
        var req = SampleRequests.SimpleScenario();
        const double reserve = 250.0;
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0, Applies = true, DirectiveType = "minimum_battery_reserve",
                Explanation = "Test",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = [14, 15, 16, 17], MinimumEnergyKwh = reserve
                }
            }
        };

        var plan = CreateSut().Solve(req, directives);

        foreach (int h in new[] { 14, 15, 16, 17 })
            Assert.True(plan[h].BatteryEnergyAfterKwh >= reserve - 1e-3,
                $"Hour {h}: SoC {plan[h].BatteryEnergyAfterKwh} < reserve {reserve}");
    }

    // ── 8. End-of-day across all cases ────────────────────────────────────────

    [Theory]
    [InlineData("no_charge_window")]
    [InlineData("no_discharge_window")]
    public void Solve_WithWindowDirectives_AlwaysEndOfDayNeutral(string directiveType)
    {
        var req = SampleRequests.SimpleScenario();
        var directives = new List<DirectiveInterpretation>
        {
            new()
            {
                NoteIndex = 0, Applies = true, DirectiveType = directiveType,
                Explanation = "Test",
                StructuredAdjustment = new StructuredAdjustment { Hours = [2, 3, 4] }
            }
        };

        var plan = CreateSut().Solve(req, directives);

        Assert.True(
            Math.Abs(plan[23].BatteryEnergyAfterKwh - req.Battery.InitialEnergyKwh) < 1e-3);
    }

    // ── 9. Zero solar still solves ────────────────────────────────────────────

    [Fact]
    public void Solve_ZeroSolar_StillReturnsFeasiblePlan()
    {
        var req = SampleRequests.SimpleScenario();
        foreach (var h in req.Hours) h.SolarKwh = 0.0;

        var plan = CreateSut().Solve(req, []);

        Assert.Equal(24, plan.Count);
        Assert.True(
            Math.Abs(plan[23].BatteryEnergyAfterKwh - req.Battery.InitialEnergyKwh) < 1e-3);
    }
}
