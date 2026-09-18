using System.Text.Json.Serialization;
using MyApp.Core.Models;

namespace MyApp.Api.Dtos;

public class OptimizeRequestDto
{
    [JsonPropertyName("scenario_id")]
    public string ScenarioId { get; set; } = string.Empty;

    [JsonPropertyName("operator_notes")]
    public List<string> OperatorNotes { get; set; } = new();

    [JsonPropertyName("hours")]
    public List<HourEntryDto> Hours { get; set; } = new();

    [JsonPropertyName("battery")]
    public BatteryDto Battery { get; set; } = new();

    public OptimizeRequest MapToDomain()
    {
        return new OptimizeRequest
        {
            ScenarioId = ScenarioId,
            OperatorNotes = OperatorNotes ?? new List<string>(),
            Hours = Hours?.Select(h => h.MapToDomain()).ToList() ?? new List<HourEntry>(),
            Battery = Battery?.MapToDomain() ?? new Battery()
        };
    }
}

public class HourEntryDto
{
    [JsonPropertyName("hour")]
    public int Hour { get; set; }

    [JsonPropertyName("demand_kwh")]
    public double DemandKwh { get; set; }

    [JsonPropertyName("solar_kwh")]
    public double SolarKwh { get; set; }

    [JsonPropertyName("tariff_bdt_per_kwh")]
    public double TariffBdtPerKwh { get; set; }

    public HourEntry MapToDomain()
    {
        return new HourEntry
        {
            Hour = Hour,
            DemandKwh = DemandKwh,
            SolarKwh = SolarKwh,
            TariffBdtPerKwh = TariffBdtPerKwh
        };
    }

    public static HourEntryDto FromDomain(HourEntry domain)
    {
        return new HourEntryDto
        {
            Hour = domain.Hour,
            DemandKwh = domain.DemandKwh,
            SolarKwh = domain.SolarKwh,
            TariffBdtPerKwh = domain.TariffBdtPerKwh
        };
    }
}

public class BatteryDto
{
    [JsonPropertyName("capacity_kwh")]
    public double CapacityKwh { get; set; }

    [JsonPropertyName("initial_energy_kwh")]
    public double InitialEnergyKwh { get; set; }

    [JsonPropertyName("minimum_energy_kwh")]
    public double MinimumEnergyKwh { get; set; }

    [JsonPropertyName("max_charge_kwh_per_hour")]
    public double MaxChargeKwhPerHour { get; set; }

    [JsonPropertyName("max_discharge_kwh_per_hour")]
    public double MaxDischargeKwhPerHour { get; set; }

    public Battery MapToDomain()
    {
        return new Battery
        {
            CapacityKwh = CapacityKwh,
            InitialEnergyKwh = InitialEnergyKwh,
            MinimumEnergyKwh = MinimumEnergyKwh,
            MaxChargeKwhPerHour = MaxChargeKwhPerHour,
            MaxDischargeKwhPerHour = MaxDischargeKwhPerHour
        };
    }

    public static BatteryDto FromDomain(Battery domain)
    {
        return new BatteryDto
        {
            CapacityKwh = domain.CapacityKwh,
            InitialEnergyKwh = domain.InitialEnergyKwh,
            MinimumEnergyKwh = domain.MinimumEnergyKwh,
            MaxChargeKwhPerHour = domain.MaxChargeKwhPerHour,
            MaxDischargeKwhPerHour = domain.MaxDischargeKwhPerHour
        };
    }
}
