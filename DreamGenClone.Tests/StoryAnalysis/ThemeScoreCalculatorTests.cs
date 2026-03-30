using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.StoryAnalysis;

namespace DreamGenClone.Tests.StoryAnalysis;

public class ThemeScoreCalculatorTests
{
    [Fact]
    public void EmptyDetections_ReturnsBaseline50()
    {
        var (score, isDisqualified, disqualifying) = ThemeScoreCalculator.Calculate(new List<ThemeDetection>());

        Assert.Equal(50.0, score);
        Assert.False(isDisqualified);
        Assert.Empty(disqualifying);
    }

    [Fact]
    public void MustHave_CentralIntensity_AddsFullPoints()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Romance", Tier = ThemeTier.MustHave, Intensity = ThemeIntensity.Central }
        };

        var (score, isDisqualified, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(60.0, score); // 50 + 10 * 1.0
        Assert.False(isDisqualified);
    }

    [Fact]
    public void MustHave_MinorIntensity_AddsQuarterPoints()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Romance", Tier = ThemeTier.MustHave, Intensity = ThemeIntensity.Minor }
        };

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(52.5, score); // 50 + 10 * 0.25
    }

    [Fact]
    public void StronglyPrefer_MajorIntensity()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Adventure", Tier = ThemeTier.StronglyPrefer, Intensity = ThemeIntensity.Major }
        };

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(54.5, score); // 50 + 6 * 0.75
    }

    [Fact]
    public void Dislike_SubtractsPoints()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Gore", Tier = ThemeTier.Dislike, Intensity = ThemeIntensity.Central }
        };

        var (score, isDisqualified, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(44.0, score); // 50 + (-6) * 1.0
        Assert.False(isDisqualified);
    }

    [Fact]
    public void HardDealBreaker_Detected_DisqualifiesWithZeroScore()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Violence", Tier = ThemeTier.HardDealBreaker, Intensity = ThemeIntensity.Minor },
            new() { ThemeName = "Romance", Tier = ThemeTier.MustHave, Intensity = ThemeIntensity.Central }
        };

        var (score, isDisqualified, disqualifying) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(0.0, score);
        Assert.True(isDisqualified);
        Assert.Single(disqualifying);
        Assert.Equal("Violence", disqualifying[0]);
    }

    [Fact]
    public void HardDealBreaker_NotDetected_NoDisqualification()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Violence", Tier = ThemeTier.HardDealBreaker, Intensity = ThemeIntensity.None }
        };

        var (score, isDisqualified, disqualifying) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(50.0, score);
        Assert.False(isDisqualified);
        Assert.Empty(disqualifying);
    }

    [Fact]
    public void Neutral_HasNoEffect()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Mystery", Tier = ThemeTier.Neutral, Intensity = ThemeIntensity.Central }
        };

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(50.0, score); // 50 + 0 * 1.0
    }

    [Fact]
    public void NoneIntensity_SkippedRegardlessOfTier()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Romance", Tier = ThemeTier.MustHave, Intensity = ThemeIntensity.None },
            new() { ThemeName = "Adventure", Tier = ThemeTier.StronglyPrefer, Intensity = ThemeIntensity.None }
        };

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(50.0, score);
    }

    [Fact]
    public void MultipleThemes_Additive()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Romance", Tier = ThemeTier.MustHave, Intensity = ThemeIntensity.Central },      // +10
            new() { ThemeName = "Adventure", Tier = ThemeTier.StronglyPrefer, Intensity = ThemeIntensity.Moderate }, // +3
            new() { ThemeName = "Comedy", Tier = ThemeTier.NiceToHave, Intensity = ThemeIntensity.Minor }          // +0.5
        };

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(63.5, score); // 50 + 10 + 3 + 0.5
    }

    [Fact]
    public void Score_ClampedToZero_WhenManyDislikes()
    {
        var detections = new List<ThemeDetection>();
        for (int i = 0; i < 20; i++)
        {
            detections.Add(new() { ThemeName = $"Bad{i}", Tier = ThemeTier.Dislike, Intensity = ThemeIntensity.Central });
        }

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(0.0, score); // 50 + 20 * (-6) = -70, clamped to 0
    }

    [Fact]
    public void Score_ClampedTo100_WhenManyMustHaves()
    {
        var detections = new List<ThemeDetection>();
        for (int i = 0; i < 20; i++)
        {
            detections.Add(new() { ThemeName = $"Good{i}", Tier = ThemeTier.MustHave, Intensity = ThemeIntensity.Central });
        }

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(100.0, score); // 50 + 20 * 10 = 250, clamped to 100
    }

    [Fact]
    public void MultipleDealBreakers_AllReported()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Violence", Tier = ThemeTier.HardDealBreaker, Intensity = ThemeIntensity.Major },
            new() { ThemeName = "Gore", Tier = ThemeTier.HardDealBreaker, Intensity = ThemeIntensity.Minor }
        };

        var (score, isDisqualified, disqualifying) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(0.0, score);
        Assert.True(isDisqualified);
        Assert.Equal(2, disqualifying.Count);
        Assert.Contains("Violence", disqualifying);
        Assert.Contains("Gore", disqualifying);
    }

    [Fact]
    public void NiceToHave_ModerateIntensity()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Humor", Tier = ThemeTier.NiceToHave, Intensity = ThemeIntensity.Moderate }
        };

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(51.0, score); // 50 + 2 * 0.5
    }
}
