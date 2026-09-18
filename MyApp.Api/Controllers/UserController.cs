using Microsoft.AspNetCore.Mvc;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Api.Controllers;

[ApiController]
[Route("users")]
public class UserController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IEnergyRepository _energyRepository;
    private readonly ILogger<UserController> _logger;

    public UserController(
        IUserRepository userRepository,
        IEnergyRepository energyRepository,
        ILogger<UserController> logger)
    {
        _userRepository = userRepository;
        _energyRepository = energyRepository;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterUser([FromBody] RegisterUserRequest? request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Valid email is required." });
        }

        try
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = request.Email.Trim().ToLowerInvariant(),
                DisplayName = request.DisplayName?.Trim() ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };

            var savedUser = await _userRepository.UpsertAsync(user, ct);
            return Ok(savedUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register/upsert user with email {Email}", request.Email);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to register user." });
        }
    }

    [HttpGet("{email}")]
    public async Task<IActionResult> GetUser(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "Email parameter cannot be empty." });
        }

        try
        {
            var user = await _userRepository.GetByEmailAsync(email.Trim().ToLowerInvariant(), ct);
            if (user == null)
            {
                return NotFound(new { error = "User not found." });
            }

            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve user {Email}", email);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve user." });
        }
    }

    [HttpGet("{email}/history")]
    public async Task<IActionResult> GetUserHistory(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "Email parameter cannot be empty." });
        }

        try
        {
            var scenarios = await _energyRepository.ListRecentScenariosAsync(20, ct);
            return Ok(scenarios);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve history for user {Email}", email);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve history." });
        }
    }
}

public class RegisterUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
