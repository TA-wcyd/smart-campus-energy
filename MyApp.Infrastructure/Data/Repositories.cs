using Microsoft.EntityFrameworkCore;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.Data;

public class EnergyRepository : IEnergyRepository
{
    private readonly AppDbContext _dbContext;

    public EnergyRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid> SaveScenarioAsync(EnergyScenario s, CancellationToken ct)
    {
        _dbContext.EnergyScenarios.Add(s);
        await _dbContext.SaveChangesAsync(ct);
        return s.Id;
    }

    public async Task SavePlanAsync(EnergyPlan p, CancellationToken ct)
    {
        _dbContext.EnergyPlans.Add(p);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<List<EnergyScenario>> ListRecentScenariosAsync(int take, CancellationToken ct)
    {
        return await _dbContext.EnergyScenarios
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<EnergyPlan?> GetPlanByScenarioAsync(Guid scenarioId, CancellationToken ct)
    {
        return await _dbContext.EnergyPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ScenarioId == scenarioId, ct);
    }
}

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _dbContext;

    public UserRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);
    }

    public async Task<User> UpsertAsync(User u, CancellationToken ct)
    {
        var existing = await _dbContext.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == u.Email.ToLower(), ct);
        if (existing == null)
        {
            _dbContext.Users.Add(u);
            await _dbContext.SaveChangesAsync(ct);
            return u;
        }

        existing.DisplayName = u.DisplayName;
        await _dbContext.SaveChangesAsync(ct);
        return existing;
    }
}
