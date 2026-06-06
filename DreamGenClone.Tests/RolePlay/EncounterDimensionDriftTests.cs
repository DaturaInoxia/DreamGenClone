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

        // Exhibitionism: +0.90 × 10 = +9 → 59
        Assert.Equal(59, stats["Exhibitionism"]);
        // DiscoveryCaution: -0.60 × 10 = -6 → 44
        Assert.Equal(44, stats["DiscoveryCaution"]);
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

        // DiscoveryCaution: +0.90 × 10 = +9 → 59
        Assert.Equal(59, stats["DiscoveryCaution"]);
        // Exhibitionism: -0.60 × 10 = -6 → 44
        Assert.Equal(44, stats["Exhibitionism"]);
        // PostEncounterGuilt: +0.45 × 10 = +4.5 → Round → +4 → 54
        Assert.Equal(54, stats["PostEncounterGuilt"]);
    }

    // ── Clamp at floor (0) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDelta_ClampsAtFloor_Zero()
    {
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Exhibitionism"] = 2,
        };

        // Desire -10 → Exhibitionism: +0.90 × -10 = -9 → 2 - 9 = -7, clamped to 0
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

        // Desire +10 → Exhibitionism: +0.90 × 10 = +9 → 98 + 9 = 107, clamped to 100
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

        // Acceptance:    -1.05 × 10 = -10.5 → Math.Round(-10.5, ToEven) = -10 → 50 + (-10) = 40
        Assert.Equal(40, stats["Acceptance"]);
        // Voyeurism:     -0.75 × 10 = -7.5 → Math.Round(-7.5, ToEven) = -8 → 50 + (-8) = 42
        Assert.Equal(42, stats["Voyeurism"]);
        // Participation: -0.60 × 10 = -6.0 → Math.Round(-6.0) = -6 → 50 + (-6) = 44
        Assert.Equal(44, stats["Participation"]);
        // Encouragement: -0.75 × 10 = -7.5 → Math.Round(-7.5, ToEven) = -8 → 50 + (-8) = 42
        Assert.Equal(42, stats["Encouragement"]);
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
        // Acceptance: -1.05 × -8 = 8.4 → Math.Round(8.4) = 8 → +8 per step → 50+48 = 98
        Assert.True(stats["Acceptance"] > 50, $"Acceptance should be > 50 but was {stats["Acceptance"]}");
        // Voyeurism: -0.75 × -8 = 6.0 → Math.Round(6.0) = 6 → +6 per step → 50+36 = 86
        Assert.True(stats["Voyeurism"] > 50, $"Voyeurism should be > 50 but was {stats["Voyeurism"]}");
        Assert.Equal(98, stats["Acceptance"]);
        Assert.Equal(86, stats["Voyeurism"]);
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
