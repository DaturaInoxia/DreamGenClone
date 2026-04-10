using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public static class ThemeScoreCalculator
{
    private static readonly Dictionary<ThemeTier, double> TierPoints = new()
    {
        [ThemeTier.MustHave] = 10.0,
        [ThemeTier.StronglyPrefer] = 6.0,
        [ThemeTier.NiceToHave] = 2.0,
        [ThemeTier.Neutral] = 0.0,
        [ThemeTier.Dislike] = -6.0,
        [ThemeTier.HardDealBreaker] = 0.0 // handled separately
    };

    private static readonly Dictionary<ThemeIntensity, double> IntensityMultipliers = new()
    {
        [ThemeIntensity.None] = 0.0,
        [ThemeIntensity.Minor] = 0.25,
        [ThemeIntensity.Moderate] = 0.5,
        [ThemeIntensity.Major] = 0.75,
        [ThemeIntensity.Central] = 1.0
    };

    public static (double Score, bool IsDisqualified, List<string> DisqualifyingThemes) Calculate(List<ThemeDetection> detections, HashSet<string>? dismissedThemeIds = null)
    {
        var disqualifyingThemes = new List<string>();

        // Check for hard deal-breakers first (skip dismissed themes)
        foreach (var detection in detections)
        {
            if (dismissedThemeIds is not null && dismissedThemeIds.Contains(detection.ThemeId))
                continue;

            if (detection.Tier == ThemeTier.HardDealBreaker && detection.Intensity != ThemeIntensity.None)
            {
                disqualifyingThemes.Add(detection.ThemeName);
            }
        }

        if (disqualifyingThemes.Count > 0)
        {
            return (0.0, true, disqualifyingThemes);
        }

        // Calculate max possible score from required tiers (MustHave + StronglyPrefer).
        // NiceToHave themes are bonuses — they add to rawScore when detected but
        // don't inflate the denominator, so their absence doesn't penalize the score.
        double maxPossible = 0.0;
        bool hasRequiredTiers = false;
        foreach (var detection in detections)
        {
            if (dismissedThemeIds is not null && dismissedThemeIds.Contains(detection.ThemeId))
                continue;

            if (detection.Tier == ThemeTier.MustHave || detection.Tier == ThemeTier.StronglyPrefer)
            {
                maxPossible += TierPoints[detection.Tier];
                hasRequiredTiers = true;
            }
        }

        // Fallback: if no MustHave/StronglyPrefer themes, use NiceToHave as denominator
        if (!hasRequiredTiers)
        {
            foreach (var detection in detections)
            {
                if (dismissedThemeIds is not null && dismissedThemeIds.Contains(detection.ThemeId))
                    continue;

                if (detection.Tier == ThemeTier.NiceToHave)
                    maxPossible += TierPoints[detection.Tier];
            }
        }

        if (maxPossible == 0.0)
        {
            return (0.0, false, disqualifyingThemes);
        }

        // Calculate raw score from detected themes
        double rawScore = 0.0;
        foreach (var detection in detections)
        {
            if (dismissedThemeIds is not null && dismissedThemeIds.Contains(detection.ThemeId))
                continue;

            if (detection.Intensity == ThemeIntensity.None)
                continue;

            var points = TierPoints.GetValueOrDefault(detection.Tier, 0.0);
            var multiplier = IntensityMultipliers.GetValueOrDefault(detection.Intensity, 0.0);
            rawScore += points * multiplier;
        }

        // Normalize to 0-100 percentage
        double score = Math.Clamp(rawScore / maxPossible * 100.0, 0.0, 100.0);

        return (Math.Round(score, 1), false, disqualifyingThemes);
    }
}
