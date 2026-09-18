using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using MyApp.Infrastructure.Configuration;
using Xunit;

namespace MyApp.Tests;

public class InterpreterTests
{
    private const string MockApiKey = "test-gemini-key";
    private const string ApiKeyEnvVar = "TEST_GEMINI_API_KEY";

    public InterpreterTests()
    {
        Environment.SetEnvironmentVariable(ApiKeyEnvVar, MockApiKey);
    }

    private static string FindContentRoot()
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

    private sealed class TestWebHostEnvironment : MyApp.Infrastructure.AI.IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = FindContentRoot();
    }

    private static string CreateGeminiResponse(string innerJson)
    {
        var escaped = JsonSerializer.Serialize(innerJson);
        return $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "text": {{escaped}}
                  }
                ],
                "role": "model"
              },
              "finishReason": "STOP"
            }
          ]
        }
        """;
    }

    private sealed class DynamicMockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public DynamicMockHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    private static LlmService CreateService(HttpMessageHandler handler, int maxRetries = 2)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new LlmOptions
        {
            ApiKeyEnvVar = ApiKeyEnvVar,
            MaxRetries = maxRetries,
            TimeoutSeconds = 5
        });
        var env = new TestWebHostEnvironment();
        var logger = NullLogger<LlmService>.Instance;

        return new LlmService(httpClient, options, env, logger);
    }

    [Fact]
    public async Task InterpretAsync_FourSection42Examples_CorrectlyParsed()
    {
        var handler = new DynamicMockHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var userText = doc.RootElement
                .GetProperty("contents")[0]
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";

            string responseJson;

            if (userText.Contains("Solar output will drop to about 20% from 1 PM to 3 PM"))
            {
                responseJson = CreateGeminiResponse(
                    "{\"directive_type\":\"solar_reduction\",\"applies\":true,\"structured_adjustment\":{\"hours\":[13,14],\"factor\":0.2},\"explanation\":\"Solar reduced to 20%.\"}");
            }
            else if (userText.Contains("Do not charge the battery between 2 PM and 4 PM"))
            {
                responseJson = CreateGeminiResponse(
                    "{\"directive_type\":\"no_charge_window\",\"applies\":true,\"structured_adjustment\":{\"hours\":[14,15]},\"explanation\":\"No charge window.\"}");
            }
            else if (userText.Contains("Keep at least 120 kWh in reserve from 6 PM until 9 PM"))
            {
                responseJson = CreateGeminiResponse(
                    "{\"directive_type\":\"minimum_battery_reserve\",\"applies\":true,\"structured_adjustment\":{\"hours\":[18,19,20],\"minimum_energy_kwh\":120.0},\"explanation\":\"Reserve 120 kWh.\"}");
            }
            else
            {
                responseJson = CreateGeminiResponse(
                    "{\"directive_type\":\"no_op\",\"applies\":false,\"structured_adjustment\":null,\"explanation\":\"Cafeteria menu not actionable.\"}");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var service = CreateService(handler);

        var notes = new[]
        {
            "Solar output will drop to about 20% from 1 PM to 3 PM.",
            "Do not charge the battery between 2 PM and 4 PM.",
            "Keep at least 120 kWh in reserve from 6 PM until 9 PM.",
            "The cafeteria menu changes tomorrow."
        };

        var results = await service.InterpretAsync(notes);

        Assert.Equal(4, results.Count);

        // 1. Solar reduction
        Assert.Equal(0, results[0].NoteIndex);
        Assert.True(results[0].Applies);
        Assert.Equal("solar_reduction", results[0].DirectiveType);
        Assert.NotNull(results[0].StructuredAdjustment);
        Assert.Equal(new List<int> { 13, 14 }, results[0].StructuredAdjustment!.Hours);
        Assert.Equal(0.2, results[0].StructuredAdjustment!.Factor);

        // 2. No charge window
        Assert.Equal(1, results[1].NoteIndex);
        Assert.True(results[1].Applies);
        Assert.Equal("no_charge_window", results[1].DirectiveType);
        Assert.NotNull(results[1].StructuredAdjustment);
        Assert.Equal(new List<int> { 14, 15 }, results[1].StructuredAdjustment!.Hours);

        // 3. Minimum battery reserve
        Assert.Equal(2, results[2].NoteIndex);
        Assert.True(results[2].Applies);
        Assert.Equal("minimum_battery_reserve", results[2].DirectiveType);
        Assert.NotNull(results[2].StructuredAdjustment);
        Assert.Equal(new List<int> { 18, 19, 20 }, results[2].StructuredAdjustment!.Hours);
        Assert.Equal(120.0, results[2].StructuredAdjustment!.MinimumEnergyKwh);

        // 4. Cafeteria no-op
        Assert.Equal(3, results[3].NoteIndex);
        Assert.False(results[3].Applies);
        Assert.Equal("no_op", results[3].DirectiveType);
        Assert.Null(results[3].StructuredAdjustment);
    }

    [Fact]
    public async Task InterpretAsync_ThreeParaphrases_ProduceIdenticalOutput()
    {
        var cannedJson = CreateGeminiResponse(
            "{\"directive_type\":\"solar_reduction\",\"applies\":true,\"structured_adjustment\":{\"hours\":[13,14],\"factor\":0.2},\"explanation\":\"Solar reduced to 20%.\"}");

        var handler = new DynamicMockHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(cannedJson, Encoding.UTF8, "application/json")
            }));

        var service = CreateService(handler);

        var paraphrases = new[]
        {
            "Solar output will drop to about 20% from 1 PM to 3 PM.",
            "Between 13:00 and 15:00, expected solar generation falls to a 0.2 factor.",
            "Expect photovoltaic generation cut to 20 percent between 1 PM and 3 PM."
        };

        var results = await service.InterpretAsync(paraphrases);

        Assert.Equal(3, results.Count);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal("solar_reduction", results[i].DirectiveType);
            Assert.True(results[i].Applies);
            Assert.Equal(new List<int> { 13, 14 }, results[i].StructuredAdjustment!.Hours);
            Assert.Equal(0.2, results[i].StructuredAdjustment!.Factor);
        }
    }

    [Fact]
    public async Task InterpretAsync_ParallelExecution_PreservesOrderedNoteIndices()
    {
        var handler = new DynamicMockHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var userText = doc.RootElement
                .GetProperty("contents")[0]
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";

            if (userText.Contains("Note_0"))
            {
                await Task.Delay(150);
            }
            else if (userText.Contains("Note_1"))
            {
                await Task.Delay(80);
            }
            else
            {
                await Task.Delay(10);
            }

            var json = CreateGeminiResponse("{\"directive_type\":\"no_op\",\"applies\":false,\"structured_adjustment\":null}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var service = CreateService(handler);

        var notes = new[] { "Note_0", "Note_1", "Note_2" };
        var results = await service.InterpretAsync(notes);

        Assert.Equal(3, results.Count);
        Assert.Equal(0, results[0].NoteIndex);
        Assert.Equal(1, results[1].NoteIndex);
        Assert.Equal(2, results[2].NoteIndex);
    }

    [Fact]
    public async Task InterpretAsync_FailureIsolation_TransientFailureReturnsNoOpWithoutAffectingOthers()
    {
        var handler = new DynamicMockHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var userText = doc.RootElement
                .GetProperty("contents")[0]
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";

            if (userText.Contains("Failing_Note"))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":\"Internal Server Error\"}", Encoding.UTF8, "application/json")
                };
            }

            var successJson = CreateGeminiResponse("{\"directive_type\":\"no_charge_window\",\"applies\":true,\"structured_adjustment\":{\"hours\":[14,15]}}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(successJson, Encoding.UTF8, "application/json")
            };
        });

        // maxRetries = 1 to keep test fast
        var service = CreateService(handler, maxRetries: 1);

        var notes = new[] { "Good_Note_A", "Failing_Note", "Good_Note_B" };
        var results = await service.InterpretAsync(notes);

        Assert.Equal(3, results.Count);

        // Good_Note_A succeeded
        Assert.Equal("no_charge_window", results[0].DirectiveType);
        Assert.True(results[0].Applies);

        // Failing_Note gracefully defaulted to no_op
        Assert.Equal("no_op", results[1].DirectiveType);
        Assert.False(results[1].Applies);
        Assert.Null(results[1].StructuredAdjustment);

        // Good_Note_B succeeded
        Assert.Equal("no_charge_window", results[2].DirectiveType);
        Assert.True(results[2].Applies);
    }

    [Fact]
    public async Task InterpretAsync_MalformedJsonFromGemini_ProducesNoOpWithoutThrowing()
    {
        var malformedGeminiResponse = CreateGeminiResponse("NOT_VALID_JSON_AT_ALL {{{[[[");

        var handler = new DynamicMockHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(malformedGeminiResponse, Encoding.UTF8, "application/json")
            }));

        var service = CreateService(handler);

        var results = await service.InterpretAsync(new[] { "Some note" });

        Assert.Single(results);
        Assert.Equal("no_op", results[0].DirectiveType);
        Assert.False(results[0].Applies);
        Assert.Null(results[0].StructuredAdjustment);
    }
}
