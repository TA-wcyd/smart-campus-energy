using Microsoft.EntityFrameworkCore;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.Data;

public sealed class EnergyRepository : IEnergyRepository
{
    private readonly AppDbContext _dbContext;

    public EnergyRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid> SaveScenarioAsync(EnergyScenario s, CancellationToken ct = default)
    {
        _dbContext.EnergyScenarios.Add(s);
        await _dbContext.SaveChangesAsync(ct);
        return s.Id;
    }

    public async Task SavePlanAsync(EnergyPlan p, CancellationToken ct = default)
    {
        _dbContext.EnergyPlans.Add(p);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<List<EnergyScenario>> ListRecentScenariosAsync(int take, CancellationToken ct = default)
    {
        return await _dbContext.EnergyScenarios
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<EnergyPlan?> GetPlanByScenarioAsync(Guid scenarioId, CancellationToken ct = default)
    {
        return await _dbContext.EnergyPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ScenarioId == scenarioId, ct);
    }
}
