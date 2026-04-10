using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.StoryAnalysis;

namespace DreamGenClone.Tests.StoryAnalysis;

public class ThemeScoreCalculatorTests
{
    [Fact]
    public void EmptyDetections_ReturnsZero()
    {
        var (score, isDisqualified, disqualifying) = ThemeScoreCalculator.Calculate(new List<ThemeDetection>());

        Assert.Equal(0.0, score); // no themes = maxPossible 0 = 0
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

        Assert.Equal(100.0, score); // 10/10 * 100 = 100
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

        Assert.Equal(25.0, score); // 2.5/10 * 100 = 25
    }

    [Fact]
    public void StronglyPrefer_MajorIntensity()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Adventure", Tier = ThemeTier.StronglyPrefer, Intensity = ThemeIntensity.Major }
        };

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(75.0, score); // 4.5/6 * 100 = 75
    }

    [Fact]
    public void Dislike_SubtractsPoints()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Gore", Tier = ThemeTier.Dislike, Intensity = ThemeIntensity.Central }
        };

        var (score, isDisqualified, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(0.0, score); // only Dislike, no positive tiers, maxPossible=0
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

        Assert.Equal(0.0, score); // no positive tiers, maxPossible=0
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

        Assert.Equal(0.0, score); // Neutral tier has 0 points, maxPossible=0
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

        Assert.Equal(0.0, score); // maxPossible=16 but rawScore=0 (nothing detected)
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

        Assert.Equal(84.4, score); // 13.5/16 * 100 = 84.4 (NiceToHave excluded from denominator)
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

        Assert.Equal(0.0, score); // only Dislikes, maxPossible=0
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

        Assert.Equal(100.0, score); // 200/200 * 100 = 100
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

        Assert.Equal(50.0, score); // 1.0/2.0 * 100 = 50
    }

    [Fact]
    public void DislikeDetected_ReducesNormalizedScore()
    {
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Romance", Tier = ThemeTier.MustHave, Intensity = ThemeIntensity.Central },   // +10
            new() { ThemeName = "Gore", Tier = ThemeTier.Dislike, Intensity = ThemeIntensity.Central }         // -6
        };

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        Assert.Equal(40.0, score); // (10-6)/10 * 100 = 40
    }

    [Fact]
    public void UserScenario_MostThemesUndetected_LowScore()
    {
        // User's exact scenario: 5 themes, only Sex Scene detected as Minor
        var detections = new List<ThemeDetection>
        {
            new() { ThemeName = "Incest", Tier = ThemeTier.Dislike, Intensity = ThemeIntensity.None },
            new() { ThemeName = "Cheating", Tier = ThemeTier.StronglyPrefer, Intensity = ThemeIntensity.None },
            new() { ThemeName = "Sharing", Tier = ThemeTier.NiceToHave, Intensity = ThemeIntensity.None },
            new() { ThemeName = "Voyeur", Tier = ThemeTier.NiceToHave, Intensity = ThemeIntensity.None },
            new() { ThemeName = "Sex Scene", Tier = ThemeTier.MustHave, Intensity = ThemeIntensity.Minor }      // +2.5
        };

        var (score, _, _) = ThemeScoreCalculator.Calculate(detections);

        // maxPossible = 6+10 = 16 (NiceToHave excluded from denominator), rawScore = 2.5
        Assert.Equal(15.6, score); // 2.5/16 * 100 = 15.6
    }
}
