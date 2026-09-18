using Google.OrTools.LinearSolver;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.AI;

/// <summary>
/// LP/MIP optimizer for the campus energy dispatch problem.
/// Uses Google OR-Tools CBC solver.
/// </summary>
public sealed class OptimizerService : IOptimizerService
{
    /// <inheritdoc/>
    public List<HourlyPlanEntry> Solve(OptimizeRequest req,
        IReadOnlyList<DirectiveInterpretation> directives)
    {
        var bat    = req.Battery;
        var solver = Solver.CreateSolver("CBC_MIXED_INTEGER_PROGRAMMING")
                     ?? throw new InvalidOperationException("Could not create CBC solver.");

        const int H   = 24;
        const double M = 1e6; // Big-M for binary linking

        // ── Decision variables ────────────────────────────────────────────────
        var grid      = new Variable[H];
        var solarUsed = new Variable[H];
        var charge    = new Variable[H];
        var discharge = new Variable[H];
        var eAfter    = new Variable[H];
        var isCharging = new Variable[H]; // binary

        for (int h = 0; h < H; h++)
        {
            double effSolar = EffectiveSolar(req, h, directives);
            double minE     = MinEnergy(req, h, directives);

            grid[h]       = solver.MakeNumVar(0.0,   double.PositiveInfinity, $"grid_{h}");
            solarUsed[h]  = solver.MakeNumVar(0.0,   effSolar,                $"solar_{h}");
            charge[h]     = solver.MakeNumVar(0.0,   bat.MaxChargeKwhPerHour, $"chg_{h}");
            discharge[h]  = solver.MakeNumVar(0.0,   bat.MaxDischargeKwhPerHour, $"dchg_{h}");
            eAfter[h]     = solver.MakeNumVar(minE,  bat.CapacityKwh,         $"soc_{h}");
            isCharging[h] = solver.MakeIntVar(0.0,   1.0,                     $"bin_{h}");
        }

        // ── Directive constraints (applied before objective) ──────────────────
        var noChargeHours    = new HashSet<int>();
        var noDischargeHours = new HashSet<int>();

        foreach (var d in directives.Where(d => d.Applies && d.StructuredAdjustment != null))
        {
            var adj = d.StructuredAdjustment!;
            switch (d.DirectiveType)
            {
                case "no_charge_window":
                    foreach (var h in (adj.Hours ?? []).Where(IsValidHour))
                        noChargeHours.Add(h);
                    break;

                case "no_discharge_window":
                    foreach (var h in (adj.Hours ?? []).Where(IsValidHour))
                        noDischargeHours.Add(h);
                    break;

                case "max_grid_window":
                    if (adj.MaxGridKwh is double cap)
                        foreach (var h in (adj.Hours ?? []).Where(IsValidHour))
                            grid[h].SetBounds(0, cap);
                    break;
            }
        }

        foreach (int h in noChargeHours)
            charge[h].SetBounds(0, 0);

        foreach (int h in noDischargeHours)
            discharge[h].SetBounds(0, 0);

        // ── Constraints ───────────────────────────────────────────────────────
        for (int h = 0; h < H; h++)
        {
            double demand = req.Hours[h].DemandKwh;

            // Energy balance: grid + solarUsed + discharge == demand + charge
            var balance = solver.MakeConstraint(demand, demand, $"balance_{h}");
            balance.SetCoefficient(grid[h],      1.0);
            balance.SetCoefficient(solarUsed[h], 1.0);
            balance.SetCoefficient(discharge[h], 1.0);
            balance.SetCoefficient(charge[h],   -1.0);

            // Battery state transition
            if (h == 0)
            {
                // eAfter[0] = initial + charge[0] - discharge[0]
                // → eAfter[0] - charge[0] + discharge[0] == initial
                var soc0 = solver.MakeConstraint(bat.InitialEnergyKwh, bat.InitialEnergyKwh, "soc_0");
                soc0.SetCoefficient(eAfter[0],    1.0);
                soc0.SetCoefficient(charge[0],   -1.0);
                soc0.SetCoefficient(discharge[0], 1.0);
            }
            else
            {
                // eAfter[h] = eAfter[h-1] + charge[h] - discharge[h]
                // → eAfter[h] - eAfter[h-1] - charge[h] + discharge[h] == 0
                var socH = solver.MakeConstraint(0.0, 0.0, $"soc_{h}");
                socH.SetCoefficient(eAfter[h],     1.0);
                socH.SetCoefficient(eAfter[h - 1],-1.0);
                socH.SetCoefficient(charge[h],    -1.0);
                socH.SetCoefficient(discharge[h],  1.0);
            }

            // Big-M: charge[h] <= MaxCharge * isCharging[h]
            var bigMCharge = solver.MakeConstraint(
                double.NegativeInfinity, 0.0, $"bigm_chg_{h}");
            bigMCharge.SetCoefficient(charge[h],      1.0);
            bigMCharge.SetCoefficient(isCharging[h], -bat.MaxChargeKwhPerHour);

            // Big-M: discharge[h] <= MaxDischarge * (1 - isCharging[h])
            // → discharge[h] + MaxDischarge * isCharging[h] <= MaxDischarge
            var bigMDchg = solver.MakeConstraint(
                double.NegativeInfinity, bat.MaxDischargeKwhPerHour, $"bigm_dchg_{h}");
            bigMDchg.SetCoefficient(discharge[h],     1.0);
            bigMDchg.SetCoefficient(isCharging[h],    bat.MaxDischargeKwhPerHour);
        }

        // End-of-day neutrality: eAfter[23] == initial
        var eod = solver.MakeConstraint(bat.InitialEnergyKwh, bat.InitialEnergyKwh, "eod_neutral");
        eod.SetCoefficient(eAfter[H - 1], 1.0);

        // ── Objective: minimise cost = Σ grid[h] * tariff[h] ─────────────────
        var objective = solver.Objective();
        for (int h = 0; h < H; h++)
            objective.SetCoefficient(grid[h], req.Hours[h].TariffBdtPerKwh);
        objective.SetMinimization();

        // ── Solve ─────────────────────────────────────────────────────────────
        var status = solver.Solve();
        if (status != Solver.ResultStatus.OPTIMAL && status != Solver.ResultStatus.FEASIBLE)
            throw new InvalidOperationException(
                $"OR-Tools solver returned non-feasible status: {status}");

        // ── Extract plan ──────────────────────────────────────────────────────
        var plan = new List<HourlyPlanEntry>(H);
        for (int h = 0; h < H; h++)
        {
            double chgVal  = charge[h].SolutionValue();
            double dchgVal = discharge[h].SolutionValue();
            double socVal  = eAfter[h].SolutionValue();
            double grdVal  = grid[h].SolutionValue();
            double slrVal  = solarUsed[h].SolutionValue();

            string action;
            double batteryKwh;
            if (chgVal > 1e-6)
            {
                action     = "charge";
                batteryKwh = chgVal;
            }
            else if (dchgVal > 1e-6)
            {
                action     = "discharge";
                batteryKwh = dchgVal;
            }
            else
            {
                action     = "idle";
                batteryKwh = 0.0;
            }

            plan.Add(new HourlyPlanEntry
            {
                Hour                  = h,
                GridKwh               = Math.Round(grdVal,  4),
                SolarUsedKwh          = Math.Round(slrVal,  4),
                BatteryAction         = action,
                BatteryKwh            = Math.Round(batteryKwh, 4),
                BatteryEnergyAfterKwh = Math.Round(socVal,  4)
            });
        }

        return plan;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsValidHour(int h) => h >= 0 && h < 24;

    /// <summary>
    /// Effective solar for hour h after folding solar_reduction directives.
    /// </summary>
    private static double EffectiveSolar(OptimizeRequest req, int h,
        IReadOnlyList<DirectiveInterpretation> directives)
    {
        double value = req.Hours[h].SolarKwh;
        foreach (var d in directives)
        {
            if (!d.Applies || d.DirectiveType != "solar_reduction") continue;
            if (d.StructuredAdjustment?.Hours?.Contains(h) == true)
                value *= d.StructuredAdjustment.Factor ?? 1.0;
        }
        return Math.Max(0.0, value);
    }

    /// <summary>
    /// Minimum battery energy for hour h after folding minimum_battery_reserve directives.
    /// </summary>
    private static double MinEnergy(OptimizeRequest req, int h,
        IReadOnlyList<DirectiveInterpretation> directives)
    {
        double value = req.Battery.MinimumEnergyKwh;
        foreach (var d in directives)
        {
            if (!d.Applies || d.DirectiveType != "minimum_battery_reserve") continue;
            if (d.StructuredAdjustment?.Hours?.Contains(h) == true &&
                d.StructuredAdjustment.MinimumEnergyKwh is double min)
                value = Math.Max(value, min);
        }
        return Math.Min(value, req.Battery.CapacityKwh);
    }
}
