using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Tests.RolePlay;

public sealed class StatTextBandResolutionTests
{
    // ── IsNeutralBand ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(35, true)]
    [InlineData(50, true)]
    [InlineData(65, true)]
    [InlineData(34, false)]
    [InlineData(66, false)]
    [InlineData(0,  false)]
    [InlineData(100, false)]
    public void IsNeutralBand_ReturnsExpected(int value, bool expected)
    {
        Assert.Equal(expected, CharacterStatTextCatalog.IsNeutralBand(value));
    }

    // ── Band boundary resolution ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,   "Band1")]
    [InlineData(20,  "Band1")]
    [InlineData(21,  "Band2")]
    [InlineData(50,  "Band2")]
    [InlineData(51,  "Band3")]
    [InlineData(75,  "Band3")]
    [InlineData(76,  "Band4")]
    [InlineData(100, "Band4")]
    public void ResolveText_DesireWife_ReturnsBandTextForBoundary(int value, string band)
    {
        var text = CharacterStatTextCatalog.ResolveText("Desire", "Wife", value);
        Assert.False(string.IsNullOrWhiteSpace(text));

        // Confirm the resolved text is distinct per band
        var b1 = CharacterStatTextCatalog.ResolveText("Desire", "Wife", 10)!;
        var b2 = CharacterStatTextCatalog.ResolveText("Desire", "Wife", 30)!;
        var b3 = CharacterStatTextCatalog.ResolveText("Desire", "Wife", 60)!;
        var b4 = CharacterStatTextCatalog.ResolveText("Desire", "Wife", 90)!;

        var expected = band switch
        {
            "Band1" => b1,
            "Band2" => b2,
            "Band3" => b3,
            "Band4" => b4,
            _       => throw new ArgumentOutOfRangeException()
        };

        Assert.Equal(expected, text);
    }

    // ── All 15 catalog combinations return non-null text ────────────────────────────────────

    [Theory]
    [InlineData("Desire",      "Wife")]
    [InlineData("Desire",      "Husband")]
    [InlineData("Desire",      "OtherMan")]
    [InlineData("Restraint",   "Wife")]
    [InlineData("Restraint",   "Husband")]
    [InlineData("Restraint",   "OtherMan")]
    [InlineData("Dominance",   "Wife")]
    [InlineData("Dominance",   "Husband")]
    [InlineData("Dominance",   "OtherMan")]
    [InlineData("Loyalty",     "Wife")]
    [InlineData("Loyalty",     "Husband")]
    [InlineData("Loyalty",     "OtherMan")]
    [InlineData("SelfRespect", "Wife")]
    [InlineData("SelfRespect", "Husband")]
    [InlineData("SelfRespect", "OtherMan")]
    public void ResolveText_AllCombinations_ReturnsNonNullForAllFourBands(string stat, string role)
    {
        foreach (var value in new[] { 10, 35, 60, 85 })
        {
            var text = CharacterStatTextCatalog.ResolveText(stat, role, value);
            Assert.False(string.IsNullOrWhiteSpace(text),
                $"{stat}/{role} value={value} should have non-empty text in catalog");
        }
    }

    // ── Unknown stat/role returns null ──────────────────────────────────────────────────────

    [Fact]
    public void ResolveText_UnknownStat_ReturnsNull()
    {
        var result = CharacterStatTextCatalog.ResolveText("Tension", "Wife", 50);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveText_UnknownRole_ReturnsNull()
    {
        var result = CharacterStatTextCatalog.ResolveText("Desire", "Unknown", 50);
        Assert.Null(result);
    }

    // ── Case-insensitive lookup ──────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveText_IsCaseInsensitive()
    {
        var lower = CharacterStatTextCatalog.ResolveText("desire", "wife", 80);
        var upper = CharacterStatTextCatalog.ResolveText("DESIRE", "WIFE", 80);
        var mixed = CharacterStatTextCatalog.ResolveText("Desire", "Wife", 80);

        Assert.Equal(mixed, lower);
        Assert.Equal(mixed, upper);
    }

    // ── All 4 bands per combination are distinct ─────────────────────────────────────────────

    [Theory]
    [InlineData("Desire",      "Wife")]
    [InlineData("Restraint",   "Husband")]
    [InlineData("SelfRespect", "OtherMan")]
    public void ResolveText_FourBands_AreDistinct(string stat, string role)
    {
        var bands = new[]
        {
            CharacterStatTextCatalog.ResolveText(stat, role, 10),
            CharacterStatTextCatalog.ResolveText(stat, role, 35),
            CharacterStatTextCatalog.ResolveText(stat, role, 60),
            CharacterStatTextCatalog.ResolveText(stat, role, 85)
        };

        Assert.Equal(4, bands.Distinct().Count());
    }
}
