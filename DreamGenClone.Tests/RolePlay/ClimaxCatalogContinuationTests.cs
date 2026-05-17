using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Tests for FinishingMoveBandMatcher (FR-010: single reusable band-matching helper)
/// and the climax catalog continuation injection behaviour.
/// </summary>
public sealed class ClimaxCatalogContinuationTests
{
    // ── FinishingMoveBandMatcher.MatchesBandEligibility ──────────────────────

    [Fact]
    public void MatchesBandEligibility_NullBands_ReturnsTrue()
    {
        Assert.True(FinishingMoveBandMatcher.MatchesBandEligibility(null, 50));
    }

    [Fact]
    public void MatchesBandEligibility_EmptyBands_ReturnsTrue()
    {
        Assert.True(FinishingMoveBandMatcher.MatchesBandEligibility("", 50));
    }

    [Theory]
    [InlineData("0-29",  0,   true)]
    [InlineData("0-29",  29,  true)]
    [InlineData("0-29",  30,  false)]
    [InlineData("30-59", 30,  true)]
    [InlineData("30-59", 59,  true)]
    [InlineData("30-59", 60,  false)]
    [InlineData("60-100", 60, true)]
    [InlineData("60-100", 100, true)]
    [InlineData("60-100", 59, false)]
    public void MatchesBandEligibility_ThreeTierBands_CorrectBoundaries(string band, double value, bool expected)
    {
        Assert.Equal(expected, FinishingMoveBandMatcher.MatchesBandEligibility(band, value));
    }

    [Fact]
    public void MatchesBandEligibility_CommaSeparatedBands_MatchesAny()
    {
        // value=15 falls in 0-29 but not 60-100
        Assert.True(FinishingMoveBandMatcher.MatchesBandEligibility("0-29, 60-100", 15));
        // value=50 falls in neither
        Assert.False(FinishingMoveBandMatcher.MatchesBandEligibility("0-29, 60-100", 50));
    }

    // ── FinishingMoveBandMatcher.MatchesNumericBand ───────────────────────────

    [Theory]
    [InlineData("any",  55, true)]
    [InlineData("ALL",  55, true)]
    [InlineData("*",    55, true)]
    [InlineData("low",  10, true)]
    [InlineData("low",  50, false)]
    [InlineData("medium", 50, true)]
    [InlineData("medium", 10, false)]
    [InlineData("high", 80, true)]
    [InlineData("high", 50, false)]
    [InlineData(">=60", 60, true)]
    [InlineData(">=60", 59, false)]
    [InlineData(">60",  61, true)]
    [InlineData(">60",  60, false)]
    [InlineData("<=40", 40, true)]
    [InlineData("<=40", 41, false)]
    [InlineData("<40",  39, true)]
    [InlineData("<40",  40, false)]
    [InlineData("50+",  50, true)]
    [InlineData("50+",  49, false)]
    [InlineData("50",   50, true)]
    [InlineData("50",   51, false)]
    public void MatchesNumericBand_Variants_CorrectResults(string band, double value, bool expected)
    {
        Assert.Equal(expected, FinishingMoveBandMatcher.MatchesNumericBand(band, value));
    }

    [Fact]
    public void MatchesNumericBand_PipeAlternatives_MatchesEither()
    {
        Assert.True(FinishingMoveBandMatcher.MatchesNumericBand("0-29|60-100", 20));
        Assert.True(FinishingMoveBandMatcher.MatchesNumericBand("0-29|60-100", 70));
        Assert.False(FinishingMoveBandMatcher.MatchesNumericBand("0-29|60-100", 50));
    }

    // ── FinishingMoveBandMatcher.TryMatchNamedBand ────────────────────────────

