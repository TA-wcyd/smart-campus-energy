using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.AI;

/// <summary>
/// Stub LLM service — interprets operator notes using keyword matching.
/// Replace with real Gemini HTTP calls when Member 2 upgrades this file.
/// </summary>
public class LlmService : ILlmService
{
    private readonly IConfiguration _config;
    private readonly ILogger<LlmService> _logger;

    public LlmService(IConfiguration config, ILogger<LlmService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<List<DirectiveInterpretation>> InterpretAsync(
        IReadOnlyList<string> notes, CancellationToken ct = default)
    {
        _logger.LogInformation("Interpreting {Count} operator notes with LLM", notes.Count);

        var list = new List<DirectiveInterpretation>();
        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            if (note.Contains("solar", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new DirectiveInterpretation
                {
                    NoteIndex = i,
                    Applies = true,
                    DirectiveType = "solar_reduction",
                    Explanation = "Solar generation capacity reduced during target afternoon window (1 PM - 3 PM).",
                    StructuredAdjustment = new StructuredAdjustment
                    {
                        Hours = new List<int> { 13, 14, 15 },
                        Factor = 0.2
                    }
                });
            }
            else if (note.Contains("charge", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new DirectiveInterpretation
                {
                    NoteIndex = i,
                    Applies = true,
                    DirectiveType = "no_charge_window",
                    Explanation = "Battery charging restricted between 2 PM and 4 PM to manage network load.",
                    StructuredAdjustment = new StructuredAdjustment
                    {
                        Hours = new List<int> { 14, 15, 16 }
                    }
                });
            }
            else
            {
                list.Add(new DirectiveInterpretation
                {
                    NoteIndex = i,
                    Applies = false,
                    DirectiveType = "no_op",
                    Explanation = "General operational note; no mathematical dispatch adjustment required."
                });
            }
        }

        return Task.FromResult(list);
    }
}
