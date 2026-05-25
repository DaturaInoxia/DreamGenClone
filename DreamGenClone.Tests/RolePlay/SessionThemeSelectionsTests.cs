using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using static DreamGenClone.Tests.RolePlay.RolePlayTestFactory;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Tests for B-008: per-session theme selections seeded via <see cref="CreateRolePlaySessionRequest"/>
/// and applied to the adaptive tracker in <see cref="RolePlayAdaptiveStateService"/>.
/// </summary>
public sealed class SessionThemeSelectionsTests
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly RPTheme ThemeA = new()
    {
        Id = "romance", Label = "Romance",
        IsEnabled = true, Weight = 3, Category = "Emotional"
    };

    private static readonly RPTheme ThemeB = new()
    {
        Id = "power-dynamics", Label = "Power Dynamics",
        IsEnabled = true, Weight = 4, Category = "Power"
    };

    private static readonly RPTheme ThemeC = new()
    {
        Id = "confession", Label = "Confession",
        IsEnabled = true, Weight = 3, Category = "Emotional"
    };

    private static Scenario MinimalScenario() => new()
    {
        Name = "Test",
        Plot = new Plot { Description = string.Empty },
        Setting = new Setting { WorldDescription = string.Empty },
        Narrative = new NarrativeSettings(),
        Characters = [],
        Openings = [],
        Examples = [],
        Locations = [],
        Objects = []
    };

    private static RolePlayAdaptiveStateService BuildService(
        IList<RPTheme> rpThemes,
        IList<RPThemeProfileThemeAssignment>? profileAssignments = null,
        TrackingRpThemeStub? stub = null)
    {
        var themeStub = stub ?? new TrackingRpThemeStub(rpThemes, profileAssignments ?? []);
        return new RolePlayAdaptiveStateService(
            new EmptyThemeCatalogService(),
            new EmptyThemePreferenceService(),
            themeStub,
            statKeywordCategoryService: null,
            new NullSteeringProfileService(),
            new NullRolePlayDebugEventSink(),
            NullLogger<RolePlayAdaptiveStateService>.Instance);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  LoadRuntimeCatalogEntries branch: SessionThemeSelections
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedFromScenarioAsync_WithSessionThemeSelections_OnlySelectedThemesInTracker()
    {
        // ThemeA and ThemeB selected; ThemeC is in the catalog but NOT selected.
        var service = BuildService([ThemeA, ThemeB, ThemeC]);
        var session = new RolePlaySession
        {
            SessionThemeSelections =
            [
                new SessionThemeSelection { ThemeId = ThemeA.Id, Tier = RPThemeTier.NiceToHave },
                new SessionThemeSelection { ThemeId = ThemeB.Id, Tier = RPThemeTier.StronglyPrefer },
            ]
        };

        await service.SeedFromScenarioAsync(session, MinimalScenario());

        Assert.Contains(ThemeA.Id, session.AdaptiveState.ThemeScores.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(ThemeB.Id, session.AdaptiveState.ThemeScores.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(ThemeC.Id, session.AdaptiveState.ThemeScores.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SeedFromScenarioAsync_WithSessionThemeSelections_CorrectTrackerCount()
    {
        var service = BuildService([ThemeA, ThemeB, ThemeC]);
        var session = new RolePlaySession
        {
            SessionThemeSelections =
            [
                new SessionThemeSelection { ThemeId = ThemeA.Id, Tier = RPThemeTier.MustHave },
            ]
        };

        await service.SeedFromScenarioAsync(session, MinimalScenario());

        Assert.Single(session.AdaptiveState.ThemeScores);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Choice signal values per tier
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedFromScenarioAsync_MustHave_SetsChoiceSignal15()
    {
        var service = BuildService([ThemeA]);
        var session = new RolePlaySession
        {
            SessionThemeSelections =
            [
                new SessionThemeSelection { ThemeId = ThemeA.Id, Tier = RPThemeTier.MustHave }
            ]
        };

        await service.SeedFromScenarioAsync(session, MinimalScenario());

        var item = session.AdaptiveState.ThemeScores[ThemeA.Id];
        Assert.Equal(15, item.Breakdown.ChoiceSignal);
    }

    [Fact]
    public async Task SeedFromScenarioAsync_MustHave_BoostedScoreIs18()
    {
        // MustHave: +15 ChoiceSignal + 3 affinity bonus = 18 base score (no scenario keywords to add).
        var service = BuildService([ThemeA]);
        var session = new RolePlaySession
        {
            SessionThemeSelections =
            [
                new SessionThemeSelection { ThemeId = ThemeA.Id, Tier = RPThemeTier.MustHave }
            ]
        };

        await service.SeedFromScenarioAsync(session, MinimalScenario());

        var item = session.AdaptiveState.ThemeScores[ThemeA.Id];
        Assert.True(item.Score >= 18, $"Expected score >= 18, got {item.Score}");
    }

    [Fact]
    public async Task SeedFromScenarioAsync_StronglyPrefer_SetsChoiceSignal8()
    {
        var service = BuildService([ThemeB]);
        var session = new RolePlaySession
        {
            SessionThemeSelections =
            [
                new SessionThemeSelection { ThemeId = ThemeB.Id, Tier = RPThemeTier.StronglyPrefer }
            ]
        };

        await service.SeedFromScenarioAsync(session, MinimalScenario());

        var item = session.AdaptiveState.ThemeScores[ThemeB.Id];
        Assert.Equal(8, item.Breakdown.ChoiceSignal);
    }

    [Fact]
    public async Task SeedFromScenarioAsync_NiceToHave_SetsChoiceSignal3()
    {
        var service = BuildService([ThemeC]);
        var session = new RolePlaySession
        {
            SessionThemeSelections =
            [
                new SessionThemeSelection { ThemeId = ThemeC.Id, Tier = RPThemeTier.NiceToHave }
            ]
        };

        await service.SeedFromScenarioAsync(session, MinimalScenario());

        var item = session.AdaptiveState.ThemeScores[ThemeC.Id];
        Assert.Equal(3, item.Breakdown.ChoiceSignal);
    }

    [Fact]
    public async Task SeedFromScenarioAsync_Discouraged_SetsNegativeChoiceSignal()
    {
        var service = BuildService([ThemeA]);
        var session = new RolePlaySession
        {
            SessionThemeSelections =
            [
                new SessionThemeSelection { ThemeId = ThemeA.Id, Tier = RPThemeTier.Discouraged }
            ]
        };

        await service.SeedFromScenarioAsync(session, MinimalScenario());

        var item = session.AdaptiveState.ThemeScores[ThemeA.Id];
        Assert.Equal(-5, item.Breakdown.ChoiceSignal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Profile branch is bypassed when SessionThemeSelections is non-empty
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedFromScenarioAsync_WithSessionThemeSelections_DoesNotCallListProfileAssignments()
    {
        var stub = new TrackingRpThemeStub([ThemeA], []);
        var service = BuildService([ThemeA], stub: stub);
        var session = new RolePlaySession
        {
            SelectedRPThemeProfileId = "some-profile-id",   // set but must be ignored
            SessionThemeSelections =
            [
                new SessionThemeSelection { ThemeId = ThemeA.Id, Tier = RPThemeTier.StronglyPrefer }
            ]
        };

        await service.SeedFromScenarioAsync(session, MinimalScenario());

        Assert.Equal(0, stub.ListProfileAssignmentsCallCount);
    }

    [Fact]
    public async Task SeedFromScenarioAsync_WithEmptySelectionsAndProfileId_CallsListProfileAssignments()
    {
        var stub = new TrackingRpThemeStub([ThemeA], [
            new RPThemeProfileThemeAssignment
            {
                ProfileId = "p1",
                ThemeId = ThemeA.Id,
                Tier = RPThemeTier.StronglyPrefer,
                IsEnabled = true
            }
        ]);
        var service = BuildService([ThemeA], stub: stub);
        var session = new RolePlaySession
        {
            SelectedRPThemeProfileId = "p1",
            SessionThemeSelections = []  // empty — should fall through to profile branch
        };

        await service.SeedFromScenarioAsync(session, MinimalScenario());

        Assert.True(stub.ListProfileAssignmentsCallCount > 0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  CreateRolePlaySessionRequest DTO — properties
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateRolePlaySessionRequest_DefaultThemeSelections_IsEmpty()
    {
        var req = new CreateRolePlaySessionRequest();
        Assert.Empty(req.ThemeSelections);
    }

    [Fact]
    public void CreateRolePlaySessionRequest_WithThemeSelections_Persists()
    {
        var selections = new List<SessionThemeSelection>
        {
            new() { ThemeId = "romance", Tier = RPThemeTier.MustHave },
            new() { ThemeId = "confession", Tier = RPThemeTier.Discouraged }
        };

        var req = new CreateRolePlaySessionRequest { ThemeSelections = selections };

        Assert.Equal(2, req.ThemeSelections.Count);
        Assert.Equal("romance", req.ThemeSelections[0].ThemeId);
        Assert.Equal(RPThemeTier.MustHave, req.ThemeSelections[0].Tier);
        Assert.Equal(RPThemeTier.Discouraged, req.ThemeSelections[1].Tier);
    }

    [Fact]
    public void CreateRolePlaySessionRequest_WithCharacterStatOverrides_Persists()
    {
        var overrides = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["char-1"] = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 75, ["Restraint"] = 20 }
        };

        var req = new CreateRolePlaySessionRequest { CharacterStatOverrides = overrides };

        Assert.True(req.CharacterStatOverrides.TryGetValue("CHAR-1", out var stats));
        Assert.Equal(75, stats!["Desire"]);
        Assert.Equal(20, stats["Restraint"]);
    }

    [Fact]
    public void CreateRolePlaySessionRequest_AwarenessProfileId_Persists()
    {
        var req = new CreateRolePlaySessionRequest { AwarenessProfileId = "profile-42" };
        Assert.Equal("profile-42", req.AwarenessProfileId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  CreateSessionAsync — theme selection vs profile routing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSessionAsync_WithThemeSelections_SessionHasSelectionsAndNullProfileId()
    {
        var scenarioService = new StubScenarioService(new Scenario
        {
            Id = "sc-1",
            Name = "Test",
            DefaultRPThemeProfileId = "default-profile",
            Plot = new Plot { Description = string.Empty },
            Setting = new Setting { WorldDescription = string.Empty },
            Narrative = new NarrativeSettings(),
            Characters = [],
            Openings = [],
            Examples = [],
            Locations = [],
            Objects = []
        });

        var engine = CreateEngineService(scenarioService: scenarioService);

        var request = new CreateRolePlaySessionRequest
        {
            ScenarioId = "sc-1",
            ThemeSelections =
            [
                new SessionThemeSelection { ThemeId = "romance", Tier = RPThemeTier.MustHave }
            ]
        };

        var session = await engine.CreateSessionAsync(request);

        Assert.Single(session.SessionThemeSelections);
        Assert.Equal("romance", session.SessionThemeSelections[0].ThemeId);
        Assert.True(
            string.IsNullOrEmpty(session.SelectedRPThemeProfileId),
            "SelectedRPThemeProfileId must be null/empty when SessionThemeSelections is used");
    }

    [Fact]
    public async Task CreateSessionAsync_WithoutThemeSelections_UsesScenarioDefaultProfile()
    {
        var scenarioService = new StubScenarioService(new Scenario
        {
            Id = "sc-2",
            Name = "Test",
            DefaultRPThemeProfileId = "default-profile",
            Plot = new Plot { Description = string.Empty },
            Setting = new Setting { WorldDescription = string.Empty },
            Narrative = new NarrativeSettings(),
            Characters = [],
            Openings = [],
            Examples = [],
            Locations = [],
            Objects = []
        });

        var engine = CreateEngineService(scenarioService: scenarioService);

        var request = new CreateRolePlaySessionRequest
        {
            ScenarioId = "sc-2",
            ThemeSelections = []  // empty
        };

        var session = await engine.CreateSessionAsync(request);

        Assert.Empty(session.SessionThemeSelections);
        Assert.Equal("default-profile", session.SelectedRPThemeProfileId);
    }

    [Fact]
    public async Task CreateSessionAsync_WithAwarenessProfileId_StoredOnSessionAndAdaptiveState()
    {
        var engine = CreateEngineService();
        var request = new CreateRolePlaySessionRequest { AwarenessProfileId = "awareness-99" };

        var session = await engine.CreateSessionAsync(request);

        Assert.Equal("awareness-99", session.SelectedAwarenessProfileId);
        Assert.Equal("awareness-99", session.AdaptiveState.HusbandAwarenessProfileId);
    }

    [Fact]
    public async Task CreateSessionAsync_WithCharacterStatOverrides_AppliedOnTopOfScenarioStats()
    {
        var scenario = new Scenario
        {
            Id = "sc-3",
            Name = "Test",
            ResolvedBaseStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 30, ["Restraint"] = 50, ["Tension"] = 40,
                ["Connection"] = 50, ["Dominance"] = 50, ["Loyalty"] = 50, ["SelfRespect"] = 50
            },
            Characters =
            [
                new Character
                {
                    Id = "char-1",
                    Name = "Leah",
                    BaseStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        { ["Desire"] = 40 }   // scenario per-char override
                }
            ],
            Plot = new Plot { Description = string.Empty },
            Setting = new Setting { WorldDescription = string.Empty },
            Narrative = new NarrativeSettings(),
            Openings = [],
            Examples = [],
            Locations = [],
            Objects = []
        };

        var engine = CreateEngineService(scenarioService: new StubScenarioService(scenario));

        var request = new CreateRolePlaySessionRequest
        {
            ScenarioId = "sc-3",
            CharacterStatOverrides = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
            {
                // Wizard overrides Desire to 80 (higher than scenario's 40)
                ["char-1"] = new(StringComparer.OrdinalIgnoreCase) { ["Desire"] = 80 }
            }
        };

        var session = await engine.CreateSessionAsync(request);

        Assert.True(session.AdaptiveState.CharacterStats.TryGetValue("Leah", out var leahStats));
        Assert.Equal(80, leahStats!.Desire);   // wizard value wins
        Assert.Equal(50, leahStats.Restraint); // base value from scenario
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Private test doubles
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class EmptyThemeCatalogService : IThemeCatalogService
    {
        public Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<ThemeCatalogEntry?>(null);

        public Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ThemeCatalogEntry>>([]);

        public Task SaveAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class EmptyThemePreferenceService : IThemePreferenceService
    {
        public Task<ThemePreference> CreateAsync(string profileId, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ThemePreference());

        public Task<List<ThemePreference>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ThemePreference>());

        public Task<List<ThemePreference>> ListByProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ThemePreference>());

        public Task<ThemePreference?> UpdateAsync(string id, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ThemePreference?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> AutoLinkToCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class NullSteeringProfileService : ISteeringProfileService
    {
        public Task<SteeringProfile> CreateAsync(string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<List<SteeringProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<SteeringProfile>());

        public Task<SteeringProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<SteeringProfile?>(null);

        public Task<SteeringProfile?> UpdateAsync(string id, string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken cancellationToken = default)
            => Task.FromResult<SteeringProfile?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    /// <summary>
    /// Minimal <see cref="IRPThemeService"/> that returns configured themes and profile assignments
    /// and records how many times <see cref="ListProfileAssignmentsAsync"/> was called.
    /// </summary>
    private sealed class TrackingRpThemeStub : IRPThemeService
    {
        private readonly IList<RPTheme> _themes;
        private readonly IList<RPThemeProfileThemeAssignment> _assignments;

        public TrackingRpThemeStub(IList<RPTheme> themes, IList<RPThemeProfileThemeAssignment> assignments)
        {
            _themes = themes;
            _assignments = assignments;
        }

        public int ListProfileAssignmentsCallCount { get; private set; }

        public Task<IReadOnlyList<RPTheme>> ListThemesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPTheme>>(_themes.ToList());

        public Task<IReadOnlyList<RPTheme>> ListThemesByProfileAsync(string profileId, bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPTheme>>(_themes.ToList());

        public Task<IReadOnlyDictionary<string, IReadOnlyList<RPSemanticEventMapping>>> ResolveSemanticEventMappingsByProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<RPSemanticEventMapping>>>(new Dictionary<string, IReadOnlyList<RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase));

        public Task<RPTheme?> GetThemeAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<RPThemeProfileThemeAssignment>> ListProfileAssignmentsAsync(string profileId, CancellationToken cancellationToken = default)
        {
            ListProfileAssignmentsCallCount++;
            return Task.FromResult<IReadOnlyList<RPThemeProfileThemeAssignment>>(
                _assignments.Where(a => string.Equals(a.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)).ToList());
        }

        // ── Remaining interface members (not needed by these tests) ─────────

        public Task<RPThemeProfile> SaveProfileAsync(RPThemeProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task<IReadOnlyList<RPThemeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPThemeProfile>>([]);

        public Task<RPThemeProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<RPThemeProfile?>(null);

        public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RPTheme> SaveThemeAsync(RPTheme theme, CancellationToken cancellationToken = default)
            => Task.FromResult(theme);

        public Task<RPTheme> CloneThemeAsync(string sourceThemeId, string newThemeId, string newThemeLabel, CancellationToken cancellationToken = default)
            => Task.FromResult(new RPTheme { Id = newThemeId, Label = newThemeLabel });

        public Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RPThemeProfileThemeAssignment> SaveProfileAssignmentAsync(RPThemeProfileThemeAssignment assignment, CancellationToken cancellationToken = default)
            => Task.FromResult(assignment);

        public Task<bool> DeleteProfileAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RPFinishingMoveMatrixRow> SaveFinishingMoveMatrixRowAsync(RPFinishingMoveMatrixRow row, CancellationToken cancellationToken = default)
            => Task.FromResult(row);

        public Task<IReadOnlyList<RPFinishingMoveMatrixRow>> ListFinishingMoveMatrixRowsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPFinishingMoveMatrixRow>>([]);

        public Task<bool> DeleteFinishingMoveMatrixRowAsync(string rowId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> ImportFinishingMoveMatrixRowsFromJsonAsync(string json, bool replaceExisting = false, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<RPSteerPositionMatrixRow> SaveSteerPositionMatrixRowAsync(RPSteerPositionMatrixRow row, CancellationToken cancellationToken = default)
            => Task.FromResult(row);

        public Task<IReadOnlyList<RPSteerPositionMatrixRow>> ListSteerPositionMatrixRowsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPSteerPositionMatrixRow>>([]);

        public Task<bool> DeleteSteerPositionMatrixRowAsync(string rowId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<int> ImportSteerPositionMatrixRowsFromJsonAsync(string json, bool replaceExisting = false, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<RPThemeImportResult>> ImportFromMarkdownAsync(IReadOnlyList<RPThemeImportFile> files, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPThemeImportResult>>([]);

        public Task<IReadOnlyList<RPThemeImportResult>> SyncFromMarkdownDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPThemeImportResult>>([]);

        public Task TruncateRolePlayAndScenarioDataAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<RPPosition> SavePositionAsync(RPPosition entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPPosition>> ListPositionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPPosition>>([]);
        public Task<IReadOnlyList<RPPosition>> ListPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPPosition>>([]);
        public Task<bool> DeletePositionAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishLocation> SaveFinishLocationAsync(RPFinishLocation entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishLocation>> ListFinishLocationsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishLocation>>([]);
        public Task<bool> DeleteFinishLocationAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishFacialType> SaveFinishFacialTypeAsync(RPFinishFacialType entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishFacialType>> ListFinishFacialTypesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishFacialType>>([]);
        public Task<bool> DeleteFinishFacialTypeAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishReceptivityLevel> SaveFinishReceptivityLevelAsync(RPFinishReceptivityLevel entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishReceptivityLevel>> ListFinishReceptivityLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishReceptivityLevel>>([]);
        public Task<bool> DeleteFinishReceptivityLevelAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishHisControlLevel> SaveFinishHisControlLevelAsync(RPFinishHisControlLevel entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishHisControlLevel>> ListFinishHisControlLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishHisControlLevel>>([]);
        public Task<bool> DeleteFinishHisControlLevelAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPFinishTransitionAction> SaveFinishTransitionActionAsync(RPFinishTransitionAction entry, CancellationToken cancellationToken = default) => Task.FromResult(entry);
        public Task<IReadOnlyList<RPFinishTransitionAction>> ListFinishTransitionActionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPFinishTransitionAction>>([]);
        public Task<bool> DeleteFinishTransitionActionAsync(string entryId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RPThemeMachineDefinition> SaveMachineDefinitionAsync(RPThemeMachineDefinition definition, CancellationToken cancellationToken = default) => Task.FromResult(definition);
        public Task<IReadOnlyList<RPThemeMachineDefinition>> ListMachineDefinitionsAsync(string themeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RPThemeMachineDefinition>>([]);
        public Task<RPThemeMachineDefinition?> GetMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<RPThemeMachineDefinition?>(null);
        public Task ActivateMachineDefinitionAsync(string themeId, string machineKey, int version, string actorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<MachineDefinitionValidationResult> ValidateMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult(new MachineDefinitionValidationResult());
        public Task MigrateSessionMachineVersionAsync(string sessionId, string themeId, string machineKey, int targetVersion, string actorId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubScenarioService : IScenarioService
    {
        private readonly Scenario _scenario;

        public StubScenarioService(Scenario scenario)
        {
            _scenario = scenario;
        }

        public Task<Scenario?> GetScenarioAsync(string id)
            => Task.FromResult<Scenario?>(string.Equals(id, _scenario.Id, StringComparison.OrdinalIgnoreCase) ? _scenario : null);

        public Task<Scenario> CreateScenarioAsync(string name, string? description = null) => throw new NotImplementedException();
        public Task<List<Scenario>> GetAllScenariosAsync() => Task.FromResult(new List<Scenario>());
        public Task<Scenario> SaveScenarioAsync(Scenario scenario) => throw new NotImplementedException();
        public Task<bool> DeleteScenarioAsync(string id) => throw new NotImplementedException();
        public Task<Scenario> CloneScenarioAsync(string id, string newName) => throw new NotImplementedException();
    }
}
