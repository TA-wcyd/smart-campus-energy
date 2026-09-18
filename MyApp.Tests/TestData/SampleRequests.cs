using MyApp.Core.Models;

namespace MyApp.Tests.TestData;

public static class SampleRequests
{
    public static OptimizeRequest SimpleScenario()
    {
        var hours = new List<HourEntry>(24);
        for (int h = 0; h < 24; h++)
        {
            // Base demand: higher during workday
            double demand = (h >= 8 && h <= 17) ? 120.0 : 60.0;

            // Solar generation: bell curve around midday (6 AM to 6 PM)
            double solar = 0.0;
            if (h >= 6 && h <= 18)
            {
                solar = Math.Max(0.0, 150.0 * Math.Sin((h - 6) * Math.PI / 12.0));
            }

            // Tariff: peak tariff between 18:00 and 21:00 (BDT 14/kWh), standard BDT 7/kWh
            double tariff = (h >= 18 && h <= 21) ? 14.0 : 7.0;

            hours.Add(new HourEntry
            {
                Hour = h,
                DemandKwh = Math.Round(demand, 2),
                SolarKwh = Math.Round(solar, 2),
                TariffBdtPerKwh = tariff
            });
        }

        return new OptimizeRequest
        {
            ScenarioId = "bup-cse-fest-scenario-001",
            OperatorNotes = new List<string>
            {
                "Solar generation capacity reduction anticipated in early afternoon.",
                "Avoid battery charging during peak grid congestion window."
            },
            Battery = new Battery
            {
                CapacityKwh = 500.0,
                InitialEnergyKwh = 200.0,
                MinimumEnergyKwh = 50.0,
                MaxChargeKwhPerHour = 100.0,
                MaxDischargeKwhPerHour = 100.0
            },
            Hours = hours
        };
    }
}
