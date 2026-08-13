using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Tests.StoryAnalysis;

public class SeductionArchetypeCatalogTests
{
    private static readonly string[] ExpectedIds =
    [
        "Charmer", "Competent", "Confidante", "Tease",
        "Protector", "Dominant", "Mysterious", "Situational"
    ];

    // ── Catalog shape ──────────────────────────────────────────────────────

    [Fact]
    public void All_HasExactly8Entries()
    {
        Assert.Equal(8, SeductionArchetypeCatalog.All.Count);
    }

    [Fact]
    public void All_IdsAreUniqueAndNonEmpty()
    {
        var ids = SeductionArchetypeCatalog.All.Select(a => a.Id).ToList();
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void All_DisplayNamesAreUniqueAndNonEmpty()
    {
        var names = SeductionArchetypeCatalog.All.Select(a => a.DisplayName).ToList();
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void All_ContainsAllExpectedIds()
    {
        var actualIds = SeductionArchetypeCatalog.All.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in ExpectedIds)
        {
            Assert.Contains(expected, actualIds);
        }
    }

    [Fact]
    public void All_DescriptionsAreNonEmptyAndWithinContractLength()
    {
        foreach (var archetype in SeductionArchetypeCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(archetype.Description));
            Assert.InRange(archetype.Description.Length, 50, 300);
        }
    }

    // ── Get ────────────────────────────────────────────────────────────────

    [Fact]
    public void Get_Null_ReturnsNull()
    {
        Assert.Null(SeductionArchetypeCatalog.Get(null));
    }

    [Fact]
    public void Get_Empty_ReturnsNull()
    {
        Assert.Null(SeductionArchetypeCatalog.Get(""));
    }

    [Fact]
    public void Get_Unknown_ReturnsNull()
    {
        Assert.Null(SeductionArchetypeCatalog.Get("nonexistent"));
    }

    [Theory]
    [InlineData("Charmer")]
    [InlineData("Competent")]
    [InlineData("Confidante")]
    [InlineData("Tease")]
    [InlineData("Protector")]
    [InlineData("Dominant")]
    [InlineData("Mysterious")]
    [InlineData("Situational")]
    public void Get_KnownId_ReturnsEntry(string id)
    {
        var archetype = SeductionArchetypeCatalog.Get(id);
        Assert.NotNull(archetype);
        Assert.Equal(id, archetype.Id, ignoreCase: true);
    }

    [Fact]
    public void Get_CaseInsensitive()
    {
        var lower = SeductionArchetypeCatalog.Get("charmer");
        var upper = SeductionArchetypeCatalog.Get("CHARMER");
        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.Equal(lower.Id, upper.Id, ignoreCase: true);
    }

    // ── BuildGuidance ──────────────────────────────────────────────────────

    [Fact]
    public void BuildGuidance_Null_ReturnsNull()
    {
        Assert.Null(SeductionArchetypeCatalog.BuildGuidance(null));
    }

    [Fact]
    public void BuildGuidance_Empty_ReturnsNull()
    {
        Assert.Null(SeductionArchetypeCatalog.BuildGuidance([]));
    }

    [Fact]
    public void BuildGuidance_UnknownIds_ReturnsNull()
    {
        Assert.Null(SeductionArchetypeCatalog.BuildGuidance(["nonexistent", "also-missing"]));
    }

