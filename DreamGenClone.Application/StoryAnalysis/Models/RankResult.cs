using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Application.StoryAnalysis.Models;

public sealed class ThemeRankResult
{
    public bool Success { get; set; }

    public double Score { get; set; }

    public bool IsDisqualified { get; set; }

    public List<string> DisqualifyingThemes { get; set; } = new();

    public string? ThemeDetectionsJson { get; set; }

    public string? ThemeSnapshotJson { get; set; }

    public Dictionary<string, string> Errors { get; set; } = new();
}

public sealed class ThemeDetection
{
    public string ThemeId { get; set; } = string.Empty;

    public string ThemeName { get; set; } = string.Empty;

    public ThemeTier Tier { get; set; }

    public bool Detected { get; set; }

    public ThemeIntensity Intensity { get; set; }

    public double Confidence { get; set; }

    public string Evidence { get; set; } = string.Empty;
}
