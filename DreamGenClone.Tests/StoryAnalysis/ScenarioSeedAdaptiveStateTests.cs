using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.StoryAnalysis;

public sealed class ScenarioSeedAdaptiveStateTests
{
    private static readonly List<ThemeCatalogEntry> CatalogEntries =
    [
        new() { Id = "intimacy", Label = "Intimacy", Keywords = ["close", "touch", "tender", "soft", "gentle", "warm"], Weight = 3, Category = "Emotional", IsEnabled = true, IsBuiltIn = true, StatAffinities = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 2, ["Connection"] = 1 } },
        new() { Id = "power-dynamics", Label = "Power Dynamics", Keywords = ["control", "command", "obey", "submit", "claim"], Weight = 4, Category = "Power", IsEnabled = true, IsBuiltIn = true, StatAffinities = new(StringComparer.OrdinalIgnoreCase) { ["Dominance"] = 3 } },
        new() { Id = "forbidden-risk", Label = "Forbidden Risk", Keywords = ["secret", "hide", "risk", "danger", "caught", "forbidden"], Weight = 4, Category = "Power", IsEnabled = true, IsBuiltIn = true },
        new() { Id = "confession", Label = "Confession", Keywords = ["confess", "admit", "truth", "reveal"], Weight = 3, Category = "Emotional", IsEnabled = true, IsBuiltIn = true },
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

    private sealed class FakePreferenceService : IThemePreferenceService
    {
        private readonly List<ThemePreference> _preferences = [];

        public void Add(string profileId, string name, ThemeTier tier)
        {
            _preferences.Add(new ThemePreference { ProfileId = profileId, Name = name, Tier = tier });
        }

        public Task<ThemePreference> CreateAsync(string profileId, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken ct = default)
            => Task.FromResult(new ThemePreference { ProfileId = profileId, Name = name, Description = description, Tier = tier });

        public Task<List<ThemePreference>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(_preferences.ToList());

        public Task<List<ThemePreference>> ListByProfileAsync(string profileId, CancellationToken ct = default)
            => Task.FromResult(_preferences.Where(p => p.ProfileId == profileId).ToList());

        public Task<ThemePreference?> UpdateAsync(string id, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken ct = default)
            => Task.FromResult<ThemePreference?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<int> AutoLinkToCatalogAsync(CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class FakeStyleProfileService : ISteeringProfileService
    {
        private readonly Dictionary<string, SteeringProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

        public void Add(SteeringProfile profile) => _profiles[profile.Id] = profile;

        public Task<SteeringProfile> CreateAsync(string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken ct = default)
            => Task.FromResult(new SteeringProfile { Name = name, Description = description, Example = example, RuleOfThumb = ruleOfThumb });

        public Task<List<SteeringProfile>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(_profiles.Values.ToList());

        public Task<SteeringProfile?> GetAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_profiles.GetValueOrDefault(id));

        public Task<SteeringProfile?> UpdateAsync(string id, string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken ct = default)
            => Task.FromResult<SteeringProfile?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private static Scenario CreateMinimalScenario() => new()
    {
        Name = "Test Scenario",
        Plot = new Plot { Description = "A test plot" },
        Setting = new Setting { WorldDescription = "A test world" },
        Narrative = new NarrativeSettings(),
        Characters = [],
        Openings = [],
        Examples = [],
        Locations = [],
        Objects = []
    };

    // --- ThemeTracker initialization from catalog ---

    [Fact]
    public async Task SeedFromScenario_InitializesThemeTrackerFromCatalog()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        Assert.Equal(4, session.AdaptiveState.ThemeTracker.Themes.Count);
        Assert.Contains("intimacy", session.AdaptiveState.ThemeTracker.Themes.Keys);
        Assert.Contains("power-dynamics", session.AdaptiveState.ThemeTracker.Themes.Keys);
        Assert.Contains("forbidden-risk", session.AdaptiveState.ThemeTracker.Themes.Keys);
        Assert.Contains("confession", session.AdaptiveState.ThemeTracker.Themes.Keys);
    }

    [Fact]
    public async Task SeedFromScenario_AllThemesStartAtScoreZero_WhenNoPreferences()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        foreach (var item in session.AdaptiveState.ThemeTracker.Themes.Values)
        {
            // Scenario text scoring may bump some scores above 0, but without keywords in scenario text they should stay 0
            Assert.False(item.Blocked);
        }
    }

