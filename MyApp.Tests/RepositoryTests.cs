using Microsoft.EntityFrameworkCore;
using MyApp.Core.Models;
using MyApp.Infrastructure.Data;
using MyApp.Tests.TestData;

namespace MyApp.Tests;

/// <summary>
/// Repository integration tests using EF Core InMemory provider.
/// These tests cover EnergyRepository CRUD and ordering semantics.
/// </summary>
public class RepositoryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new AppDbContext(options);
    }

    private static EnergyScenario MakeScenario(string scenarioId = "s1") =>
        new()
        {
            ScenarioId        = scenarioId,
            OperatorNotesJson = "[]",
            HoursJson         = "[]",
            BatteryJson       = "{}",
            CreatedAtUtc      = DateTime.UtcNow
        };

    private static EnergyPlan MakePlan(Guid scenarioRowId) =>
        new()
        {
            ScenarioId                   = scenarioRowId,
            DirectiveInterpretationJson  = "[]",
            HourlyPlanJson               = "[]",
            TotalGridKwh                 = 1000.0,
            TotalCostBdt                 = 7000.0,
            PeakGridKwh                  = 150.0,
            PlanSummary                  = "Test plan",
            CreatedAtUtc                 = DateTime.UtcNow
        };

    // ── 1. Save scenario → retrieve by ScenarioId ─────────────────────────────

    [Fact]
    public async Task SaveScenario_ThenGetPlan_RoundTrips()
    {
        await using var ctx  = CreateContext(nameof(SaveScenario_ThenGetPlan_RoundTrips));
        var energyRepo = new EnergyRepository(ctx);

        var scenario = MakeScenario("round-trip-001");
        var rowId    = await energyRepo.SaveScenarioAsync(scenario, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, rowId);

        var plan = MakePlan(rowId);
        await energyRepo.SavePlanAsync(plan, CancellationToken.None);

        var retrieved = await energyRepo.GetPlanByScenarioAsync(rowId, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(rowId,        retrieved.ScenarioId);
        Assert.Equal("Test plan",  retrieved.PlanSummary);
        Assert.Equal(1000.0,       retrieved.TotalGridKwh);
    }

    // ── 2. GetPlanByScenarioAsync returns null for unknown id ─────────────────

    [Fact]
    public async Task GetPlan_UnknownId_ReturnsNull()
    {
        await using var ctx  = CreateContext(nameof(GetPlan_UnknownId_ReturnsNull));
        var energyRepo = new EnergyRepository(ctx);

        var result = await energyRepo.GetPlanByScenarioAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result);
    }

    // ── 3. ListRecentScenarios returns newest first ────────────────────────────

    [Fact]
    public async Task ListRecentScenarios_ReturnsNewestFirst()
    {
        await using var ctx  = CreateContext(nameof(ListRecentScenarios_ReturnsNewestFirst));
        var energyRepo = new EnergyRepository(ctx);

        var s1 = MakeScenario("oldest");
        s1.CreatedAtUtc = DateTime.UtcNow.AddHours(-2);

        var s2 = MakeScenario("middle");
        s2.CreatedAtUtc = DateTime.UtcNow.AddHours(-1);

        var s3 = MakeScenario("newest");
        s3.CreatedAtUtc = DateTime.UtcNow;

        await energyRepo.SaveScenarioAsync(s1, CancellationToken.None);
        await energyRepo.SaveScenarioAsync(s2, CancellationToken.None);
        await energyRepo.SaveScenarioAsync(s3, CancellationToken.None);

        var recent = await energyRepo.ListRecentScenariosAsync(3, CancellationToken.None);

        Assert.Equal(3, recent.Count);
        Assert.Equal("newest", recent[0].ScenarioId);
        Assert.Equal("middle", recent[1].ScenarioId);
        Assert.Equal("oldest", recent[2].ScenarioId);
    }

    // ── 4. ListRecentScenarios respects take limit ─────────────────────────────

    [Fact]
    public async Task ListRecentScenarios_RespectsLimit()
    {
        await using var ctx  = CreateContext(nameof(ListRecentScenarios_RespectsLimit));
        var energyRepo = new EnergyRepository(ctx);

        for (int i = 0; i < 5; i++)
        {
            var s = MakeScenario($"s{i}");
            s.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i);
            await energyRepo.SaveScenarioAsync(s, CancellationToken.None);
        }

        var recent = await energyRepo.ListRecentScenariosAsync(2, CancellationToken.None);
        Assert.Equal(2, recent.Count);
    }

    // ── 5. UserRepository Upsert ──────────────────────────────────────────────

    [Fact]
    public async Task UserRepository_Upsert_CreatesAndUpdates()
    {
        await using var ctx      = CreateContext(nameof(UserRepository_Upsert_CreatesAndUpdates));
        var userRepo = new UserRepository(ctx);

        var user = new User { Email = "ratul@campus.edu", DisplayName = "Ratul" };
        var saved = await userRepo.UpsertAsync(user, CancellationToken.None);
        Assert.Equal("Ratul", saved.DisplayName);

        // Update display name
        var updated = await userRepo.UpsertAsync(
            new User { Email = "ratul@campus.edu", DisplayName = "Ratul Updated" },
            CancellationToken.None);
        Assert.Equal("Ratul Updated", updated.DisplayName);
    }

    // ── 6. UserRepository GetByEmail ─────────────────────────────────────────

    [Fact]
    public async Task UserRepository_GetByEmail_ReturnsNullForUnknown()
    {
        await using var ctx  = CreateContext(nameof(UserRepository_GetByEmail_ReturnsNullForUnknown));
        var userRepo = new UserRepository(ctx);

        var result = await userRepo.GetByEmailAsync("nobody@campus.edu", CancellationToken.None);
        Assert.Null(result);
    }
}
