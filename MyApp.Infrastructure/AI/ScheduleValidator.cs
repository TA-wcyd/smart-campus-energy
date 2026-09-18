using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.AI;

public class ScheduleValidator : IScheduleValidator
{
    public (bool IsValid, string? Reason) Validate(OptimizeRequest req, IReadOnlyList<HourlyPlanEntry> plan, IReadOnlyList<DirectiveInterpretation> directives)
    {
        if (plan.Count != 24)
        {
            return (false, "Dispatch plan must contain exactly 24 entries.");
        }

        foreach (var p in plan)
        {
            if (p.BatteryEnergyAfterKwh < req.Battery.MinimumEnergyKwh - 0.01)
            {
                return (false, $"Battery SoC at hour {p.Hour} dropped below minimum reserve limit ({req.Battery.MinimumEnergyKwh} kWh).");
            }
            if (p.BatteryEnergyAfterKwh > req.Battery.CapacityKwh + 0.01)
            {
                return (false, $"Battery SoC at hour {p.Hour} exceeded maximum capacity ({req.Battery.CapacityKwh} kWh).");
            }
        }

        return (true, null);
    }
}