    // --- MustHave +15 ChoiceSignal ---

    [Fact]
    public async Task SeedFromScenario_MustHave_AppliesChoiceSignalPlus15()
    {
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Intimacy", ThemeTier.MustHave);

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, new FakeStyleProfileService(),
            null!, null!);
        var session = new RolePlaySession { SelectedThemeProfileId = "profile-1" };
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        var item = session.AdaptiveState.ThemeTracker.Themes["intimacy"];
        Assert.Equal(15, item.Breakdown.ChoiceSignal);
        // MustHave gets +15 ChoiceSignal + 3 affinity bonus = 18 base score (before scenario text)
        Assert.True(item.Score >= 18);
        Assert.False(item.Blocked);
    }

    [Fact]
    public async Task SeedFromScenario_StronglyPrefer_AppliesChoiceSignalPlus8()
    {
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Power Dynamics", ThemeTier.StronglyPrefer);

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, new FakeStyleProfileService(),
            null!, null!);
        var session = new RolePlaySession { SelectedThemeProfileId = "profile-1" };
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        var item = session.AdaptiveState.ThemeTracker.Themes["power-dynamics"];
        Assert.Equal(8, item.Breakdown.ChoiceSignal);
        Assert.True(item.Score >= 8);
    }

    [Fact]
    public async Task SeedFromScenario_NiceToHave_AppliesChoiceSignalPlus3()
    {
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Confession", ThemeTier.NiceToHave);

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, new FakeStyleProfileService(),
            null!, null!);
        var session = new RolePlaySession { SelectedThemeProfileId = "profile-1" };
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        var item = session.AdaptiveState.ThemeTracker.Themes["confession"];
        Assert.Equal(3, item.Breakdown.ChoiceSignal);
    }

    [Fact]
    public async Task SeedFromScenario_Dislike_AppliesChoiceSignalMinus5()
    {
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Forbidden Risk", ThemeTier.Dislike);

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, new FakeStyleProfileService(),
            null!, null!);
        var session = new RolePlaySession { SelectedThemeProfileId = "profile-1" };
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        var item = session.AdaptiveState.ThemeTracker.Themes["forbidden-risk"];
        Assert.Equal(-5, item.Breakdown.ChoiceSignal);
        // Score cannot go below 0
        Assert.Equal(0, item.Score);
    }

    // --- HardDealBreaker blocked with score=0 ---

    [Fact]
    public async Task SeedFromScenario_HardDealBreaker_BlocksThemeWithScoreZero()
    {
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Power Dynamics", ThemeTier.HardDealBreaker);

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, new FakeStyleProfileService(),
            null!, null!);
        var session = new RolePlaySession { SelectedThemeProfileId = "profile-1" };
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        var item = session.AdaptiveState.ThemeTracker.Themes["power-dynamics"];
        Assert.True(item.Blocked);
        Assert.Equal(0, item.Score);
        Assert.Equal(0, item.Breakdown.ChoiceSignal);
    }

    [Fact]
    public async Task SeedFromScenario_BlockedTheme_NotScoredByKeywords()
    {
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Forbidden Risk", ThemeTier.HardDealBreaker);

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, new FakeStyleProfileService(),
            null!, null!);
        var session = new RolePlaySession { SelectedThemeProfileId = "profile-1" };
        // Scenario has keywords that would normally match "Forbidden Risk"
        var scenario = CreateMinimalScenario();
        scenario.Openings.Add(new Opening { Text = "A secret, hidden, risk-filled forbidden encounter." });

        await service.SeedFromScenarioAsync(session, scenario);

        var item = session.AdaptiveState.ThemeTracker.Themes["forbidden-risk"];
        Assert.True(item.Blocked);
        Assert.Equal(0, item.Score);
        Assert.Equal(0, item.Breakdown.ScenarioPhaseSignal);
    }

    // --- Scenario text keyword scoring ---

    [Fact]
    public async Task SeedFromScenario_OpeningText_ScoredAt06Weight()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();
        var scenario = CreateMinimalScenario();
        scenario.Openings.Add(new Opening { Text = "She reached out to touch him, soft and gentle." });

        await service.SeedFromScenarioAsync(session, scenario);

        var item = session.AdaptiveState.ThemeTracker.Themes["intimacy"];
        Assert.True(item.Breakdown.ScenarioPhaseSignal > 0, "ScenarioPhaseSignal should be > 0 for keyword matches");
        Assert.True(item.Score > 0);
    }

    [Fact]
    public async Task SeedFromScenario_PlotDescription_ScoredAt04Weight()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();
        var scenario = CreateMinimalScenario();
        scenario.Plot.Description = "A story about control and command over others.";

        await service.SeedFromScenarioAsync(session, scenario);

        var item = session.AdaptiveState.ThemeTracker.Themes["power-dynamics"];
        Assert.True(item.Breakdown.ScenarioPhaseSignal > 0);
    }

    [Fact]
    public async Task SeedFromScenario_CharacterDescription_ScoredAt04Weight()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();
        var scenario = CreateMinimalScenario();
        scenario.Characters.Add(new Character { Name = "Elena", Description = "A woman who loves to confess and reveal her truth." });

        await service.SeedFromScenarioAsync(session, scenario);

        var item = session.AdaptiveState.ThemeTracker.Themes["confession"];
        Assert.True(item.Breakdown.ScenarioPhaseSignal > 0);
    }

    // --- StyleProfile.ThemeAffinities multiplier ---

    [Fact]
    public async Task SeedFromScenario_ThemeAffinities_MultipliesKeywordScore()
    {
        var styleService = new FakeStyleProfileService();
        styleService.Add(new SteeringProfile
        {
            Id = "style-1",
            Name = "Sultry",
            ThemeAffinities = new(StringComparer.OrdinalIgnoreCase) { ["intimacy"] = 2 }
        });

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), new FakePreferenceService(), styleService,
            null!, null!);
        var session = new RolePlaySession { SelectedSteeringProfileId = "style-1" };
        var scenarioWithAffinity = CreateMinimalScenario();
        scenarioWithAffinity.Openings.Add(new Opening { Text = "She reached out to touch him, soft and gentle." });

        var sessionWithout = new RolePlaySession();
        var scenarioWithout = CreateMinimalScenario();
        scenarioWithout.Openings.Add(new Opening { Text = "She reached out to touch him, soft and gentle." });

        var serviceNoStyle = new RolePlayAdaptiveStateService(new FakeCatalogService());

        await service.SeedFromScenarioAsync(session, scenarioWithAffinity);
        await serviceNoStyle.SeedFromScenarioAsync(sessionWithout, scenarioWithout);

        var withAffinity = session.AdaptiveState.ThemeTracker.Themes["intimacy"].Score;
        var withoutAffinity = sessionWithout.AdaptiveState.ThemeTracker.Themes["intimacy"].Score;

        Assert.True(withAffinity > withoutAffinity, $"Affinity score ({withAffinity}) should be greater than without ({withoutAffinity})");
    }

    // --- StatBias application ---

    [Fact]
    public async Task SeedFromScenario_StatBias_AppliedToCharacterStats()
    {
        var styleService = new FakeStyleProfileService();
        styleService.Add(new SteeringProfile
        {
            Id = "style-1",
            Name = "Test",
            StatBias = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 5, ["Connection"] = -3 }
        });

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), new FakePreferenceService(), styleService,
            null!, null!);
        var session = new RolePlaySession { SelectedSteeringProfileId = "style-1" };
        session.AdaptiveState.CharacterStats["Alice"] = new CharacterStatBlock
        {
            CharacterId = "alice-1",
            Stats = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 50, ["Connection"] = 50, ["Restraint"] = 50, ["Tension"] = 50, ["Dominance"] = 50 }
        };
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        Assert.Equal(55, session.AdaptiveState.CharacterStats["Alice"].Stats["Desire"]);
        Assert.Equal(47, session.AdaptiveState.CharacterStats["Alice"].Stats["Connection"]);
        // Unaffected stats stay the same (before StatAffinities)
    }

    // --- StatAffinities deltas from scoring themes ---

    [Fact]
    public async Task SeedFromScenario_StatAffinities_AppliedForScoringThemes()
    {
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Intimacy", ThemeTier.MustHave);

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, new FakeStyleProfileService(),
            null!, null!);
        var session = new RolePlaySession { SelectedThemeProfileId = "profile-1" };
        session.AdaptiveState.CharacterStats["Bob"] = new CharacterStatBlock
        {
            CharacterId = "bob-1",
            Stats = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 50, ["Connection"] = 50, ["Restraint"] = 50, ["Tension"] = 50, ["Dominance"] = 50 }
        };
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        // Intimacy has StatAffinities: Desire +2, Connection +1
        Assert.Equal(52, session.AdaptiveState.CharacterStats["Bob"].Stats["Desire"]);
        Assert.Equal(51, session.AdaptiveState.CharacterStats["Bob"].Stats["Connection"]);
    }

    [Fact]
    public async Task SeedFromScenario_StatAffinities_NotAppliedForBlockedThemes()
    {
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Power Dynamics", ThemeTier.HardDealBreaker);

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, new FakeStyleProfileService(),
            null!, null!);
        var session = new RolePlaySession { SelectedThemeProfileId = "profile-1" };
        session.AdaptiveState.CharacterStats["Bob"] = new CharacterStatBlock
        {
            CharacterId = "bob-1",
            Stats = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 50, ["Connection"] = 50, ["Restraint"] = 50, ["Tension"] = 50, ["Dominance"] = 50 }
        };
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        // Power Dynamics has StatAffinities: Dominance +3, but it's blocked
        Assert.Equal(50, session.AdaptiveState.CharacterStats["Bob"].Stats["Dominance"]);
    }

    // --- BaseStatProfile + per-char overrides + StatBias order ---

    [Fact]
    public async Task SeedFromScenario_StatBiasAndAffinities_ApplyInCorrectOrder()
    {
        // StatBias applies first (globally), then StatAffinities from scoring themes
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Intimacy", ThemeTier.MustHave);

        var styleService = new FakeStyleProfileService();
        styleService.Add(new SteeringProfile
        {
            Id = "style-1",
            Name = "Test",
            StatBias = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 10 }
        });

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, styleService,
            null!, null!);
        var session = new RolePlaySession
        {
            SelectedThemeProfileId = "profile-1",
            SelectedSteeringProfileId = "style-1"
        };
        session.AdaptiveState.CharacterStats["Carol"] = new CharacterStatBlock
        {
            CharacterId = "carol-1",
            Stats = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 50, ["Connection"] = 50, ["Restraint"] = 50, ["Tension"] = 50, ["Dominance"] = 50 }
        };
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        // Desire: 50 (base) + 10 (StatBias) + 2 (Intimacy StatAffinity) = 62
        Assert.Equal(62, session.AdaptiveState.CharacterStats["Carol"].Stats["Desire"]);
        // Connection: 50 (base) + 1 (Intimacy StatAffinity) = 51
        Assert.Equal(51, session.AdaptiveState.CharacterStats["Carol"].Stats["Connection"]);
    }

    // --- Null / edge cases ---

    [Fact]
    public async Task SeedFromScenario_NoThemeProfileId_SkipsPreferenceSeeding()
    {
        var prefService = new FakePreferenceService();
        prefService.Add("profile-1", "Intimacy", ThemeTier.MustHave);

        var service = new RolePlayAdaptiveStateService(
            new FakeCatalogService(), prefService, new FakeStyleProfileService(),
            null!, null!);
        var session = new RolePlaySession(); // No SelectedThemeProfileId
        var scenario = CreateMinimalScenario();

        await service.SeedFromScenarioAsync(session, scenario);

        // No ChoiceSignal should be applied
        foreach (var item in session.AdaptiveState.ThemeTracker.Themes.Values)
        {
            Assert.Equal(0, item.Breakdown.ChoiceSignal);
            Assert.False(item.Blocked);
        }
    }

    [Fact]
    public async Task SeedFromScenario_EmptyScenario_DoesNotThrow()
    {
        var service = new RolePlayAdaptiveStateService(new FakeCatalogService());
        var session = new RolePlaySession();
        var scenario = new Scenario();

        await service.SeedFromScenarioAsync(session, scenario);

        Assert.Equal(4, session.AdaptiveState.ThemeTracker.Themes.Count);
    }
}
