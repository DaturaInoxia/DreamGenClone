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

    public static (double Score, bool IsDisqualified, List<string> DisqualifyingThemes) Calculate(List<ThemeDetection> detections)
    {
        var disqualifyingThemes = new List<string>();

        // Check for hard deal-breakers first
        foreach (var detection in detections)
        {
            if (detection.Tier == ThemeTier.HardDealBreaker && detection.Intensity != ThemeIntensity.None)
            {
                disqualifyingThemes.Add(detection.ThemeName);
            }
        }

        if (disqualifyingThemes.Count > 0)
        {
            return (0.0, true, disqualifyingThemes);
        }

        // Additive scoring from baseline of 50
        double score = 50.0;

        foreach (var detection in detections)
        {
            if (detection.Intensity == ThemeIntensity.None)
                continue;

            var points = TierPoints.GetValueOrDefault(detection.Tier, 0.0);
            var multiplier = IntensityMultipliers.GetValueOrDefault(detection.Intensity, 0.0);
            score += points * multiplier;
        }

        score = Math.Clamp(score, 0.0, 100.0);

        return (Math.Round(score, 1), false, disqualifyingThemes);
    }
}
