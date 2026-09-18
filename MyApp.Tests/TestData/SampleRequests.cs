using MyApp.Core.Models;

namespace MyApp.Tests.TestData;

/// <summary>
/// Shared test-data factory.
/// </summary>
public static class SampleRequests
{
    /// <summary>
    /// A standard 24-hour campus scenario.
    /// Battery: 500 kWh capacity, 200 kWh initial, 50 kWh minimum reserve,
    ///          100 kWh/h charge/discharge rate.
    /// Solar: peaks at midday (hours 10-14).
    /// Tariff: peak at evening hours 18-21 (18 BDT/kWh).
    /// </summary>
    public static OptimizeRequest SimpleScenario() => new()
    {
        ScenarioId    = "test-simple-001",
        OperatorNotes = new List<string>(),
        Battery = new Battery
        {
            CapacityKwh           = 500.0,
            InitialEnergyKwh      = 200.0,
            MinimumEnergyKwh      = 50.0,
            MaxChargeKwhPerHour   = 100.0,
            MaxDischargeKwhPerHour = 100.0
        },
        Hours = BuildHours()
    };

    // ── Private helpers ───────────────────────────────────────────────────────

    private static List<HourEntry> BuildHours()
    {
        var hours = new List<HourEntry>(24);
        for (int h = 0; h < 24; h++)
        {
            hours.Add(new HourEntry
            {
                Hour             = h,
                DemandKwh        = BaseDemand(h),
                SolarKwh         = SolarProfile(h),
                TariffBdtPerKwh  = TariffProfile(h)
            });
        }
        return hours;
    }

    /// <summary>Flat base demand with slight morning/evening bumps.</summary>
    private static double BaseDemand(int h) => h switch
    {
        >= 6  and < 9  => 120.0, // morning ramp
        >= 9  and < 17 => 100.0, // daytime
        >= 17 and < 22 => 150.0, // evening peak
        >= 22 or  < 6  =>  60.0, // overnight
        _               => 100.0
    };

    /// <summary>Solar bell curve peaking at noon.</summary>
    private static double SolarProfile(int h) => h switch
    {
        6  => 10.0,
        7  => 30.0,
        8  => 60.0,
        9  => 90.0,
        10 => 120.0,
        11 => 150.0,
        12 => 160.0,
        13 => 150.0,
        14 => 120.0,
        15 => 90.0,
        16 => 60.0,
        17 => 30.0,
        18 => 10.0,
        _  =>  0.0
    };

    /// <summary>Peak tariff 18-21, off-peak otherwise.</summary>
    private static double TariffProfile(int h) =>
        h is >= 18 and <= 21 ? 18.0 : 7.0;
}
