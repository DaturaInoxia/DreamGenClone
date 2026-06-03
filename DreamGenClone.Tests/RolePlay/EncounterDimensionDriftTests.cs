using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class EncounterDimensionDriftTests
{
    // ── Wife — Desire drift ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDelta_WifeDesirePlus10_DriftsExhibitionismAndDiscoveryCaution()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"]    = 50,
            ["DiscoveryCaution"] = 50,
        };

        StatToDimensionMappings.ApplyDelta(stats, "Wife", "Desire", +10);

        // Exhibitionism: +0.30 × 10 = +3 → 53
        Assert.Equal(53, stats["Exhibitionism"]);
        // DiscoveryCaution: -0.20 × 10 = -2 → 48
        Assert.Equal(48, stats["DiscoveryCaution"]);
    }

    // ── Wife — Restraint drift ──────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDelta_WifeRestraintPlus10_DriftsDiscoveryCautionExhibitionismAndPostEncounterGuilt()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["DiscoveryCaution"]   = 50,
            ["Exhibitionism"]      = 50,
            ["PostEncounterGuilt"] = 50,
        };

        StatToDimensionMappings.ApplyDelta(stats, "Wife", "Restraint", +10);

        // DiscoveryCaution: +0.30 × 10 = +3 → 53
        Assert.Equal(53, stats["DiscoveryCaution"]);
        // Exhibitionism: -0.20 × 10 = -2 → 48
        Assert.Equal(48, stats["Exhibitionism"]);
        // PostEncounterGuilt: +0.15 × 10 = +1.5 → Round → +2 → 52
        Assert.Equal(52, stats["PostEncounterGuilt"]);
    }

    // ── Clamp at floor (0) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDelta_ClampsAtFloor_Zero()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"] = 2,
        };

        // Desire -10 → Exhibitionism: +0.30 × -10 = -3 → 2 - 3 = -1, clamped to 0
        StatToDimensionMappings.ApplyDelta(stats, "Wife", "Desire", -10);

        Assert.Equal(0, stats["Exhibitionism"]);
    }

    // ── Clamp at ceiling (100) ──────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDelta_ClampsAtCeiling_100()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"] = 98,
        };

        // Desire +10 → Exhibitionism: +0.30 × 10 = +3 → 98 + 3 = 101, clamped to 100
        StatToDimensionMappings.ApplyDelta(stats, "Wife", "Desire", +10);

        Assert.Equal(100, stats["Exhibitionism"]);
    }

    // ── Zero delta is a no-op ───────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDelta_ZeroDelta_DoesNotModifyStats()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"]    = 50,
            ["DiscoveryCaution"] = 50,
        };

        StatToDimensionMappings.ApplyDelta(stats, "Wife", "Desire", 0);

        Assert.Equal(50, stats["Exhibitionism"]);
        Assert.Equal(50, stats["DiscoveryCaution"]);
    }

    // ── OtherMan has no rules ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetRules_OtherMan_ReturnsEmptyList()
    {
        var rules = StatToDimensionMappings.GetRules("OtherMan");
        Assert.Empty(rules);
    }

    [Fact]
    public void ApplyDelta_OtherMan_DoesNotModifyStats()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"] = 50,
        };

        StatToDimensionMappings.ApplyDelta(stats, "OtherMan", "Desire", +20);

        Assert.Equal(50, stats["Exhibitionism"]);
    }

    // ── Husband rules ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDelta_HusbandDominancePlus10_DriftsAcceptanceVoyeurismParticipationEncouragement()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Acceptance"]    = 50,
            ["Voyeurism"]     = 50,
            ["Participation"] = 50,
            ["Encouragement"] = 50,
        };

        StatToDimensionMappings.ApplyDelta(stats, "Husband", "Dominance", +10);

        // Acceptance:    -0.35 × 10 = -3.5 → Math.Round(-3.5, ToEven) = -4 → 50 + (-4) = 46
        Assert.Equal(46, stats["Acceptance"]);
        // Voyeurism:     -0.25 × 10 = -2.5 → Math.Round(-2.5, ToEven) = -2 → 50 + (-2) = 48
        Assert.Equal(48, stats["Voyeurism"]);
        // Participation: -0.20 × 10 = -2.0 → Math.Round(-2.0) = -2 → 50 + (-2) = 48
        Assert.Equal(48, stats["Participation"]);
        // Encouragement: -0.25 × 10 = -2.5 → Math.Round(-2.5, ToEven) = -2 → 50 + (-2) = 48
        Assert.Equal(48, stats["Encouragement"]);
    }

    // ── Husband — Dominance -8 × 6 cumulative drift ────────────────────────────────────────

    [Fact]
    public void ApplyDelta_HusbandDominanceMinus8_Six_Iterations_AcceptanceAndVoyeurismRise()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Acceptance"] = 50,
            ["Voyeurism"]  = 50,
        };

        for (var i = 0; i < 6; i++)
            StatToDimensionMappings.ApplyDelta(stats, "Husband", "Dominance", -8);

        // Per iteration Dominance=-8:
        // Acceptance: -0.35 × -8 = 2.8 → Math.Round(2.8) = 3 → +3 per step → 50+18 = 68
        Assert.True(stats["Acceptance"] > 50, $"Acceptance should be > 50 but was {stats["Acceptance"]}");
        // Voyeurism: -0.25 × -8 = 2.0 → +2 per step → 50+12 = 62
        Assert.True(stats["Voyeurism"] > 50, $"Voyeurism should be > 50 but was {stats["Voyeurism"]}");
        Assert.Equal(68, stats["Acceptance"]);
        Assert.Equal(62, stats["Voyeurism"]);
    }

    // ── Profile rebind resets RuntimeEncounterStats ────────────────────────────────────────

    [Fact]
    public void RebindEncounterProfile_ResetsRuntimeEncounterStatsToNewProfileStats()
    {
        var service = new RolePlayAdaptiveStateService(new RolePlayTestFactory.FakeThemeCatalogService());
        var state = new AdaptiveScenarioState();

        // Arrange: character with existing drift
        var profile = CharacterStatProfileV2Accessor.CreateDefault("Sarah");
        profile.RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"] = 80,
        };
        state.CharacterStats["Sarah"] = profile;

        // Act: rebind to a new profile with different EncounterStats
        var newEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"] = 30,
            ["DiscoveryCaution"] = 55,
        };
        service.RebindEncounterProfile(state, "Sarah", "profile-new", newEncounterStats, "Wife");

        // Assert: RuntimeEncounterStats now matches new profile exactly (prior drift discarded)
        Assert.NotNull(profile.RuntimeEncounterStats);
        Assert.Equal(30, profile.RuntimeEncounterStats["Exhibitionism"]);
        Assert.Equal(55, profile.RuntimeEncounterStats["DiscoveryCaution"]);
        Assert.Equal("profile-new", state.CharacterEncounterProfileIds["Sarah"]);
        Assert.Equal("Wife", state.CharacterRoles["Sarah"]);
    }

    [Fact]
    public void RebindEncounterProfile_NullEncounterStats_ClearsRuntimeEncounterStats()
    {
        var service = new RolePlayAdaptiveStateService(new RolePlayTestFactory.FakeThemeCatalogService());
        var state = new AdaptiveScenarioState();

        var profile = CharacterStatProfileV2Accessor.CreateDefault("Sarah");
        profile.RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"] = 80,
        };
        state.CharacterStats["Sarah"] = profile;

        service.RebindEncounterProfile(state, "Sarah", "profile-x", null);

        Assert.Null(profile.RuntimeEncounterStats);
    }
}
