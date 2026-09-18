using System.Text.Json.Serialization;
using MyApp.Core.Models;

namespace MyApp.Api.Dtos;

public class OptimizeResponseDto
{
    [JsonPropertyName("scenario_id")]
    public string ScenarioId { get; set; } = string.Empty;

    [JsonPropertyName("directive_interpretation")]
    public List<DirectiveInterpretationDto> DirectiveInterpretation { get; set; } = new();

    [JsonPropertyName("hourly_plan")]
    public List<HourlyPlanEntryDto> HourlyPlan { get; set; } = new();

    [JsonPropertyName("total_grid_kwh")]
    public double TotalGridKwh { get; set; }

    [JsonPropertyName("total_cost_bdt")]
    public double TotalCostBdt { get; set; }

    [JsonPropertyName("peak_grid_kwh")]
    public double PeakGridKwh { get; set; }

    [JsonPropertyName("plan_summary")]
    public string PlanSummary { get; set; } = string.Empty;

    public static OptimizeResponseDto FromDomain(OptimizeResponse domain)
    {
        return new OptimizeResponseDto
        {
            ScenarioId = domain.ScenarioId,
            DirectiveInterpretation = domain.DirectiveInterpretation?
                .Select(DirectiveInterpretationDto.FromDomain).ToList() ?? new(),
            HourlyPlan = domain.HourlyPlan?
                .Select(HourlyPlanEntryDto.FromDomain).ToList() ?? new(),
            TotalGridKwh = domain.TotalGridKwh,
            TotalCostBdt = domain.TotalCostBdt,
            PeakGridKwh = domain.PeakGridKwh,
            PlanSummary = domain.PlanSummary
        };
    }
}

public class DirectiveInterpretationDto
{
    [JsonPropertyName("note_index")]
    public int NoteIndex { get; set; }

    [JsonPropertyName("applies")]
    public bool Applies { get; set; }

    [JsonPropertyName("directive_type")]
    public string DirectiveType { get; set; } = string.Empty;

    [JsonPropertyName("structured_adjustment")]
    public StructuredAdjustmentDto? StructuredAdjustment { get; set; }

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    public static DirectiveInterpretationDto FromDomain(DirectiveInterpretation domain)
    {
        return new DirectiveInterpretationDto
        {
            NoteIndex = domain.NoteIndex,
            Applies = domain.Applies,
            DirectiveType = domain.DirectiveType,
            StructuredAdjustment = domain.StructuredAdjustment != null
                ? StructuredAdjustmentDto.FromDomain(domain.StructuredAdjustment)
                : null,
            Explanation = domain.Explanation
        };
    }
}

public class StructuredAdjustmentDto
{
    [JsonPropertyName("hours")]
    public List<int>? Hours { get; set; }

    [JsonPropertyName("factor")]
    public double? Factor { get; set; }

    [JsonPropertyName("minimum_energy_kwh")]
    public double? MinimumEnergyKwh { get; set; }

    [JsonPropertyName("max_grid_kwh")]
    public double? MaxGridKwh { get; set; }

    public static StructuredAdjustmentDto FromDomain(StructuredAdjustment domain)
    {
        return new StructuredAdjustmentDto
        {
            Hours = domain.Hours != null ? new List<int>(domain.Hours) : null,
            Factor = domain.Factor,
            MinimumEnergyKwh = domain.MinimumEnergyKwh,
            MaxGridKwh = domain.MaxGridKwh
        };
    }
}

public class HourlyPlanEntryDto
{
    [JsonPropertyName("hour")]
    public int Hour { get; set; }

    [JsonPropertyName("grid_kwh")]
    public double GridKwh { get; set; }

    [JsonPropertyName("solar_used_kwh")]
    public double SolarUsedKwh { get; set; }

    [JsonPropertyName("battery_action")]
    public string BatteryAction { get; set; } = string.Empty;

    [JsonPropertyName("battery_kwh")]
    public double BatteryKwh { get; set; }

    [JsonPropertyName("battery_energy_after_kwh")]
    public double BatteryEnergyAfterKwh { get; set; }

    public static HourlyPlanEntryDto FromDomain(HourlyPlanEntry domain)
    {
        return new HourlyPlanEntryDto
        {
            Hour = domain.Hour,
            GridKwh = domain.GridKwh,
            SolarUsedKwh = domain.SolarUsedKwh,
            BatteryAction = domain.BatteryAction,
            BatteryKwh = domain.BatteryKwh,
            BatteryEnergyAfterKwh = domain.BatteryEnergyAfterKwh
        };
    }
}
