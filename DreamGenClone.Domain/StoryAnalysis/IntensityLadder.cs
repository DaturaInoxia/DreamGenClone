namespace DreamGenClone.Domain.StoryAnalysis;

public static class IntensityLadder
{
    public static readonly IReadOnlyList<(IntensityLevel Intensity, string Label)> Levels =
    [
        (IntensityLevel.Emotional, "Emotional"),
        (IntensityLevel.SuggestivePg12, "Suggestive"),
        (IntensityLevel.SensualMature, "Sensual"),
        (IntensityLevel.Explicit, "Erotic"),
        (IntensityLevel.Hardcore, "Hardcore")
    ];

    public static string GetLabel(IntensityLevel intensity)
    {
        return intensity switch
        {
            IntensityLevel.Intro => "Atmospheric",
            IntensityLevel.Emotional => "Emotional",
            IntensityLevel.SuggestivePg12 => "Suggestive",
            IntensityLevel.SensualMature => "Sensual",
            IntensityLevel.Explicit => "Erotic",
            _ => "Hardcore"
        };
    }

    public static string GetLabel(int scale)
    {
        return GetLabel((IntensityLevel)Math.Clamp(scale, 0, 5));
    }

    public static int? ParseScale(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("atmospheric") || normalized.Contains("intro") || normalized.Contains("pg12") || normalized.Contains("pg-12")) return 0;
        if (normalized.Contains("emotional") || normalized.Contains("pg13") || normalized.Contains("pg-13")) return 1;
        if (normalized.Contains("suggestive")) return 2;
        if (normalized.Contains("sensual") || normalized.Contains("mature")) return 3;
        if (normalized.Contains("erotic") || normalized.Contains("explicit")) return 4;
        if (normalized.Contains("hardcore")) return 5;
        return null;
    }

    public static string GetDefaultDescription(IntensityLevel intensity)
    {
        return intensity switch
        {
            IntensityLevel.Intro => "Atmosphere-first, low-intensity scene setting and mood building.",
            IntensityLevel.Emotional => "Emotion-forward intimacy and relationship focus.",
            IntensityLevel.SuggestivePg12 => "Flirty and suggestive with restrained explicitness.",
            IntensityLevel.SensualMature => "Sensory, mature tone emphasizing tension and pacing.",
            IntensityLevel.Explicit => "Direct and explicit physical delivery. Each turn describes specific acts, body parts, and sensations. Scene pacing sustains the encounter across multiple turns without rushing to resolution.",
            IntensityLevel.Hardcore => "Maximum-intensity explicit delivery. Every physical act and sensation is named with full specificity and no softening. Scene sustains and escalates across turns with zero fade-to-black.",
            _ => "Explicit intensity level with no softening of content."
        };
    }
}