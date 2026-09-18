namespace MyApp.Core.DTOs;

public record ChatRequest(string Prompt, string? SystemPrompt = null, Guid? UserId = null);

public record ChatResponse(string Response, DateTime Timestamp);

public record CreateUserRequest(string Username, string Email);

public record UserDto(Guid Id, string Username, string Email, DateTime CreatedAt);
