using Microsoft.EntityFrameworkCore;
using MyApp.Core.Models;
using MyApp.Infrastructure.Data;
using Xunit;

namespace MyApp.Tests;

public class RepositoryTests
{
    private AppDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task EnergyRepository_SaveScenarioAndPlan_RetrievesSuccessfully()
    {
        var dbName = Guid.NewGuid().ToString();
        using var dbContext = CreateInMemoryDbContext(dbName);
        var repo = new EnergyRepository(dbContext);

        var scenario = new EnergyScenario
        {
            Id = Guid.NewGuid(),
            ScenarioId = "test-scenario-repo-01",
            OperatorNotesJson = "[\"note1\"]",
            HoursJson = "[]",
            BatteryJson = "{}",
            CreatedAtUtc = DateTime.UtcNow
        };

        var scenarioId = await repo.SaveScenarioAsync(scenario);
        Assert.Equal(scenario.Id, scenarioId);

        var plan = new EnergyPlan
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenarioId,
            DirectiveInterpretationJson = "[]",
            HourlyPlanJson = "[]",
            TotalGridKwh = 1250.50,
            TotalCostBdt = 8750.00,
            PeakGridKwh = 110.00,
            PlanSummary = "Test plan summary",
            CreatedAtUtc = DateTime.UtcNow
        };

        await repo.SavePlanAsync(plan);

        var retrievedPlan = await repo.GetPlanByScenarioAsync(scenarioId);
        Assert.NotNull(retrievedPlan);
        Assert.Equal(plan.Id, retrievedPlan.Id);
        Assert.Equal(scenarioId, retrievedPlan.ScenarioId);
        Assert.Equal(1250.50, retrievedPlan.TotalGridKwh);
    }

    [Fact]
    public async Task EnergyRepository_ListRecentScenarios_ReturnsNewestFirst()
    {
        var dbName = Guid.NewGuid().ToString();
        using var dbContext = CreateInMemoryDbContext(dbName);
        var repo = new EnergyRepository(dbContext);

        var oldScenario = new EnergyScenario
        {
            Id = Guid.NewGuid(),
            ScenarioId = "old-scenario",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        var newScenario = new EnergyScenario
        {
            Id = Guid.NewGuid(),
            ScenarioId = "new-scenario",
            CreatedAtUtc = DateTime.UtcNow
        };

        await repo.SaveScenarioAsync(oldScenario);
        await repo.SaveScenarioAsync(newScenario);

        var recent = await repo.ListRecentScenariosAsync(10);
        Assert.NotNull(recent);
        Assert.Equal(2, recent.Count);
        Assert.Equal("new-scenario", recent[0].ScenarioId);
        Assert.Equal("old-scenario", recent[1].ScenarioId);
    }

    [Fact]
    public async Task UserRepository_UpsertAndGetByEmail_WorksCorrectly()
    {
        var dbName = Guid.NewGuid().ToString();
        using var dbContext = CreateInMemoryDbContext(dbName);
        var repo = new UserRepository(dbContext);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "operator@bup-fest.edu",
            DisplayName = "Grid Operator"
        };

        var savedUser = await repo.UpsertAsync(user);
        Assert.NotNull(savedUser);
        Assert.Equal("operator@bup-fest.edu", savedUser.Email);

        var retrievedUser = await repo.GetByEmailAsync("OPERATOR@BUP-FEST.EDU");
        Assert.NotNull(retrievedUser);
        Assert.Equal("Grid Operator", retrievedUser.DisplayName);

        // Update display name
        retrievedUser.DisplayName = "Lead Dispatcher";
        var updatedUser = await repo.UpsertAsync(retrievedUser);
        Assert.Equal("Lead Dispatcher", updatedUser.DisplayName);
    }
}
