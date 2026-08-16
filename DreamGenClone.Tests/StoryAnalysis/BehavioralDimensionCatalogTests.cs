using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Tests.StoryAnalysis;

public class BehavioralDimensionCatalogTests
{
    // ── GetDimensions ────────────────────────────────────────────────────────

    [Fact]
    public void GetDimensions_OtherMan_Returns4Dimensions()
    {
        var dims = BehavioralDimensionCatalog.GetDimensions("OtherMan");
        Assert.Equal(4, dims.Count);
        Assert.All(dims, d => Assert.NotNull(d));
    }

    [Fact]
    public void GetDimensions_Any_ReturnsEmpty()
    {
        var dims = BehavioralDimensionCatalog.GetDimensions("Any");
        Assert.Empty(dims);
    }

    [Fact]
    public void GetDimensions_Unknown_ReturnsEmpty()
    {
        var dims = BehavioralDimensionCatalog.GetDimensions("UnknownRole");
        Assert.Empty(dims);
    }

    // ── ResolveTierText — tier boundary tests ────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void ResolveTierText_Tier1Boundary_ReturnsTier1Text(int value)
    {
        var tier1 = BehavioralDimensionCatalog.ResolveTierText("Husband", "Awareness", value);
        var dim = BehavioralDimensionCatalog.FindDimension("Husband", "Awareness");
        Assert.NotNull(dim);
        Assert.Equal(dim.Tier1Text, tier1);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(50)]
    public void ResolveTierText_Tier2Boundary_ReturnsTier2Text(int value)
    {
        var result = BehavioralDimensionCatalog.ResolveTierText("Husband", "Awareness", value);
        var dim = BehavioralDimensionCatalog.FindDimension("Husband", "Awareness");
        Assert.NotNull(dim);
        Assert.Equal(dim.Tier2Text, result);
    }

    [Theory]
    [InlineData(51)]
    [InlineData(75)]
    public void ResolveTierText_Tier3Boundary_ReturnsTier3Text(int value)
    {
        var result = BehavioralDimensionCatalog.ResolveTierText("Husband", "Awareness", value);
        var dim = BehavioralDimensionCatalog.FindDimension("Husband", "Awareness");
        Assert.NotNull(dim);
        Assert.Equal(dim.Tier3Text, result);
    }

    [Theory]
    [InlineData(76)]
    [InlineData(100)]
    public void ResolveTierText_Tier4Boundary_ReturnsTier4Text(int value)
    {
        var result = BehavioralDimensionCatalog.ResolveTierText("Husband", "Awareness", value);
        var dim = BehavioralDimensionCatalog.FindDimension("Husband", "Awareness");
        Assert.NotNull(dim);
        Assert.Equal(dim.Tier4Text, result);
    }

    // ── ResolveTierText — all 14 named dimensions at value=50 ────────────────

    [Theory]
    [InlineData("Husband", "Awareness")]
    [InlineData("Husband", "Acceptance")]
    [InlineData("Husband", "Voyeurism")]
    [InlineData("Husband", "Participation")]
    [InlineData("Husband", "Encouragement")]
    [InlineData("Husband", "RiskTolerance")]
    [InlineData("Wife", "DiscoveryCaution")]
    [InlineData("Wife", "Exhibitionism")]
    [InlineData("Wife", "EmotionalEngagement")]
    [InlineData("Wife", "PostEncounterGuilt")]
    [InlineData("OtherMan", "HusbandAwareness")]
    [InlineData("OtherMan", "MarriageContextUse")]
    [InlineData("OtherMan", "DiscoveryRisk")]
    [InlineData("OtherMan", "PersistencePastLimits")]
    public void ResolveTierText_AllDimensions_At50_ReturnsNonEmpty(string role, string name)
    {
        var result = BehavioralDimensionCatalog.ResolveTierText(role, name, 50);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    // ── ResolveTierText — unknown dimension ──────────────────────────────────

    [Fact]
    public void ResolveTierText_UnknownDimensionName_ReturnsEmpty()
    {
        var result = BehavioralDimensionCatalog.ResolveTierText("Husband", "NonExistentDimension", 50);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResolveTierText_UnknownRole_ReturnsEmpty()
    {
        var result = BehavioralDimensionCatalog.ResolveTierText("UnknownRole", "Awareness", 50);
        Assert.Equal(string.Empty, result);
    }

    // ── FindDimension ────────────────────────────────────────────────────────

    [Fact]
    public void FindDimension_ExistingDimension_ReturnsNotNull()
    {
        var dim = BehavioralDimensionCatalog.FindDimension("Wife", "DiscoveryCaution");
        Assert.NotNull(dim);
        Assert.Equal("DiscoveryCaution", dim.Name);
        Assert.Equal("Wife", dim.TargetRole);
    }

    [Fact]
    public void FindDimension_NonExistentDimension_ReturnsNull()
    {
        var dim = BehavioralDimensionCatalog.FindDimension("Husband", "NonExistent");
        Assert.Null(dim);
    }

    [Fact]
    public void FindDimension_IsCaseInsensitive()
    {
        var dim = BehavioralDimensionCatalog.FindDimension("husband", "awareness");
        Assert.NotNull(dim);
        Assert.Equal("Awareness", dim.Name);
    }
}
