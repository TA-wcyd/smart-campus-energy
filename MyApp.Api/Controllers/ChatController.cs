using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MyApp.Core.Models;

namespace MyApp.Api.Controllers;

[ApiController]
[Route("chat")]
public class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    // In-memory backing store for user chat messages
    private static readonly List<ChatMessage> ChatStore = new();
    private static readonly object StoreLock = new();

    public ChatController(ILogger<ChatController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    public IActionResult PostChatMessage([FromBody] ChatMessageRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message cannot be empty." });
        }

        try
        {
            var userMsg = new ChatMessage
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId != Guid.Empty ? request.UserId : Guid.NewGuid(),
                Role = "user",
                Content = request.Message.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };

            var botMsg = new ChatMessage
            {
                Id = Guid.NewGuid(),
                UserId = userMsg.UserId,
                Role = "assistant",
                Content = $"Received message: \"{userMsg.Content}\". System is online and monitoring grid status.",
                CreatedAtUtc = DateTime.UtcNow.AddMilliseconds(50)
            };

            lock (StoreLock)
            {
                ChatStore.Add(userMsg);
                ChatStore.Add(botMsg);
            }

            _logger.LogInformation("Saved chat message for user {UserId}", userMsg.UserId);

            return Ok(new
            {
                status = "ack",
                user_message = userMsg,
                reply_message = botMsg
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing chat message.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to store chat message." });
        }
    }

    [HttpGet("{userId:guid}")]
    public IActionResult GetChatMessages(Guid userId)
    {
        try
        {
            lock (StoreLock)
            {
                var messages = ChatStore
                    .Where(m => m.UserId == userId)
                    .OrderBy(m => m.CreatedAtUtc)
                    .TakeLast(50)
                    .ToList();

                return Ok(messages);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat messages for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve chat messages." });
        }
    }
}

public class ChatMessageRequest
{
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
