using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.AI;

/// <summary>
/// Validates a dispatch plan by replaying it hour-by-hour, independent of the optimizer.
/// Never throws — always returns a (bool, string?) tuple.
/// </summary>
public sealed class ScheduleValidator : IScheduleValidator
{
    /// <inheritdoc/>
    public (bool IsValid, string? Reason) Validate(
        OptimizeRequest req,
        IReadOnlyList<HourlyPlanEntry> plan,
        IReadOnlyList<DirectiveInterpretation> directives)
    {
        try
        {
            return ValidateInternal(req, plan, directives);
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected validation error: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static (bool, string?) ValidateInternal(
        OptimizeRequest req,
        IReadOnlyList<HourlyPlanEntry> plan,
        IReadOnlyList<DirectiveInterpretation> directives)
    {
        const double Tol = 1e-4;

        // 1. Plan must have exactly 24 entries, hours 0..23 in order
        if (plan.Count != 24)
            return (false, $"Plan must have 24 entries, got {plan.Count}.");

        for (int i = 0; i < 24; i++)
        {
            if (plan[i].Hour != i)
                return (false, $"Plan entry {i} has hour={plan[i].Hour}, expected {i}.");
        }

        var bat = req.Battery;

        // Pre-compute per-hour effective solar and min-energy from directives
        var effSolar   = new double[24];
        var minEnergy  = new double[24];
        var noCharge   = new HashSet<int>();
        var noDischarge = new HashSet<int>();
        var maxGrid    = new Dictionary<int, double>();

        for (int h = 0; h < 24; h++)
        {
            effSolar[h]  = req.Hours[h].SolarKwh;
            minEnergy[h] = bat.MinimumEnergyKwh;
        }

        foreach (var d in directives.Where(d => d.Applies && d.StructuredAdjustment != null))
        {
            var adj = d.StructuredAdjustment!;
            switch (d.DirectiveType)
            {
                case "solar_reduction":
                    foreach (var h in (adj.Hours ?? []).Where(IsValidHour))
                        effSolar[h] *= adj.Factor ?? 1.0;
                    break;

                case "minimum_battery_reserve":
                    if (adj.MinimumEnergyKwh is double rMin)
                        foreach (var h in (adj.Hours ?? []).Where(IsValidHour))
                            minEnergy[h] = Math.Max(minEnergy[h], rMin);
                    break;

                case "no_charge_window":
                    foreach (var h in (adj.Hours ?? []).Where(IsValidHour))
                        noCharge.Add(h);
                    break;

                case "no_discharge_window":
                    foreach (var h in (adj.Hours ?? []).Where(IsValidHour))
                        noDischarge.Add(h);
                    break;

                case "max_grid_window":
                    if (adj.MaxGridKwh is double gMax)
                        foreach (var h in (adj.Hours ?? []).Where(IsValidHour))
                        {
                            if (!maxGrid.ContainsKey(h) || gMax < maxGrid[h])
                                maxGrid[h] = gMax;
                        }
                    break;
            }
        }

        // Clamp minEnergy to capacity
        for (int h = 0; h < 24; h++)
            minEnergy[h] = Math.Min(minEnergy[h], bat.CapacityKwh);

        // 2. Replay hour-by-hour
        double soc = bat.InitialEnergyKwh;

        for (int h = 0; h < 24; h++)
        {
            var p      = plan[h];
            var entry  = req.Hours[h];
            string act = p.BatteryAction?.ToLower() ?? "";

            // 2a. Valid battery action
            if (act != "charge" && act != "discharge" && act != "idle")
                return (false, $"Hour {h}: invalid BatteryAction '{p.BatteryAction}'.");

            // 2b. Idle → battery_kwh must be 0
            if (act == "idle" && Math.Abs(p.BatteryKwh) > Tol)
                return (false, $"Hour {h}: BatteryAction='idle' but BatteryKwh={p.BatteryKwh}.");

            // 2c. Charge/discharge rate limits
            if (act == "charge" && p.BatteryKwh > bat.MaxChargeKwhPerHour + Tol)
                return (false, $"Hour {h}: charge {p.BatteryKwh} > MaxChargeKwhPerHour {bat.MaxChargeKwhPerHour}.");

            if (act == "discharge" && p.BatteryKwh > bat.MaxDischargeKwhPerHour + Tol)
                return (false, $"Hour {h}: discharge {p.BatteryKwh} > MaxDischargeKwhPerHour {bat.MaxDischargeKwhPerHour}.");

            // 2d. Solar used ≤ effective solar
            if (p.SolarUsedKwh > effSolar[h] + Tol)
                return (false, $"Hour {h}: SolarUsedKwh {p.SolarUsedKwh} > effective solar {effSolar[h]}.");

            // 2e. Energy balance: grid + solar + discharge == demand + charge
            double chgKwh  = act == "charge"    ? p.BatteryKwh : 0.0;
            double dchgKwh = act == "discharge" ? p.BatteryKwh : 0.0;
            double lhs     = p.GridKwh + p.SolarUsedKwh + dchgKwh;
            double rhs     = entry.DemandKwh + chgKwh;
            if (Math.Abs(lhs - rhs) > Tol)
                return (false, $"Hour {h}: energy balance violated. LHS={lhs:F4} RHS={rhs:F4}.");

            // 2f. Battery state transition replay
            soc += chgKwh - dchgKwh;

            // 2g. BatteryEnergyAfter matches replay
            if (Math.Abs(soc - p.BatteryEnergyAfterKwh) > Tol)
                return (false,
                    $"Hour {h}: BatteryEnergyAfterKwh mismatch. Expected {soc:F4}, got {p.BatteryEnergyAfterKwh:F4}.");

            // 2h. Reserve floor
            if (soc < minEnergy[h] - Tol)
                return (false, $"Hour {h}: SoC {soc:F4} < reserve floor {minEnergy[h]:F4}.");

            // 2i. Capacity ceiling
            if (soc > bat.CapacityKwh + Tol)
                return (false, $"Hour {h}: SoC {soc:F4} > capacity {bat.CapacityKwh:F4}.");

            // 2j. Directive window checks
            if (noCharge.Contains(h) && act == "charge")
                return (false, $"Hour {h}: charge action violates no_charge_window directive.");

            if (noDischarge.Contains(h) && act == "discharge")
                return (false, $"Hour {h}: discharge action violates no_discharge_window directive.");

            if (maxGrid.TryGetValue(h, out double gridCap) && p.GridKwh > gridCap + Tol)
                return (false, $"Hour {h}: GridKwh {p.GridKwh:F4} > max_grid_window cap {gridCap:F4}.");
        }

        // 3. End-of-day neutrality
        if (Math.Abs(soc - bat.InitialEnergyKwh) > Tol)
            return (false,
                $"End-of-day SoC {soc:F4} != initial {bat.InitialEnergyKwh:F4}.");

        return (true, null);
    }

    private static bool IsValidHour(int h) => h >= 0 && h < 24;
}
