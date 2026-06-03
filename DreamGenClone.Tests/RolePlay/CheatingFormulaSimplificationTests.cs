using System.Reflection;
using DreamGenClone.Infrastructure.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Verifies the simplified cheating pressure formula: Loyalty - Desire/2 + Restraint/2.
/// T006 removed the Tension parameter. These tests guard against reintroduction.
/// </summary>
public sealed class CheatingFormulaSimplificationTests
{
    private static readonly MethodInfo BuildStatInterpretationMethod =
        typeof(ScenarioGuidanceGenerator).GetMethod(
            "BuildStatInterpretation",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildStatInterpretation not found via reflection");

    // ── Formula arithmetic ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheatingPressure_Loyalty70_Desire60_Restraint50_Equals65()
    {
        // Formula: Loyalty - Desire/2 + Restraint/2
        // 70 - (60/2) + (50/2) = 70 - 30 + 25 = 65
        const double loyalty = 70, desire = 60, restraint = 50;
        var cheatingPressure = loyalty - (desire / 2.0) + (restraint / 2.0);
        Assert.Equal(65.0, cheatingPressure, precision: 4);
    }

    [Fact]
    public void BuildStatInterpretation_Loyalty70_Desire60_Restraint50_ContainsModerateHighText()
    {
        // cheatingPressure = 65 → "moderate-high" band (≥60 and <80)
        var result = (string)BuildStatInterpretationMethod.Invoke(null, [60.0, 50.0, 50.0, 70.0])!;
        Assert.Contains("moderate-high", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Parameter signature — no Tension/Connection ─────────────────────────────────────────

    [Fact]
    public void BuildStatInterpretation_MethodSignature_HasExactlyFourParameters_NoTensionOrConnection()
    {
        // T006 removed averageTension. The method should only accept
        // (averageDesire, averageRestraint, averageDominance, averageLoyalty).
        var parameters = BuildStatInterpretationMethod.GetParameters();
        Assert.Equal(4, parameters.Length);

        var paramNames = parameters.Select(p => p.Name ?? string.Empty).ToArray();
        Assert.DoesNotContain(paramNames, p => p.Contains("tension", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paramNames, p => p.Contains("connection", StringComparison.OrdinalIgnoreCase));
    }

    // ── Band boundary verification ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(20.0, 100.0, 50.0, 90.0, "high")]           // pressure = 90 - 10 + 50 = 130 → high
    [InlineData(60.0, 50.0, 50.0, 70.0, "moderate-high")]   // pressure = 70 - 30 + 25 = 65 → moderate-high
    [InlineData(70.0, 50.0, 50.0, 55.0, "mixed")]            // pressure = 55 - 35 + 25 = 45 → mixed
    [InlineData(90.0, 20.0, 50.0, 20.0, "low")]              // pressure = 20 - 45 + 10 = -15 → low
    public void BuildStatInterpretation_CheatingPressureBands_ProducesCorrectBandText(
        double desire, double restraint, double dominance, double loyalty, string expectedBandKeyword)
    {
        var result = (string)BuildStatInterpretationMethod.Invoke(null, [desire, restraint, dominance, loyalty])!;
        Assert.Contains(expectedBandKeyword, result, StringComparison.OrdinalIgnoreCase);
    }
}
