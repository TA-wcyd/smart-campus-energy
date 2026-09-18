namespace MyApp.Core.Models;

public class OptimizeRequest
{
    public string ScenarioId { get; set; } = string.Empty;
    public List<string> OperatorNotes { get; set; } = new();
    public List<HourEntry> Hours { get; set; } = new();
    public Battery Battery { get; set; } = new();
}

public class HourEntry
{
    public int Hour { get; set; }
    public double DemandKwh { get; set; }
    public double SolarKwh { get; set; }
    public double TariffBdtPerKwh { get; set; }
}

public class Battery
{
    public double CapacityKwh { get; set; }
    public double InitialEnergyKwh { get; set; }
    public double MinimumEnergyKwh { get; set; }
    public double MaxChargeKwhPerHour { get; set; }
    public double MaxDischargeKwhPerHour { get; set; }
}

public class DirectiveInterpretation
{
    public int NoteIndex { get; set; }
    public bool Applies { get; set; }
    public string DirectiveType { get; set; } = string.Empty;
    public StructuredAdjustment? StructuredAdjustment { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public class StructuredAdjustment
{
    public List<int>? Hours { get; set; }
    public double? Factor { get; set; }
    public double? MinimumEnergyKwh { get; set; }
    public double? MaxGridKwh { get; set; }
}

public class HourlyPlanEntry
{
    public int Hour { get; set; }
    public double GridKwh { get; set; }
    public double SolarUsedKwh { get; set; }
    public string BatteryAction { get; set; } = string.Empty;
    public double BatteryKwh { get; set; }
    public double BatteryEnergyAfterKwh { get; set; }
}

public class OptimizeResponse
{
    public string ScenarioId { get; set; } = string.Empty;
    public List<DirectiveInterpretation> DirectiveInterpretation { get; set; } = new();
    public List<HourlyPlanEntry> HourlyPlan { get; set; } = new();
    public double TotalGridKwh { get; set; }
    public double TotalCostBdt { get; set; }
    public double PeakGridKwh { get; set; }
    public string PlanSummary { get; set; } = string.Empty;
}

public class EnergyScenario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ScenarioId { get; set; } = string.Empty;
    public string OperatorNotesJson { get; set; } = "[]";
    public string HoursJson { get; set; } = "[]";
    public string BatteryJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class EnergyPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public string DirectiveInterpretationJson { get; set; } = "[]";
    public string HourlyPlanJson { get; set; } = "[]";
    public double TotalGridKwh { get; set; }
    public double TotalCostBdt { get; set; }
    public double PeakGridKwh { get; set; }
    public string PlanSummary { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
