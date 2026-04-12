using System.Text.Json;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Xunit;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class LegacyStatMappingTests
{
    [Theory]
    [InlineData("Arousal", "Desire")]
    [InlineData("Inhibition", "Restraint")]
    [InlineData("Trust", "Connection")]
    [InlineData("Agency", "Dominance")]
    [InlineData("Jealousy", "Tension")]
    [InlineData("DominanceDrive", "Dominance")]
    [InlineData("Shame", "Restraint")]
    [InlineData("RiskAppetite", "Dominance")]
    public void NormalizeLegacyStatName_MapsOldNameToCanonical(string legacy, string expected)
    {
        var result = AdaptiveStatCatalog.NormalizeLegacyStatName(legacy);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Desire")]
    [InlineData("Restraint")]
    [InlineData("Connection")]
    [InlineData("Dominance")]
    [InlineData("Tension")]
    public void NormalizeLegacyStatName_PreservesCanonicalNames(string canonical)
    {
        var result = AdaptiveStatCatalog.NormalizeLegacyStatName(canonical);
        Assert.Equal(canonical, result);
    }

    [Theory]
    [InlineData("arousal", "Desire")]
    [InlineData("TRUST", "Connection")]
    [InlineData("desire", "Desire")]
    public void NormalizeLegacyStatName_IsCaseInsensitive(string input, string expected)
    {
        var result = AdaptiveStatCatalog.NormalizeLegacyStatName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void NormalizeLegacyStatName_ReturnsInputForNullOrWhitespace(string? input)
    {
        var result = AdaptiveStatCatalog.NormalizeLegacyStatName(input!);
        Assert.Equal(input, result);
    }

    [Fact]
    public void NormalizeLegacyStatName_PassesThroughUnknownNames()
    {
        var result = AdaptiveStatCatalog.NormalizeLegacyStatName("CustomStat");
        Assert.Equal("CustomStat", result);
    }

    [Fact]
    public void RolePlaySession_DeserializesLegacySelectedRankingProfileId()
    {
        var json = """{"SelectedRankingProfileId":"profile-old-1"}""";
        var session = JsonSerializer.Deserialize<RolePlaySession>(json)!;
        Assert.Equal("profile-old-1", session.SelectedThemeProfileId);
    }

    [Fact]
    public void RolePlaySession_PrefersNewNameOverLegacy()
    {
        var json = """{"SelectedThemeProfileId":"new-id","SelectedRankingProfileId":"old-id"}""";
        var session = JsonSerializer.Deserialize<RolePlaySession>(json)!;
        Assert.Equal("new-id", session.SelectedThemeProfileId);
    }

    [Fact]
    public void Scenario_DeserializesLegacyDefaultRankingProfileId()
    {
        var json = """{"Id":"s1","Name":"Test","DefaultRankingProfileId":"profile-old-1"}""";
        var scenario = JsonSerializer.Deserialize<Scenario>(json)!;
        Assert.Equal("profile-old-1", scenario.DefaultThemeProfileId);
    }

    [Fact]
    public void Scenario_PrefersNewNameOverLegacy()
    {
        var json = """{"Id":"s1","Name":"Test","DefaultThemeProfileId":"new-id","DefaultRankingProfileId":"old-id"}""";
        var scenario = JsonSerializer.Deserialize<Scenario>(json)!;
        Assert.Equal("new-id", scenario.DefaultThemeProfileId);
    }
}
