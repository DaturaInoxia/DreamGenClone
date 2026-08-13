using DreamGenClone.Domain.Templates;

namespace DreamGenClone.Tests.Templates;

public class AttractivenessTierCatalogTests
{
    // ── Catalog shape ──────────────────────────────────────────────────────

    [Fact]
    public void All_HasExactly5Bands()
    {
        Assert.Equal(5, AttractivenessTierCatalog.All.Count);
    }

    [Fact]
    public void All_BandsAreNonOverlapping()
    {
        var bands = AttractivenessTierCatalog.All.OrderBy(t => t.Min).ToList();
        for (var i = 1; i < bands.Count; i++)
        {
            Assert.True(bands[i].Min > bands[i - 1].Max,
                $"Band {bands[i].Label} overlaps {bands[i - 1].Label}");
        }
    }

    [Fact]
    public void All_BandsCoverExactly1To10()
    {
        var bands = AttractivenessTierCatalog.All.OrderBy(t => t.Min).ToList();
        Assert.Equal(1, bands[0].Min);
        Assert.Equal(10, bands[^1].Max);
        // Contiguous: each band starts immediately after the previous ends.
        for (var i = 1; i < bands.Count; i++)
        {
            Assert.Equal(bands[i - 1].Max + 1, bands[i].Min);
        }
    }

    [Fact]
    public void All_LabelsAreTheExpectedFive()
    {
        var labels = AttractivenessTierCatalog.All.Select(t => t.Label).ToHashSet(StringComparer.Ordinal);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "Striking", "Attractive", "Average", "Plain", "Repelling"
        };
        Assert.Equal(expected, labels);
    }

    // ── Resolve ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(10, "Striking")]
    [InlineData(9, "Striking")]
    [InlineData(8, "Attractive")]
    [InlineData(7, "Attractive")]
    [InlineData(6, "Average")]
    [InlineData(5, "Average")]
    [InlineData(4, "Plain")]
    [InlineData(3, "Plain")]
    [InlineData(2, "Repelling")]
    [InlineData(1, "Repelling")]
    public void Resolve_EachRatingMapsToExpectedBand(int rating, string expectedLabel)
    {
        var tier = AttractivenessTierCatalog.Resolve(rating);
        Assert.NotNull(tier);
        Assert.Equal(expectedLabel, tier.Label);
    }

    [Fact]
    public void Resolve_Null_ReturnsNull()
    {
        Assert.Null(AttractivenessTierCatalog.Resolve(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Resolve_OutOfRange_ReturnsNull(int rating)
    {
        Assert.Null(AttractivenessTierCatalog.Resolve(rating));
    }

    [Fact]
    public void Resolve_IsDeterministic()
    {
        for (var rating = 1; rating <= 10; rating++)
        {
            var a = AttractivenessTierCatalog.Resolve(rating);
            var b = AttractivenessTierCatalog.Resolve(rating);
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(a.Label, b.Label);
            Assert.Equal(a.Prose, b.Prose);
        }
    }

    // ── Prose contract (SC-002): ≥1 physical descriptor + ≥1 behavioral cue ─

    private static readonly string[] PhysicalTokens =
    [
        "features", "symmetry", "face", "body", "well-kept",
        "ordinary", "unremarkable", "forgettable", "neglected", "unappealing"
    ];

    private static readonly string[] BehavioralTokens =
    [
        "turn to look", "flustered", "nervous", "attention follows", "presence",
        "lingering looks", "smiles", "warmer", "pull", "interest",
        "avoid eye contact", "distance", "avoidance"
    ];

    [Theory]
    [InlineData("Striking")]
    [InlineData("Attractive")]
    [InlineData("Average")]
    [InlineData("Plain")]
    [InlineData("Repelling")]
    public void Prose_EachBand_HasPhysicalDescriptorAndBehavioralCue(string label)
    {
        var tier = AttractivenessTierCatalog.All.First(t => t.Label == label);
        var prose = tier.Prose.ToLowerInvariant();

        var hasPhysical = PhysicalTokens.Any(tok => prose.Contains(tok, StringComparison.Ordinal));
        var hasBehavioral = BehavioralTokens.Any(tok => prose.Contains(tok, StringComparison.Ordinal));

        Assert.True(hasPhysical, $"Band '{label}' prose lacks a physical-descriptor token: {tier.Prose}");
        Assert.True(hasBehavioral, $"Band '{label}' prose lacks a behavioral-cue token: {tier.Prose}");
    }

    [Fact]
    public void Prose_AllBands_NonEmpty()
    {
        Assert.All(AttractivenessTierCatalog.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Prose)));
    }
}
