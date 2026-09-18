using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyApp.Core.Interfaces;

namespace MyApp.Infrastructure.AI;

/// <summary>
/// Starter implementation for LLM service.
/// Connect to OpenAI, Anthropic, Gemini, or Ollama using their respective SDK or HTTP clients.
/// </summary>
public class LlmService : ILlmService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LlmService> _logger;

    public LlmService(IConfiguration configuration, ILogger<LlmService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GenerateResponseAsync(string prompt, string? systemPrompt = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating LLM response for prompt: {Prompt}", prompt);

        // Simulation delay - replace this with actual LLM API invocation
        await Task.Delay(100, cancellationToken);

        var sysContext = !string.IsNullOrWhiteSpace(systemPrompt) ? $"[System Context: {systemPrompt}] " : string.Empty;
        return $"{sysContext}AI Assistant: I received your message: \"{prompt}\". Ready for LLM integration!";
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        string prompt,
        string? systemPrompt = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Streaming LLM response for prompt: {Prompt}", prompt);

        string[] tokens = { "AI", " Assistant", ":", " Echoing", " back", " ->", $" \"{prompt}\"" };

        foreach (var token in tokens)
        {
            await Task.Delay(50, cancellationToken);
            yield return token;
        }
    }
}
