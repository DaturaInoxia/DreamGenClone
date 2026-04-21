using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Web.Application.Models;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayContinuationNarrativeValidationTests
{
    [Fact]
    public async Task ContinueBatchAsync_NarrativePrompt_UsesSceneTransitionGuardrails()
    {
        var completion = new QueueCompletionClient([
            "The crowd drifted toward the terrace while the hallway settled into a quieter rhythm."
        ]);

        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "s1",
            PersonaName = "Becky"
        };

        var result = await service.ContinueBatchAsync(
            session,
            actors: [],
            includeNarrative: true,
            customActorName: null,
            promptText: "Continue the scene");

        Assert.True(result.Success);
        Assert.Single(completion.Prompts);

        var prompt = completion.Prompts[0];
        Assert.Contains("Keep this section focused on scene description and transitions", prompt, StringComparison.Ordinal);
        Assert.Contains("Do NOT write extended dialogue exchanges in Narrative", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Prefer externally observable actions, dialogue", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContinueBatchAsync_NarrativeValidation_RetriesAndPrefersSaferOutput()
    {
        var completion = new QueueCompletionClient([
            "\"He doesn't suspect a thing, does he?\" Dean whispered. \"He could hear us,\" Becky said. \"Let him,\" Dean replied.",
            "The hall dimmed as voices from the party receded behind the half-closed door, and the scene shifted toward a tighter, risk-laced stillness."
        ]);

        var service = CreateService(completion, out var debugSink);
        var session = new RolePlaySession
        {
            Id = "s2",
            PersonaName = "Becky"
        };

        var result = await service.ContinueBatchAsync(
            session,
            actors: [],
            includeNarrative: true,
            customActorName: null,
            promptText: "Continue the scene");

        Assert.NotNull(result.NarrativeOutput);
        Assert.Equal("The hall dimmed as voices from the party receded behind the half-closed door, and the scene shifted toward a tighter, risk-laced stillness.", result.NarrativeOutput!.Content);
        Assert.Equal(2, completion.Prompts.Count);

        var validationEvents = debugSink.Records.Where(x => string.Equals(x.EventKind, "NarrativeValidation", StringComparison.Ordinal)).ToList();
        Assert.True(validationEvents.Count >= 2);
        Assert.Contains(validationEvents, x => string.Equals(x.Severity, "Warning", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContinueBatchAsync_NarrativeValidation_CompliantOutputSkipsRetry()
    {
        var completion = new QueueCompletionClient([
            "Rain tapped softly on the windows while the room settled into a fragile quiet, and the evening eased into its next turn without spectacle."
        ]);

        var service = CreateService(completion, out _);
        var session = new RolePlaySession
        {
            Id = "s3",
            PersonaName = "Becky"
        };

        var result = await service.ContinueBatchAsync(
            session,
            actors: [],
            includeNarrative: true,
            customActorName: null,
            promptText: "Continue the scene");

        Assert.NotNull(result.NarrativeOutput);
        Assert.Equal(1, completion.Prompts.Count);
        Assert.Equal("Rain tapped softly on the windows while the room settled into a fragile quiet, and the evening eased into its next turn without spectacle.", result.NarrativeOutput!.Content);
    }

    [Fact]
    public async Task ContinueAsync_WhenThemeGuidanceEnabled_AppendsThemeHintsToPrompt()
    {
        var completion = new QueueCompletionClient([
            "Dean stepped closer and lowered his voice."
        ]);

        var rpThemeService = new StubRpThemeService(new RPTheme
        {
            Id = "infidelity-public-facade",
            AIGenerationNotes =
            [
                new RPThemeAIGuidanceNote
                {
                    Section = RPThemeAIGuidanceSection.InteractionDynamics,
                    Text = "Escalate excuse complexity over time.",
                    SortOrder = 0
                }
            ]
        });

        var service = CreateService(completion, out _, rpThemeService);
        var session = new RolePlaySession
        {
            Id = "s4",
            PersonaName = "Becky",
            
            UseThemeAIGuidanceNotesInPrompt = true,
            ThemeAIGuidanceInfluencePercent = 55,
            MaxThemeAIGuidanceNotes = 4,
            AdaptiveState = new RolePlayAdaptiveState
            {
                ActiveScenarioId = "infidelity-public-facade",
                CurrentNarrativePhase = DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Committed
            }
        };

        await service.ContinueAsync(
            session,
            ContinueAsActor.Npc,
            customActorName: null,
            intent: PromptIntent.Message,
            promptText: "Continue naturally.");

        Assert.Single(completion.Prompts);
        var prompt = completion.Prompts[0];
        Assert.Contains("Theme AI Guidance (soft hints, influence=55%):", prompt, StringComparison.Ordinal);
        Assert.Contains("Escalate excuse complexity over time.", prompt, StringComparison.Ordinal);
    }

    private static RolePlayContinuationService CreateService(
        QueueCompletionClient completion,
        out RecordingDebugEventSink debugSink,
        IRPThemeService? rpThemeService = null)
    {
        debugSink = new RecordingDebugEventSink();

        return new RolePlayContinuationService(
            completion,
            new StubModelResolutionService(),
            new StubModelSettingsService(),
            new RolePlayTestFactory.NullScenarioService(),
            new AllowAllPromptDealbreakerService(),
            new EmptyThemePreferenceService(),
            new NullIntensityProfileService(),
            new NullSteeringProfileService(),
            new StubScenarioGuidanceContextFactory(),
            debugSink,
                NullLogger<RolePlayContinuationService>.Instance,
                diagnosticsService: null,
                rpThemeService: rpThemeService);
    }

    private sealed class QueueCompletionClient : ICompletionClient
    {
        private readonly Queue<string> _responses;

        public QueueCompletionClient(IEnumerable<string> responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<string> Prompts { get; } = [];

        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            Prompts.Add(prompt);
            if (_responses.Count == 0)
            {
                return Task.FromResult("fallback narrative");
            }

            return Task.FromResult(_responses.Dequeue());
        }

        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult("unused");

        public async Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            var content = await GenerateAsync(prompt, resolved, cancellationToken);
            await onChunk(content);
            return content;
        }

        public async Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            await onChunk("unused");
            return "unused";
        }

        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(bool Success, string Message)> CheckModelHealthAsync(string providerBaseUrl, string chatCompletionsPath, int timeoutSeconds, string? decryptedApiKey, string modelIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult((true, "ok"));
    }

    private sealed class StubModelResolutionService : IModelResolutionService
    {
        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResolvedModel(
                ProviderBaseUrl: "http://localhost",
                ChatCompletionsPath: "/v1/chat/completions",
                ProviderTimeoutSeconds: 30,
                ApiKeyEncrypted: null,
                ModelIdentifier: "test-model",
                Temperature: 0.7,
                TopP: 0.9,
                MaxTokens: 400,
                ProviderName: "test-provider",
                IsSessionOverride: false));
        }
    }

    private sealed class StubModelSettingsService : IModelSettingsService
    {
        public ModelSettings GetSettings(string sessionId) => new();

        public void UpdateSettings(string sessionId, ModelSettings settings)
        {
        }

        public void ClearSettings(string sessionId)
        {
        }
    }

    private sealed class AllowAllPromptDealbreakerService : IPromptDealbreakerService
    {
        public Task<PromptDealbreakerResult> ValidateAsync(string text, string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PromptDealbreakerResult { IsAllowed = true });
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
            => Task.FromResult(true);

        public Task<int> AutoLinkToCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class NullIntensityProfileService : IIntensityProfileService
    {
        public Task<IntensityProfile> CreateAsync(
            string name,
            string description,
            IntensityLevel intensity,
            int buildUpPhaseOffset,
            int committedPhaseOffset,
            int approachingPhaseOffset,
            int climaxPhaseOffset,
            int resetPhaseOffset,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<List<IntensityProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<IntensityProfile>());

        public Task<IntensityProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<IntensityProfile?>(null);

        public Task<IntensityProfile?> UpdateAsync(
            string id,
            string name,
            string description,
            IntensityLevel intensity,
            int buildUpPhaseOffset,
            int committedPhaseOffset,
            int approachingPhaseOffset,
            int climaxPhaseOffset,
            int resetPhaseOffset,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IntensityProfile?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
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
            => Task.FromResult(true);
    }

    private sealed class StubScenarioGuidanceContextFactory : IScenarioGuidanceContextFactory
    {
        public Task<ScenarioGuidanceContext> CreateAsync(ScenarioGuidanceInput input, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScenarioGuidanceContext(
                Phase: input.CurrentPhase,
                ActiveScenarioId: input.ActiveScenarioId,
                GuidanceText: "Keep pacing coherent.",
                ExcludedScenarioIds: []));
        }
    }

    private sealed class StubRpThemeService : IRPThemeService
    {
        private readonly RPTheme _theme;

        public StubRpThemeService(RPTheme theme)
        {
            _theme = theme;
        }

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

        public Task<IReadOnlyList<RPTheme>> ListThemesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPTheme>>([_theme]);

        public Task<IReadOnlyList<RPTheme>> ListThemesByProfileAsync(string profileId, bool includeDisabled = false, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPTheme>>([_theme]);

        public Task<RPTheme?> GetThemeAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Equals(id, _theme.Id, StringComparison.OrdinalIgnoreCase) ? _theme : null);

        public Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RPThemeProfileThemeAssignment> SaveProfileAssignmentAsync(RPThemeProfileThemeAssignment assignment, CancellationToken cancellationToken = default)
            => Task.FromResult(assignment);

        public Task<IReadOnlyList<RPThemeProfileThemeAssignment>> ListProfileAssignmentsAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RPThemeProfileThemeAssignment>>([]);

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
    }

    private sealed class RecordingDebugEventSink : IRolePlayDebugEventSink
    {
        public List<RolePlayDebugEventRecord> Records { get; } = [];

        public Task WriteAsync(RolePlayDebugEventRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}
