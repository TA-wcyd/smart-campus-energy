using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MyApp.Api.Dtos;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Api.Controllers;

[ApiController]
[Route("optimize-energy")]
public class EnergyController : ControllerBase
{
    private readonly ILlmService _llmService;
    private readonly IOptimizerService _optimizerService;
    private readonly IScheduleValidator _scheduleValidator;
    private readonly IEnergyRepository _energyRepository;
    private readonly ILogger<EnergyController> _logger;

    public EnergyController(
        ILlmService llmService,
        IOptimizerService optimizerService,
        IScheduleValidator scheduleValidator,
        IEnergyRepository energyRepository,
        ILogger<EnergyController> logger)
    {
        _llmService = llmService;
        _optimizerService = optimizerService;
        _scheduleValidator = scheduleValidator;
        _energyRepository = energyRepository;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Optimize([FromBody] OptimizeRequestDto? requestDto, CancellationToken ct)
    {
        return await ProcessOptimizationAsync(requestDto, persist: true, ct);
    }

    [HttpPost("raw")]
    public async Task<IActionResult> OptimizeRaw([FromBody] OptimizeRequestDto? requestDto, CancellationToken ct)
    {
        return await ProcessOptimizationAsync(requestDto, persist: false, ct);
    }

    private async Task<IActionResult> ProcessOptimizationAsync(OptimizeRequestDto? requestDto, bool persist, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        if (requestDto == null)
        {
            return BadRequest(new { error = "Request body cannot be null." });
        }

        if (requestDto.Hours == null || requestDto.Hours.Count != 24)
        {
            return BadRequest(new { error = "Optimization request must contain exactly 24 hour entries." });
        }

        if (requestDto.OperatorNotes == null || requestDto.OperatorNotes.Count < 1 || requestDto.OperatorNotes.Count > 3)
        {
            return BadRequest(new { error = "Operator notes count must be between 1 and 3." });
        }

        var domainReq = requestDto.MapToDomain();

        // 1. LLM Directive Interpretation
        List<DirectiveInterpretation> directives;
        try
        {
            directives = await _llmService.InterpretAsync(domainReq.OperatorNotes, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM interpretation failed for scenario {ScenarioId}", domainReq.ScenarioId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "LLM interpretation failed." });
        }

        // 2. Solve Energy Schedule
        List<HourlyPlanEntry> plan;
        try
        {
            plan = _optimizerService.Solve(domainReq, directives);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Optimization solver failed for scenario {ScenarioId}", domainReq.ScenarioId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Optimization solver failed." });
        }

        // 3. Validate Schedule
        var (isValid, reason) = _scheduleValidator.Validate(domainReq, plan, directives);
        if (!isValid)
        {
            _logger.LogWarning("Schedule validation failed for scenario {ScenarioId}: {Reason}", domainReq.ScenarioId, reason);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = reason ?? "Schedule validation failed." });
        }

        // 4. Recompute Totals from Plan
        var totalGridKwh = plan.Sum(p => p.GridKwh);
        var totalCostBdt = plan.Sum(p =>
        {
            var hourEntry = domainReq.Hours.FirstOrDefault(h => h.Hour == p.Hour);
            return p.GridKwh * (hourEntry?.TariffBdtPerKwh ?? 0.0);
        });
        var peakGridKwh = plan.Count > 0 ? plan.Max(p => p.GridKwh) : 0.0;
        var activeDirectivesCount = directives.Count(d => d.Applies);
        var planSummary = $"Optimized 24-hour schedule with {activeDirectivesCount} active directive(s). Total Grid: {totalGridKwh:F2} kWh, Total Cost: {totalCostBdt:F2} BDT, Peak Grid: {peakGridKwh:F2} kWh.";

        // 5. Persist if requested
        if (persist)
        {
            try
            {
                var scenario = new EnergyScenario
                {
                    Id = Guid.NewGuid(),
                    ScenarioId = domainReq.ScenarioId,
                    OperatorNotesJson = JsonSerializer.Serialize(domainReq.OperatorNotes),
                    HoursJson = JsonSerializer.Serialize(domainReq.Hours),
                    BatteryJson = JsonSerializer.Serialize(domainReq.Battery),
                    CreatedAtUtc = DateTime.UtcNow
                };

                var scenarioGuid = await _energyRepository.SaveScenarioAsync(scenario, ct);

                var energyPlan = new EnergyPlan
                {
                    Id = Guid.NewGuid(),
                    ScenarioId = scenarioGuid,
                    DirectiveInterpretationJson = JsonSerializer.Serialize(directives),
                    HourlyPlanJson = JsonSerializer.Serialize(plan),
                    TotalGridKwh = totalGridKwh,
                    TotalCostBdt = totalCostBdt,
                    PeakGridKwh = peakGridKwh,
                    PlanSummary = planSummary,
                    CreatedAtUtc = DateTime.UtcNow
                };

                await _energyRepository.SavePlanAsync(energyPlan, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist scenario/plan for {ScenarioId}", domainReq.ScenarioId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to persist scenario and plan." });
            }
        }

        stopwatch.Stop();
        _logger.LogInformation("Completed optimization for scenario {ScenarioId} in {ElapsedMilliseconds} ms (Persisted: {Persisted})",
            domainReq.ScenarioId, stopwatch.ElapsedMilliseconds, persist);

        var responseDomain = new OptimizeResponse
        {
            ScenarioId = domainReq.ScenarioId,
            DirectiveInterpretation = directives,
            HourlyPlan = plan,
            TotalGridKwh = totalGridKwh,
            TotalCostBdt = totalCostBdt,
            PeakGridKwh = peakGridKwh,
            PlanSummary = planSummary
        };

        return Ok(OptimizeResponseDto.FromDomain(responseDomain));
    }
}
