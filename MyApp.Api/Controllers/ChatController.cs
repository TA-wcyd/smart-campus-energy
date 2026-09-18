using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Core.DTOs;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;
using MyApp.Infrastructure.Data;

namespace MyApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly ILlmService _llmService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ChatController> _logger;

    public ChatController(ILlmService llmService, AppDbContext dbContext, ILogger<ChatController> logger)
    {
        _llmService = llmService;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> SendMessage([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Prompt cannot be empty.");
        }

        // 1. Record user message
        var userMsg = new ChatMessage
        {
            UserId = request.UserId,
            Role = "user",
            Content = request.Prompt,
            Timestamp = DateTime.UtcNow
        };
        _dbContext.ChatMessages.Add(userMsg);

        // 2. Generate LLM response
        var responseText = await _llmService.GenerateResponseAsync(request.Prompt, request.SystemPrompt, ct);

        // 3. Record assistant message
        var assistantMsg = new ChatMessage
        {
            UserId = request.UserId,
            Role = "assistant",
            Content = responseText,
            Timestamp = DateTime.UtcNow
        };
        _dbContext.ChatMessages.Add(assistantMsg);

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new ChatResponse(responseText, assistantMsg.Timestamp));
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<ChatMessage>>> GetChatHistory([FromQuery] Guid? userId, CancellationToken ct)
    {
        var query = _dbContext.ChatMessages.AsNoTracking();

        if (userId.HasValue)
        {
            query = query.Where(m => m.UserId == userId.Value);
        }

        var messages = await query.OrderBy(m => m.Timestamp).ToListAsync(ct);
        return Ok(messages);
    }

    [HttpGet("stream")]
    public async IAsyncEnumerable<string> StreamMessage(
        [FromQuery] string prompt,
        [FromQuery] string? systemPrompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");

        await foreach (var token in _llmService.StreamResponseAsync(prompt, systemPrompt, ct))
        {
            yield return $"data: {token}\n\n";
        }
    }
}
