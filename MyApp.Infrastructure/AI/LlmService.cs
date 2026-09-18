using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using MyApp.Infrastructure.Configuration;

namespace MyApp.Infrastructure.AI
{
    public interface IWebHostEnvironment
    {
        string ContentRootPath { get; }
    }

    public sealed class LlmService : ILlmService
    {
        private readonly HttpClient _httpClient;
        private readonly LlmOptions _options;
        private readonly ILogger<LlmService> _logger;
        private readonly string _apiKey;
        private readonly string _systemPrompt;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public LlmService(
            HttpClient httpClient,
            IOptions<LlmOptions> options,
            IWebHostEnvironment env,
            ILogger<LlmService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvVar) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException($"API key not found in environment variable '{_options.ApiKeyEnvVar}'.");
            }

            var promptPath = Path.Combine(env.ContentRootPath, "AI", "Prompts", "interpreter-prompt.txt");
            if (!File.Exists(promptPath))
            {
                var fallbackPath = Path.Combine(AppContext.BaseDirectory, "AI", "Prompts", "interpreter-prompt.txt");
                if (File.Exists(fallbackPath))
                {
                    promptPath = fallbackPath;
                }
                else
                {
                    throw new FileNotFoundException($"System prompt file not found at '{promptPath}'.", promptPath);
                }
            }

            _systemPrompt = File.ReadAllText(promptPath);
        }

        // Secondary constructor enabling ASP.NET Core DI activation when HttpClient or IOptions is not explicitly registered
        public LlmService(
            IServiceProvider serviceProvider,
            ILogger<LlmService> logger)
            : this(
                serviceProvider.GetService(typeof(HttpClient)) as HttpClient ?? new HttpClient(),
                serviceProvider.GetService(typeof(IOptions<LlmOptions>)) as IOptions<LlmOptions> ?? Options.Create(new LlmOptions()),
                serviceProvider.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment ?? new FallbackWebHostEnvironment(),
                logger)
        {
        }

        private sealed class FallbackWebHostEnvironment : IWebHostEnvironment
        {
            public string ContentRootPath
            {
                get
                {
                    var dir = AppContext.BaseDirectory;
                    while (!string.IsNullOrEmpty(dir))
                    {
                        if (Directory.Exists(Path.Combine(dir, "MyApp.Infrastructure", "AI", "Prompts")))
                        {
                            return Path.Combine(dir, "MyApp.Infrastructure");
                        }
                        if (Directory.Exists(Path.Combine(dir, "AI", "Prompts")))
                        {
                            return dir;
                        }
                        var parent = Directory.GetParent(dir);
                        if (parent == null) break;
                        dir = parent.FullName;
                    }
                    return AppContext.BaseDirectory;
                }
            }
        }

        public async Task<List<DirectiveInterpretation>> InterpretAsync(IReadOnlyList<string> notes, CancellationToken ct = default)
        {
            if (notes == null || notes.Count == 0)
            {
                return new List<DirectiveInterpretation>();
            }

            var tasks = notes.Select((note, i) => InterpretOneIndexedAsync(note, i, ct)).ToArray();
            var results = await Task.WhenAll(tasks);
            return results.OrderBy(r => r.NoteIndex).ToList();
        }

        private async Task<DirectiveInterpretation> InterpretOneIndexedAsync(string note, int index, CancellationToken ct)
        {
            try
            {
                var raw = await InterpretOneWithRetryAsync(note, ct);
                return Guardrails.Normalize(raw, index);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure interpreting note {Index}: '{Note}'", index, note);
                return Guardrails.Normalize(new LlmDirectiveRaw
                {
                    DirectiveType = "no_op",
                    Applies = false,
                    StructuredAdjustment = null,
                    Explanation = "Unexpected failure; defaulted to no_op."
                }, index);
            }
        }

        private async Task<LlmDirectiveRaw> InterpretOneWithRetryAsync(string note, CancellationToken ct)
        {
            for (int attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                try
                {
                    return await CallGeminiAsync(note, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Attempt {Attempt} failed for note '{Note}'.", attempt + 1, note);
                    if (attempt < _options.MaxRetries)
                    {
                        await Task.Delay(300 * (attempt + 1), ct);
                    }
                }
            }

            _logger.LogError("All {Count} attempts failed for note '{Note}'. Defaulting to no_op.", _options.MaxRetries + 1, note);
            return new LlmDirectiveRaw
            {
                DirectiveType = "no_op",
                Applies = false,
                StructuredAdjustment = null,
                Explanation = "LLM unavailable; defaulted to no_op."
            };
        }

        private async Task<LlmDirectiveRaw> CallGeminiAsync(string note, CancellationToken ct)
        {
            var url = $"{_options.BaseUrl.TrimEnd('/')}/v1beta/models/{_options.Model}:generateContent?key={_apiKey}";

            var requestBody = new
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = _systemPrompt } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = "OPERATOR NOTE:\n" + note + "\n\nReturn only the JSON object." }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = _options.Temperature,
                    responseMimeType = "application/json"
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(url, content, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Gemini API error (Status: {(int)response.StatusCode} {response.StatusCode}): {responseText}");
            }

            string rawCandidateText;
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var candidates = doc.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException("No candidates returned in response.");
                }

                rawCandidateText = candidates[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? string.Empty;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Failed to parse candidates JSON from response: {ResponseText}", responseText);
                return new LlmDirectiveRaw
                {
                    DirectiveType = "no_op",
                    Applies = false,
                    StructuredAdjustment = null,
                    Explanation = "Malformed response structure from LLM."
                };
            }

            _logger.LogInformation("Gemini raw response: {RawText}", rawCandidateText);

            try
            {
                var raw = JsonSerializer.Deserialize<LlmDirectiveRaw>(rawCandidateText, JsonOptions);
                return raw ?? new LlmDirectiveRaw
                {
                    DirectiveType = "no_op",
                    Applies = false,
                    StructuredAdjustment = null,
                    Explanation = "Deserialization returned null."
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Malformed JSON from Gemini: {RawText}", rawCandidateText);
                return new LlmDirectiveRaw
                {
                    DirectiveType = "no_op",
                    Applies = false,
                    StructuredAdjustment = null,
                    Explanation = "Malformed JSON from LLM; defaulted to no_op."
                };
            }
        }
    }
}
