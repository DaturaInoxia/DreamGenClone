using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.StoryAnalysis;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class PromptDealbreakerServiceTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsViolation_WhenHardDealbreakerThemeMatches()
    {
        var service = new PromptDealbreakerService(new FakeThemePreferenceService(new List<ThemePreference>
        {
            new()
            {
                Id = "t1",
                ProfileId = "p1",
                Name = "Violence",
                Description = "graphic violence",
                Tier = ThemeTier.HardDealBreaker
            }
        }));

        var result = await service.ValidateAsync("The scene turns to graphic violence.", "p1");

        Assert.False(result.IsAllowed);
        Assert.Contains("Violence", result.ViolatedThemes);
    }

    [Fact]
    public async Task ValidateAsync_AllowsPrompt_WhenNoHardDealbreakerMatches()
    {
        var service = new PromptDealbreakerService(new FakeThemePreferenceService(new List<ThemePreference>
        {
            new()
            {
                Id = "t1",
                ProfileId = "p1",
                Name = "Violence",
                Description = "graphic violence",
                Tier = ThemeTier.HardDealBreaker
            }
        }));

        var result = await service.ValidateAsync("The scene builds emotional tension.", "p1");

        Assert.True(result.IsAllowed);
        Assert.Empty(result.ViolatedThemes);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotTrigger_OnSingleGenericTokenOverlap()
    {
        var service = new PromptDealbreakerService(new FakeThemePreferenceService(new List<ThemePreference>
        {
            new()
            {
                Id = "t1",
                ProfileId = "p1",
                Name = "Gay",
                Description = "same sex relationship between men",
                Tier = ThemeTier.HardDealBreaker
            }
        }));

        var result = await service.ValidateAsync("Continue the same plot and narrative style.", "p1");

        Assert.True(result.IsAllowed);
        Assert.Empty(result.ViolatedThemes);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotTrigger_ForGenericRelationshipPrompt_WhenThemeIsGay()
    {
        var service = new PromptDealbreakerService(new FakeThemePreferenceService(new List<ThemePreference>
        {
            new()
            {
                Id = "t1",
                ProfileId = "p1",
                Name = "Gay",
                Description = "same sex relationship between men",
                Tier = ThemeTier.HardDealBreaker
            }
        }));

        var result = await service.ValidateAsync("Describe the tension between two married women and the men around them.", "p1");

        Assert.True(result.IsAllowed);
        Assert.Empty(result.ViolatedThemes);
    }

    [Fact]
    public async Task ValidateAsync_Triggers_WhenExplicitDealbreakerPhraseIsPresent()
    {
        var service = new PromptDealbreakerService(new FakeThemePreferenceService(new List<ThemePreference>
        {
            new()
            {
                Id = "t1",
                ProfileId = "p1",
                Name = "Gay",
                Description = "same sex relationship between men",
                Tier = ThemeTier.HardDealBreaker
            }
        }));

        var result = await service.ValidateAsync("The prompt asks for a same sex relationship between men.", "p1");

        Assert.False(result.IsAllowed);
        Assert.Contains("Gay", result.ViolatedThemes);
    }

    private sealed class FakeThemePreferenceService : IThemePreferenceService
    {
        private readonly List<ThemePreference> _themes;

        public FakeThemePreferenceService(List<ThemePreference> themes)
        {
            _themes = themes;
        }

        public Task<ThemePreference> CreateAsync(string profileId, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<List<ThemePreference>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_themes);

        public Task<List<ThemePreference>> ListByProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(_themes.Where(x => x.ProfileId == profileId).ToList());

        public Task<ThemePreference?> UpdateAsync(string id, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<int> AutoLinkToCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}