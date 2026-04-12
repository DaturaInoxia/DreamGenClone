using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class StyleResolverProfileDrivenTests
{
    private static RolePlaySession CreateSession(string? primaryThemeId = null, string? secondaryThemeId = null, int desireStat = 50)
    {
        var session = new RolePlaySession();
        session.AdaptiveState.ThemeTracker.PrimaryThemeId = primaryThemeId;
        session.AdaptiveState.ThemeTracker.SecondaryThemeId = secondaryThemeId;
        session.AdaptiveState.CharacterStats["Alice"] = new CharacterStatBlock
        {
            CharacterId = "alice",
            Stats = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = desireStat, ["Restraint"] = 50, ["Tension"] = 50, ["Connection"] = 50, ["Dominance"] = 50 }
        };
        // Add enough interactions to avoid early-session penalty
        for (var i = 0; i < 8; i++)
            session.Interactions.Add(new RolePlayInteraction { Content = "test" });
        return session;
    }

    // --- Escalation with profile EscalatingThemeIds ---

    [Fact]
    public void ProfileEscalatingThemeIds_Escalates_WhenPrimaryMatches()
    {
        var session = CreateSession(primaryThemeId: "custom-theme");
        var profile = new SteeringProfile
        {
            EscalatingThemeIds = ["custom-theme", "another-theme"]
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, styleProfile: profile);

        Assert.Contains("theme=escalating(+1)", reason);
    }

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

    // --- Fallback to legacy hardcoded list ---

    [Fact]
    public void NoProfile_FallsBackToLegacyEscalatingThemes()
    {
        var session = CreateSession(primaryThemeId: "dominance");

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12);

        Assert.Contains("theme=escalating(+1)", reason);
    }

    [Fact]
    public void NoProfile_NonLegacyTheme_NoEscalation()
    {
        var session = CreateSession(primaryThemeId: "confession");

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12);

        Assert.DoesNotContain("theme=escalating", reason);
    }

    [Fact]
    public void EmptyEscalatingThemeIds_FallsBackToLegacy()
    {
        var session = CreateSession(primaryThemeId: "forbidden-risk");
        var profile = new SteeringProfile
        {
            EscalatingThemeIds = [] // empty list
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, styleProfile: profile);

        // Empty list means fallback to legacy
        Assert.Contains("theme=escalating(+1)", reason);
    }

    // --- MustHave +1 push ---

    [Fact]
    public void MustHave_PrimaryTheme_AddsPlusOnePush()
    {
        var session = CreateSession(primaryThemeId: "intimacy");
        var preferences = new List<ThemePreference>
        {
            new() { Name = "intimacy", Tier = ThemeTier.MustHave }
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, themePreferences: preferences);

        Assert.Contains("musthave-push(+1)", reason);
    }

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

    [Fact]
    public void HardDealBreaker_PrimaryTheme_SuppressesEscalation()
    {
        var session = CreateSession(primaryThemeId: "dominance"); // would normally escalate
        var preferences = new List<ThemePreference>
        {
            new() { Name = "dominance", Tier = ThemeTier.HardDealBreaker }
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, themePreferences: preferences);

        Assert.Contains("dealbreaker-suppressed", reason);
        Assert.DoesNotContain("theme=escalating", reason);
        Assert.DoesNotContain("musthave-push", reason);
    }

    [Fact]
    public void HardDealBreaker_SecondaryTheme_SuppressesEscalation()
    {
        var session = CreateSession(primaryThemeId: "confession", secondaryThemeId: "power-dynamics");
        var preferences = new List<ThemePreference>
        {
            new() { Name = "power-dynamics", Tier = ThemeTier.HardDealBreaker }
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, themePreferences: preferences);

        Assert.Contains("dealbreaker-suppressed", reason);
    }

    [Fact]
    public void HardDealBreaker_AlsoSuppressesMustHavePush()
    {
        var session = CreateSession(primaryThemeId: "dominance");
        var preferences = new List<ThemePreference>
        {
            new() { Name = "dominance", Tier = ThemeTier.HardDealBreaker },
            new() { Name = "dominance", Tier = ThemeTier.MustHave } // contradictory, but HardDealBreaker wins
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, themePreferences: preferences);

        Assert.Contains("dealbreaker-suppressed", reason);
        Assert.DoesNotContain("musthave-push", reason);
    }

    // --- Combined profile + preferences ---

    [Fact]
    public void ProfileEscalation_CombinedWithMustHavePush()
    {
        var session = CreateSession(primaryThemeId: "custom-theme");
        var profile = new SteeringProfile
        {
            EscalatingThemeIds = ["custom-theme"]
        };
        var preferences = new List<ThemePreference>
        {
            new() { Name = "custom-theme", Tier = ThemeTier.MustHave }
        };

        var (_, reason) = RolePlayStyleResolver.ResolveEffectiveStyle(session, IntensityLevel.SuggestivePg12, profile, preferences);

        Assert.Contains("theme=escalating(+1)", reason);
        Assert.Contains("musthave-push(+1)", reason);
    }
}
