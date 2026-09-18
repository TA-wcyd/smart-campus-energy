namespace MyApp.Core.Interfaces;

public interface ILlmService
{
    Task<string> GenerateResponseAsync(string prompt, string? systemPrompt = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamResponseAsync(string prompt, string? systemPrompt = null, CancellationToken cancellationToken = default);
}
