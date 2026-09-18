using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Api.Pages;

public class HistoryDetailModel : PageModel
{
    private readonly IEnergyRepository _energyRepository;
    private readonly ILogger<HistoryDetailModel> _logger;

    public Guid Id { get; set; }
    public EnergyScenario? Scenario { get; set; }
    public EnergyPlan? Plan { get; set; }
    public List<DirectiveInterpretation> Directives { get; set; } = new();
    public List<HourlyPlanEntry> HourlyPlan { get; set; } = new();

    public HistoryDetailModel(IEnergyRepository energyRepository, ILogger<HistoryDetailModel> logger)
    {
        _energyRepository = energyRepository;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Id = id;
        try
        {
            var scenarios = await _energyRepository.ListRecentScenariosAsync(100, ct);
            Scenario = scenarios.FirstOrDefault(s => s.Id == id);

            Plan = await _energyRepository.GetPlanByScenarioAsync(id, ct);

            if (Plan != null)
            {
                if (!string.IsNullOrWhiteSpace(Plan.DirectiveInterpretationJson))
                {
                    Directives = JsonSerializer.Deserialize<List<DirectiveInterpretation>>(Plan.DirectiveInterpretationJson)
                                 ?? new List<DirectiveInterpretation>();
                }

                if (!string.IsNullOrWhiteSpace(Plan.HourlyPlanJson))
                {
                    HourlyPlan = JsonSerializer.Deserialize<List<HourlyPlanEntry>>(Plan.HourlyPlanJson)
                                 ?? new List<HourlyPlanEntry>();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load details for scenario {ScenarioId}", id);
        }

        return Page();
    }
}
