using Google.OrTools.LinearSolver;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.AI;

public sealed class OptimizerService : IOptimizerService
{
    public List<HourlyPlanEntry> Solve(OptimizeRequest req, IReadOnlyList<DirectiveInterpretation> directives)
    {
        ArgumentNullException.ThrowIfNull(req);
        directives ??= Array.Empty<DirectiveInterpretation>();

        if (req.Hours == null || req.Hours.Count != 24)
        {
            throw new ArgumentException("Request must contain exactly 24 hour entries.", nameof(req));
        }

        // Sort hours to ensure 0..23 ordering
        var sortedHours = req.Hours.OrderBy(h => h.Hour).ToList();

        // Create Mixed Integer Programming Solver
        Solver solver = Solver.CreateSolver("CBC_MIXED_INTEGER_PROGRAMMING")
                        ?? Solver.CreateSolver("SCIP")
                        ?? Solver.CreateSolver("CBC")
                        ?? throw new InvalidOperationException("Could not initialize Google OR-Tools Linear Solver.");

        var grid = new Variable[24];
        var solarUsed = new Variable[24];
        var charge = new Variable[24];
        var discharge = new Variable[24];
        var eAfter = new Variable[24];
        var isCharging = new Variable[24];

        // 1. Declare Variables
        for (int h = 0; h < 24; h++)
        {
            double effSolar = EffectiveSolar(req, h, directives);
            double minSoc = MinEnergy(req, h, directives);

            grid[h] = solver.MakeNumVar(0.0, double.PositiveInfinity, $"grid_{h}");
            solarUsed[h] = solver.MakeNumVar(0.0, effSolar, $"solarUsed_{h}");
            charge[h] = solver.MakeNumVar(0.0, req.Battery.MaxChargeKwhPerHour, $"charge_{h}");
            discharge[h] = solver.MakeNumVar(0.0, req.Battery.MaxDischargeKwhPerHour, $"discharge_{h}");
            eAfter[h] = solver.MakeNumVar(minSoc, req.Battery.CapacityKwh, $"eAfter_{h}");
            isCharging[h] = solver.MakeBoolVar($"isCharging_{h}");
        }

        // 2. Apply Directive-specific Variable Bounds
        foreach (var d in directives.Where(d => d.Applies && d.StructuredAdjustment?.Hours != null))
        {
            var adjHours = d.StructuredAdjustment!.Hours!;
            if (d.DirectiveType.Equals("no_charge_window", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var h in adjHours.Where(hr => hr >= 0 && hr < 24))
                {
                    charge[h].SetBounds(0.0, 0.0);
                }
            }
            else if (d.DirectiveType.Equals("no_discharge_window", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var h in adjHours.Where(hr => hr >= 0 && hr < 24))
                {
                    discharge[h].SetBounds(0.0, 0.0);
                }
            }
            else if (d.DirectiveType.Equals("max_grid_window", StringComparison.OrdinalIgnoreCase) && d.StructuredAdjustment.MaxGridKwh.HasValue)
            {
                double cap = d.StructuredAdjustment.MaxGridKwh.Value;
                foreach (var h in adjHours.Where(hr => hr >= 0 && hr < 24))
                {
                    grid[h].SetBounds(0.0, cap);
                }
            }
        }

        // 3. Set Objective Function: Minimize sum(grid[h] * tariff[h])
        Objective objective = solver.Objective();
        for (int h = 0; h < 24; h++)
        {
            objective.SetCoefficient(grid[h], sortedHours[h].TariffBdtPerKwh);
        }
        objective.SetMinimization();

        // 4. Set Constraints
        for (int h = 0; h < 24; h++)
        {
            double demand = sortedHours[h].DemandKwh;

            // Energy balance per hour: grid + solarUsed + discharge == demand + charge
            // <=> grid + solarUsed + discharge - charge == demand
            Constraint balance = solver.MakeConstraint(demand, demand, $"balance_{h}");
            balance.SetCoefficient(grid[h], 1.0);
            balance.SetCoefficient(solarUsed[h], 1.0);
            balance.SetCoefficient(discharge[h], 1.0);
            balance.SetCoefficient(charge[h], -1.0);

            // Battery transition
            if (h == 0)
            {
                // eAfter[0] = initial + charge[0] - discharge[0]
                // <=> eAfter[0] - charge[0] + discharge[0] = initial
                Constraint trans0 = solver.MakeConstraint(req.Battery.InitialEnergyKwh, req.Battery.InitialEnergyKwh, "trans_0");
                trans0.SetCoefficient(eAfter[0], 1.0);
                trans0.SetCoefficient(charge[0], -1.0);
                trans0.SetCoefficient(discharge[0], 1.0);
            }
            else
            {
                // eAfter[h] = eAfter[h-1] + charge[h] - discharge[h]
                // <=> eAfter[h] - eAfter[h-1] - charge[h] + discharge[h] = 0
                Constraint transH = solver.MakeConstraint(0.0, 0.0, $"trans_{h}");
                transH.SetCoefficient(eAfter[h], 1.0);
                transH.SetCoefficient(eAfter[h - 1], -1.0);
                transH.SetCoefficient(charge[h], -1.0);
                transH.SetCoefficient(discharge[h], 1.0);
            }

            // Big-M mutual exclusivity binaries
            // charge[h] <= MaxCharge * isCharging[h]
            // <=> charge[h] - MaxCharge * isCharging[h] <= 0
            Constraint bigMCharge = solver.MakeConstraint(double.NegativeInfinity, 0.0, $"bigMCharge_{h}");
            bigMCharge.SetCoefficient(charge[h], 1.0);
            bigMCharge.SetCoefficient(isCharging[h], -req.Battery.MaxChargeKwhPerHour);

            // discharge[h] <= MaxDischarge * (1 - isCharging[h])
            // <=> discharge[h] + MaxDischarge * isCharging[h] <= MaxDischarge
            Constraint bigMDischarge = solver.MakeConstraint(double.NegativeInfinity, req.Battery.MaxDischargeKwhPerHour, $"bigMDischarge_{h}");
            bigMDischarge.SetCoefficient(discharge[h], 1.0);
            bigMDischarge.SetCoefficient(isCharging[h], req.Battery.MaxDischargeKwhPerHour);
        }

        // End-of-day neutrality constraint: eAfter[23] == initial
        Constraint neutrality = solver.MakeConstraint(req.Battery.InitialEnergyKwh, req.Battery.InitialEnergyKwh, "neutrality");
        neutrality.SetCoefficient(eAfter[23], 1.0);

        // 5. Solve the optimization model
        Solver.ResultStatus status = solver.Solve();
        if (status != Solver.ResultStatus.OPTIMAL && status != Solver.ResultStatus.FEASIBLE)
        {
            throw new InvalidOperationException($"OR-Tools solver failed with status: {status}");
        }

        // 6. Extract the Hourly Plan Entries
        var plan = new List<HourlyPlanEntry>(24);
        for (int h = 0; h < 24; h++)
        {
            double cVal = charge[h].SolutionValue();
            double dVal = discharge[h].SolutionValue();
            double gVal = grid[h].SolutionValue();
            double sVal = solarUsed[h].SolutionValue();
            double eVal = eAfter[h].SolutionValue();

            string action;
            double batteryKwh;

            if (cVal > 1e-6)
            {
                action = "charge";
                batteryKwh = cVal;
            }
            else if (dVal > 1e-6)
            {
                action = "discharge";
                batteryKwh = dVal;
            }
            else
            {
                action = "idle";
                batteryKwh = 0.0;
            }

            plan.Add(new HourlyPlanEntry
            {
                Hour = h,
                GridKwh = Math.Round(gVal, 4),
                SolarUsedKwh = Math.Round(sVal, 4),
                BatteryAction = action,
                BatteryKwh = Math.Round(batteryKwh, 4),
                BatteryEnergyAfterKwh = Math.Round(eVal, 4)
            });
        }

        return plan;
    }

