using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-034: Option A "Wife Willingness to Cheat" calculator tests — the unified score,
/// the cross-character snapshot helper, verdict band mapping, and the explicitness ceiling.
/// </summary>
public sealed class WifeWillingnessCalculatorTests
{
    // ── Direct formula ───────────────────────────────────────────────────────

    [Fact]
    public void ComputeWillingnessToCheat_NeutralInputs_NoMaritalDeficit_Returns50()
    {
        // Fully attentive husband (100) → marital deficit term is 0; all other inputs
        // neutral (equal drive/behavior) → score stays at the 50 baseline.
        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(
            desire: 50, loyalty: 50,
            seductionReceptivity: 50, boundaryFirmness: 50,
            attentiveness: 100, intimacyAvailability: 100);

        Assert.Equal(50, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_AllFiftyInputs_MaritalDeficitAddsTwentyFive()
    {
        // At att=intim=50 the deficit term is ((100-50)+(100-50))*0.25 = 25, so the
        // baseline rises to 75 — documents the formula's actual behavior.
        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(
            desire: 50, loyalty: 50,
            seductionReceptivity: 50, boundaryFirmness: 50,
            attentiveness: 50, intimacyAvailability: 50);

        Assert.Equal(75, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_DesireExceedsLoyalty_RaisesScore()
    {
        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(
            desire: 80, loyalty: 20,
            seductionReceptivity: 50, boundaryFirmness: 50,
            attentiveness: 100, intimacyAvailability: 100);

        // 50 + (80-20)*0.5 = 80
        Assert.Equal(80, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_SeductionReceptivityExceedsBoundaryFirmness_RaisesScore()
    {
        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(
            desire: 50, loyalty: 50,
            seductionReceptivity: 80, boundaryFirmness: 20,
            attentiveness: 100, intimacyAvailability: 100);

        // 50 + (80-20)*0.5 = 80
        Assert.Equal(80, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_MaritalNeglect_RaisesScore()
    {
        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(
            desire: 50, loyalty: 50,
            seductionReceptivity: 50, boundaryFirmness: 50,
            attentiveness: 20, intimacyAvailability: 20);

        // 50 + ((100-20)+(100-20))*0.25 = 50 + 40 = 90
        Assert.Equal(90, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_ClampsAtUpperBound()
    {
        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(
            desire: 100, loyalty: 0,
            seductionReceptivity: 100, boundaryFirmness: 0,
            attentiveness: 0, intimacyAvailability: 0);

        Assert.Equal(100, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_ClampsAtLowerBound()
    {
        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(
            desire: 0, loyalty: 100,
            seductionReceptivity: 0, boundaryFirmness: 100,
            attentiveness: 100, intimacyAvailability: 100);

        Assert.Equal(0, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_RespectsCustomWeights()
    {
        // With DesireLoyaltyWeight=1.0, the full difference applies.
        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(
            desire: 80, loyalty: 20,
            seductionReceptivity: 50, boundaryFirmness: 50,
            attentiveness: 50, intimacyAvailability: 50,
            desireLoyaltyWeight: 1.0, behaviorWeight: 0.0, maritalDeficitWeight: 0.0);

        // 50 + (80-20)*1.0 = 110 → clamp 100
        Assert.Equal(100, score);
    }

    // ── Cross-character snapshot helper ─────────────────────────────────────

    [Fact]
    public void ComputeWillingnessToCheat_FromSnapshots_ResolvesWifeAndHusband()
    {
        var snapshots = new Dictionary<string, CharacterStatProfileV2>(StringComparer.OrdinalIgnoreCase)
        {
            ["char-wife"] = new()
            {
                CharacterId = "char-wife",
                CharacterRole = "Wife",
                Desire = 80,
                Loyalty = 20,
                RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SeductionReceptivity"] = 70,
                    ["BoundaryFirmness"] = 30
                }
            },
            ["char-husband"] = new()
            {
                CharacterId = "char-husband",
                CharacterRole = "Husband",
                RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Attentiveness"] = 40,
                    ["IntimacyAvailability"] = 40
                }
            }
        };

        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(snapshots);

        // 50 + (80-20)*0.5 + (70-30)*0.5 + ((100-40)+(100-40))*0.25
        // = 50 + 30 + 20 + 30 = 130 → clamp 100
        Assert.Equal(100, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_FromSnapshots_MissingHusband_DefaultsTo50()
    {
        var snapshots = new Dictionary<string, CharacterStatProfileV2>(StringComparer.OrdinalIgnoreCase)
        {
            ["char-wife"] = new()
            {
                CharacterId = "char-wife",
                CharacterRole = "Wife",
                Desire = 60,
                Loyalty = 50,
                RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SeductionReceptivity"] = 50,
                    ["BoundaryFirmness"] = 50
                }
            }
        };

        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(snapshots);

        // No Husband → Attentiveness/IntimacyAvailability default 50 (neutral → deficit term = 25).
        // 50 + (60-50)*0.5 + 0 + ((100-50)+(100-50))*0.25 = 50 + 5 + 25 = 80
        Assert.Equal(80, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_FromSnapshots_MissingWife_ReturnsNeutral50()
    {
        var snapshots = new Dictionary<string, CharacterStatProfileV2>(StringComparer.OrdinalIgnoreCase)
        {
            ["char-husband"] = new()
            {
                CharacterId = "char-husband",
                CharacterRole = "Husband",
                RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Attentiveness"] = 20,
                    ["IntimacyAvailability"] = 20
                }
            }
        };

        var score = WifeWillingnessCalculator.ComputeWillingnessToCheat(snapshots);

        // No Wife → all Wife inputs default 50; Husband stats are ignored (no Wife to compute for).
        Assert.Equal(50, score);
    }

    [Fact]
    public void ComputeWillingnessToCheat_FromSnapshots_NullOrEmpty_ReturnsNeutral50()
    {
        Assert.Equal(50, WifeWillingnessCalculator.ComputeWillingnessToCheat(null));
        Assert.Equal(50, WifeWillingnessCalculator.ComputeWillingnessToCheat(
            new Dictionary<string, CharacterStatProfileV2>()));
    }

    // ── Verdict mapping ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "NO")]
    [InlineData(40, "NO")]
    [InlineData(41, "MAYBE")]
    [InlineData(70, "MAYBE")]
    [InlineData(71, "YES")]
    [InlineData(100, "YES")]
    public void ResolveVerdict_DefaultBands_MapsCorrectly(int willingness, string expected)
    {
        Assert.Equal(expected, WifeWillingnessCalculator.ResolveVerdict(willingness));
    }

    [Fact]
    public void ResolveVerdict_CustomBands_Respected()
    {
        Assert.Equal("NO", WifeWillingnessCalculator.ResolveVerdict(25, noMax: 30, maybeMax: 60));
        Assert.Equal("MAYBE", WifeWillingnessCalculator.ResolveVerdict(50, noMax: 30, maybeMax: 60));
        Assert.Equal("YES", WifeWillingnessCalculator.ResolveVerdict(75, noMax: 30, maybeMax: 60));
    }

    // ── Ceiling ─────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeCeiling_IsMinOfDesireAndWillingness()
    {
        // Low desire bounds a high willingness score.
        Assert.Equal(30, WifeWillingnessCalculator.ComputeCeiling(willingness: 80, desire: 30));
        // Low willingness bounds a high desire score.
        Assert.Equal(20, WifeWillingnessCalculator.ComputeCeiling(willingness: 20, desire: 80));
        // Equal inputs pass through.
        Assert.Equal(50, WifeWillingnessCalculator.ComputeCeiling(willingness: 50, desire: 50));
    }

    [Fact]
    public void ComputeCeiling_ClampsToZeroToHundred()
    {
        Assert.Equal(0, WifeWillingnessCalculator.ComputeCeiling(willingness: 10, desire: -5));
        Assert.Equal(100, WifeWillingnessCalculator.ComputeCeiling(willingness: 120, desire: 110));
    }
}
