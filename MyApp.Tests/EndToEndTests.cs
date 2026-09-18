using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Api.Dtos;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using Xunit;

namespace MyApp.Tests;

public class EndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EndToEndTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OptimizeEnergyRaw_ValidScenario_Returns200AndCalculatedPlan()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Register mock/test implementations if needed
                services.AddScoped<ILlmService, MockLlmService>();
                services.AddScoped<IOptimizerService, MockOptimizerService>();
                services.AddScoped<IScheduleValidator, MockScheduleValidator>();
                services.AddScoped<IEnergyRepository, MockEnergyRepository>();
                services.AddScoped<IUserRepository, MockUserRepository>();
            });
        }).CreateClient();

        var requestDto = new OptimizeRequestDto
        {
            ScenarioId = "test-scenario-e2e",
            OperatorNotes = new List<string>
            {
                "Solar output will drop to about 20% from 1 PM to 3 PM.",
                "Do not charge the battery between 2 PM and 4 PM.",
                "The cafeteria menu changes tomorrow."
            },
            Battery = new BatteryDto
            {
                CapacityKwh = 500,
                InitialEnergyKwh = 200,
                MinimumEnergyKwh = 50,
                MaxChargeKwhPerHour = 100,
                MaxDischargeKwhPerHour = 100
            },
            Hours = Enumerable.Range(0, 24).Select(h => new HourEntryDto
            {
                Hour = h,
                DemandKwh = 100 + h * 5,
                SolarKwh = (h >= 6 && h <= 18) ? 150 : 0,
                TariffBdtPerKwh = (h >= 17 && h <= 21) ? 12.0 : 6.0
            }).ToList()
        };

        // Act
        var response = await client.PostAsJsonAsync("/optimize-energy/raw", requestDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<OptimizeResponseDto>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(result);
        Assert.Equal("test-scenario-e2e", result.ScenarioId);
        Assert.Equal(24, result.HourlyPlan.Count);
        Assert.Equal(3, result.DirectiveInterpretation.Count);

        // Verify totals recomputed correctly
        var expectedTotalGrid = result.HourlyPlan.Sum(p => p.GridKwh);
        Assert.Equal(expectedTotalGrid, result.TotalGridKwh, precision: 2);

        var expectedPeakGrid = result.HourlyPlan.Max(p => p.GridKwh);
        Assert.Equal(expectedPeakGrid, result.PeakGridKwh, precision: 2);

        Assert.False(string.IsNullOrWhiteSpace(result.PlanSummary));
    }

    #region Mock Implementations for Isolated End-to-End Testing

    private class MockLlmService : ILlmService
    {
        public Task<List<DirectiveInterpretation>> InterpretAsync(IReadOnlyList<string> notes, CancellationToken ct = default)
        {
            var interpretations = notes.Select((note, i) => LlmService.ParseDirectiveDeterministic(note, i)).ToList();
            return Task.FromResult(interpretations);
        }
    }

    private class MockOptimizerService : IOptimizerService
    {
        public List<HourlyPlanEntry> Solve(OptimizeRequest req, IReadOnlyList<DirectiveInterpretation> directives)
        {
            var plan = new List<HourlyPlanEntry>();
            double currentSoc = req.Battery.InitialEnergyKwh;

            foreach (var h in req.Hours)
            {
                double solar = h.SolarKwh;
                double demand = h.DemandKwh;
                double net = demand - solar;
                double grid = Math.Max(0, net);
                string action = "HOLD";
                double batteryKwh = 0;

                plan.Add(new HourlyPlanEntry
                {
                    Hour = h.Hour,
                    GridKwh = grid,
                    SolarUsedKwh = Math.Min(demand, solar),
                    BatteryAction = action,
                    BatteryKwh = batteryKwh,
                    BatteryEnergyAfterKwh = currentSoc
                });
            }
            return plan;
        }
    }

    private class MockScheduleValidator : IScheduleValidator
    {
        public (bool IsValid, string? Reason) Validate(OptimizeRequest req, IReadOnlyList<HourlyPlanEntry> plan, IReadOnlyList<DirectiveInterpretation> directives)
        {
            return (true, null);
        }
    }

    private class MockEnergyRepository : IEnergyRepository
    {
        public Task<Guid> SaveScenarioAsync(EnergyScenario s, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task SavePlanAsync(EnergyPlan p, CancellationToken ct) => Task.CompletedTask;
        public Task<List<EnergyScenario>> ListRecentScenariosAsync(int take, CancellationToken ct) => Task.FromResult(new List<EnergyScenario>());
        public Task<EnergyPlan?> GetPlanByScenarioAsync(Guid scenarioId, CancellationToken ct) => Task.FromResult<EnergyPlan?>(null);
    }

    private class MockUserRepository : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email, CancellationToken ct) => Task.FromResult<User?>(null);
        public Task<User> UpsertAsync(User u, CancellationToken ct) => Task.FromResult(u);
    }

    #endregion
}
