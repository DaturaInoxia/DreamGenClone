using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RPThemeCloneTests : IDisposable
{
    private readonly string _databasePath;

    public RPThemeCloneTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-rptheme-clone-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task CloneThemeAsync_CopiesAllThemeData_WithIndependentChildIdentities()
    {
        var service = await CreateServiceAsync();
        var source = BuildSourceTheme("source-theme", "Source Theme");
        await service.SaveThemeAsync(source);

        var clone = await service.CloneThemeAsync(source.Id, "source-theme-v2", "Source Theme V2");

        Assert.Equal("source-theme-v2", clone.Id);
        Assert.Equal("Source Theme V2", clone.Label);
        Assert.Equal(source.Description, clone.Description);
        Assert.Equal(source.Category, clone.Category);
        Assert.Equal(source.Weight, clone.Weight);
        Assert.Equal(source.IsEnabled, clone.IsEnabled);

        Assert.Equal(source.Keywords.Count, clone.Keywords.Count);
        Assert.Equal(source.StatAffinities.Count, clone.StatAffinities.Count);
        Assert.Equal(source.PhaseGuidance.Count, clone.PhaseGuidance.Count);
        Assert.Equal(source.GuidancePoints.Count, clone.GuidancePoints.Count);
        Assert.Equal(source.FitRules.Count, clone.FitRules.Count);
        Assert.Equal(source.AIGenerationNotes.Count, clone.AIGenerationNotes.Count);
        Assert.Equal(source.NarrativeGateRules.Count, clone.NarrativeGateRules.Count);

        Assert.NotEqual(source.Keywords[0].Id, clone.Keywords[0].Id);
        Assert.NotEqual(source.StatAffinities[0].Id, clone.StatAffinities[0].Id);
        Assert.NotEqual(source.PhaseGuidance[0].Id, clone.PhaseGuidance[0].Id);
        Assert.NotEqual(source.GuidancePoints[0].Id, clone.GuidancePoints[0].Id);
        Assert.NotEqual(source.FitRules[0].Id, clone.FitRules[0].Id);
        Assert.NotEqual(source.FitRules[0].Clauses[0].Id, clone.FitRules[0].Clauses[0].Id);
        Assert.NotEqual(source.AIGenerationNotes[0].Id, clone.AIGenerationNotes[0].Id);
        Assert.Equal(clone.Id, clone.Keywords[0].ThemeId);
        Assert.Equal(clone.Id, clone.FitRules[0].ThemeId);
        Assert.Equal(clone.FitRules[0].Id, clone.FitRules[0].Clauses[0].FitRuleId);
    }

    [Fact]
    public async Task CloneThemeAsync_DuplicateTargetId_Throws()
    {
        var service = await CreateServiceAsync();
        var source = BuildSourceTheme("source-theme", "Source Theme");
        await service.SaveThemeAsync(source);
        await service.SaveThemeAsync(BuildSourceTheme("existing-theme", "Existing Theme"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloneThemeAsync(source.Id, "existing-theme", "Conflicting Clone"));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloneThemeAsync_ChangesToClone_DoNotMutateSource()
    {
        var service = await CreateServiceAsync();
        var source = BuildSourceTheme("source-theme", "Source Theme");
        await service.SaveThemeAsync(source);

        var clone = await service.CloneThemeAsync(source.Id, "source-theme-v2", "Source Theme V2");
        clone.Keywords[0].Keyword = "changed-keyword";
        clone.Description = "changed-description";
        await service.SaveThemeAsync(clone);

        var sourceReloaded = await service.GetThemeAsync(source.Id);
        var cloneReloaded = await service.GetThemeAsync(clone.Id);

        Assert.NotNull(sourceReloaded);
        Assert.NotNull(cloneReloaded);
        Assert.Equal("secret-glance", sourceReloaded!.Keywords[0].Keyword);
        Assert.Equal("Source description", sourceReloaded.Description);
        Assert.Equal("changed-keyword", cloneReloaded!.Keywords[0].Keyword);
        Assert.Equal("changed-description", cloneReloaded.Description);
    }

    private async Task<RPThemeService> CreateServiceAsync()
    {
        var persistenceOptions = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={_databasePath}"
        });

        var sqlite = new SqlitePersistence(
            persistenceOptions,
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
        await sqlite.InitializeAsync();

        return new RPThemeService(persistenceOptions, NullLogger<RPThemeService>.Instance);
    }

    private static RPTheme BuildSourceTheme(string id, string label)
    {
        return new RPTheme
        {
            Id = id,
            Label = label,
            Description = "Source description",
            Category = "Power",
            Weight = 4,
            IsEnabled = true,
            Keywords =
            [
                new RPThemeKeyword { GroupName = "Context", Keyword = "secret-glance", SortOrder = 1 }
            ],
            StatAffinities =
            [
                new RPThemeStatAffinity { StatName = "Desire", Value = 1, Rationale = "baseline" },
                new RPThemeStatAffinity { StatName = "Restraint", Value = -1, Rationale = "baseline" },
                new RPThemeStatAffinity { StatName = "Tension", Value = 2, Rationale = "baseline" },
                new RPThemeStatAffinity { StatName = "Connection", Value = 1, Rationale = "baseline" },
                new RPThemeStatAffinity { StatName = "Dominance", Value = 0, Rationale = "baseline" },
                new RPThemeStatAffinity { StatName = "Loyalty", Value = 0, Rationale = "baseline" },
                new RPThemeStatAffinity { StatName = "SelfRespect", Value = 0, Rationale = "baseline" }
            ],
            PhaseGuidance =
            [
                new RPThemePhaseGuidance { Phase = NarrativePhase.BuildUp, GuidanceText = "Build tension slowly." }
            ],
            GuidancePoints =
            [
                new RPThemeGuidancePoint { Phase = NarrativePhase.BuildUp, PointType = RPThemeGuidancePointType.Emphasis, Text = "Use eye contact.", SortOrder = 1 }
            ],
            FitRules =
            [
                new RPThemeFitRule
                {
                    RoleName = "Narrative",
                    RoleWeight = 1.0,
                    Clauses =
                    [
                        new RPThemeFitRuleClause { StatName = "Tension", Comparator = ">=", Threshold = 50, PenaltyWeight = 1.0, Description = "Needs tension" }
                    ]
                }
            ],
            AIGenerationNotes =
            [
                new RPThemeAIGuidanceNote { Section = RPThemeAIGuidanceSection.KeyScenarioElement, Text = "Keep it subtle.", SortOrder = 1 }
            ],
            NarrativeGateRules =
            [
                new NarrativeGateRule { SortOrder = 1, FromPhase = "BuildUp", ToPhase = "Committed", MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 60m },
                new NarrativeGateRule { SortOrder = 2, FromPhase = "Committed", ToPhase = "Approaching", MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 60m },
                new NarrativeGateRule { SortOrder = 3, FromPhase = "Approaching", ToPhase = "Climax", MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 80m },
                new NarrativeGateRule { SortOrder = 4, FromPhase = "Climax", ToPhase = "Reset", MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 12m },
                new NarrativeGateRule { SortOrder = 5, FromPhase = "Reset", ToPhase = "BuildUp", MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment, Comparator = NarrativeGateComparators.GreaterThanOrEqual, Threshold = 3m }
            ]
        };
    }

    public void Dispose()
    {
        if (!File.Exists(_databasePath))
        {
            return;
        }

        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // Test assertions completed; provider cleanup can briefly keep a file handle.
        }
    }
}
