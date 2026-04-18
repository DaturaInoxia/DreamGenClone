using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ScenarioSelectionDominantRoleGateTests
{
    [Fact]
    public async Task EvaluateCandidatesAsync_DominantRoleGate_BlocksWhenNoRoleMeetsThreshold()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(desire: 70, restraint: 40, tension: 55);
        var candidates = new[] { new ScenarioDefinition("scenario-a", "Scenario A", 1) };

        var fitResults = new Dictionary<string, ScenarioFitResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["scenario-a"] = new ScenarioFitResult
            {
                ScenarioId = "scenario-a",
                FitScore = 0.95,
                CharacterRoleScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Wife"] = 0.82,
                    ["Husband"] = 0.84
                }
            }
        };

        var service = CreateService(fitResults, minRoleScore: 0.85);
        var evaluations = await service.EvaluateCandidatesAsync(state, candidates);

        Assert.Single(evaluations);
        Assert.False(evaluations[0].StageBEligible);
        Assert.Equal(0m, evaluations[0].FitScore);
        Assert.Contains("Dominant-role gate failed", evaluations[0].Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateCandidatesAsync_DominantRoleGate_PassesWhenAnyRoleMeetsThreshold()
    {
        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(desire: 70, restraint: 40, tension: 55);
        var candidates = new[] { new ScenarioDefinition("scenario-a", "Scenario A", 1) };

        var fitResults = new Dictionary<string, ScenarioFitResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["scenario-a"] = new ScenarioFitResult
            {
                ScenarioId = "scenario-a",
                FitScore = 0.90,
                CharacterRoleScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Wife"] = 0.91,
                    ["Husband"] = 0.50
                },
                Failures = ["Husband.Tension below minimum (20 < 40)"]
            }
        };

        var service = CreateService(fitResults, minRoleScore: 0.85);
        var evaluations = await service.EvaluateCandidatesAsync(state, candidates);

        Assert.Single(evaluations);
        Assert.True(evaluations[0].StageBEligible);
        Assert.True(evaluations[0].FitScore > 0m);
        Assert.Contains("Dominant-role gate passed", evaluations[0].Rationale, StringComparison.OrdinalIgnoreCase);
    }

    private static ScenarioSelectionService CreateService(
        IReadOnlyDictionary<string, ScenarioFitResult> fitResults,
        double minRoleScore)
    {
        var options = Options.Create(new StoryAnalysisOptions
        {
            BuildUpSelectionCandidateGateStrategy = "dominant-role",
            BuildUpSelectionDominantRoleMinScore = minRoleScore
        });

        return new ScenarioSelectionService(
            NullLogger<ScenarioSelectionService>.Instance,
            new FakeThemeCatalogService(),
            new FakeCharacterStateScenarioMapper(fitResults),
            options);
    }

    private sealed class FakeThemeCatalogService : IThemeCatalogService
    {
        public Task<ThemeCatalogEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<ThemeCatalogEntry?>(null);

        public Task<IReadOnlyList<ThemeCatalogEntry>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ThemeCatalogEntry> entries =
            [
                new ThemeCatalogEntry { Id = "scenario-a", IsEnabled = true },
                new ThemeCatalogEntry { Id = "scenario-b", IsEnabled = true }
            ];

            return Task.FromResult(entries);
        }

        public Task SaveAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeCharacterStateScenarioMapper : ICharacterStateScenarioMapper
    {
        private readonly IReadOnlyDictionary<string, ScenarioFitResult> _fitResults;

        public FakeCharacterStateScenarioMapper(IReadOnlyDictionary<string, ScenarioFitResult> fitResults)
        {
            _fitResults = fitResults;
        }

        public Task<IReadOnlyDictionary<string, ScenarioFitResult>> EvaluateAllScenariosAsync(
            AdaptiveScenarioState state,
            IReadOnlyList<ThemeCatalogEntry> catalogEntries,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_fitResults);
    }
}
