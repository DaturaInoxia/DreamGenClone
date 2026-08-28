using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// B-034: Unified "Wife Willingness to Cheat" (Option A) — single source of truth.
///
/// Computes one 0-100 score from the Wife's own state (Desire, Loyalty, behavioral
/// dimensions) plus Husband marital-neglect inputs, then derives:
///   - Verdict: YES / MAYBE / NO from configured verdict bands.
///   - Ceiling: min(Desire, willingness) — what she is willing to do with the other man.
///
/// This is shared by <see cref="ScenarioGuidanceGenerator"/> (prompt injection), the
/// derived-formula evaluator (fit rules / gates), and the adaptive panel.
/// </summary>
public static class WifeWillingnessCalculator
{
    /// <summary>
    /// Computes the Option A willingness score (0-100) from resolved inputs.
    /// All values are 0-100. Missing inputs should be passed as 50 (neutral).
    /// </summary>
    public static int ComputeWillingnessToCheat(
        int desire,
        int loyalty,
        int seductionReceptivity,
        int boundaryFirmness,
        int attentiveness,
        int intimacyAvailability,
        double desireLoyaltyWeight = 0.5,
        double behaviorWeight = 0.5,
        double maritalDeficitWeight = 0.25)
    {
        var score = 50.0
            + (desire - loyalty) * desireLoyaltyWeight
            + (seductionReceptivity - boundaryFirmness) * behaviorWeight
            + ((100 - attentiveness) + (100 - intimacyAvailability)) * maritalDeficitWeight;

        return (int)Math.Clamp(Math.Round(score, MidpointRounding.AwayFromZero), 0, 100);
    }

    /// <summary>
    /// Computes the willingness score from the full session snapshot dictionary.
    /// Resolves Wife and Husband by <see cref="CharacterStatProfileV2.CharacterRole"/>.
    /// Missing Wife / Husband / dimensions default to 50 (neutral).
    /// </summary>
    public static int ComputeWillingnessToCheat(
        IReadOnlyDictionary<string, CharacterStatProfileV2>? snapshots,
        double desireLoyaltyWeight = 0.5,
        double behaviorWeight = 0.5,
        double maritalDeficitWeight = 0.25)
    {
        var wife = FindRole(snapshots, "Wife");
        var husband = FindRole(snapshots, "Husband");

        // B-034: the willingness score is the WIFE's — with no Wife snapshot there is
        // nothing to compute, so return the neutral baseline (50). Callers already gate
        // on Wife presence; this keeps the cross-character helper consistent.
        if (wife is null)
        {
            return 50;
        }

        var desire = wife.Desire;
        var loyalty = wife.Loyalty;
        var seductionReceptivity = GetEncounterStat(wife, "SeductionReceptivity");
        var boundaryFirmness = GetEncounterStat(wife, "BoundaryFirmness");
        var attentiveness = GetEncounterStat(husband, "Attentiveness");
        var intimacyAvailability = GetEncounterStat(husband, "IntimacyAvailability");

        return ComputeWillingnessToCheat(
            desire, loyalty, seductionReceptivity, boundaryFirmness,
            attentiveness, intimacyAvailability,
            desireLoyaltyWeight, behaviorWeight, maritalDeficitWeight);
    }

    /// <summary>
    /// Resolves the verdict label (YES / MAYBE / NO) for a willingness score.
    /// Bands are inclusive on the low end: NO = 0..noMax, MAYBE = noMax+1..maybeMax, YES = maybeMax+1..100.
    /// </summary>
    public static string ResolveVerdict(int willingness, int noMax = 40, int maybeMax = 70)
    {
        if (willingness <= noMax) return "NO";
        if (willingness <= maybeMax) return "MAYBE";
        return "YES";
    }

    /// <summary>
    /// The explicitness ceiling — bounded by the Wife's Desire. A low-Desire Wife
    /// with a high willingness score still has a low ceiling (over-clothes vs intercourse).
    /// </summary>
    public static int ComputeCeiling(int willingness, int desire)
        => Math.Clamp(Math.Min(desire, willingness), 0, 100);

    private static CharacterStatProfileV2? FindRole(
        IReadOnlyDictionary<string, CharacterStatProfileV2>? snapshots,
        string role)
    {
        if (snapshots is null) return null;
        return snapshots.Values.FirstOrDefault(s =>
            string.Equals(s.CharacterRole, role, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetEncounterStat(CharacterStatProfileV2? profile, string key)
    {
        if (profile?.RuntimeEncounterStats is null) return 50;
        return profile.RuntimeEncounterStats.TryGetValue(key, out var val) ? val : 50;
    }
}
