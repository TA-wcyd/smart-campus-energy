using MyApp.Core.Models;

namespace MyApp.Core.Interfaces;

public interface ILlmService
{
    Task<List<DirectiveInterpretation>> InterpretAsync(IReadOnlyList<string> notes, CancellationToken ct = default);
}

public interface IOptimizerService
{
    List<HourlyPlanEntry> Solve(OptimizeRequest req, IReadOnlyList<DirectiveInterpretation> directives);
}

public interface IScheduleValidator
{
    (bool IsValid, string? Reason) Validate(OptimizeRequest req, IReadOnlyList<HourlyPlanEntry> plan, IReadOnlyList<DirectiveInterpretation> directives);
}

public interface IEnergyRepository
{
    Task<Guid> SaveScenarioAsync(EnergyScenario s, CancellationToken ct);
    Task SavePlanAsync(EnergyPlan p, CancellationToken ct);
    Task<List<EnergyScenario>> ListRecentScenariosAsync(int take, CancellationToken ct);
    Task<EnergyPlan?> GetPlanByScenarioAsync(Guid scenarioId, CancellationToken ct);
}

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User> UpsertAsync(User u, CancellationToken ct);
}
