using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class PerInteractionAffinityTests
{
    private static readonly List<ThemeCatalogEntry> CatalogEntries =
    [
        new() { Id = "intimacy", Label = "Intimacy", Keywords = ["close", "touch", "tender", "soft", "gentle", "warm"], Weight = 3, Category = "Emotional", IsEnabled = true, IsBuiltIn = true, StatAffinities = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 3, ["Connection"] = 2 } },
        new() { Id = "power-dynamics", Label = "Power Dynamics", Keywords = ["control", "command", "obey", "submit", "claim"], Weight = 4, Category = "Power", IsEnabled = true, IsBuiltIn = true, StatAffinities = new(StringComparer.OrdinalIgnoreCase) { ["Dominance"] = 3 } },
        new() { Id = "forbidden-risk", Label = "Forbidden Risk", Keywords = ["secret", "hide", "risk", "danger", "caught", "forbidden"], Weight = 4, Category = "Power", IsEnabled = true, IsBuiltIn = true },
    ];

    private sealed class FakeCatalogService : IThemeCatalogService
    {
        public Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(CatalogEntries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ThemeCatalogEntry>>(CatalogEntries);

        public Task SaveAsync(ThemeCatalogEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SeedDefaultsAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeStyleProfileService : ISteeringProfileService
    {
        private readonly Dictionary<string, SteeringProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

        public void Add(SteeringProfile profile) => _profiles[profile.Id] = profile;

        public Task<SteeringProfile> CreateAsync(string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken ct = default)
            => Task.FromResult(new SteeringProfile { Name = name });

        public Task<List<SteeringProfile>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(_profiles.Values.ToList());

        public Task<SteeringProfile?> GetAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_profiles.GetValueOrDefault(id));

        public Task<SteeringProfile?> UpdateAsync(string id, string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken ct = default)
            => Task.FromResult<SteeringProfile?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakePreferenceService : IThemePreferenceService
    {
        public Task<ThemePreference> CreateAsync(string profileId, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken ct = default)
            => Task.FromResult(new ThemePreference { ProfileId = profileId, Name = name, Tier = tier });
        public Task<List<ThemePreference>> ListAsync(CancellationToken ct = default) => Task.FromResult(new List<ThemePreference>());
        public Task<List<ThemePreference>> ListByProfileAsync(string profileId, CancellationToken ct = default) => Task.FromResult(new List<ThemePreference>());
        public Task<ThemePreference?> UpdateAsync(string id, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken ct = default) => Task.FromResult<ThemePreference?>(null);
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> AutoLinkToCatalogAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    // --- T042: ThemeAffinities multiplication ---

    [Fact]
    public async Task ThemeAffinities_MultipliesInteractionScore()
    {
        var styleService = new FakeStyleProfileService();
        styleService.Add(new SteeringProfile
        {
            Id = "style-1",
            Name = "Sultry",
            ThemeAffinities = new(StringComparer.OrdinalIgnoreCase) { ["intimacy"] = 5 } // 1.5× multiplier
        });

        var serviceWithAffinity = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), new FakePreferenceService(), styleService,
            null!, null!);
        var serviceWithout = new RolePlayAdaptiveStateService(new FakeCatalogService());

        var sessionWith = new RolePlaySession { SelectedSteeringProfileId = "style-1" };
        var sessionWithout = new RolePlaySession();

        var interaction = new RolePlayInteraction
        {
            ActorName = "Alice",
            Content = "She reached out to touch him, close and tender."
        };

        await serviceWithAffinity.UpdateFromInteractionAsync(sessionWith, interaction);
        await serviceWithout.UpdateFromInteractionAsync(sessionWithout, interaction);

        var scoreWith = sessionWith.AdaptiveState.ThemeTracker.Themes["intimacy"].Score;
        var scoreWithout = sessionWithout.AdaptiveState.ThemeTracker.Themes["intimacy"].Score;

        Assert.True(scoreWith > scoreWithout, $"With affinity ({scoreWith}) should be > without ({scoreWithout})");
    }

    // --- T043: StatAffinities deltas for acting character ---

    [Fact]
    public async Task StatAffinities_AppliedToActingCharacter()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();
        session.AdaptiveState.CurrentNarrativePhase = NarrativePhase.Committed;
        // Pre-initialize character stats
        session.AdaptiveState.CharacterStats["Alice"] = new CharacterStatBlock
        {
            CharacterId = "alice",
            Stats = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 50, ["Connection"] = 50, ["Restraint"] = 50, ["Tension"] = 50, ["Dominance"] = 50
            }
        };

        // Interaction with intimacy keywords triggers StatAffinities: Desire +3, Connection +2
        var interaction = new RolePlayInteraction
        {
            ActorName = "Alice",
            Content = "She moved close and touched his arm tenderly."
        };

        await service.UpdateFromInteractionAsync(session, interaction);

        var stats = session.AdaptiveState.CharacterStats["Alice"].Stats;
        // Desire should be higher than 50 (base keyword delta + StatAffinity delta)
        Assert.True(stats["Desire"] > 50, $"Desire should be > 50, got {stats["Desire"]}");
        Assert.True(stats["Connection"] > 50, $"Connection should be > 50, got {stats["Connection"]}");
    }

    [Fact]
    public async Task EarlyInteractions_DoNotSpikeDesireIntoSeventies()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();
        session.AdaptiveState.CharacterStats["Becky"] = new CharacterStatBlock
        {
            CharacterId = "becky",
            Stats = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 50,
                ["Connection"] = 50,
                ["Restraint"] = 50,
                ["Tension"] = 50,
                ["Dominance"] = 50
            }
        };

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "She moved close and touched his arm tenderly."
        });

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Becky",
            Content = "She stayed close, wanting more, her touch warm and careful."
        });

        Assert.InRange(session.AdaptiveState.CharacterStats["Becky"].Stats["Desire"], 50, 64);
    }

    // --- T044: HardDealBreaker skip-scoring with SuppressedHitCount ---

    [Fact]
    public async Task BlockedTheme_IncrementsSuppressedHitCount_NotScore()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();

        // Pre-block the forbidden-risk theme
        session.AdaptiveState.ThemeTracker.Themes["forbidden-risk"] = new ThemeTrackerItem
        {
            ThemeId = "forbidden-risk",
            ThemeName = "Forbidden Risk",
            Blocked = true,
            Score = 0,
            SuppressedHitCount = 0
        };

        var interaction = new RolePlayInteraction
        {
            ActorName = "Bob",
            Content = "It was a secret and dangerous risk, hidden from everyone."
        };

        await service.UpdateFromInteractionAsync(session, interaction);

        var item = session.AdaptiveState.ThemeTracker.Themes["forbidden-risk"];
        Assert.True(item.Blocked);
        Assert.Equal(0, item.Score);
        Assert.True(item.SuppressedHitCount > 0, $"SuppressedHitCount should be > 0, got {item.SuppressedHitCount}");
    }

    [Fact]
    public async Task BlockedTheme_ScoreRemainsZero_AfterMultipleInteractions()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();

        session.AdaptiveState.ThemeTracker.Themes["power-dynamics"] = new ThemeTrackerItem
        {
            ThemeId = "power-dynamics",
            ThemeName = "Power Dynamics",
            Blocked = true,
            Score = 0
        };

        for (var i = 0; i < 5; i++)
        {
            await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
            {
                ActorName = "Alice",
                Content = "She took control and commanded him to obey."
            });
        }

        var item = session.AdaptiveState.ThemeTracker.Themes["power-dynamics"];
        Assert.Equal(0, item.Score);
        Assert.Equal(5, item.SuppressedHitCount);
    }

    [Fact]
    public async Task NonActiveTheme_GainsSuppressedEvidence_WithPerTurnCap()
    {
        var styleService = new FakeStyleProfileService();
        var options = Options.Create(new StoryAnalysisOptions
        {
            SuppressedEvidenceMultiplier = 0.20,
            SuppressedEvidencePerTurnCap = 1.5
        });

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(),
            scenarioDefinitionService: null,
            scenarioSelectionEngine: null,
            narrativePhaseManager: null,
            themePreferenceService: new FakePreferenceService(),
            rpThemeService: null,
            statKeywordCategoryService: null,
            styleProfileService: styleService,
            debugEventSink: null!,
            logger: null!,
            intensityProfileService: null,
            storyAnalysisOptions: options);

        var session = new RolePlaySession();
        session.AdaptiveState.ActiveScenarioId = "intimacy";

        await service.UpdateFromInteractionAsync(session, new RolePlayInteraction
        {
            ActorName = "Alice",
            Content = "The secret risk could get them caught in danger."
        });

        var nonActive = session.AdaptiveState.ThemeTracker.Themes["forbidden-risk"];
        Assert.True(nonActive.SuppressedHitCount > 0);
        Assert.Equal(1.5, nonActive.Score, 3);
        Assert.Equal(1.5, nonActive.Breakdown.InteractionEvidenceSignal, 3);
    }
}