    [Fact]
    public void BuildGuidance_SingleKnown_ReturnsCombinedText()
    {
        var result = SeductionArchetypeCatalog.BuildGuidance(["Charmer"]);
        Assert.NotNull(result);
        Assert.Contains("The Charmer / Smooth Talker", result);
        Assert.Contains("calibrated compliments", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildGuidance_MultipleKnown_JoinsWithSpace()
    {
        var result = SeductionArchetypeCatalog.BuildGuidance(["Competent", "Confidante"]);
        Assert.NotNull(result);
        Assert.Contains("The Competent / Capable Man", result);
        Assert.Contains("The Confidante / Emotional Connection", result);
        // Both display names present = both archetypes joined.
    }

    [Fact]
    public void BuildGuidance_OutputIsDeterministic()
    {
        var a = SeductionArchetypeCatalog.BuildGuidance(["Tease", "Dominant"]);
        var b = SeductionArchetypeCatalog.BuildGuidance(["Tease", "Dominant"]);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a, b);
    }

    [Fact]
    public void BuildGuidance_NoLeadingTrailingNewlines()
    {
        var result = SeductionArchetypeCatalog.BuildGuidance(["Mysterious", "Protector"]);
        Assert.NotNull(result);
        Assert.False(result.StartsWith("\n"));
        Assert.False(result.EndsWith("\n"));
        Assert.False(result.StartsWith(" "));
        Assert.False(result.EndsWith(" "));
    }

    [Fact]
    public void BuildGuidance_SkipsUnknownButKeepsKnown()
    {
        var result = SeductionArchetypeCatalog.BuildGuidance(["Charmer", "nonexistent"]);
        Assert.NotNull(result);
        Assert.Contains("The Charmer / Smooth Talker", result);
        Assert.DoesNotContain("nonexistent", result);
    }

    // ── B-078 follow-up: semantic event ids + descriptions ─────────────────

    [Fact]
    public void ToEventId_Charmer_ReturnsOthermanCharmer()
    {
        Assert.Equal("otherman-charmer", SeductionArchetypeCatalog.ToEventId("Charmer"));
    }

    [Fact]
    public void ToEventId_Unknown_ReturnsNull()
    {
        Assert.Null(SeductionArchetypeCatalog.ToEventId("nonexistent"));
        Assert.Null(SeductionArchetypeCatalog.ToEventId(null));
    }

    [Theory]
    [InlineData("otherman-charmer")]
    [InlineData("otherman-competent")]
    [InlineData("otherman-confidante")]
    [InlineData("otherman-tease")]
    [InlineData("otherman-protector")]
    [InlineData("otherman-dominant")]
    [InlineData("otherman-mysterious")]
    [InlineData("otherman-situational")]
    public void IsOtherManSeductionEvent_KnownEvent_True(string eventId)
    {
        Assert.True(SeductionArchetypeCatalog.IsOtherManSeductionEvent(eventId));
    }

    [Theory]
    [InlineData("otherman-nonexistent")]
    [InlineData("emotional-surrender")]
    [InlineData("desire-spoken")]
    [InlineData("")]
    [InlineData(null)]
    public void IsOtherManSeductionEvent_UnknownOrNonTropeEvent_False(string? eventId)
    {
        Assert.False(SeductionArchetypeCatalog.IsOtherManSeductionEvent(eventId));
    }

    [Fact]
    public void IsOtherManSeductionEvent_CaseInsensitive()
    {
        Assert.True(SeductionArchetypeCatalog.IsOtherManSeductionEvent("OTHERMAN-CHARMER"));
    }

    [Fact]
    public void BuildSemanticEventDescriptions_HasExactly8Entries()
    {
        var descriptions = SeductionArchetypeCatalog.BuildSemanticEventDescriptions();
        Assert.Equal(8, descriptions.Count);
        Assert.All(SeductionArchetypeCatalog.All, a => Assert.Contains(descriptions.Keys, k => string.Equals(k, $"otherman-{a.Id.ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void BuildSemanticEventDescriptions_EachDescriptionMentionsWifeTarget()
    {
        var descriptions = SeductionArchetypeCatalog.BuildSemanticEventDescriptions();
        Assert.All(descriptions.Values, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d));
            Assert.Contains("Wife", d, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void BuildSemanticEventDescriptions_EveryKeyIsAnOtherManSeductionEvent()
    {
        var descriptions = SeductionArchetypeCatalog.BuildSemanticEventDescriptions();
        Assert.All(descriptions.Keys, k => Assert.True(SeductionArchetypeCatalog.IsOtherManSeductionEvent(k)));
    }
}
