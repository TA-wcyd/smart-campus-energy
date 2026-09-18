using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.AI;

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
