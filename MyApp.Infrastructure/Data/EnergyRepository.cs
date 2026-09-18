using Microsoft.EntityFrameworkCore;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.Data;

/// <summary>
/// Repository for EnergyScenario and EnergyPlan persistence.
/// </summary>
public sealed class EnergyRepository : IEnergyRepository
{
    private readonly AppDbContext _db;

    public EnergyRepository(AppDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<Guid> SaveScenarioAsync(EnergyScenario scenario, CancellationToken ct)
    {
        _db.EnergyScenarios.Add(scenario);
        await _db.SaveChangesAsync(ct);
        return scenario.Id;
    }

    /// <inheritdoc/>
    public async Task SavePlanAsync(EnergyPlan plan, CancellationToken ct)
    {
        _db.EnergyPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<EnergyScenario>> ListRecentScenariosAsync(int take, CancellationToken ct)
    {
        return await _db.EnergyScenarios
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<EnergyPlan?> GetPlanByScenarioAsync(Guid scenarioId, CancellationToken ct)
    {
        return await _db.EnergyPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ScenarioId == scenarioId, ct);
    }
}