    [Theory]
    [InlineData("low",    0,   true,  true)]
    [InlineData("low",    33,  true,  true)]
    [InlineData("low",    34,  true,  false)]
    [InlineData("medium", 34,  true,  true)]
    [InlineData("medium", 66,  true,  true)]
    [InlineData("medium", 67,  true,  false)]
    [InlineData("high",   67,  true,  true)]
    [InlineData("high",   100, true,  true)]
    [InlineData("other",  50,  false, false)]
    public void TryMatchNamedBand_KnownAndUnknown(string name, double value, bool expectedReturn, bool expectedMatch)
    {
        var returned = FinishingMoveBandMatcher.TryMatchNamedBand(name, value, out var matches);
        Assert.Equal(expectedReturn, returned);
        Assert.Equal(expectedMatch, matches);
    }

    // ── FinishingMoveBandMatcher.TryParseRangeBand ────────────────────────────

    [Theory]
    [InlineData("30-59",    30, 59)]
    [InlineData("30 to 59", 30, 59)]
    [InlineData("30..59",   30, 59)]
    [InlineData("30~59",    30, 59)]
    [InlineData("59-30",    30, 59)]  // reversed pair — normalised to min,max
    public void TryParseRangeBand_ValidFormats_ParsedAndNormalised(string band, double expectedMin, double expectedMax)
    {
        Assert.True(FinishingMoveBandMatcher.TryParseRangeBand(band, out var min, out var max));
        Assert.Equal(expectedMin, min);
        Assert.Equal(expectedMax, max);
    }

    [Fact]
    public void TryParseRangeBand_NonRange_ReturnsFalse()
    {
        Assert.False(FinishingMoveBandMatcher.TryParseRangeBand("high", out _, out _));
    }

    // ── Catalog filter logic (eligibility filtering applied to domain models) ─

    [Fact]
    public void ReceptivityLevel_EligibilityFilter_ExcludesOutOfBandEntry()
    {
        var levels = new[]
        {
            new RPFinishReceptivityLevel { Id = "1", Name = "Eager",    EligibleDesireBands = "60-100", EligibleSelfRespectBands = string.Empty },
            new RPFinishReceptivityLevel { Id = "2", Name = "Enduring", EligibleDesireBands = "0-29",   EligibleSelfRespectBands = string.Empty },
        };

        // desire=70 → matches "60-100" (Eager) but not "0-29" (Enduring)
        var eligible = levels
            .Where(r => FinishingMoveBandMatcher.MatchesBandEligibility(r.EligibleDesireBands, 70)
                     && FinishingMoveBandMatcher.MatchesBandEligibility(r.EligibleSelfRespectBands, 50))
            .Select(r => r.Name)
            .ToList();

        Assert.Single(eligible);
        Assert.Contains("Eager", eligible);
    }

    [Fact]
    public void HisControlLevel_EligibilityFilter_NullBandsAlwaysEligible()
    {
        var levels = new[]
        {
            new RPFinishHisControlLevel { Id = "1", Name = "Dominant",   EligibleOtherManDominanceBands = string.Empty },
            new RPFinishHisControlLevel { Id = "2", Name = "Submissive", EligibleOtherManDominanceBands = "0-29" },
        };

        // otherManDominance=75 → null=any (Dominant eligible), "0-29" = not eligible (Submissive excluded)
        var eligible = levels
            .Where(hc => FinishingMoveBandMatcher.MatchesBandEligibility(hc.EligibleOtherManDominanceBands, 75))
            .Select(hc => hc.Name)
            .ToList();

        Assert.Single(eligible);
        Assert.Contains("Dominant", eligible);
    }

    [Fact]
    public void EligibilityFilter_NoMatchingEntries_ReturnsEmptyList()
    {
        var levels = new[]
        {
            new RPFinishReceptivityLevel { Id = "1", Name = "Eager", EligibleDesireBands = "60-100", EligibleSelfRespectBands = "60-100" },
        };

        // desire=20, selfRespect=20 → no entries match
        var eligible = levels
            .Where(r => FinishingMoveBandMatcher.MatchesBandEligibility(r.EligibleDesireBands, 20)
                     && FinishingMoveBandMatcher.MatchesBandEligibility(r.EligibleSelfRespectBands, 20))
            .ToList();

        Assert.Empty(eligible);
    }
}
