using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public static class RolePlayDerivedFormulaEvaluator
{
    private static readonly IReadOnlyDictionary<string, Func<CharacterStatProfileV2, double>> FormulaById =
        new Dictionary<string, Func<CharacterStatProfileV2, double>>(StringComparer.OrdinalIgnoreCase)
        {
            ["RiskAppetite"] = profile => GetEncounterStat(profile, "Tension") + (profile.Desire / 2.0) - (profile.Restraint / 2.0) - (profile.Loyalty / 3.0),
            ["EscalationResistance"] = profile => profile.Restraint + profile.Loyalty - profile.Desire,
            ["Vulnerability"] = profile => 100.0 - profile.Dominance - (profile.SelfRespect / 2.0) + (GetEncounterStat(profile, "Connection") / 2.0),
            ["EmotionalVolatility"] = profile => GetEncounterStat(profile, "Tension") - (profile.Restraint / 2.0) - (GetEncounterStat(profile, "Connection") / 3.0),
            ["IntimacyCapacity"] = profile => GetEncounterStat(profile, "Connection") + profile.Desire + (profile.Restraint / 2.0),
            ["BoundariesStrength"] = profile => profile.SelfRespect + profile.Restraint + (profile.Loyalty / 2.0),
            ["ConsentThreshold"] = profile => profile.SelfRespect + profile.Dominance + profile.Restraint - (profile.Desire / 2.0),
            ["SubmissivenessCapacity"] = profile => 100.0 - profile.Dominance - (profile.SelfRespect / 2.0) + GetEncounterStat(profile, "Connection"),
            ["HotwifeCompatibility"] = profile => profile.Desire + GetEncounterStat(profile, "Connection") - profile.Dominance + (100.0 - profile.Loyalty),
            ["DeceptionCapacity"] = profile => profile.Restraint + (100.0 - GetEncounterStat(profile, "Connection")) - GetEncounterStat(profile, "Tension")
        };

    public static IReadOnlyDictionary<string, double> EvaluateAll(CharacterStatProfileV2 profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (formulaId, formula) in FormulaById)
        {
            scores[formulaId] = ClampFormulaScore(formula(profile));
        }

        return scores;
    }

    public static bool TryGetScore(CharacterStatProfileV2 profile, string formulaId, out double score)
    {
        score = 0;
        if (profile is null || string.IsNullOrWhiteSpace(formulaId))
        {
            return false;
        }

        if (!FormulaById.TryGetValue(formulaId, out var formula))
        {
            return false;
        }

        score = ClampFormulaScore(formula(profile));
        return true;
    }

    private static double GetEncounterStat(CharacterStatProfileV2 profile, string key)
    {
        if (profile.RuntimeEncounterStats is null)
        {
            return 50;
        }
        return profile.RuntimeEncounterStats.TryGetValue(key, out var val) ? val : 50;
    }

    private static double ClampFormulaScore(double value)
        => Math.Clamp(Math.Round(value, 4, MidpointRounding.AwayFromZero), 0.0, 150.0);
}