using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayDerivedFormulaEvaluatorTests
{
    [Fact]
    public void EvaluateAll_ComputesExpectedScores()
    {
        var profile = new CharacterStatProfileV2
        {
            CharacterId = "alpha",
            Desire = 70,
            Restraint = 40,
            Tension = 60,
            Connection = 50,
            Dominance = 30,
            Loyalty = 20,
            SelfRespect = 40
        };

        var scores = RolePlayDerivedFormulaEvaluator.EvaluateAll(profile);

        Assert.Equal(68.3333, scores["RiskAppetite"], 4);
        Assert.Equal(0.0, scores["EscalationResistance"], 4);
        Assert.Equal(75.0, scores["Vulnerability"], 4);
        Assert.Equal(23.3333, scores["EmotionalVolatility"], 4);
        Assert.Equal(140.0, scores["IntimacyCapacity"], 4);
        Assert.Equal(90.0, scores["BoundariesStrength"], 4);
        Assert.Equal(75.0, scores["ConsentThreshold"], 4);
        Assert.Equal(100.0, scores["SubmissivenessCapacity"], 4);
        Assert.Equal(150.0, scores["HotwifeCompatibility"], 4);
        Assert.Equal(30.0, scores["DeceptionCapacity"], 4);
    }

    [Fact]
    public void TryGetScore_ReturnsFalse_ForUnknownFormula()
    {
        var profile = new CharacterStatProfileV2 { CharacterId = "alpha" };

        var ok = RolePlayDerivedFormulaEvaluator.TryGetScore(profile, "UnknownFormula", out var score);

        Assert.False(ok);
        Assert.Equal(0.0, score);
    }
}