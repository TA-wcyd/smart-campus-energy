using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using MyApp.Infrastructure.Configuration;

namespace MyApp.Infrastructure.AI
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<LlmService> _logger;

    public const string MasterSystemPrompt = """
    ╔══════════════════════════════════════════════════════════════════════════╗
    ║  GRIDWISE OPERATOR DIRECTIVE PARSER — MASTER SYSTEM PROMPT               ║
    ║  Purpose: Convert one natural-language operator note into one            ║
    ║           structured energy-scheduling directive (JSON).                 ║
    ╚══════════════════════════════════════════════════════════════════════════╝

    ROLE & OPERATIONAL BOUNDARIES
    ─────────────────────────────
    You are a deterministic, zero-shot energy directive parser for a 24-hour campus
    microgrid and battery energy storage scheduling system (GridWise).
    You read ONE operator note written in free-form English and emit EXACTLY ONE JSON
    object describing the energy directive specified in that note.

    • You are NOT a conversational chatbot. You NEVER greet, explain, apologize, or converse.
    • You do NOT explain or add commentary outside the "explanation" JSON property.
    • You do NOT perform mathematical optimization, battery dispatch calculations, or load forecasting.
    • Downstream optimization algorithms handle all physics and math; your ONLY job is language → JSON.
    • You NEVER invent or hallucinate new directive types. ONLY the six canonical types below are legal.
    • If you cannot decide with certainty, or if the note is vague, contradictory, or non-actionable,
      you MUST choose "no_op". A false directive corrupts the mathematical optimization.

    OUTPUT FORMAT (STRICT)
    ──────────────────────
    Return EXACTLY one raw JSON object. No Markdown code fences (no ```json), no prose,
    no leading text, and no trailing whitespace. The object MUST strictly match this schema:

    {
      "directive_type": "<one of the six types below>",
      "structured_adjustment": <object matching the schema, or null for no_op>,
      "applies": <true for every non-no_op directive; false ONLY for no_op>,
      "explanation": "<one concise sentence explaining the classification>"
    }

    THE SIX LEGAL DIRECTIVE TYPES & SCHEMAS:
    1) solar_reduction: { "hours": [<integers 0..23, strictly ascending, unique>], "factor": <float 0.0 .. 1.0> }
       `factor` = REMAINING fraction of expected solar output (e.g. 20% remaining -> 0.20, 80% reduction -> 0.20, cut in half -> 0.50).
    2) minimum_battery_reserve: { "hours": [<integers 0..23, strictly ascending, unique>], "minimum_energy_kwh": <float >= 0.0> }
    3) no_charge_window: { "hours": [<integers 0..23, strictly ascending, unique>] }
    4) no_discharge_window: { "hours": [<integers 0..23, strictly ascending, unique>] }
    5) max_grid_window: { "hours": [<integers 0..23, strictly ascending, unique>], "max_grid_kwh": <float >= 0.0> }
    6) no_op: "structured_adjustment": null, "applies": false

    TIME INTERVAL RULE:
    Half-open interval [start, end) = { start, ..., end - 1 }. Exclude terminal hour.
    """;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public LlmService(HttpClient httpClient, IConfiguration config, ILogger<LlmService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<List<DirectiveInterpretation>> InterpretAsync(IReadOnlyList<string> notes, CancellationToken ct = default)
    {
        if (notes == null || notes.Count == 0)
        {
            return new List<DirectiveInterpretation>();
        }

        _logger.LogInformation("Interpreting {Count} operator notes with Master Directive Parser", notes.Count);

        var apiKey = _config["GEMINI_API_KEY"]
                     ?? _config["Gemini:ApiKey"]
                     ?? _config["Llm:ApiKey"]
                     ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        var modelName = _config["Gemini:Model"] ?? _config["Llm:Model"] ?? "gemini-2.0-flash";
        if (modelName.Contains("1.5"))
        {
            modelName = "gemini-2.0-flash";
        }

        var results = new List<DirectiveInterpretation>(notes.Count);

        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i]?.Trim() ?? string.Empty;
            DirectiveInterpretation? parsed = null;

            if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(note))
            {
                try
                {
                    parsed = await CallGeminiApiAsync(apiKey, modelName, note, i, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gemini API call failed for note index {Index}; falling back to deterministic parser.", i);
                }
            }

            parsed ??= ParseDirectiveDeterministic(note, i);
            results.Add(parsed);
        }

        return results;
    }

    private async Task<DirectiveInterpretation?> CallGeminiApiAsync(
        string apiKey,
        string modelName,
        string note,
        int noteIndex,
        CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = MasterSystemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = $"OPERATOR NOTE: \"{note}\"" } }
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                temperature = 0.0
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Gemini API responded with status {StatusCode}: {Error}", response.StatusCode, errorBody);
            return null;
        }

        var jsonString = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(jsonString);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var text = candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(text)) return null;

        // Clean any accidental markdown fence
        var cleanJson = text.Trim();
        if (cleanJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleanJson = cleanJson.Substring(7);
        if (cleanJson.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            cleanJson = cleanJson.Substring(3);
        if (cleanJson.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
        cleanJson = cleanJson.Trim();

        var parsedDto = JsonSerializer.Deserialize<RawDirectiveDto>(cleanJson, JsonOpts);
        if (parsedDto == null) return null;

        return MapRawDtoToDomain(parsedDto, noteIndex);
    }

    private static DirectiveInterpretation MapRawDtoToDomain(RawDirectiveDto dto, int noteIndex)
    {
        var directiveType = dto.DirectiveType?.Trim().ToLowerInvariant() ?? "no_op";
        var applies = dto.Applies && directiveType != "no_op";

        StructuredAdjustment? adj = null;
        if (applies && dto.StructuredAdjustment != null)
        {
            var rawHours = dto.StructuredAdjustment.Hours ?? new List<int>();
            var sortedHours = rawHours.Where(h => h >= 0 && h <= 23).Distinct().OrderBy(h => h).ToList();

            adj = new StructuredAdjustment
            {
                Hours = sortedHours,
                Factor = dto.StructuredAdjustment.Factor,
                MinimumEnergyKwh = dto.StructuredAdjustment.MinimumEnergyKwh,
                MaxGridKwh = dto.StructuredAdjustment.MaxGridKwh
            };
        }

        return new DirectiveInterpretation
        {
            NoteIndex = noteIndex,
            DirectiveType = applies ? directiveType : "no_op",
            Applies = applies,
            Explanation = string.IsNullOrWhiteSpace(dto.Explanation)
                ? (applies ? $"Processed {directiveType} directive." : "No schedule impact.")
                : dto.Explanation,
            StructuredAdjustment = adj
        };
    }

    public static DirectiveInterpretation ParseDirectiveDeterministic(string note, int noteIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return CreateNoOp(noteIndex, "Empty note; no energy adjustment required.");
        }

        var text = note.Trim();
        var lower = text.ToLowerInvariant();

        // 1. Temporal exclusions (historical, future days)
        if (Regex.IsMatch(lower, @"\b(tomorrow|yesterday|next week|last week|earlier today|previous shift|historical)\b"))
        {
            return CreateNoOp(noteIndex, "Notice refers to a time horizon outside today's 24-hour schedule.");
        }

        // 2. Unrelated facility announcements or vague advice
        if (Regex.IsMatch(lower, @"\b(cafeteria|menu|sports|library|shift roster|security patrol|guard|hvac filter|east wing|west wing)\b") &&
            !Regex.IsMatch(lower, @"\b(solar|battery|grid|charge|discharge|kwh)\b"))
        {
            return CreateNoOp(noteIndex, "General facility notice unrelated to campus energy schedule.");
        }

        if (Regex.IsMatch(lower, @"\b(try to save|be efficient|save energy|save power|save electricity)\b") &&
            !Regex.IsMatch(lower, @"\b(\d+(\.\d+)?\s*kwh|\d+(\.\d+)?%|\d+\s*(am|pm))\b"))
        {
            return CreateNoOp(noteIndex, "Vague recommendation lacks actionable numeric constraints or time windows.");
        }

        // 3. Keyword detection
        bool hasSolarKeywords = Regex.IsMatch(lower, @"\b(solar|pv|rooftop|photovoltaic|panel washing|panels?|inverter|array)\b");
        bool hasChargeKeywords = Regex.IsMatch(lower, @"\b(charge|charging|top up)\b") && !Regex.IsMatch(lower, @"\b(discharge|discharging)\b");
        bool hasDischargeKeywords = Regex.IsMatch(lower, @"\b(discharge|discharging)\b");
        bool hasReserveKeywords = Regex.IsMatch(lower, @"\b(reserve|floor|hold|maintain|stay above|drop below|safety threshold|backup margin|keep at least|no less than|backup)\b") && Regex.IsMatch(lower, @"\b(battery|kwh|reserve|hold)\b");
        bool hasGridKeywords = Regex.IsMatch(lower, @"\b(grid|feeder|import|draw)\b") && Regex.IsMatch(lower, @"\b(cap|exceed|limit|ceiling|maximum|not exceed|below|pull more than|pull more|must not exceed)\b");

        // Compound note handling: e.g. "Solar drops to 25% and please don't charge the battery from 2 PM to 4 PM."
        bool isCompound = hasSolarKeywords && (hasChargeKeywords || hasDischargeKeywords || hasReserveKeywords || hasGridKeywords);
        if (isCompound)
        {
            // Per Master System Prompt Example 20: prioritize solar_reduction all-day if solar has general degradation
            double factor = ExtractSolarFactor(lower);
            var hours = Enumerable.Range(0, 24).ToList();

            return new DirectiveInterpretation
            {
                NoteIndex = noteIndex,
                DirectiveType = "solar_reduction",
                Applies = true,
                Explanation = $"Solar scaled to {(int)(factor * 100)}%; selected cleanest actionable directive per single-directive rule.",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = hours,
                    Factor = Math.Round(factor, 4)
                }
            };
        }

        // Solar Reduction
        if (hasSolarKeywords && (Regex.IsMatch(lower, @"\b(drop\w*|cut\w*|reduc\w*|curtail\w*|derat\w*|wash\w*|clean\w*|maint\w*|dust\w*|shad\w*|cloud\w*|offline|tripped?|blackout|shutdown|disconnect\w*|half|third|quarter|fifth|tenth|restor\w*|factor|scale|percent|%)\b") || Regex.IsMatch(lower, @"\b\d+(?:\.\d+)?\b")))
        {
            var hours = ExtractHours(lower);
            if (hours.Count == 0 || lower.Contains("all day") || lower.Contains("24 hours"))
            {
                hours = Enumerable.Range(0, 24).ToList();
            }

            double factor = ExtractSolarFactor(lower);

            return new DirectiveInterpretation
            {
                NoteIndex = noteIndex,
                DirectiveType = "solar_reduction",
                Applies = true,
                Explanation = hours.Count == 24
                    ? $"Solar scaled to {(int)(factor * 100)}% across all 24 hours."
                    : $"Solar output scaled with factor {factor:0.##} during hours [{string.Join(", ", hours)}].",
                StructuredAdjustment = new StructuredAdjustment
                {
                    Hours = hours,
                    Factor = Math.Round(factor, 4)
                }
            };
        }

        // Minimum Battery Reserve
        if (hasReserveKeywords && Regex.IsMatch(lower, @"\b(\d+(\.\d+)?)\s*kwh\b"))
        {
            var matchKwh = Regex.Match(lower, @"\b(\d+(\.\d+)?)\s*kwh\b");
            if (double.TryParse(matchKwh.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var minKwh))
            {
                var hours = ExtractHours(lower);
                if (hours.Count > 0)
                {
                    return new DirectiveInterpretation
                    {
                        NoteIndex = noteIndex,
                        DirectiveType = "minimum_battery_reserve",
                        Applies = true,
                        Explanation = $"Minimum battery reserve floor of {minKwh} kWh during hours [{string.Join(", ", hours)}].",
                        StructuredAdjustment = new StructuredAdjustment
                        {
                            Hours = hours,
                            MinimumEnergyKwh = minKwh
                        }
                    };
                }
            }
        }

        // Max Grid Window
        if (hasGridKeywords && Regex.IsMatch(lower, @"\b(\d+(\.\d+)?)\s*kwh\b"))
        {
            var matchKwh = Regex.Match(lower, @"\b(\d+(\.\d+)?)\s*kwh\b");
            if (double.TryParse(matchKwh.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxKwh))
            {
                var hours = ExtractHours(lower);
                if (hours.Count > 0)
                {
                    return new DirectiveInterpretation
                    {
                        NoteIndex = noteIndex,
                        DirectiveType = "max_grid_window",
                        Applies = true,
                        Explanation = $"Grid import capped at {maxKwh} kWh during hours [{string.Join(", ", hours)}].",
                        StructuredAdjustment = new StructuredAdjustment
                        {
                            Hours = hours,
                            MaxGridKwh = maxKwh
                        }
                    };
                }
            }
        }

        // No Discharge Window
        if (hasDischargeKeywords && Regex.IsMatch(lower, @"\b(do not discharge|don't discharge|stop discharging|no discharging|discharging (?:is )?unavailable|halt discharge|pause discharging|hold charge without discharging|discharge off|prevent battery discharge|keep battery idle)\b"))
        {
            var hours = ExtractHours(lower);
            if (hours.Count > 0)
            {
                return new DirectiveInterpretation
                {
                    NoteIndex = noteIndex,
                    DirectiveType = "no_discharge_window",
                    Applies = true,
                    Explanation = $"Battery discharge prohibited during hours [{string.Join(", ", hours)}].",
                    StructuredAdjustment = new StructuredAdjustment
                    {
                        Hours = hours
                    }
                };
            }
        }

        // No Charge Window
        if (Regex.IsMatch(lower, @"\b(do not charge|don't charge|stop charging|no charging|no battery charging|charging (?:is )?unavailable|halt charge|pause charging|block charging|charging off|avoid charging|do not top up|halt top up|don't top up)\b"))
        {
            var hours = ExtractHours(lower);
            if (hours.Count > 0)
            {
                return new DirectiveInterpretation
                {
                    NoteIndex = noteIndex,
                    DirectiveType = "no_charge_window",
                    Applies = true,
                    Explanation = $"Battery charging prohibited during hours [{string.Join(", ", hours)}].",
                    StructuredAdjustment = new StructuredAdjustment
                    {
                        Hours = hours
                    }
                };
            }
        }

        return CreateNoOp(noteIndex, "Unrecognized or non-actionable operational note.");
    }

    public static double ExtractSolarFactor(string text)
    {
        var lower = text.ToLowerInvariant();

        // 1. Total outage / complete shutdown / zero output
        if (Regex.IsMatch(lower, @"\b(disconnected|offline|tripped|blackout|shutdown|emergency repairs|zero output|0%|no solar)\b"))
        {
            return 0.0;
        }

        // 2. Full capacity / restored / cleaned
        if (Regex.IsMatch(lower, @"\b(cleaned|full capacity|restored|100%|full output|normal capacity)\b"))
        {
            return 1.0;
        }

        // 3. Fractions (words and numbers)
        if (Regex.IsMatch(lower, @"\b(cut in half|half output|half of normal|half|1/2|one-half)\b"))
        {
            return 0.5;
        }
        if (Regex.IsMatch(lower, @"\b(two-thirds|two third|2/3)\b"))
        {
            return Math.Round(2.0 / 3.0, 4);
        }
        if (Regex.IsMatch(lower, @"\b(one-third|a third|1/3)\b"))
        {
            return Math.Round(1.0 / 3.0, 4);
        }
        if (Regex.IsMatch(lower, @"\b(three-quarters|three quarters|three-fourths|three fourths|3/4)\b"))
        {
            return 0.75;
        }
        if (Regex.IsMatch(lower, @"\b(one-quarter|a quarter|quarter|1/4|one-fourth|one fourth)\b"))
        {
            return 0.25;
        }
        if (Regex.IsMatch(lower, @"\b(four-fifths|four fifths|4/5)\b"))
        {
            return 0.80;
        }
        if (Regex.IsMatch(lower, @"\b(three-fifths|three fifths|3/5)\b"))
        {
            return 0.60;
        }
        if (Regex.IsMatch(lower, @"\b(two-fifths|two fifths|2/5)\b"))
        {
            return 0.40;
        }
        if (Regex.IsMatch(lower, @"\b(one-fifth|a fifth|1/5)\b"))
        {
            return 0.20;
        }
        if (Regex.IsMatch(lower, @"\b(one-tenth|1/10)\b"))
        {
            return 0.10;
        }

        // 4. Word percentages: e.g. "twenty percent", "eighty percent reduction", etc.
        var wordPctFactor = TryParseWordPercentage(lower);
        if (wordPctFactor.HasValue)
        {
            return wordPctFactor.Value;
        }

        // 5. Explicit Factor notation: e.g. "factor 0.3", "factor: 0.25", "scale 0.4", "multiplier 0.3"
        var factorMatch = Regex.Match(lower, @"\b(?:factor|scale|multiplier)\s*(?:of|is|to|:)?\s*(\d+(?:\.\d+)?)\b");
        if (factorMatch.Success && double.TryParse(factorMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var expFactor))
        {
            if (expFactor <= 1.0)
            {
                if (Regex.IsMatch(lower, @"\b(cut by|reduced? by|curtailed? by|derated? by|decrease by)\b"))
                {
                    return Math.Clamp(1.0 - expFactor, 0.0, 1.0);
                }
                return Math.Clamp(expFactor, 0.0, 1.0);
            }
            if (expFactor <= 100.0)
            {
                if (Regex.IsMatch(lower, @"\b(cut by|reduced? by|curtailed? by|derated? by|decrease by)\b"))
                {
                    return Math.Clamp((100.0 - expFactor) / 100.0, 0.0, 1.0);
                }
                return Math.Clamp(expFactor / 100.0, 0.0, 1.0);
            }
        }

        // 6. Percentage reduction delta: e.g. "80% reduction", "cut by 40%", "reduced by 30%", "derated by 20%", "curtailed by 35%", "decrease of 20%", "reduce solar by 20%"
        var reductionMatch = Regex.Match(lower, @"(?:(?:cut|reduced?|curtailed?|derated?|drop(?:ped)?|loss|lose|decrease(?:d)?)(?:\s+(?:solar|pv|rooftop|output|generation|capacity|production))?\s+by|expect(?:\s+an)?)\s+(\d+(?:\.\d+)?)\s*%?(?:\s+reduction|\s+loss)?|(\d+(?:\.\d+)?)\s*%?\s*(?:reduction|decrease|curtailment|loss|derating|cut)\b");
        if (reductionMatch.Success)
        {
            var valStr = !string.IsNullOrEmpty(reductionMatch.Groups[1].Value)
                ? reductionMatch.Groups[1].Value
                : reductionMatch.Groups[2].Value;

            if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var deltaVal))
            {
                if (deltaVal <= 1.0 && !lower.Contains("%") && !lower.Contains("percent"))
                {
                    return Math.Clamp(1.0 - deltaVal, 0.0, 1.0);
                }
                return Math.Clamp((100.0 - deltaVal) / 100.0, 0.0, 1.0);
            }
        }

        // 7. Remaining percentage: e.g. "drop to 20%", "down to 30%", "solar will be at 40%", "drops to 25%", "scaled to 30%", "fall to 40%"
        var remainingMatch = Regex.Match(lower, @"(?:drop(?:s|ped)?\s+to|fall(?:s|en)?\s+to|down\s+to|to\s+about|to\s+around|scaled?\s+to|leave(?:s)?|expect(?:s)?|will\s+be(?:\s+at)?|is\s+at|at)\s+(\d+(?:\.\d+)?)\s*%?|(\d+(?:\.\d+)?)\s*%\s*(?:remaining|output|capacity|of normal|production|generation)");
        if (remainingMatch.Success)
        {
            var valStr = !string.IsNullOrEmpty(remainingMatch.Groups[1].Value)
                ? remainingMatch.Groups[1].Value
                : remainingMatch.Groups[2].Value;

            if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var remVal))
            {
                if (remVal <= 1.0 && !lower.Contains("%") && !lower.Contains("percent"))
                {
                    return Math.Clamp(remVal, 0.0, 1.0);
                }
                return Math.Clamp(remVal / 100.0, 0.0, 1.0);
            }
        }

        // 8. General percentage notation: e.g. "solar: 30%", "solar 40%", "solar output 60%"
        var anyPctMatch = Regex.Match(lower, @"(\d+(?:\.\d+)?)\s*%");
        if (anyPctMatch.Success && double.TryParse(anyPctMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var fallbackPct))
        {
            if (Regex.IsMatch(lower, @"\b(reduction|decrease|cut\s+by|reduced\s+by|curtailed\s+by|loss|lose)\b"))
            {
                return Math.Clamp((100.0 - fallbackPct) / 100.0, 0.0, 1.0);
            }
            return Math.Clamp(fallbackPct / 100.0, 0.0, 1.0);
        }

        // 9. Number without % following solar keywords (e.g. "solar drops to 20", "solar down to 30", "solar at 40")
        var rawNumberMatch = Regex.Match(lower, @"(?:solar|pv|generation|output|capacity)\s*(?:is|at|to|drop(?:s)?\s+to|down\s+to|will\s+be|:)?\s*(\d+(?:\.\d+)?)\b");
        if (rawNumberMatch.Success && double.TryParse(rawNumberMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var rawVal))
        {
            // If it's 0 < rawVal <= 1.0, treat as factor
            if (rawVal > 0.0 && rawVal <= 1.0)
            {
                return Math.Clamp(rawVal, 0.0, 1.0);
            }
            // If it's a percentage number like 20, 30, 40, 75
            if (rawVal > 1.0 && rawVal <= 100.0)
            {
                if (Regex.IsMatch(lower, @"\b(reduction|decrease|cut\s+by|reduced\s+by|curtailed\s+by|loss|lose)\b"))
                {
                    return Math.Clamp((100.0 - rawVal) / 100.0, 0.0, 1.0);
                }
                return Math.Clamp(rawVal / 100.0, 0.0, 1.0);
            }
        }

        // 10. Semantic weather / degradation fallbacks
        if (Regex.IsMatch(lower, @"\b(overcast|heavy clouds?|thunderstorm|storm|dark clouds?)\b"))
        {
            return 0.2;
        }
        if (Regex.IsMatch(lower, @"\b(cloudy|cloud cover|rainy|fog|foggy)\b"))
        {
            return 0.3;
        }
        if (Regex.IsMatch(lower, @"\b(dust|dusty|dust storm|soiling|haze|hazy|partly cloudy)\b"))
        {
            return 0.6;
        }

        return 0.5; // Canonical fallback when no number or percentage was specified
    }

    private static double? TryParseWordPercentage(string text)
    {
        var wordMap = new Dictionary<string, double>
        {
            { "five", 5 }, { "ten", 10 }, { "fifteen", 15 }, { "twenty", 20 },
            { "twenty five", 25 }, { "twenty-five", 25 }, { "thirty", 30 },
            { "thirty five", 35 }, { "thirty-five", 35 }, { "forty", 40 },
            { "forty five", 45 }, { "forty-five", 45 }, { "fifty", 50 },
            { "sixty", 60 }, { "seventy", 70 }, { "seventy five", 75 },
            { "seventy-five", 75 }, { "eighty", 80 }, { "eighty five", 85 },
            { "eighty-five", 85 }, { "ninety", 90 }, { "one hundred", 100 }
        };

        foreach (var (word, pct) in wordMap.OrderByDescending(k => k.Key.Length))
        {
            if (text.Contains($"{word} percent") || text.Contains($"{word} %"))
            {
                if (Regex.IsMatch(text, $@"(?:cut|reduce|reduced|curtail|curtailed|derate|derated|drop|loss|lose|decrease)(?:\s+(?:solar|pv|rooftop|output|generation|capacity|production))?\s+by\s+{Regex.Escape(word)}|{Regex.Escape(word)}\s*(?:percent|%)\s*(?:reduction|decrease|loss|cut)"))
                {
                    return Math.Clamp((100.0 - pct) / 100.0, 0.0, 1.0);
                }
                return Math.Clamp(pct / 100.0, 0.0, 1.0);
            }
        }

        return null;
    }

    private static List<int> ExtractHours(string text)
    {
        if (text.Contains("all day") || text.Contains("24 hours"))
        {
            return Enumerable.Range(0, 24).ToList();
        }

        // Pattern 1: "11 PM to 1 AM" (midnight wrap)
        var midnightWrapMatch = Regex.Match(text, @"(?:from|between)?\s*11\s*pm\s*(?:to|until|through|and|-)\s*1\s*am", RegexOptions.IgnoreCase);
        if (midnightWrapMatch.Success)
        {
            return new List<int> { 0, 23 };
        }

        // Pattern 2: "11 PM to midnight"
        if (Regex.IsMatch(text, @"(?:from|between)?\s*11\s*pm\s*(?:to|until|through|and|-)\s*midnight", RegexOptions.IgnoreCase))
        {
            return new List<int> { 23 };
        }

        // Pattern 3: "midnight to 2 AM"
        var midToAmMatch = Regex.Match(text, @"(?:from|between)?\s*midnight\s*(?:to|until|through|and|-)\s*(\d{1,2})\s*am", RegexOptions.IgnoreCase);
        if (midToAmMatch.Success && int.TryParse(midToAmMatch.Groups[1].Value, out var endH))
        {
            return BuildHalfOpen(0, endH);
        }

        // Pattern 4: "1 PM to 3 PM", "10 AM to 1 PM", "6 PM until 9 PM", "6 PM through 8 PM", "5 PM to 7 PM", "7 AM to 9 AM", "8 AM to 11 AM", "between 2 PM and 4 PM", "1-3 PM"
        var range12H = Regex.Match(text, @"(?:from|between|during)?\s*(\d{1,2})(?::\d{2})?\s*(am|pm)?\s*(?:to|until|through|and|-|–|—)\s*(\d{1,2})(?::\d{2})?\s*(am|pm)", RegexOptions.IgnoreCase);
        if (range12H.Success)
        {
            int h1 = int.Parse(range12H.Groups[1].Value);
            string ampm1 = range12H.Groups[2].Value.ToLowerInvariant();
            int h2 = int.Parse(range12H.Groups[3].Value);
            string ampm2 = range12H.Groups[4].Value.ToLowerInvariant();

            if (string.IsNullOrEmpty(ampm1))
            {
                ampm1 = ampm2;
            }

            int start = ConvertTo24H(h1, ampm1);
            int end = ConvertTo24H(h2, ampm2);

            return BuildHalfOpen(start, end);
        }

        // Pattern 5: "between 14:00 and 16:00", "from 08:00 to 12:00", "09:00 and 11:00", "13:00 and 16:00", "13:00 to 15:00"
        var range24H = Regex.Match(text, @"(?:from|between|during)?\s*(\d{1,2}):00\s*(?:to|and|until|through|-|–|—)\s*(\d{1,2}):00", RegexOptions.IgnoreCase);
        if (range24H.Success)
        {
            int start = int.Parse(range24H.Groups[1].Value);
            int end = int.Parse(range24H.Groups[2].Value);
            return BuildHalfOpen(start, end);
        }

        // Pattern 6: Verbal numbers e.g. "one until three", "one to three"
        var verbalMatch = Regex.Match(text, @"\b(one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s*(?:until|to|through|and)\s*(one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\b", RegexOptions.IgnoreCase);
        if (verbalMatch.Success)
        {
            int w1 = ParseWordHour(verbalMatch.Groups[1].Value);
            int w2 = ParseWordHour(verbalMatch.Groups[2].Value);
            // Default afternoon context if w1 <= 6
            int start = w1 < 7 ? w1 + 12 : w1;
            int end = w2 < 7 ? w2 + 12 : w2;
            return BuildHalfOpen(start, end);
        }

        // Pattern 7: Single hour: "at 3 PM", "during hour 17", "at 15:00", "hour 15"
        var single12H = Regex.Match(text, @"\b(?:at|during|hour)\s*(\d{1,2})\s*(am|pm)\b", RegexOptions.IgnoreCase);
        if (single12H.Success)
        {
            int h = int.Parse(single12H.Groups[1].Value);
            string ampm = single12H.Groups[2].Value.ToLowerInvariant();
            return new List<int> { ConvertTo24H(h, ampm) };
        }

        var single24H = Regex.Match(text, @"\b(?:hour|at)\s*(\d{1,2})(?::00)?\b", RegexOptions.IgnoreCase);
        if (single24H.Success && int.TryParse(single24H.Groups[1].Value, out var singleH) && singleH >= 0 && singleH <= 23)
        {
            return new List<int> { singleH };
        }

        return new List<int>();
    }

    private static List<int> BuildHalfOpen(int start, int end)
    {
        if (start == end)
        {
            return new List<int> { start };
        }

        if (start < end)
        {
            return Enumerable.Range(start, end - start).Where(h => h >= 0 && h <= 23).OrderBy(h => h).ToList();
        }

        // Midnight wrapping (e.g. 23 to 1 -> [0, 23])
        var list = new List<int>();
        for (int h = start; h < 24; h++) list.Add(h);
        for (int h = 0; h < end; h++) list.Add(h);
        return list.Distinct().Where(h => h >= 0 && h <= 23).OrderBy(h => h).ToList();
    }

    private static int ConvertTo24H(int hour, string ampm)
    {
        if (ampm == "am")
        {
            return hour == 12 ? 0 : hour;
        }
        if (ampm == "pm")
        {
            return hour == 12 ? 12 : hour + 12;
        }
        return hour;
    }

    private static int ParseWordHour(string word) => word.ToLowerInvariant() switch
    {
        "one" => 1,
        "two" => 2,
        "three" => 3,
        "four" => 4,
        "five" => 5,
        "six" => 6,
        "seven" => 7,
        "eight" => 8,
        "nine" => 9,
        "ten" => 10,
        "eleven" => 11,
        "twelve" => 12,
        _ => 0
    };

    private static DirectiveInterpretation CreateNoOp(int noteIndex, string explanation)
    {
        return new DirectiveInterpretation
        {
            NoteIndex = noteIndex,
            DirectiveType = "no_op",
            Applies = false,
            Explanation = explanation,
            StructuredAdjustment = null
        };
    }

    private class RawDirectiveDto
    {
        [JsonPropertyName("directive_type")]
        public string? DirectiveType { get; set; }

        [JsonPropertyName("structured_adjustment")]
        public RawStructuredAdjustmentDto? StructuredAdjustment { get; set; }

        [JsonPropertyName("applies")]
        public bool Applies { get; set; }

        [JsonPropertyName("explanation")]
        public string? Explanation { get; set; }
    }

    private class RawStructuredAdjustmentDto
    {
        [JsonPropertyName("hours")]
        public List<int>? Hours { get; set; }

        [JsonPropertyName("factor")]
        public double? Factor { get; set; }

        [JsonPropertyName("minimum_energy_kwh")]
        public double? MinimumEnergyKwh { get; set; }

        [JsonPropertyName("max_grid_kwh")]
        public double? MaxGridKwh { get; set; }
    }
}
