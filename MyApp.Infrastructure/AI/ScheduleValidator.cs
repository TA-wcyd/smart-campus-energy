using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.AI;

public sealed class ScheduleValidator : IScheduleValidator
{
    public (bool IsValid, string? Reason) Validate(
        OptimizeRequest req,
        IReadOnlyList<HourlyPlanEntry> plan,
        IReadOnlyList<DirectiveInterpretation> directives)
    {
        if (req == null)
        {
            return (false, "Optimization request cannot be null.");
        }

        if (plan == null)
        {
            return (false, "Hourly dispatch plan cannot be null.");
        }

        if (plan.Count != 24)
        {
            return (false, $"Dispatch plan must contain exactly 24 entries, but found {plan.Count}.");
        }

        if (req.Hours == null || req.Hours.Count != 24)
        {
            return (false, "Request must contain exactly 24 hour entries.");
        }

        directives ??= Array.Empty<DirectiveInterpretation>();
        var sortedHours = req.Hours.OrderBy(h => h.Hour).ToList();

        double prevSoc = req.Battery.InitialEnergyKwh;

        for (int h = 0; h < 24; h++)
        {
            var p = plan[h];
            var hrReq = sortedHours[h];

            if (p.Hour != h)
            {
                return (false, $"Plan entry at index {h} has mismatched hour {p.Hour} (expected {h}).");
            }

            var action = (p.BatteryAction ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "charge" && action != "discharge" && action != "idle")
            {
                return (false, $"Hour {h}: Invalid battery action '{p.BatteryAction}'. Must be 'charge', 'discharge', or 'idle'.");
            }

            // Non-negativity checks
            if (p.GridKwh < -1e-4)
            {
                return (false, $"Hour {h}: Grid kWh cannot be negative ({p.GridKwh}).");
            }
            if (p.SolarUsedKwh < -1e-4)
            {
                return (false, $"Hour {h}: Solar used kWh cannot be negative ({p.SolarUsedKwh}).");
            }
            if (p.BatteryKwh < -1e-4)
            {
                return (false, $"Hour {h}: Battery kWh cannot be negative ({p.BatteryKwh}).");
            }

            // Directive specific window checks
            foreach (var d in directives.Where(d => d.Applies && d.StructuredAdjustment?.Hours != null))
            {
                var adjHours = d.StructuredAdjustment!.Hours!;
                if (adjHours.Contains(h))
                {
                    if (d.DirectiveType.Equals("no_charge_window", StringComparison.OrdinalIgnoreCase))
                    {
                        if (action == "charge" && p.BatteryKwh > 1e-4)
                        {
                            return (false, $"Hour {h}: Battery charging occurred during prohibited 'no_charge_window'.");
                        }
                    }
                    else if (d.DirectiveType.Equals("no_discharge_window", StringComparison.OrdinalIgnoreCase))
                    {
                        if (action == "discharge" && p.BatteryKwh > 1e-4)
                        {
                            return (false, $"Hour {h}: Battery discharging occurred during prohibited 'no_discharge_window'.");
                        }
                    }
                    else if (d.DirectiveType.Equals("max_grid_window", StringComparison.OrdinalIgnoreCase) && d.StructuredAdjustment.MaxGridKwh.HasValue)
                    {
                        double cap = d.StructuredAdjustment.MaxGridKwh.Value;
                        if (p.GridKwh > cap + 1e-3)
                        {
                            return (false, $"Hour {h}: Grid import ({p.GridKwh:F4} kWh) exceeded max grid cap ({cap:F4} kWh).");
                        }
                    }
                }
            }

            // Effective solar bound check
            double effSolar = OptimizerService.EffectiveSolar(req, h, directives);
            if (p.SolarUsedKwh > effSolar + 1e-3)
            {
                return (false, $"Hour {h}: Solar used ({p.SolarUsedKwh} kWh) exceeds effective solar generation ({effSolar} kWh).");
            }

            // Battery action rate limit & idle checks
            if (action == "idle" && p.BatteryKwh > 1e-4)
            {
                return (false, $"Hour {h}: Battery kWh must be 0 for 'idle' action, but was {p.BatteryKwh}.");
            }
            if (action == "charge" && p.BatteryKwh > req.Battery.MaxChargeKwhPerHour + 1e-3)
            {
                return (false, $"Hour {h}: Battery charge ({p.BatteryKwh} kWh) exceeds maximum charge rate ({req.Battery.MaxChargeKwhPerHour} kWh/h).");
            }
            if (action == "discharge" && p.BatteryKwh > req.Battery.MaxDischargeKwhPerHour + 1e-3)
            {
                return (false, $"Hour {h}: Battery discharge ({p.BatteryKwh} kWh) exceeds maximum discharge rate ({req.Battery.MaxDischargeKwhPerHour} kWh/h).");
            }

            // Reserve floor and capacity bounds check
            double minSoc = OptimizerService.MinEnergy(req, h, directives);
            if (p.BatteryEnergyAfterKwh < minSoc - 1e-3)
            {
                return (false, $"Hour {h}: Battery SoC ({p.BatteryEnergyAfterKwh:F4} kWh) dropped below required reserve floor ({minSoc:F4} kWh).");
            }
            if (p.BatteryEnergyAfterKwh > req.Battery.CapacityKwh + 1e-3)
            {
                return (false, $"Hour {h}: Battery SoC ({p.BatteryEnergyAfterKwh:F4} kWh) exceeded battery capacity ({req.Battery.CapacityKwh:F4} kWh).");
            }

            // Energy balance check: grid + solarUsed + discharge == demand + charge
            double dischargeDelta = action == "discharge" ? p.BatteryKwh : 0.0;
            double chargeDelta = action == "charge" ? p.BatteryKwh : 0.0;

            double generation = p.GridKwh + p.SolarUsedKwh + dischargeDelta;
            double demandAndCharge = hrReq.DemandKwh + chargeDelta;

            if (Math.Abs(generation - demandAndCharge) > 1e-3)
            {
                return (false, $"Hour {h}: Energy balance violation. Generation/Supply ({generation:F4} kWh) != Demand/Load ({demandAndCharge:F4} kWh).");
            }

            // Battery transition check
            double expectedSoc = (h == 0 ? req.Battery.InitialEnergyKwh : prevSoc) + chargeDelta - dischargeDelta;
            if (Math.Abs(p.BatteryEnergyAfterKwh - expectedSoc) > 1e-3)
            {
                return (false, $"Hour {h}: Battery transition mismatch. Recorded SoC ({p.BatteryEnergyAfterKwh:F4} kWh) != Expected SoC ({expectedSoc:F4} kWh).");
            }

            prevSoc = p.BatteryEnergyAfterKwh;
        }

        // End-of-day neutrality check: eAfter[23] == initial
        if (Math.Abs(plan[23].BatteryEnergyAfterKwh - req.Battery.InitialEnergyKwh) > 1e-3)
        {
            return (false, $"End-of-day neutrality violation. Final SoC ({plan[23].BatteryEnergyAfterKwh:F4} kWh) != Initial SoC ({req.Battery.InitialEnergyKwh:F4} kWh).");
        }

        return (true, null);
    }
}