    public static double EffectiveSolar(OptimizeRequest req, int h, IReadOnlyList<DirectiveInterpretation> directives)
    {
        var hourEntry = req.Hours.FirstOrDefault(e => e.Hour == h);
        double val = hourEntry?.SolarKwh ?? 0.0;
        if (directives != null)
        {
            foreach (var d in directives.Where(d => d.Applies && d.DirectiveType.Equals("solar_reduction", StringComparison.OrdinalIgnoreCase)))
            {
                if (d.StructuredAdjustment?.Hours != null && d.StructuredAdjustment.Hours.Contains(h))
                {
                    val *= (d.StructuredAdjustment.Factor ?? 1.0);
                }
            }
        }
        return Math.Max(0.0, val);
    }

    public static double MinEnergy(OptimizeRequest req, int h, IReadOnlyList<DirectiveInterpretation> directives)
    {
        double val = req.Battery.MinimumEnergyKwh;
        if (directives != null)
        {
            foreach (var d in directives.Where(d => d.Applies &&
                (d.DirectiveType.Equals("minimum_battery_reserve", StringComparison.OrdinalIgnoreCase) ||
                 d.DirectiveType.Equals("battery_reserve", StringComparison.OrdinalIgnoreCase))))
            {
                if (d.StructuredAdjustment?.Hours != null && d.StructuredAdjustment.Hours.Contains(h))
                {
                    if (d.StructuredAdjustment.MinimumEnergyKwh.HasValue)
                    {
                        val = Math.Max(val, d.StructuredAdjustment.MinimumEnergyKwh.Value);
                    }
                }
            }
        }
        return Math.Min(val, req.Battery.CapacityKwh);
    }
}
