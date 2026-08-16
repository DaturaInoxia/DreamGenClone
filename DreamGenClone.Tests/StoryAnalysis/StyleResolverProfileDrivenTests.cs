using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class StyleResolverProfileDrivenTests
{
    private static RolePlaySession CreateSession(string? primaryThemeId = null, string? secondaryThemeId = null, int desireStat = 50)
    {
        var session = new RolePlaySession();
        session.AdaptiveState.PrimaryThemeId = primaryThemeId;
        session.AdaptiveState.SecondaryThemeId = secondaryThemeId;
        session.AdaptiveState.CharacterStats["Alice"] = new CharacterStatProfileV2
        {
            CharacterId = "alice",
            Desire = desireStat,
            Restraint = 50,
            Dominance = 50,
            RuntimeEncounterStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Tension"] = 50, ["Connection"] = 50 }
        };
        // Add enough interactions to avoid early-session penalty
        for (var i = 0; i < 8; i++)
            session.Interactions.Add(new RolePlayInteraction { Content = "test" });
        return session;
    }

    // --- Escalation with profile EscalatingThemeIds ---

    [Fact]
    public void ProfileEscalatingThemeIds_NoEscalation_WhenThemeNotInProfile()
    {
        var session = CreateSession(primaryThemeId: "dominance"); // legacy escalating, but not in profile
        var profile = new SteeringProfile
        {
            EscalatingThemeIds = ["custom-theme"]
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, styleProfile: profile);

        Assert.DoesNotContain("theme=escalating", reason);
    }

    // --- No fallback behavior ---

    [Fact]
    public void NoProfile_NoFallbackEscalation()
    {
        var session = CreateSession(primaryThemeId: "dominance");

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12);

        Assert.DoesNotContain("theme=escalating", reason);
    }

    [Fact]
    public void NoProfile_NonLegacyTheme_NoEscalation()
    {
        var session = CreateSession(primaryThemeId: "confession");

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12);

        Assert.DoesNotContain("theme=escalating", reason);
    }

    [Fact]
    public void EmptyEscalatingThemeIds_NoEscalation()
    {
        var session = CreateSession(primaryThemeId: "forbidden-risk");
        var profile = new SteeringProfile
        {
            EscalatingThemeIds = [] // empty list
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, styleProfile: profile);

        Assert.DoesNotContain("theme=escalating", reason);
    }

    // --- MustHave +1 push ---

    [Fact]
    public void MustHave_SecondaryTheme_NoPush()
    {
        var session = CreateSession(primaryThemeId: "confession", secondaryThemeId: "intimacy");
        var preferences = new List<ThemePreference>
        {
            new() { Name = "intimacy", Tier = ThemeTier.MustHave }
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, themePreferences: preferences);

        // MustHave push only applies to primary
        Assert.DoesNotContain("musthave-push", reason);
    }

    // --- HardDealBreaker suppression ---

    // --- Combined profile + preferences ---

    [Fact]
    public void ManualPin_On_ResolvedUsesSelectedScale()
    {
        var session = CreateSession(primaryThemeId: "custom-theme", desireStat: 90);
        session.IsIntensityManuallyPinned = true;

        var (label, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(
            session,
            baseIntensityLevel: IntensityLevel.Emotional,
            adaptiveIntensityLevel: IntensityLevel.Hardcore);

        Assert.Equal("Emotional", label);
        Assert.Contains("manual-pin=on(resolved=selected)", reason);
        Assert.DoesNotContain("desire=", reason);
    }

    [Fact]
    public void ManualPin_Off_ResolvedStartsFromAdaptiveScale()
    {
        var session = CreateSession(primaryThemeId: null, desireStat: 50);
        session.IsIntensityManuallyPinned = false;

        var (label, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(
            session,
            baseIntensityLevel: IntensityLevel.Emotional,
            adaptiveIntensityLevel: IntensityLevel.Explicit);

        Assert.Equal("Erotic", label);
        Assert.Contains("selected=Emotional", reason);
        Assert.Contains("adaptive=Explicit", reason);
    }

    [Fact]
    public void ManualPin_On_ApproachingStillUsesSelectedScale()
    {
        var session = CreateSession(primaryThemeId: null, desireStat: 90);
        session.IsIntensityManuallyPinned = true;
        session.AdaptiveState.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching;

        var (label, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(
            session,
            baseIntensityLevel: IntensityLevel.Hardcore,
            adaptiveIntensityLevel: IntensityLevel.Explicit);

        Assert.Equal("Hardcore", label);
        Assert.Contains("manual-pin=on(resolved=selected)", reason);
    }
}
