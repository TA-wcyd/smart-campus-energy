using MyApp.Infrastructure.AI;
using Xunit;

namespace MyApp.Tests;

public class LlmDirectiveParserTests
{
    [Fact]
    public void Example1_SolarReduction_ExplicitPercentageRemaining()
    {
        var note = "Solar output will drop to about 20% from 1 PM to 3 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 13, 14 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.2, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void Example2_SolarReduction_ReductionDeltaPercentage()
    {
        var note = "Expect an 80% reduction in rooftop solar during the 1-3 PM maintenance window.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 13, 14 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.2, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void Example3_SolarReduction_VerbalFraction()
    {
        var note = "Panel washing from one until three will leave roughly one-fifth of normal solar output.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 13, 14 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.2, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void Example4_SolarReduction_HalfOutput()
    {
        var note = "PV production will be cut in half between 10 AM and 1 PM for inverter service.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 10, 11, 12 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.5, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void Example5_SolarReduction_CompleteShutdown()
    {
        var note = "Rooftop array disconnected for emergency repairs from 8 AM to 11 AM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 8, 9, 10 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.0, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void Example6_SolarReduction_AllDayDegradation()
    {
        var note = "Solar will be at 40% all day due to heavy haze.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(24, result.StructuredAdjustment.Hours!.Count);
        Assert.Equal(Enumerable.Range(0, 24).ToList(), result.StructuredAdjustment.Hours);
        Assert.Equal(0.4, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void Example7_NoChargeWindow_StandardAfternoonWindow()
    {
        var note = "Do not charge the battery between 2 PM and 4 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("no_charge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 14, 15 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void Example8_NoChargeWindow_SingleHourPointRestriction()
    {
        var note = "No battery charging at 3 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("no_charge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 15 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void Example9_NoChargeWindow_MidnightWrapping()
    {
        var note = "Do not charge the battery from 11 PM to 1 AM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("no_charge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 0, 23 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void Example10_NoDischargeWindow_StandardEveningWindow()
    {
        var note = "Battery discharging is unavailable from 6 PM through 8 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("no_discharge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 18, 19 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void Example11_NoDischargeWindow_MorningMaintenance()
    {
        var note = "Prevent battery discharge between 7 AM and 9 AM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("no_discharge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 7, 8 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void Example12_MinimumBatteryReserve_EveningEmergencyMargin()
    {
        var note = "Keep at least 120 kWh in reserve from 6 PM until 9 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("minimum_battery_reserve", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 18, 19, 20 }, result.StructuredAdjustment.Hours);
        Assert.Equal(120.0, result.StructuredAdjustment.MinimumEnergyKwh!.Value, precision: 2);
    }

    [Fact]
    public void Example13_MinimumBatteryReserve_AlternateWording()
    {
        var note = "From 5 PM to 7 PM, the battery must hold no less than 200 kWh for backup.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("minimum_battery_reserve", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 17, 18 }, result.StructuredAdjustment.Hours);
        Assert.Equal(200.0, result.StructuredAdjustment.MinimumEnergyKwh!.Value, precision: 2);
    }

    [Fact]
    public void Example14_MaxGridWindow_PeakImportCap()
    {
        var note = "Grid draw must not exceed 50 kWh during 5 PM to 7 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("max_grid_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 17, 18 }, result.StructuredAdjustment.Hours);
        Assert.Equal(50.0, result.StructuredAdjustment.MaxGridKwh!.Value, precision: 2);
    }

    [Fact]
    public void Example15_MaxGridWindow_FeederLimit()
    {
        var note = "Limit the import from the grid to a maximum of 80 kWh between 09:00 and 11:00.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("max_grid_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 9, 10 }, result.StructuredAdjustment.Hours);
        Assert.Equal(80.0, result.StructuredAdjustment.MaxGridKwh!.Value, precision: 2);
    }

    [Fact]
    public void Example16_NoOp_UnrelatedTopic()
    {
        var note = "The cafeteria menu changes tomorrow.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.False(result.Applies);
        Assert.Equal("no_op", result.DirectiveType);
        Assert.Null(result.StructuredAdjustment);
    }

    [Fact]
    public void Example17_NoOp_VagueDirection()
    {
        var note = "Try to save energy if possible.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.False(result.Applies);
        Assert.Equal("no_op", result.DirectiveType);
        Assert.Null(result.StructuredAdjustment);
    }

    [Fact]
    public void Example18_NoOp_FutureScheduleNotice()
    {
        var note = "Next week we will test the diesel generator.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.False(result.Applies);
        Assert.Equal("no_op", result.DirectiveType);
        Assert.Null(result.StructuredAdjustment);
    }

    [Fact]
    public void Example19_NoOp_HistoricalFacilityMaintenance()
    {
        var note = "The HVAC filter in the east wing was changed yesterday.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.False(result.Applies);
        Assert.Equal("no_op", result.DirectiveType);
        Assert.Null(result.StructuredAdjustment);
    }

    [Fact]
    public void Example20_CompoundNote_SingleDirectivePrioritization()
    {
        var note = "Solar drops to 25% and please don't charge the battery from 2 PM to 4 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(24, result.StructuredAdjustment.Hours!.Count);
        Assert.Equal(0.25, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_CleanedSolarPanels_Factor1()
    {
        var note = "Cleaned solar panels, full capacity expected from 9 AM to 4 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 9, 10, 11, 12, 13, 14, 15 }, result.StructuredAdjustment.Hours);
        Assert.Equal(1.0, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_CutBy40Percent_Leaves60Percent()
    {
        var note = "Solar output cut by 40% from 11 AM to 2 PM due to dust storm.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 11, 12, 13 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.6, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_QuarterSolarOutput()
    {
        var note = "Shading on rooftop panels leaves one-quarter of normal output from 8 AM to 12 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 8, 9, 10, 11 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.25, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_ThreeQuartersSolarOutput()
    {
        var note = "Inverter derated to three-quarters output between 13:00 and 16:00.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 13, 14, 15 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.75, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_MidnightTo2Am()
    {
        var note = "Do not charge the battery from midnight to 2 AM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("no_charge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 0, 1 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void EdgeCase_11PmToMidnight()
    {
        var note = "Do not discharge the battery from 11 PM to midnight.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("no_discharge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 23 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void EdgeCase_SingleHour17()
    {
        var note = "Grid draw must not exceed 45 kWh during hour 17.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("max_grid_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 17 }, result.StructuredAdjustment.Hours);
        Assert.Equal(45.0, result.StructuredAdjustment.MaxGridKwh!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_BatterySafetyThreshold()
    {
        var note = "Maintain safety threshold of 250 kWh in battery between 19:00 and 22:00.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("minimum_battery_reserve", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 19, 20, 21 }, result.StructuredAdjustment.Hours);
        Assert.Equal(250.0, result.StructuredAdjustment.MinimumEnergyKwh!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_DoNotDropBelowFloor()
    {
        var note = "Battery reserve must not drop below 80 kWh from 6 PM to 10 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("minimum_battery_reserve", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 18, 19, 20, 21 }, result.StructuredAdjustment.Hours);
        Assert.Equal(80.0, result.StructuredAdjustment.MinimumEnergyKwh!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_DoNotPullMoreThanFromGrid()
    {
        var note = "Do not pull more than 65 kWh from the grid between 18:00 and 21:00.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("max_grid_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 18, 19, 20 }, result.StructuredAdjustment.Hours);
        Assert.Equal(65.0, result.StructuredAdjustment.MaxGridKwh!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_HaltTopUp_NoCharge()
    {
        var note = "Halt top up of battery storage from 2 PM to 5 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("no_charge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 14, 15, 16 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void EdgeCase_KeepBatteryIdle_NoDischarge()
    {
        var note = "Keep battery idle without discharging between 14:00 and 17:00.";
        var result = LlmService.ParseDirectiveDeterministic(note);

        Assert.True(result.Applies);
        Assert.Equal("no_discharge_window", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 14, 15, 16 }, result.StructuredAdjustment.Hours);
    }

    [Fact]
    public void EdgeCase_NonActionable_SecurityAndSports()
    {
        var note1 = "Security guard shift change scheduled for 10 PM.";
        var result1 = LlmService.ParseDirectiveDeterministic(note1);
        Assert.False(result1.Applies);
        Assert.Equal("no_op", result1.DirectiveType);

        var note2 = "Inter-university sports tournament happening on campus today.";
        var result2 = LlmService.ParseDirectiveDeterministic(note2);
        Assert.False(result2.Applies);
        Assert.Equal("no_op", result2.DirectiveType);

        var note3 = "Sunny skies and clear weather anticipated for the day.";
        var result3 = LlmService.ParseDirectiveDeterministic(note3);
        Assert.False(result3.Applies);
        Assert.Equal("no_op", result3.DirectiveType);
    }

    [Fact]
    public void EdgeCase_ExplicitDecimalFactor_0_35()
    {
        var note = "Solar factor 0.35 from 10 AM to 2 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);
        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 10, 11, 12, 13 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.35, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_ScaleSolarDecimal_0_4()
    {
        var note = "Scale solar output to 0.4 between 11:00 and 15:00.";
        var result = LlmService.ParseDirectiveDeterministic(note);
        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 11, 12, 13, 14 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.4, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_WordPercentageReduction_TwentyPercent()
    {
        var note = "Reduce solar by twenty percent from 1 PM to 3 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);
        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 13, 14 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.8, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_NumberWithoutPercentSymbol_DropsTo30()
    {
        var note = "Solar output drops to 30 from 1 PM to 4 PM.";
        var result = LlmService.ParseDirectiveDeterministic(note);
        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 13, 14, 15 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.3, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }

    [Fact]
    public void EdgeCase_TwoThirdsFractions()
    {
        var note = "Two-thirds of normal solar output expected between 8 AM and 11 AM.";
        var result = LlmService.ParseDirectiveDeterministic(note);
        Assert.True(result.Applies);
        Assert.Equal("solar_reduction", result.DirectiveType);
        Assert.NotNull(result.StructuredAdjustment);
        Assert.Equal(new List<int> { 8, 9, 10 }, result.StructuredAdjustment.Hours);
        Assert.Equal(0.67, result.StructuredAdjustment.Factor!.Value, precision: 2);
    }
}
