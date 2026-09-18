using Microsoft.AspNetCore.Mvc.RazorPages;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Api.Pages;

public class HistoryModel : PageModel
{
    private readonly IEnergyRepository _energyRepository;
    private readonly ILogger<HistoryModel> _logger;

    public List<EnergyScenario> Scenarios { get; set; } = new();

    public HistoryModel(IEnergyRepository energyRepository, ILogger<HistoryModel> logger)
    {
        _energyRepository = energyRepository;
        _logger = logger;
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Scenarios = await _energyRepository.ListRecentScenariosAsync(20, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load history scenarios.");
            Scenarios = new List<EnergyScenario>();
        }
    }
}
