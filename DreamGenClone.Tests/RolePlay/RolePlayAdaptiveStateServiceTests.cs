using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using static DreamGenClone.Tests.RolePlay.RolePlayTestFactory;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayAdaptiveStateServiceTests
{
    [Fact]
    public async Task UpdateFromInteractionAsync_InitializesCharacterStatsAndThemes()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession { PersonaName = "Ken" };
        var interaction = new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "I watch from the shadows and feel a dangerous thrill and desire."
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.True(state.CharacterStats.ContainsKey("Becky"));
        Assert.NotEmpty(state.CharacterStats["Becky"].Stats);
        Assert.Equal(10, state.ThemeTracker.Themes.Count);
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.PrimaryThemeId));
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.SecondaryThemeId));
        Assert.Equal("Top2Blend", state.ThemeTracker.ThemeSelectionRule);
        Assert.NotEmpty(state.ThemeTracker.RecentEvidence);
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_ClampsStatValues()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        // High repetition should not push deltas beyond clamp rules.
        var interaction = new RolePlayInteraction
        {
            ActorName = "Dean",
            Content = string.Join(' ', Enumerable.Repeat("control command claim obey desire heat thrill risk", 30))
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);
        var stats = state.CharacterStats["Dean"].Stats;

        Assert.All(stats.Values, value => Assert.InRange(value, 0, 100));
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_SelectsTop2Blend_WhenTopThemesAreClose()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        var interaction = new RolePlayInteraction
        {
            ActorName = "Alex",
            Content = "I want control and command, but there is danger, risk, secret heat and control again."
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.Equal("Top2Blend", state.ThemeTracker.ThemeSelectionRule);
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.PrimaryThemeId));
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.SecondaryThemeId));
    }

    [Fact]
    public async Task UpdateFromInteractionAsync_DoesNotTrackNarrativeAsCharacterStats()
    {
        var service = new RolePlayAdaptiveStateService(new FakeThemeCatalogService());
        var session = new RolePlaySession();

        var interaction = new RolePlayInteraction
        {
            ActorName = "Narrative",
            InteractionType = InteractionType.System,
            Content = "The scene grows warmer and closer with trust and comfort."
        };

        var state = await service.UpdateFromInteractionAsync(session, interaction);

        Assert.False(state.CharacterStats.ContainsKey("Narrative"));
        Assert.Equal("Top2Blend", state.ThemeTracker.ThemeSelectionRule);
        Assert.False(string.IsNullOrWhiteSpace(state.ThemeTracker.SecondaryThemeId));
    }
}