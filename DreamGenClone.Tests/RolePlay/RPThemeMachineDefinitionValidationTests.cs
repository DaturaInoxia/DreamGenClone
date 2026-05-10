using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RPThemeMachineDefinitionValidationTests : IDisposable
{
    private readonly string _databasePath;

    public RPThemeMachineDefinitionValidationTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-rptheme-machine-validation-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task ValidateMachineDefinitionAsync_ReturnsInvalid_WhenInitialStateMissing()
    {
        var service = await CreateServiceAsync();
        var theme = await service.SaveThemeAsync(BuildTheme("theme-machine-1"));

        var definition = await service.SaveMachineDefinitionAsync(new RPThemeMachineDefinition
        {
            ThemeId = theme.Id,
            MachineKey = "infidelity-brief-disappearance",
            Version = 1,
            Name = "Invalid Machine",
            IsActive = false,
            States =
            [
                new RPThemeMachineState { StateCode = "PublicBaseline", Label = "Public Baseline", IsInitial = false, SortOrder = 0 },
                new RPThemeMachineState { StateCode = "EncounterInProgress", Label = "Encounter", IsInitial = false, SortOrder = 1 }
            ],
            Transitions =
            [
                new RPThemeMachineTransition
                {
                    FromStateCode = "PublicBaseline",
                    ToStateCode = "EncounterInProgress",
                    Priority = 10,
                    TriggerType = "encounter-start",
                    GateConfigJson = "{}",
                    BlockReasonCode = "none",
                    IsEnabled = true
                }
            ]
        });

        var result = await service.ValidateMachineDefinitionAsync(definition.DefinitionId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("initial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateMachineDefinitionAsync_ReturnsValid_ForWellFormedDefinition()
    {
        var service = await CreateServiceAsync();
        var theme = await service.SaveThemeAsync(BuildTheme("theme-machine-2"));

        var definition = await service.SaveMachineDefinitionAsync(new RPThemeMachineDefinition
        {
            ThemeId = theme.Id,
            MachineKey = "infidelity-brief-disappearance",
            Version = 1,
            Name = "Valid Machine",
            IsActive = false,
            States =
            [
                new RPThemeMachineState { StateCode = "PublicBaseline", Label = "Public Baseline", IsInitial = true, SortOrder = 0 },
                new RPThemeMachineState { StateCode = "EncounterInProgress", Label = "Encounter", SortOrder = 1 }
            ],
            Transitions =
            [
                new RPThemeMachineTransition
                {
                    FromStateCode = "PublicBaseline",
                    ToStateCode = "EncounterInProgress",
                    Priority = 10,
                    TriggerType = "encounter-start",
                    GateConfigJson = "{}",
                    BlockReasonCode = "none",
                    IsEnabled = true
                }
            ]
        });

        var result = await service.ValidateMachineDefinitionAsync(definition.DefinitionId);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateMachineDefinitionAsync_ReturnsInvalid_WhenCooldownRequiresReturnBeatWithoutSignals()
    {
        var service = await CreateServiceAsync();
        var theme = await service.SaveThemeAsync(BuildTheme("theme-machine-3"));

        var definition = await service.SaveMachineDefinitionAsync(new RPThemeMachineDefinition
        {
            ThemeId = theme.Id,
            MachineKey = "infidelity-brief-disappearance",
            Version = 1,
            Name = "Missing Return Beat Signals",
            IsActive = false,
            States =
            [
                new RPThemeMachineState { StateCode = "PublicBaseline", Label = "Public Baseline", IsInitial = true, SortOrder = 0 },
                new RPThemeMachineState { StateCode = "ReintegrationCooldown", Label = "Reintegration Cooldown", SortOrder = 1 },
                new RPThemeMachineState { StateCode = "NextDisappearanceEligible", Label = "Next Eligible", SortOrder = 2 }
            ],
            Transitions =
            [
                new RPThemeMachineTransition
                {
                    FromStateCode = "ReintegrationCooldown",
                    ToStateCode = "NextDisappearanceEligible",
                    Priority = 10,
                    TriggerType = "cooldown-eligibility",
                    GateConfigJson = "{\"minimumInteractions\":3,\"requireReturnBeatCompleted\":true}",
                    BlockReasonCode = "ReintegrationCooldownGateBlocked",
                    IsEnabled = true
                }
            ]
        });

        var result = await service.ValidateMachineDefinitionAsync(definition.DefinitionId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("returnBeatCompletionSignals", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateMachineDefinitionAsync_ReturnsInvalid_WhenCooldownRequiresReturnBeatWithoutRolePair()
    {
        var service = await CreateServiceAsync();
        var theme = await service.SaveThemeAsync(BuildTheme("theme-machine-4"));

        var definition = await service.SaveMachineDefinitionAsync(new RPThemeMachineDefinition
        {
            ThemeId = theme.Id,
            MachineKey = "infidelity-brief-disappearance",
            Version = 1,
            Name = "Missing Return Beat Role Pair",
            IsActive = false,
            States =
            [
                new RPThemeMachineState { StateCode = "PublicBaseline", Label = "Public Baseline", IsInitial = true, SortOrder = 0 },
                new RPThemeMachineState { StateCode = "ReintegrationCooldown", Label = "Reintegration Cooldown", SortOrder = 1 },
                new RPThemeMachineState { StateCode = "NextDisappearanceEligible", Label = "Next Eligible", SortOrder = 2 }
            ],
            Transitions =
            [
                new RPThemeMachineTransition
                {
                    FromStateCode = "ReintegrationCooldown",
                    ToStateCode = "NextDisappearanceEligible",
                    Priority = 10,
                    TriggerType = "cooldown-eligibility",
                    GateConfigJson = "{\"minimumInteractions\":3,\"requireReturnBeatCompleted\":true,\"returnBeatCompletionSignals\":[\"returned safely\"]}",
                    BlockReasonCode = "ReintegrationCooldownGateBlocked",
                    IsEnabled = true
                }
            ]
        });

        var result = await service.ValidateMachineDefinitionAsync(definition.DefinitionId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("returnBeatTransgressorRole", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<RPThemeService> CreateServiceAsync()
    {
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={_databasePath}"
        });

        var sqlite = new SqlitePersistence(
            options,
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
        await sqlite.InitializeAsync();

        return new RPThemeService(options, NullLogger<RPThemeService>.Instance);
    }

    private static RPTheme BuildTheme(string id)
    {
        return new RPTheme
        {
            Id = id,
            Label = $"Theme {id}",
            Description = "Theme for machine validation tests",
            Category = "Validation",
            Weight = 3,
            IsEnabled = true,
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
            // Provider cleanup can hold a transient handle after test completion.
        }
    }
}
