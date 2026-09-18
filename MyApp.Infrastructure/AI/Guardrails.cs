using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyApp.Core.Models;

namespace MyApp.Core.Models
{
    public class LlmDirectiveRaw
    {
        [JsonPropertyName("directive_type")]
        public string? DirectiveType { get; set; }

        [JsonPropertyName("structured_adjustment")]
        public JsonElement? StructuredAdjustment { get; set; }

        [JsonPropertyName("applies")]
        public bool? Applies { get; set; }

        [JsonPropertyName("explanation")]
        public string? Explanation { get; set; }
    }
}

namespace MyApp.Infrastructure.AI
{
    public static class Guardrails
    {
        private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
        {
            "solar_reduction",
            "minimum_battery_reserve",
            "no_charge_window",
            "no_discharge_window",
            "max_grid_window",
            "no_op"
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        public static DirectiveInterpretation Normalize(LlmDirectiveRaw? raw, int index)
        {
            if (raw == null || string.IsNullOrWhiteSpace(raw.DirectiveType) || !AllowedTypes.Contains(raw.DirectiveType))
            {
                return NoOp(index, "Unknown or missing directive type.");
            }

            if (raw.DirectiveType == "no_op")
            {
                return new DirectiveInterpretation
                {
                    NoteIndex = index,
                    Applies = false, // FORCED
                    DirectiveType = "no_op",
                    StructuredAdjustment = null, // FORCED
                    Explanation = raw.Explanation ?? "Not applicable."
                };
            }

            // For any non-no_op directive:
            // applies is FORCED to true. Never read raw.Applies for the decision.
            if (raw.Applies.HasValue && raw.Applies.Value != true)
            {
                Debug.WriteLine($"[Guardrails] NoteIndex {index}: LLM returned applies={raw.Applies.Value} for non-no_op directive '{raw.DirectiveType}', forcing applies=true.");
            }

            // structured_adjustment validation
            if (raw.StructuredAdjustment == null)
            {
                return NoOp(index, "Missing structured_adjustment.");
            }

            if (raw.StructuredAdjustment.Value.ValueKind == JsonValueKind.Null ||
                raw.StructuredAdjustment.Value.ValueKind == JsonValueKind.Undefined)
            {
                return NoOp(index, "Null structured_adjustment.");
            }

            StructuredAdjustment? adj;
            try
            {
                adj = JsonSerializer.Deserialize<StructuredAdjustment>(raw.StructuredAdjustment.Value.GetRawText(), JsonOptions);
            }
            catch (Exception)
            {
                return NoOp(index, "Malformed structured_adjustment.");
            }

            if (adj == null || adj.Hours == null || adj.Hours.Count == 0)
            {
                return NoOp(index, "Missing or empty hours.");
            }

            // Validate hours: 0..23, strictly ascending, no duplicates
            for (int i = 0; i < adj.Hours.Count; i++)
            {
                int hour = adj.Hours[i];
                if (hour < 0 || hour > 23)
                {
                    return NoOp(index, "Hour out of range [0, 23].");
                }
                if (i > 0 && hour <= adj.Hours[i - 1])
                {
                    return NoOp(index, "Hours must be strictly ascending with no duplicates.");
                }
            }

            // Type-specific numeric validation
            switch (raw.DirectiveType)
            {
                case "solar_reduction":
                    if (!adj.Factor.HasValue || adj.Factor.Value < 0.0 || adj.Factor.Value > 1.0)
                    {
                        return NoOp(index, "solar_reduction requires Factor between 0 and 1.");
                    }
                    break;

                case "minimum_battery_reserve":
                    if (!adj.MinimumEnergyKwh.HasValue || adj.MinimumEnergyKwh.Value < 0.0)
                    {
                        return NoOp(index, "minimum_battery_reserve requires MinimumEnergyKwh >= 0.");
                    }
                    break;

                case "max_grid_window":
                    if (!adj.MaxGridKwh.HasValue || adj.MaxGridKwh.Value < 0.0)
                    {
                        return NoOp(index, "max_grid_window requires MaxGridKwh >= 0.");
                    }
                    break;

                case "no_charge_window":
                case "no_discharge_window":
                    // Hours-only validation already satisfied
                    break;

                default:
                    return NoOp(index, "Unsupported directive type.");
            }

            return new DirectiveInterpretation
            {
                NoteIndex = index,
                Applies = true, // FORCED for non-no_op
                DirectiveType = raw.DirectiveType,
                StructuredAdjustment = adj,
                Explanation = raw.Explanation ?? string.Empty
            };
        }

        private static DirectiveInterpretation NoOp(int index, string reason)
        {
            return new DirectiveInterpretation
            {
                NoteIndex = index,
                Applies = false,
                DirectiveType = "no_op",
                StructuredAdjustment = null,
                Explanation = reason
            };
        }
    }
}
