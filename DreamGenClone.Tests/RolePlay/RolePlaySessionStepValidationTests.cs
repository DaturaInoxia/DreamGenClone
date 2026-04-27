using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlaySessionStepValidationTests
{
    private readonly ITestOutputHelper _output;

    public RolePlaySessionStepValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SessionFlow_StepByStep_ValidatesInputsOutputsAndTransitions()
    {
        var profile = BuildProfile();
        var profileService = new StubNarrativeGateProfileService(profile);
        var selectionService = new ScenarioSelectionService(
            NullLogger<ScenarioSelectionService>.Instance,
            narrativeGateProfileService: profileService,
            engineSettingsRepository: new StubScenarioEngineSettingsRepository());
        var lifecycleService = new ScenarioLifecycleService(
            NullLogger<ScenarioLifecycleService>.Instance,
            gateProfileService: profileService);

        var state = RolePlayV2AcceptanceFixtureData.BuildBoundaryState(desire: 70, restraint: 35, tension: 60);
        state.SessionId = "step-validation-session";
        state.CurrentPhase = NarrativePhase.BuildUp;
        state.SelectedNarrativeGateProfileId = profile.Id;
        state.InteractionCountInPhase = 3;

        var evaluations = new[]
        {
            new ScenarioCandidateEvaluation
            {
                SessionId = state.SessionId,
                ScenarioId = "threesome-spontaneous-exclusion-v2",
                StageBEligible = true,
                FitScore = 72.27m,
                UnpenalizedFitScore = 72.27m,
                Confidence = 0.82m,
                TieBreakKey = "001:threesome-spontaneous-exclusion-v2"
            }
        };

        _output.WriteLine("STEP 1: BuildUp commit gate should block at interactions=3 with threshold=12");
        _output.WriteLine($"INPUT  : phase={state.CurrentPhase}, fit={evaluations[0].FitScore:0.##}, interactions={state.InteractionCountInPhase}, selectedProfile={state.SelectedNarrativeGateProfileId}");
        var commitBlocked = await selectionService.TryCommitScenarioAsync(state, evaluations);
        _output.WriteLine($"OUTPUT : committed={commitBlocked.Committed}, reason={commitBlocked.Reason}");
        Assert.False(commitBlocked.Committed);
        Assert.Contains("InteractionsSinceCommitment >= 12", commitBlocked.Reason, StringComparison.OrdinalIgnoreCase);

        _output.WriteLine("STEP 2: BuildUp commit gate should pass at interactions=12");
        state.InteractionCountInPhase = 12;
        _output.WriteLine($"INPUT  : phase={state.CurrentPhase}, fit={evaluations[0].FitScore:0.##}, interactions={state.InteractionCountInPhase}");
        var commitPassed = await selectionService.TryCommitScenarioAsync(state, evaluations);
        _output.WriteLine($"OUTPUT : committed={commitPassed.Committed}, scenario={commitPassed.ScenarioId}, reason={commitPassed.Reason}");
        Assert.True(commitPassed.Committed);
        Assert.Equal("threesome-spontaneous-exclusion-v2", commitPassed.ScenarioId);

        // Mirror engine post-commit state mutation for the lifecycle stage checks.
        state.ActiveScenarioId = commitPassed.ScenarioId;
        state.CurrentPhase = NarrativePhase.Committed;
        state.InteractionCountInPhase = 0;

        _output.WriteLine("STEP 3: Committed -> Approaching should stay blocked at interactions=1");
        var committedBlocked = await lifecycleService.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            NarrativeGateProfileId = profile.Id,
            InteractionsSinceCommitment = 1,
            ActiveScenarioFitScore = evaluations[0].UnpenalizedFitScore,
            ActiveScenarioConfidence = evaluations[0].Confidence
        });
        _output.WriteLine($"INPUT  : phase={state.CurrentPhase}, fit={evaluations[0].UnpenalizedFitScore:0.##}, interactions=1");
        _output.WriteLine($"OUTPUT : transitioned={committedBlocked.Transitioned}, target={committedBlocked.TargetPhase}, reason={committedBlocked.Reason}");
        Assert.False(committedBlocked.Transitioned);
        Assert.Equal(NarrativePhase.Committed, committedBlocked.TargetPhase);

        _output.WriteLine("STEP 4: Committed -> Approaching should pass at interactions=2");
        var committedPassed = await lifecycleService.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            NarrativeGateProfileId = profile.Id,
            InteractionsSinceCommitment = 2,
            ActiveScenarioFitScore = evaluations[0].UnpenalizedFitScore,
            ActiveScenarioConfidence = evaluations[0].Confidence
        });
        _output.WriteLine($"INPUT  : phase={state.CurrentPhase}, fit={evaluations[0].UnpenalizedFitScore:0.##}, interactions=2");
        _output.WriteLine($"OUTPUT : transitioned={committedPassed.Transitioned}, target={committedPassed.TargetPhase}, reason={committedPassed.Reason}");
        Assert.True(committedPassed.Transitioned);
        Assert.Equal(NarrativePhase.Approaching, committedPassed.TargetPhase);
        state.CurrentPhase = committedPassed.TargetPhase;

        _output.WriteLine("STEP 5: Approaching -> Climax should stay blocked at interactions=1");
        var approachingBlocked = await lifecycleService.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            NarrativeGateProfileId = profile.Id,
            InteractionsSinceCommitment = 1,
            ActiveScenarioFitScore = 85m,
            ActiveScenarioConfidence = 0.9m
        });
        _output.WriteLine("INPUT  : phase=Approaching, fit=85, interactions=1");
        _output.WriteLine($"OUTPUT : transitioned={approachingBlocked.Transitioned}, target={approachingBlocked.TargetPhase}, reason={approachingBlocked.Reason}");
        Assert.False(approachingBlocked.Transitioned);
        Assert.Equal(NarrativePhase.Approaching, approachingBlocked.TargetPhase);

        _output.WriteLine("STEP 6: Approaching -> Climax should pass at interactions=2");
        var approachingPassed = await lifecycleService.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            NarrativeGateProfileId = profile.Id,
            InteractionsSinceCommitment = 2,
            ActiveScenarioFitScore = 85m,
            ActiveScenarioConfidence = 0.9m
        });
        _output.WriteLine("INPUT  : phase=Approaching, fit=85, interactions=2");
        _output.WriteLine($"OUTPUT : transitioned={approachingPassed.Transitioned}, target={approachingPassed.TargetPhase}, reason={approachingPassed.Reason}");
        Assert.True(approachingPassed.Transitioned);
        Assert.Equal(NarrativePhase.Climax, approachingPassed.TargetPhase);
        state.CurrentPhase = approachingPassed.TargetPhase;

        _output.WriteLine("STEP 7: Climax should remain until explicit completion signal");
        var climaxBlocked = await lifecycleService.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            NarrativeGateProfileId = profile.Id,
            InteractionsSinceCommitment = 99,
            ActiveScenarioFitScore = 90m,
            ActiveScenarioConfidence = 0.95m,
            ClimaxCompletionRequested = false
        });
        _output.WriteLine("INPUT  : phase=Climax, completionRequested=false");
        _output.WriteLine($"OUTPUT : transitioned={climaxBlocked.Transitioned}, target={climaxBlocked.TargetPhase}, reason={climaxBlocked.Reason}");
        Assert.False(climaxBlocked.Transitioned);
        Assert.Equal(NarrativePhase.Climax, climaxBlocked.TargetPhase);

        _output.WriteLine("STEP 8: Climax -> Reset should pass with explicit completion signal");
        var climaxPassed = await lifecycleService.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            NarrativeGateProfileId = profile.Id,
            ClimaxCompletionRequested = true
        });
        _output.WriteLine("INPUT  : phase=Climax, completionRequested=true");
        _output.WriteLine($"OUTPUT : transitioned={climaxPassed.Transitioned}, target={climaxPassed.TargetPhase}, reason={climaxPassed.Reason}");
        Assert.True(climaxPassed.Transitioned);
        Assert.Equal(NarrativePhase.Reset, climaxPassed.TargetPhase);
        state.CurrentPhase = climaxPassed.TargetPhase;

        _output.WriteLine("STEP 9: Reset -> BuildUp should stay blocked at interactions=2");
        var resetBlocked = await lifecycleService.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            NarrativeGateProfileId = profile.Id,
            InteractionsSinceCommitment = 2
        });
        _output.WriteLine("INPUT  : phase=Reset, interactions=2");
        _output.WriteLine($"OUTPUT : transitioned={resetBlocked.Transitioned}, target={resetBlocked.TargetPhase}, reason={resetBlocked.Reason}");
        Assert.False(resetBlocked.Transitioned);
        Assert.Equal(NarrativePhase.Reset, resetBlocked.TargetPhase);

        _output.WriteLine("STEP 10: Reset -> BuildUp should pass at interactions=3");
        var resetPassed = await lifecycleService.EvaluateTransitionAsync(state, new LifecycleInputs
        {
            NarrativeGateProfileId = profile.Id,
            InteractionsSinceCommitment = 3
        });
        _output.WriteLine("INPUT  : phase=Reset, interactions=3");
        _output.WriteLine($"OUTPUT : transitioned={resetPassed.Transitioned}, target={resetPassed.TargetPhase}, reason={resetPassed.Reason}");
        Assert.True(resetPassed.Transitioned);
        Assert.Equal(NarrativePhase.BuildUp, resetPassed.TargetPhase);
    }

    private static NarrativeGateProfile BuildProfile()
    {
        return new NarrativeGateProfile
        {
            Id = "session-step-profile",
            Name = "Session Step Validation Profile",
            IsDefault = false,
            Rules =
            [
                new NarrativeGateRule
                {
                    SortOrder = 1,
                    FromPhase = "BuildUp",
                    ToPhase = "Committed",
                    MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore,
                    Comparator = NarrativeGateComparators.GreaterThanOrEqual,
                    Threshold = 60m
                },
                new NarrativeGateRule
                {
                    SortOrder = 2,
                    FromPhase = "BuildUp",
                    ToPhase = "Committed",
                    MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment,
                    Comparator = NarrativeGateComparators.GreaterThanOrEqual,
                    Threshold = 12m
                },
                new NarrativeGateRule
                {
                    SortOrder = 3,
                    FromPhase = "Committed",
                    ToPhase = "Approaching",
                    MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore,
                    Comparator = NarrativeGateComparators.GreaterThanOrEqual,
                    Threshold = 60m
                },
                new NarrativeGateRule
                {
                    SortOrder = 4,
                    FromPhase = "Committed",
                    ToPhase = "Approaching",
                    MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment,
                    Comparator = NarrativeGateComparators.GreaterThanOrEqual,
                    Threshold = 2m
                },
                new NarrativeGateRule
                {
                    SortOrder = 5,
                    FromPhase = "Approaching",
                    ToPhase = "Climax",
                    MetricKey = NarrativeGateMetricKeys.ActiveScenarioScore,
                    Comparator = NarrativeGateComparators.GreaterThanOrEqual,
                    Threshold = 80m
                },
                new NarrativeGateRule
                {
                    SortOrder = 6,
                    FromPhase = "Approaching",
                    ToPhase = "Climax",
                    MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment,
                    Comparator = NarrativeGateComparators.GreaterThanOrEqual,
                    Threshold = 2m
                },
                new NarrativeGateRule
                {
                    SortOrder = 7,
                    FromPhase = "Reset",
                    ToPhase = "BuildUp",
                    MetricKey = NarrativeGateMetricKeys.InteractionsSinceCommitment,
                    Comparator = NarrativeGateComparators.GreaterThanOrEqual,
                    Threshold = 3m
                }
            ]
        };
    }

    private sealed class StubScenarioEngineSettingsRepository : IScenarioEngineSettingsRepository
    {
        private readonly ScenarioEngineSettings _settings;
        public StubScenarioEngineSettingsRepository(ScenarioEngineSettings? settings = null)
            => _settings = settings ?? new ScenarioEngineSettings();
        public Task<ScenarioEngineSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);
        public Task SaveAsync(ScenarioEngineSettings settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubNarrativeGateProfileService : INarrativeGateProfileService
    {
        private readonly Dictionary<string, NarrativeGateProfile> _profiles;

        public StubNarrativeGateProfileService(params NarrativeGateProfile[] profiles)
        {
            _profiles = profiles.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        }

        public Task<NarrativeGateProfile> SaveAsync(NarrativeGateProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(profile);

        public Task<List<NarrativeGateProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.Values.ToList());

        public Task<NarrativeGateProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.TryGetValue(id, out var profile) ? profile : null);

        public Task<NarrativeGateProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.Values.FirstOrDefault(x => x.IsDefault));

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_profiles.Remove(id));
    }
}
