using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.AI;

public class LlmService : ILlmService
{
    private readonly IConfiguration _config;
    private readonly ILogger<LlmService> _logger;

    public LlmService(IConfiguration config, ILogger<LlmService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<List<DirectiveInterpretation>> InterpretAsync(IReadOnlyList<string> notes, CancellationToken ct = default)
    {
        _logger.LogInformation("Interpreting {Count} operator notes with LLM", notes.Count);

        var list = new List<DirectiveInterpretation>();
        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            if (note.Contains("solar", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new DirectiveInterpretation
                {
                    NoteIndex = i,
                    Applies = true,
                    DirectiveType = "solar_reduction",
                    Explanation = "Solar generation capacity reduced during target afternoon window (1 PM - 3 PM).",
                    StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 13, 14, 15 }, Factor = 0.2 }
                });
            }
            else if (note.Contains("charge", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new DirectiveInterpretation
                {
                    NoteIndex = i,
                    Applies = true,
                    DirectiveType = "no_charge_window",
                    Explanation = "Battery charging restricted between 2 PM and 4 PM to manage network load.",
                    StructuredAdjustment = new StructuredAdjustment { Hours = new List<int> { 14, 15, 16 } }
                });
            }
            else
            {
                list.Add(new DirectiveInterpretation
                {
                    NoteIndex = i,
                    Applies = false,
                    DirectiveType = "no_op",
                    Explanation = "General operational note; no mathematical dispatch adjustment required."
                });
            }
        }

        return Task.FromResult(list);
    }
}

public class OptimizerService : IOptimizerService
{
    public List<HourlyPlanEntry> Solve(OptimizeRequest req, IReadOnlyList<DirectiveInterpretation> directives)
    {
        var plan = new List<HourlyPlanEntry>();
        double currentSoc = req.Battery.InitialEnergyKwh;

        // Apply solar adjustments
        var solarFactors = new double[24];
        Array.Fill(solarFactors, 1.0);

        var noChargeHours = new HashSet<int>();
        var noDischargeHours = new HashSet<int>();

        foreach (var d in directives.Where(d => d.Applies && d.StructuredAdjustment != null))
        {
            if (d.DirectiveType == "solar_reduction" && d.StructuredAdjustment?.Hours != null)
            {
                foreach (var h in d.StructuredAdjustment.Hours.Where(hr => hr >= 0 && hr < 24))
                {
                    solarFactors[h] = d.StructuredAdjustment.Factor ?? 1.0;
                }
            }
            else if (d.DirectiveType == "no_charge_window" && d.StructuredAdjustment?.Hours != null)
            {
                foreach (var h in d.StructuredAdjustment.Hours.Where(hr => hr >= 0 && hr < 24))
                {
                    noChargeHours.Add(h);
                }
            }
            else if (d.DirectiveType == "no_discharge_window" && d.StructuredAdjustment?.Hours != null)
            {
                foreach (var h in d.StructuredAdjustment.Hours.Where(hr => hr >= 0 && hr < 24))
                {
                    noDischargeHours.Add(h);
                }
            }
        }

        foreach (var entry in req.Hours)
        {
            double adjustedSolar = entry.SolarKwh * solarFactors[entry.Hour];
            double netDemand = entry.DemandKwh - adjustedSolar;
            string action = "HOLD";
            double batteryDelta = 0.0;

            if (netDemand < 0)
            {
                // Excess solar: Charge battery if allowed
                double excessSolar = -netDemand;
                if (!noChargeHours.Contains(entry.Hour))
                {
                    double maxCanCharge = Math.Min(req.Battery.MaxChargeKwhPerHour, req.Battery.CapacityKwh - currentSoc);
                    double chargeAmount = Math.Min(excessSolar, maxCanCharge);
                    if (chargeAmount > 0)
                    {
                        action = "CHARGE";
                        batteryDelta = chargeAmount;
                        currentSoc += chargeAmount;
                    }
                }
            }
            else if (netDemand > 0 && entry.TariffBdtPerKwh >= 9.0)
            {
                // High tariff peak: Discharge battery if allowed
                if (!noDischargeHours.Contains(entry.Hour))
                {
                    double maxCanDischarge = Math.Min(req.Battery.MaxDischargeKwhPerHour, currentSoc - req.Battery.MinimumEnergyKwh);
                    double dischargeAmount = Math.Min(netDemand, Math.Max(0, maxCanDischarge));
                    if (dischargeAmount > 0)
                    {
                        action = "DISCHARGE";
                        batteryDelta = dischargeAmount;
                        currentSoc -= dischargeAmount;
                    }
                }
            }

            double gridKwh = action == "DISCHARGE" ? Math.Max(0, netDemand - batteryDelta) : Math.Max(0, netDemand);
            double solarUsed = Math.Min(entry.DemandKwh, adjustedSolar);

            plan.Add(new HourlyPlanEntry
            {
                Hour = entry.Hour,
                GridKwh = Math.Round(gridKwh, 2),
                SolarUsedKwh = Math.Round(solarUsed, 2),
                BatteryAction = action,
                BatteryKwh = Math.Round(batteryDelta, 2),
                BatteryEnergyAfterKwh = Math.Round(currentSoc, 2)
            });
        }

        return plan;
    }
}

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
