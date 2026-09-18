using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Core.DTOs;
using MyApp.Core.Models;
using MyApp.Infrastructure.Data;

namespace MyApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<UserController> _logger;

    public UserController(AppDbContext dbContext, ILogger<UserController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers(CancellationToken ct)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.CreatedAt))
            .ToListAsync(ct);

        return Ok(users);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> GetUserById(Guid id, CancellationToken ct)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user == null)
        {
            return NotFound();
        }

        return Ok(new UserDto(user.Id, user.Username, user.Email, user.CreatedAt));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("Username and Email are required.");
        }

        var user = new User
        {
            Username = request.Username.Trim(),
            Email = request.Email.Trim()
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, new UserDto(user.Id, user.Username, user.Email, user.CreatedAt));
    }
}
