using System.Text.Json;
using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using Xunit;

namespace MyApp.Tests;

public class GuardrailTests
{
    private static JsonElement ToJsonElement(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement ToRawJsonElement(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void UnknownDirectiveType_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "unknown_action",
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 10, 11 } })
        };

        var result = Guardrails.Normalize(raw, 0);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
        Assert.Null(result.StructuredAdjustment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingDirectiveType_ReturnsNoOp(string? directiveType)
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = directiveType,
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 10, 11 } })
        };

        var result = Guardrails.Normalize(raw, 0);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
        Assert.Null(result.StructuredAdjustment);
    }

    [Fact]
    public void NonNoOp_WithRawAppliesFalse_ForcedToTrue()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "no_charge_window",
            Applies = false, // Contrasting raw value
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 14, 15 } }),
            Explanation = "Do not charge between 2 PM and 4 PM."
        };

        var result = Guardrails.Normalize(raw, 1);

        Assert.True(result.Applies);
        Assert.Equal("no_charge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 14, 15 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void NoOp_WithRawAppliesTrue_ForcedToFalse()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "no_op",
            Applies = true, // Contradictory raw value
            Explanation = "Informational weather note."
        };

        var result = Guardrails.Normalize(raw, 2);

        Assert.False(result.Applies);
        Assert.Equal("no_op", result.DirectiveType);
        Assert.Null(result.StructuredAdjustment);
    }

    [Fact]
    public void NoOp_WithNonNullStructuredAdjustment_ForcedToNull()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "no_op",
            Applies = false,
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 12, 13 } }),
            Explanation = "Cafeteria update."
        };

        var result = Guardrails.Normalize(raw, 3);

        Assert.False(result.Applies);
        Assert.Equal("no_op", result.DirectiveType);
        Assert.Null(result.StructuredAdjustment);
    }

    [Fact]
    public void Hours_WithDuplicates_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "no_discharge_window",
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 14, 14, 15 } })
        };

        var result = Guardrails.Normalize(raw, 4);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    [InlineData(99)]
    public void Hours_OutOfRange_ReturnsNoOp(int invalidHour)
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "no_charge_window",
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 10, invalidHour } })
        };

        var result = Guardrails.Normalize(raw, 5);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }

    [Fact]
    public void Hours_NonAscending_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "no_charge_window",
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 15, 14 } })
        };

        var result = Guardrails.Normalize(raw, 6);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }

    [Fact]
    public void Hours_Empty_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "no_discharge_window",
            StructuredAdjustment = ToJsonElement(new { hours = Array.Empty<int>() })
        };

        var result = Guardrails.Normalize(raw, 7);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }

    [Fact]
    public void SolarReduction_FactorGreaterThanOne_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "solar_reduction",
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 13, 14 }, factor = 1.5 })
        };

        var result = Guardrails.Normalize(raw, 8);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }

    [Fact]
    public void SolarReduction_FactorNegative_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "solar_reduction",
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 13, 14 }, factor = -0.1 })
        };

        var result = Guardrails.Normalize(raw, 9);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }

    [Fact]
    public void MinimumBatteryReserve_NegativeKwh_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "minimum_battery_reserve",
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 18, 19 }, minimum_energy_kwh = -10.0 })
        };

        var result = Guardrails.Normalize(raw, 10);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }

    [Fact]
    public void MaxGridWindow_NegativeKwh_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "max_grid_window",
            StructuredAdjustment = ToJsonElement(new { hours = new[] { 17, 18 }, max_grid_kwh = -5.0 })
        };

        var result = Guardrails.Normalize(raw, 11);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }

    [Fact]
    public void MalformedStructuredAdjustment_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "max_grid_window",
            StructuredAdjustment = ToRawJsonElement("{\"hours\": \"not_an_array\", \"max_grid_kwh\": 100}")
        };

        var result = Guardrails.Normalize(raw, 12);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }

    [Fact]
    public void NullStructuredAdjustment_ForNonNoOp_ReturnsNoOp()
    {
        var raw = new LlmDirectiveRaw
        {
            DirectiveType = "solar_reduction",
            StructuredAdjustment = null
        };

        var result = Guardrails.Normalize(raw, 13);

        Assert.Equal("no_op", result.DirectiveType);
        Assert.False(result.Applies);
    }
}
