namespace MyApp.Infrastructure.Configuration;

public sealed class LlmOptions
{
    public string Provider { get; set; } = "Gemini";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
    public string Model { get; set; } = "gemini-1.5-flash";
    public string ApiKeyEnvVar { get; set; } = "GEMINI_API_KEY";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxRetries { get; set; } = 2;
    public double Temperature { get; set; } = 0.0;
}
