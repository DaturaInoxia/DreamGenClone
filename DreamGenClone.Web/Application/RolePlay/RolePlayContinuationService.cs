using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Logging;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Web.Application.Models;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NarrativePhase = DreamGenClone.Domain.RolePlay.NarrativePhase;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayContinuationService : IRolePlayContinuationService
{
    private const int NarrativeValidationRetryLimit = 1;
    private const int NarrativeQuotedBlockRetryThreshold = 2;
    private const int NarrativeQuotedBlockHardViolationThreshold = 4;
    private const double NarrativeQuotedTextRatioRetryThreshold = 0.20;

    // Number of non-excluded interactions during which other characters are suppressed from
    // narrative focus, allowing the persona-partner relationship to be established first.
    // Other characters remain in the scene but unnamed and unaddressed until this threshold is passed.
    private const int OpeningPeripheralTurnCount = 6;

    private static readonly Regex QuotedBlockRegex = new("\"[^\"\\r\\n]{2,}\"", RegexOptions.Compiled);
    private static readonly Regex FirstPersonLeakRegex = new("\\b(I|me|my|mine|myself)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CharacterInteriorityRegex = new("\\b[A-Z][a-zA-Z'-]{1,}\\s+(thought|felt|wondered|remembered|realized|decided|knew)\\b", RegexOptions.Compiled);
    private static readonly Regex DialogueAttributionRegex = new("\\b(said|asked|whispered|murmured|replied|snapped|called)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly IModelSettingsService _modelSettingsService;
    private readonly IScenarioService _scenarioService;
    private readonly IPromptDealbreakerService _dealbreakerService;
    private readonly IThemePreferenceService _themePreferenceService;
    private readonly IIntensityProfileService _intensityProfileService;
    private readonly ISteeringProfileService _steeringProfileService;
    private readonly IScenarioGuidanceContextFactory _scenarioGuidanceContextFactory;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly IRPThemeService? _rpThemeService;
    private readonly IRolePlayDiagnosticsService? _diagnosticsService;
    private readonly IClimaxBeatRepository? _climaxBeatRepository;
    private readonly IHusbandAwarenessProfileService? _husbandAwarenessProfileService;
    private readonly bool _enableLocationServices;
    private readonly bool _includeCandidateMenuWhileObserving;
    private readonly IOptions<RolePlayMemoryOptions>? _memoryOptions;
    private readonly RolePlayPromptBuilder _promptBuilder;
    private readonly ActorProfileResolver _actorProfileResolver;
    private readonly IPhaseRuleOfThumbRepository _phaseRuleOfThumbRepository;
    private readonly ILogger<RolePlayContinuationService> _logger;

    public RolePlayContinuationService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        IModelSettingsService modelSettingsService,
        IScenarioService scenarioService,
        IPromptDealbreakerService dealbreakerService,
        IThemePreferenceService themePreferenceService,
        IIntensityProfileService toneProfileService,
        ISteeringProfileService styleProfileService,
        IScenarioGuidanceContextFactory scenarioGuidanceContextFactory,
        IRolePlayDebugEventSink debugEventSink,
        RolePlayPromptBuilder promptBuilder,
        ActorProfileResolver actorProfileResolver,
        IPhaseRuleOfThumbRepository phaseRuleOfThumbRepository,
        ILogger<RolePlayContinuationService> logger,
        IRolePlayDiagnosticsService? diagnosticsService = null,
        IRPThemeService? rpThemeService = null,
        IClimaxBeatRepository? climaxBeatRepository = null,
        IHusbandAwarenessProfileService? husbandAwarenessProfileService = null,
        IOptions<RolePlayDecisionOptions>? rolePlayDecisionOptions = null,
        IOptions<RolePlayFeatureFlagsOptions>? rolePlayFeatureFlagsOptions = null,
        IOptions<RolePlayMemoryOptions>? memoryOptions = null)
    {
        _completionClient = completionClient;
        _modelResolver = modelResolver;
        _modelSettingsService = modelSettingsService;
        _scenarioService = scenarioService;
        _dealbreakerService = dealbreakerService;
        _themePreferenceService = themePreferenceService;
        _intensityProfileService = toneProfileService;
        _steeringProfileService = styleProfileService;
        _scenarioGuidanceContextFactory = scenarioGuidanceContextFactory;
        _debugEventSink = debugEventSink;
        _promptBuilder = promptBuilder;
        _actorProfileResolver = actorProfileResolver;
        _phaseRuleOfThumbRepository = phaseRuleOfThumbRepository;
        _rpThemeService = rpThemeService;
        _diagnosticsService = diagnosticsService;
        _climaxBeatRepository = climaxBeatRepository;
        _husbandAwarenessProfileService = husbandAwarenessProfileService;
        _enableLocationServices = rolePlayDecisionOptions?.Value.EnableLocationServices ?? true;
        _includeCandidateMenuWhileObserving = rolePlayFeatureFlagsOptions?.Value.IncludeCandidateMenuWhileObserving ?? true;
        _memoryOptions = memoryOptions;
        _logger = logger;
    }

    public async Task<RolePlayInteraction> ContinueAsync(
        RolePlaySession session,
        ContinueAsActor actor,
        string? customActorName,
        PromptIntent intent,
        string promptText,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default,
        int? turnIndex = null,
        int? positionInTurn = null,
        int? turnActorCount = null)
    {
        await ValidateDirectiveTextAsync(session, promptText, cancellationToken);

        var correlationId = Guid.NewGuid().ToString("N");
        var actorLabel = string.IsNullOrWhiteSpace(customActorName) ? actor.ToString() : customActorName;
        var diagnostics = _diagnosticsService is null
            ? null
            : await _diagnosticsService.GetSnapshotAsync(session.Id, correlationId, cancellationToken);

        var prompt = await BuildPromptViaBuilderAsync(session, actor, customActorName, intent, promptText, cancellationToken,
            turnIndex, positionInTurn, turnActorCount);
        
        // Capture prompt text for storage (best-effort, truncated to reduce size)
        string? capturedPromptText = null;
        try
        {
            capturedPromptText = PromptTextTruncation.TrimInteractionHistoryBlock(prompt);
            if (capturedPromptText != null)
            {
                _logger.LogInformation("Successfully captured prompt text for interaction in session {SessionId}, length: {Length}", 
                    session.Id, capturedPromptText.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture prompt text for interaction in session {SessionId}", session.Id);
        }
        
        if (diagnostics is not null)
        {
            prompt = $"[V2 Diagnostics: candidates={diagnostics.CandidateEvaluations.Count}, transitions={diagnostics.TransitionEvents.Count}, decisions={diagnostics.DecisionPoints.Count}]\n{prompt}";
            _logger.LogInformation(
                RolePlayV2LogEvents.DiagnosticsSnapshotPublished,
                diagnostics.SessionId,
                diagnostics.CorrelationId,
                diagnostics.CandidateEvaluations.Count,
                diagnostics.TransitionEvents.Count,
                diagnostics.DecisionPoints.Count,
                diagnostics.CompatibilityErrors.Count);
        }
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            EventKind = "PromptBuilt",
            Severity = "Info",
            ActorName = actorLabel,
            Summary = $"Prompt prepared for {actor} ({intent})",
            MetadataJson = JsonSerializer.Serialize(new
            {
                actor,
                customActorName,
                intent,
                prompt,
                promptLength = prompt.Length
            })
        }, cancellationToken);

        var sessionSettings = _modelSettingsService.GetSettings(session.Id);
        var resolved = await _modelResolver.ResolveAsync(
            AppFunction.RolePlayGeneration,
            sessionModelId: sessionSettings.SessionModelId,
            sessionTemperature: sessionSettings.SessionModelId != null ? sessionSettings.Temperature : null,
            sessionTopP: sessionSettings.SessionModelId != null ? sessionSettings.TopP : null,
            sessionMaxTokens: sessionSettings.SessionModelId != null ? sessionSettings.MaxTokens : null,
            cancellationToken: cancellationToken);
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            EventKind = "LlmRequestSent",
            Severity = "Info",
            ActorName = actorLabel,
            ModelIdentifier = resolved.ModelIdentifier,
            ProviderName = resolved.ProviderName,
            Summary = "Dispatching completion request",
            MetadataJson = JsonSerializer.Serialize(new
            {
                resolved.ModelIdentifier,
                resolved.ProviderName,
                resolved.ProviderBaseUrl,
                resolved.ChatCompletionsPath,
                resolved.Temperature,
                resolved.TopP,
                resolved.MaxTokens,
                resolved.ProviderTimeoutSeconds
            })
        }, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        string output;
        string? reasoningContent = null;
        try
        {
            if (onChunk is null)
            {
                var (content, reasoning) = await _completionClient.GenerateWithReasoningAsync(prompt, resolved, cancellationToken);
                output = content;
                reasoningContent = reasoning;
            }
            else
            {
                var (content, reasoning) = await _completionClient.StreamGenerateWithReasoningAsync(prompt, resolved, onChunk, cancellationToken);
                output = content;
                reasoningContent = reasoning;
            }
        }
        catch (Exception ex)
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                CorrelationId = correlationId,
                EventKind = "ErrorRaised",
                Severity = "Error",
                ActorName = actorLabel,
                ModelIdentifier = resolved.ModelIdentifier,
                ProviderName = resolved.ProviderName,
                Summary = "Completion request failed",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    ex.Message,
                    ExceptionType = ex.GetType().Name
                })
            }, cancellationToken);

            throw;
        }

        stopwatch.Stop();
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            EventKind = "LlmResponseReceived",
            Severity = "Info",
            ActorName = actorLabel,
            ModelIdentifier = resolved.ModelIdentifier,
            ProviderName = resolved.ProviderName,
            DurationMs = (int)stopwatch.ElapsedMilliseconds,
            Summary = "Completion response received",
            MetadataJson = JsonSerializer.Serialize(new
            {
                output,
                outputLength = output.Length,
                reasoningContent,
                reasoningLength = reasoningContent?.Length ?? 0,
                durationMs = stopwatch.ElapsedMilliseconds
            })
        }, cancellationToken);

        var interaction = new RolePlayInteraction
        {
            InteractionType = actor switch
            {
                ContinueAsActor.You => InteractionType.User,
                ContinueAsActor.Npc => InteractionType.Npc,
                ContinueAsActor.Custom => InteractionType.Custom,
                _ => InteractionType.System
            },
            ActorName = !string.IsNullOrWhiteSpace(customActorName)
                ? customActorName.Trim()
                : actor switch
                {
                    ContinueAsActor.You => string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim(),
                    ContinueAsActor.Npc => "NPC",
                    _ => "Custom"
                },
            Content = string.IsNullOrWhiteSpace(output) ? "(No output generated)" : output.Trim(),
            GeneratedByModelId = resolved.ModelIdentifier,
            GeneratedByModelName = resolved.ModelIdentifier,
            GeneratedByCommand = "Continue",
            GeneratedByProvider = resolved.ProviderName,
            GeneratedTemperature = resolved.Temperature,
            GeneratedTopP = resolved.TopP,
            GeneratedMaxTokens = resolved.MaxTokens,
            ReasoningContent = reasoningContent,
            NarrativePhaseAtCreation = session.AdaptiveState.CurrentPhase,
            PromptText = capturedPromptText
        };

        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            InteractionId = interaction.Id,
            EventKind = "InteractionPrepared",
            Severity = "Info",
            ActorName = interaction.ActorName,
            ModelIdentifier = resolved.ModelIdentifier,
            ProviderName = resolved.ProviderName,
            Summary = "Role-play interaction prepared from model output",
            MetadataJson = JsonSerializer.Serialize(new
            {
                interaction.Id,
                interaction.ActorName,
                interaction.InteractionType,
                interaction.Content
            })
        }, cancellationToken);

        _logger.LogInformation("Role-play continuation prepared for actor {Actor} in session {SessionId}", interaction.ActorName, session.Id);
        return interaction;
    }

    public async Task<RolePlayInteraction> ContinueNarrativeAsync(
        RolePlaySession session,
        string actorName,
        string promptText,
        CancellationToken cancellationToken = default,
        int? turnIndex = null,
        int? turnActorCount = null)
    {
        var narrativePrompt = string.IsNullOrWhiteSpace(promptText)
            ? "Synthesize the scene with vivid narrative description."
            : promptText;

        await ValidateDirectiveTextAsync(session, narrativePrompt, cancellationToken);

        var prompt = await BuildPromptViaBuilderAsync(
            session,
            ContinueAsActor.Npc,
            actorName,
            PromptIntent.Narrative,
            narrativePrompt,
            cancellationToken,
            turnIndex,
            positionInTurn: null,
            turnActorCount);

        // Capture prompt text for storage (best-effort, truncated to reduce size)
        string? capturedPromptText = null;
        try
        {
            capturedPromptText = PromptTextTruncation.TrimInteractionHistoryBlock(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture prompt text for narrative interaction in session {SessionId}", session.Id);
        }

        // Log the narrative prompt for debugging (same as character prompts).
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            EventKind = "PromptBuilt",
            Severity = "Info",
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Narrative" : actorName,
            Summary = $"Prompt prepared for Narrative ({PromptIntent.Narrative})",
            MetadataJson = JsonSerializer.Serialize(new
            {
                actor = actorName,
                intent = PromptIntent.Narrative.ToString(),
                prompt,
                promptLength = prompt.Length
            })
        }, cancellationToken);

        var settings = _modelSettingsService.GetSettings(session.Id);
        var resolved = await _modelResolver.ResolveAsync(
            AppFunction.RolePlayGeneration,
            sessionModelId: settings.SessionModelId,
            sessionTemperature: settings.SessionModelId != null ? settings.Temperature : null,
            sessionTopP: settings.SessionModelId != null ? settings.TopP : null,
            sessionMaxTokens: settings.SessionModelId != null ? settings.MaxTokens : null,
            cancellationToken: cancellationToken);

        var output = await GenerateNarrativeWithValidationAsync(session, prompt, resolved, cancellationToken);

        return new RolePlayInteraction
        {
            InteractionType = InteractionType.System,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Narrative" : actorName,
            Content = string.IsNullOrWhiteSpace(output) ? "(No output generated)" : output.Trim(),
            GeneratedByModelId = resolved.ModelIdentifier,
            GeneratedByModelName = resolved.ModelIdentifier,
            GeneratedByCommand = "Narrative",
            GeneratedByProvider = resolved.ProviderName,
            GeneratedTemperature = resolved.Temperature,
            GeneratedTopP = resolved.TopP,
            GeneratedMaxTokens = resolved.MaxTokens,
            NarrativePhaseAtCreation = session.AdaptiveState.CurrentPhase,
            PromptText = capturedPromptText
        };
    }

    public async Task<ContinueAsResult> ContinueBatchAsync(
        RolePlaySession session,
        IReadOnlyList<ContinueAsActor> actors,
        bool includeNarrative,
        string? customActorName,
        string promptText,
        CancellationToken cancellationToken = default)
    {
        var result = new ContinueAsResult { Success = true };
        foreach (var actor in ContinueAsOrdering.OrderDistinct(actors))
        {
            var interaction = await ContinueAsync(
                session,
                actor,
                customActorName,
                PromptIntent.Message,
                promptText,
                null,
                cancellationToken);
            result.ParticipantOutputs.Add(interaction);
        }

        if (includeNarrative)
        {
            var narrativePrompt = string.IsNullOrWhiteSpace(promptText)
                ? "Synthesize the scene with vivid narrative description."
                : promptText;

            await ValidateDirectiveTextAsync(session, narrativePrompt, cancellationToken);

            var prompt = await BuildPromptViaBuilderAsync(
                session,
                ContinueAsActor.Npc,
                null,
                PromptIntent.Narrative,
                narrativePrompt,
                cancellationToken);

            // Capture prompt text for storage (best-effort, truncated to reduce size)
            string? capturedPromptText = null;
            try
            {
                capturedPromptText = PromptTextTruncation.TrimInteractionHistoryBlock(prompt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture prompt text for batch narrative interaction in session {SessionId}", session.Id);
            }

            var narrativeSettings = _modelSettingsService.GetSettings(session.Id);
            var narrativeResolved = await _modelResolver.ResolveAsync(
                AppFunction.RolePlayGeneration,
                sessionModelId: narrativeSettings.SessionModelId,
                sessionTemperature: narrativeSettings.SessionModelId != null ? narrativeSettings.Temperature : null,
                sessionTopP: narrativeSettings.SessionModelId != null ? narrativeSettings.TopP : null,
                sessionMaxTokens: narrativeSettings.SessionModelId != null ? narrativeSettings.MaxTokens : null,
                cancellationToken: cancellationToken);
            var output = await GenerateNarrativeWithValidationAsync(session, prompt, narrativeResolved, cancellationToken);
            result.NarrativeOutput = new RolePlayInteraction
            {
                InteractionType = InteractionType.System,
                ActorName = "Narrative",
                Content = string.IsNullOrWhiteSpace(output) ? "(No output generated)" : output.Trim(),
                GeneratedByModelId = narrativeResolved.ModelIdentifier,
                GeneratedByModelName = narrativeResolved.ModelIdentifier,
                GeneratedByCommand = "Narrative",
                GeneratedByProvider = narrativeResolved.ProviderName,
                GeneratedTemperature = narrativeResolved.Temperature,
                GeneratedTopP = narrativeResolved.TopP,
                GeneratedMaxTokens = narrativeResolved.MaxTokens,
                NarrativePhaseAtCreation = session.AdaptiveState.CurrentPhase,
                PromptText = capturedPromptText
            };
        }

        return result;
    }

    private async Task ValidateDirectiveTextAsync(RolePlaySession session, string directiveText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.SelectedThemeProfileId)
            || string.IsNullOrWhiteSpace(directiveText))
        {
            return;
        }

        var validation = await _dealbreakerService.ValidateAsync(directiveText, session.SelectedThemeProfileId, cancellationToken);
        if (!validation.IsAllowed)
        {
            throw new InvalidOperationException(validation.Message ?? "Prompt violated a hard dealbreaker.");
        }
    }

    private async Task<string> BuildPromptViaBuilderAsync(
        RolePlaySession session,
        ContinueAsActor actor,
        string? customActorName,
        PromptIntent intent,
        string promptText,
        CancellationToken cancellationToken,
        int? turnIndex = null,
        int? positionInTurn = null,
        int? turnActorCount = null)
    {
        // ── Resolve variant from intent ──
        var variant = intent == PromptIntent.Narrative
            ? PromptVariant.Narrative
            : PromptVariant.Character;

        // ── Fetch scenario once (used for actor profile, character data, and opening couple filter) ──
        var scenario = !string.IsNullOrWhiteSpace(session.ScenarioId)
            ? await _scenarioService.GetScenarioAsync(session.ScenarioId)
            : null;

        // ── Resolve actor profile (uses all characters — filtering happens later for context) ──
        var allScenarioCharacters = scenario?.Characters
            .Select(c => new ScenarioCharacter(c.Id, c.Name ?? string.Empty, c.Role))
            .ToList() ?? [];

        var actorProfile = _actorProfileResolver.Resolve(actor, customActorName, intent, session, allScenarioCharacters);

        // ── Resolve phase ──
        var phase = session.AdaptiveState.CurrentPhase.ToString();

        // ── Opening phase: resolve couple IDs for character filtering ──
        // The opening scene is exclusively about the couple (user + spouse).
        // Other characters are not introduced yet — the prompt must not know they exist.
        HashSet<string>? openingCoupleIds = null;
        if (phase == nameof(NarrativePhase.Opening) && scenario is not null)
        {
            openingCoupleIds = ResolveOpeningCoupleIds(session, scenario);
        }

        // ── Filter scenario characters for the prompt context ──
        // During opening, only the couple characters are visible to all slots.
        var scenarioCharacters = openingCoupleIds is not null
            ? allScenarioCharacters.Where(c => openingCoupleIds.Contains(c.Id)).ToList()
            : allScenarioCharacters;

        // ── Filter characters excluded from current scene location by affinity ──
        // Characters with an Excluded affinity for the current location must not
        // appear in character data, behavioral frames, or any prompt content.
        // This prevents the AI from writing excluded characters into the scene,
        // which would then cause location detection to incorrectly place them there.
        var currentLocation = session.AdaptiveState.CurrentSceneLocation;
        if (!string.IsNullOrWhiteSpace(currentLocation) && scenario is not null)
        {
            var excludedIds = scenario.Characters
                .Where(c => c.LocationAffinities.Any(a =>
                    a.AffinityType == DreamGenClone.Web.Domain.Scenarios.AffinityType.Excluded &&
                    string.Equals(a.LocationName, currentLocation, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (excludedIds.Count > 0)
            {
                scenarioCharacters = scenarioCharacters
                    .Where(c => !excludedIds.Contains(c.Id))
                    .ToList();
            }
        }

        // ── Resolve MaxPromptChars (fail-fast if missing) ──
        var maxPromptChars = session.MaxPromptChars ?? 35000;

        // ── Build character details for CharacterDataSlot ──
        IReadOnlyDictionary<string, ResolvedCharacterDetail>? charDetails = null;
        if (scenario is not null && scenario.Characters.Count > 0)
        {
            var dict = new Dictionary<string, ResolvedCharacterDetail>(StringComparer.OrdinalIgnoreCase);
            foreach (var ch in scenario.Characters)
            {
                // During opening, only include the couple characters
                if (openingCoupleIds is not null && !openingCoupleIds.Contains(ch.Id)) continue;
                if (string.IsNullOrWhiteSpace(ch.Id)) continue;
                var appearance = PhysicalAttributesFormatter.FormatBlock(ch.PhysicalAttributes, ch.Gender);
                dict[ch.Id] = new ResolvedCharacterDetail
                {
                    Description = ch.Description,
                    AppearanceText = appearance,
                    ComparisonText = null,
                    Gender = ch.Gender,
                };
            }
            charDetails = dict;
        }

        // ── Resolve phase Rule-of-Thumb from DB (fail-fast if missing per FR-014) ──
        var phaseRoTRow = await _phaseRuleOfThumbRepository.GetByPhaseAsync(phase, cancellationToken);
        if (phaseRoTRow is null || string.IsNullOrWhiteSpace(phaseRoTRow.RuleOfThumbText))
        {
            throw new InvalidOperationException(
                $"MissingPromptConfig: WritingStyle.PhaseRuleOfThumb is missing or empty. " +
                $"Session phase: '{phase}'. FR-014 requires a PhaseRuleOfThumb row for every phase.");
        }

        // ── Resolve scenario guidance context (behavioral frames + stat state texts) ──
        IReadOnlyDictionary<string, string>? characterBehavioralFrames = null;
        IReadOnlyDictionary<string, string>? characterStatStateTexts = null;
        if (scenario is not null)
        {
            var runtimeStats = session.AdaptiveState.CharacterSnapshots
                .Where(s => !string.IsNullOrWhiteSpace(s.CharacterId))
                .ToDictionary(s => s.CharacterId!, StringComparer.OrdinalIgnoreCase);

            var snapshots = runtimeStats.Values.ToList();
            var avgDesire = snapshots.Count > 0 ? snapshots.Average(s => s.Desire) : 50.0;
            var avgRestraint = snapshots.Count > 0 ? snapshots.Average(s => s.Restraint) : 50.0;
            var avgDominance = snapshots.Count > 0 ? snapshots.Average(s => s.Dominance) : 50.0;
            var avgLoyalty = snapshots.Count > 0 ? snapshots.Average(s => s.Loyalty) : 50.0;

            var guidanceInput = new ScenarioGuidanceInput(
                SessionId: session.Id,
                CurrentPhase: phase,
                ActiveScenarioId: session.ScenarioId,
                VariantId: null,
                AverageDesire: avgDesire,
                AverageRestraint: avgRestraint,
                AverageDominance: avgDominance,
                AverageLoyalty: avgLoyalty,
                SelectedWillingnessProfileId: session.AdaptiveState.SelectedWillingnessProfileId,
                CharacterEncounterProfileIds: session.AdaptiveState.CharacterEncounterProfileIds,
                Characters: scenarioCharacters,
                SuppressedScenarioIds: [],
                CharacterRuntimeStats: runtimeStats,
                SelectedResistanceProfileId: session.AdaptiveState.SelectedResistanceProfileId);

            var guidance = await _scenarioGuidanceContextFactory.CreateAsync(guidanceInput, cancellationToken);
            characterBehavioralFrames = guidance.CharacterBehavioralFrames;
            characterStatStateTexts = guidance.CharacterStatStateTexts;
        }

        // ── Build context for builder ──
        var defaultStartingLocationName = await ResolveDefaultStartingLocationAsync(
            session.ScenarioId, session.AdaptiveState.CurrentSceneLocation, cancellationToken);
        var context = new PromptBuildContext
        {
            Session = session,
            ActorProfile = actorProfile,
            Variant = variant,
            Phase = phase,
            TurnIndex = turnIndex,
            PositionInTurn = positionInTurn,
            TurnActorCount = turnActorCount,
            PromptText = promptText,
            MaxPromptChars = maxPromptChars,
            WorldState = null,
            Scenario = new ResolvedScenarioData
            {
                ScenarioId = session.ScenarioId,
                Name = string.Empty,
                Description = string.Empty,
                PlotDescription = string.Empty,
                WorldDescription = string.Empty,
                TimeFrame = null,
                Goals = [],
                Conflicts = [],
                WorldRules = [],
                EnvironmentalDetails = [],
                NarrativeGuidelines = [],
                Characters = scenarioCharacters,
                LocationNames = [],
                DefaultSteeringProfileId = null,
                DefaultIntensityProfileId = null,
                DefaultStartingLocationName = defaultStartingLocationName,
            },
            Theme = await ResolveThemeAsync(session, phase, cancellationToken),
            Intensity = await ResolveIntensityAsync(session, phase, cancellationToken),
            WritingStyle = await ResolveWritingStyleAsync(session, phaseRoTRow, cancellationToken),
            EncounterSummaries = [],
            RecentInteractions = session.Interactions
                .Where(i => !i.IsExcluded)
                .TakeLast(session.ContextWindowSize > 0 ? session.ContextWindowSize : 12)
                .ToList(),
            CharacterDetails = charDetails,
            CharacterBehavioralFrames = characterBehavioralFrames,
            CharacterStatStateTexts = characterStatStateTexts,
        };

        // ── Delegate to builder ──
        return await _promptBuilder.BuildAsync(context, cancellationToken);
    }

    /// <summary>
    /// Resolves the character IDs that form the opening couple (user + spouse).
    /// During the Opening phase, only these characters are visible to the prompt —
    /// other characters are not introduced yet.
    /// Uses <see cref="RolePlaySession.PersonaCharacterId"/> to identify the persona
    /// and matches <see cref="Character.RelationTargetId"/> (which stores character IDs, not names).
    /// </summary>
    private static HashSet<string> ResolveOpeningCoupleIds(
        RolePlaySession session,
        DreamGenClone.Web.Domain.Scenarios.Scenario scenario)
    {
        var coupleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find the persona character by its session-tracked character ID.
        // IsPersona flag may not be set on scenario characters — the authoritative
        // persona identity is session.PersonaCharacterId.
        var personaId = session.PersonaCharacterId;
        if (!string.IsNullOrWhiteSpace(personaId))
        {
            coupleIds.Add(personaId);

            // Find the spouse: NPC whose RelationTargetId points to the persona's character ID.
            // RelationTargetId stores character IDs (GUIDs), not names.
            var spouseChar = scenario.Characters.FirstOrDefault(c =>
                !string.IsNullOrWhiteSpace(c.RelationTargetId) &&
                string.Equals(c.RelationTargetId.Trim(), personaId, StringComparison.OrdinalIgnoreCase));
            if (spouseChar is not null)
                coupleIds.Add(spouseChar.Id);
        }

        return coupleIds;
    }

    private async Task<string?> ResolveDefaultStartingLocationAsync(
        string? scenarioId, string? currentSceneLocation, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(currentSceneLocation))
            return null;
        if (string.IsNullOrWhiteSpace(scenarioId))
            return null;

        var scenario = await _scenarioService.GetScenarioAsync(scenarioId);
        if (scenario is null || string.IsNullOrWhiteSpace(scenario.DefaultStartingLocationId))
            return null;

        var location = scenario.Locations.FirstOrDefault(
            l => string.Equals(l.Id, scenario.DefaultStartingLocationId, StringComparison.OrdinalIgnoreCase));
        return location?.Name;
    }

    private async Task<ResolvedWritingStyleData> ResolveWritingStyleAsync(
        RolePlaySession session,
        PhaseRuleOfThumbRow phaseRoT,
        CancellationToken cancellationToken)
    {
        var desc = string.Empty;
        var example = string.Empty;
        var profileDefaultRoT = string.Empty;
        var styleHint = string.Empty;

        var selectedStyleProfileId = session.SelectedSteeringProfileId;
        if (!string.IsNullOrWhiteSpace(selectedStyleProfileId))
        {
            var styleProfile = await _steeringProfileService.GetAsync(selectedStyleProfileId, cancellationToken);
            if (styleProfile is not null)
            {
                desc = styleProfile.Description ?? string.Empty;
                example = styleProfile.Example ?? string.Empty;
                profileDefaultRoT = styleProfile.RuleOfThumb ?? string.Empty;
            }
        }

        // Resolve scenario style for hint
        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                styleHint = string.Join(" / ", new[]
                {
                    scenario.Narrative.ProseStyle,
                    scenario.Narrative.NarrativeTone
                }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            }
        }

        // Fail-fast on missing profile default (FR-014)
        if (string.IsNullOrWhiteSpace(profileDefaultRoT))
        {
            throw new InvalidOperationException(
                "MissingPromptConfig: WritingStyle.ProfileDefaultRuleOfThumb is missing or empty. FR-014 requires a profile default Rule-of-Thumb.");
        }

        return new ResolvedWritingStyleData
        {
            Description = desc,
            Example = example,
            ProfileDefaultRuleOfThumb = profileDefaultRoT,
            PhaseRuleOfThumb = phaseRoT.RuleOfThumbText,
            StyleHint = styleHint,
        };
    }

    private async Task<ResolvedIntensityData> ResolveIntensityAsync(
        RolePlaySession session, string phase, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.SelectedIntensityProfileId))
            return new ResolvedIntensityData();

        var profile = await _intensityProfileService.GetAsync(session.SelectedIntensityProfileId, cancellationToken);
        if (profile is null)
            return new ResolvedIntensityData();

        // Compute phase-adjusted intensity level
        var phaseEnum = global::DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Opening;
        Enum.TryParse<global::DreamGenClone.Domain.StoryAnalysis.NarrativePhase>(phase, out phaseEnum);
        var offset = profile.GetPhaseOffset(phaseEnum);
        var levelCount = Enum.GetValues<IntensityLevel>().Length;
        var adjustedValue = Math.Clamp((int)profile.Intensity + offset, 0, levelCount - 1);
        var resolvedLabel = ((IntensityLevel)adjustedValue).ToString();

        _logger.LogDebug(
            "ResolveIntensity: SessionId={SessionId} Phase={Phase} Base={Base} Offset={Offset} Resolved={Resolved}",
            session.Id, phase, profile.Intensity, offset, resolvedLabel);

        // Resolve scene direction from phase + active theme
        var activeTheme = session.AdaptiveState.PrimaryThemeId is not null
            ? await _rpThemeService?.GetThemeAsync(session.AdaptiveState.PrimaryThemeId, cancellationToken)
            : null;
        var sceneDirection = SceneDirectionResolver.Resolve(phase, activeTheme, ClimaxSubPhase.None, PromptIntent.Message);

        return new ResolvedIntensityData
        {
            BaseLevel = profile.Intensity,
            Description = profile.Description,
            AdaptiveLevel = (IntensityLevel)adjustedValue,
            ResolvedLabel = resolvedLabel,
            SceneDirection = sceneDirection,
        };
    }

    private async Task<ResolvedThemeData> ResolveThemeAsync(
        RolePlaySession session, string phase, CancellationToken cancellationToken)
    {
        if (_rpThemeService is null)
            return new ResolvedThemeData();

        var themeId = session.AdaptiveState.PrimaryThemeId;

        // Fallback: if PrimaryThemeId isn't synced from the V2 tracker yet,
        // use the first active theme from ThemeScores (set by theme machine).
        if (string.IsNullOrWhiteSpace(themeId) && session.AdaptiveState.ThemeScores is { Count: > 0 })
        {
            themeId = session.AdaptiveState.ThemeScores.Keys.First();
        }

        if (string.IsNullOrWhiteSpace(themeId))
            return new ResolvedThemeData();

        var theme = await _rpThemeService.GetThemeAsync(themeId, cancellationToken);
        if (theme is null)
            return new ResolvedThemeData();

        // Filter phase guidance for the current narrative phase
        var phaseEnum = Enum.TryParse<NarrativePhase>(phase, out var p) ? p : NarrativePhase.Opening;
        var phaseGuidance = theme.PhaseGuidance
            .Where(g => g.Phase == phaseEnum)
            .ToList();

        return new ResolvedThemeData
        {
            ActiveTheme = theme,
            PhaseGuidanceLines = phaseGuidance
                .Select(g => g.GuidanceText)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim())
                .ToList(),
            PhaseDirectiveLines = phaseGuidance
                .Select(g => g.DirectiveText)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim())
                .ToList(),
            AiGuidanceNotes = theme.AIGenerationNotes
                .Where(n => !string.IsNullOrWhiteSpace(n.Text))
                .ToList(),
        };
    }

    private async Task AppendObservingCandidateMenuAsync(
        StringBuilder sb,
        RolePlaySession session,
        string currentPhase,
        CancellationToken cancellationToken)
    {
        if (_rpThemeService is null)
        {
            return;
        }

        var themeIds = (session.SessionThemeSelections ?? [])
            .Select(x => x.ThemeId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (themeIds.Count < 2)
        {
            // Need at least two candidates for the menu to be meaningful as an option space.
            return;
        }

        var candidateLabels = new List<string>(themeIds.Count);
        foreach (var themeId in themeIds)
        {
            try
            {
                var theme = await _rpThemeService.GetThemeAsync(themeId, cancellationToken);
                if (theme is null)
                {
                    continue;
                }
                var label = string.IsNullOrWhiteSpace(theme.Label) ? theme.Id : theme.Label;
                candidateLabels.Add(label);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Unable to resolve candidate theme label for {ThemeId} in session {SessionId} (observing menu).",
                    themeId,
                    session.Id);
            }
        }

        if (candidateLabels.Count < 2)
        {
            return;
        }

        sb.AppendLine("Theme Observation (no theme committed yet):");
        sb.AppendLine($"- Current narrative phase: {currentPhase}");
        sb.AppendLine($"- Candidates: {string.Join(", ", candidateLabels)}");
        sb.AppendLine("- Let natural events emerge from the persona, characters and recent context. Do not steer the scene toward any single candidate; treat this list only as awareness of the option space.");
    }

    private static void InjectSessionMemoryBlock(
        StringBuilder sb,
        List<DreamGenClone.Domain.RolePlay.EncounterSummaryRecord> summaries,
        int effectiveMilestones,
        int effectiveArcCompletions,
        int effectiveEncounterCompletions,
        int currentCycleIndex)
    {
        // Arc completions: take most recent N (DESC by OccurredUtc, then reverse to chronological)
        var arcCompletions = summaries
            .Where(s => s.SummaryType == DreamGenClone.Domain.RolePlay.EncounterSummaryType.ArcCompletion)
            .OrderByDescending(s => s.OccurredUtc)
            .Take(effectiveArcCompletions)
            .OrderBy(s => s.OccurredUtc)
            .ToList();

        // Encounter completions: only current arc, take most recent N (DESC, then reverse to chronological)
        var encounterCompletions = summaries
            .Where(s => s.SummaryType == DreamGenClone.Domain.RolePlay.EncounterSummaryType.EncounterCompletion && s.CycleIndex == currentCycleIndex)
            .OrderByDescending(s => s.OccurredUtc)
            .Take(effectiveEncounterCompletions)
            .OrderBy(s => s.OccurredUtc)
            .ToList();

        // Phase milestones: only current arc, take most recent N (DESC, then reverse)
        var milestones = summaries
            .Where(s => s.SummaryType == DreamGenClone.Domain.RolePlay.EncounterSummaryType.PhaseMilestone && s.CycleIndex == currentCycleIndex)
            .OrderByDescending(s => s.OccurredUtc)
            .Take(effectiveMilestones)
            .OrderBy(s => s.OccurredUtc)
            .ToList();

        if (arcCompletions.Count == 0 && encounterCompletions.Count == 0 && milestones.Count == 0)
        {
            return;
        }

        sb.AppendLine("Session Memory:");

        // Render order: arc completions → encounter completions → phase milestones
        foreach (var record in arcCompletions)
        {
            sb.AppendLine($"[Arc {record.CycleIndex + 1} Complete — {record.CharacterId}]");
            if (!string.IsNullOrWhiteSpace(record.ActiveSummary))
            {
                sb.AppendLine(record.ActiveSummary);
            }
        }

        foreach (var record in encounterCompletions)
        {
            // Per-character encounter memory — number as "N/M" where M is the total encounters
            // in the arc (we don't know M cheaply, so we render the encounter number alone with
            // the arc index for context; the LLM prose already says "encounter N of arc").
            sb.AppendLine($"[Encounter {record.EncounterNumber} — {record.CharacterId}]");
            if (!string.IsNullOrWhiteSpace(record.ActiveSummary))
            {
                sb.AppendLine(record.ActiveSummary);
            }
        }

        foreach (var record in milestones)
        {
            sb.AppendLine($"[{record.FromPhase} → {record.ToPhase} — {record.CharacterId}]");
            if (!string.IsNullOrWhiteSpace(record.ActiveSummary))
            {
                sb.AppendLine(record.ActiveSummary);
            }
        }
    }

    private static void AppendActiveThemeContract(StringBuilder sb, RPTheme? activeTheme, string phase)
    {
        if (activeTheme is null)
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(activeTheme.Label) ? activeTheme.Id : activeTheme.Label;
        sb.AppendLine("Active Adaptive Theme Contract:");
        sb.AppendLine($"- Theme: {label} ({activeTheme.Id})");

        if (!string.IsNullOrWhiteSpace(activeTheme.Description))
        {
            sb.AppendLine($"- Theme Description: {activeTheme.Description.Trim()}");
        }

        var phaseGuidance = activeTheme.PhaseGuidance
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace(x.GuidanceText))
            .OrderBy(x => x.GuidanceText, StringComparer.OrdinalIgnoreCase)
            .Select(x => Regex.Replace(x.GuidanceText.Trim(), @"\[Beat[^\]]*\]", "", RegexOptions.IgnoreCase).Trim())
            .Select(x => Regex.Replace(x, @"\[Aftermath:husband-contrast\]", "", RegexOptions.IgnoreCase).Trim())
            .ToList();
        if (phaseGuidance.Count > 0)
        {
            sb.AppendLine($"- Required Phase Constraints ({phase}) — these directives are hard requirements for this response:");
            foreach (var line in phaseGuidance)
            {
                sb.AppendLine($"  - {line}");
            }
        }

        var keyEmphasis = activeTheme.GuidancePoints
            .Where(x => string.Equals(x.Phase.ToString(), phase, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.PointType == RPThemeGuidancePointType.Emphasis)
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Text.Trim())
            .ToList();
        if (keyEmphasis.Count > 0)
        {
            sb.AppendLine($"- Key Emphasis ({phase}): {string.Join(" | ", keyEmphasis)}");
        }

        sb.AppendLine("- STEERING RANK: This theme contract is the highest-ranking directive in this prompt. Character behavioral frames and stat state texts describe tendencies, not requirements — the theme contract overrides them when they conflict.");
        sb.AppendLine("- If multiple continuations are possible, choose the one that best matches this theme description while preserving continuity and safety constraints.");
    }

    private static void AppendSteerGuidance(
        StringBuilder sb,
        RolePlaySession session,
        string currentPhase,
        RPTheme? activeTheme,
        string steerDirective,
        bool includeThemeNotes,
        int maxThemeNotes,
        bool enableLocationServices)
    {
        sb.AppendLine("Steer Flow Guidance:");
        sb.AppendLine($"- Requested steer direction: {steerDirective}");
        sb.AppendLine($"- Current narrative phase: {currentPhase}");

        var location = session.AdaptiveState.CurrentSceneLocation;
        if (enableLocationServices && !string.IsNullOrWhiteSpace(location))
        {
            sb.AppendLine($"- Keep this steer plausible for the current surroundings at '{location}'.");
            sb.AppendLine("- If the steer implies changing location, add an explicit transition beat before characters arrive in a new place.");
        }

        if (activeTheme is not null)
        {
            var themeLabel = string.IsNullOrWhiteSpace(activeTheme.Label) ? activeTheme.Id : activeTheme.Label;
            sb.AppendLine($"- Active theme anchor: {themeLabel} ({activeTheme.Id}).");

            var phaseGuidance = RolePlayAssistantPrompts.GetThemePhaseGuidanceLines(activeTheme, currentPhase);
            if (phaseGuidance.Count > 0)
            {
                sb.AppendLine($"- Apply theme phase guidance for {currentPhase}:");
                foreach (var line in phaseGuidance)
                {
                    sb.AppendLine($"  - {line}");
                }
            }

            if (includeThemeNotes)
            {
                var notes = RolePlayAssistantPrompts.GetPhaseRelevantThemeAIGuidanceNotes(
                    activeTheme,
                    currentPhase,
                    Math.Clamp(maxThemeNotes, 1, 6),
                    includeFormulaNotes: false);
                if (notes.Count > 0)
                {
                    sb.AppendLine("- Relevant theme AI guidance notes for this phase:");
                    foreach (var note in notes)
                    {
                        sb.AppendLine($"  - [{note.Section}] {note.Text.Trim()}");
                    }
                }
            }
        }
        else
        {
            var availableThemes = session.AdaptiveState.ThemeScores.Values
                .Where(x => !x.Blocked)
                .OrderByDescending(x => x.Score)
                .Take(3)
                .ToList();

            if (availableThemes.Count > 0)
            {
                sb.AppendLine("- No active theme is currently locked. Guide direction toward one of these available high-fit themes:");
                foreach (var theme in availableThemes)
                {
                    var name = string.IsNullOrWhiteSpace(theme.ThemeName) ? theme.ThemeId : theme.ThemeName;
                    sb.AppendLine($"  - {name} ({theme.ThemeId}), score={theme.Score:F1}, intensity={theme.Intensity}");
                }
                sb.AppendLine("- Choose a direction that naturally increases coherence with one of the above themes without abrupt pivots.");
            }
            else
            {
                sb.AppendLine("- No active or available themes are established yet. Steer toward a coherent, phase-appropriate direction and preserve scene plausibility.");
            }
        }
    }

        private static string? ResolveSteerDirective(RolePlaySession session, string promptText, PromptIntent intent)
    {
        if (intent == PromptIntent.Instruction
            && TryExtractSteerDirective(promptText, out var directive))
        {
            return directive;
        }

        return null;
    }

    private static bool TryExtractSteerDirective(string promptText, out string directive)
    {
        directive = string.Empty;
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return false;
        }

        var raw = promptText.Trim();
        if (!raw.StartsWith("/steer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remaining = raw.Length > 6 ? raw[6..].Trim() : string.Empty;
        directive = string.IsNullOrWhiteSpace(remaining)
            ? "Steer the scene in a meaningful, phase-consistent direction."
            : remaining;
        return true;
    }

    private static bool TryExtractTimeSkipDirective(string promptText, out string label)
    {
        label = string.Empty;
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return false;
        }

        var raw = promptText.Trim();
        if (!raw.StartsWith("/timeskip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remaining = raw.Length > 9 ? raw[9..].Trim() : string.Empty;
        label = string.IsNullOrWhiteSpace(remaining) ? "the next scene" : remaining;
        return true;
    }

    /// <summary>
    /// Time skips are one-shot — only resolved from the current turn, never carried forward from history.
    /// </summary>
    private static string? ResolveTimeSkipDirective(RolePlaySession session, string promptText, PromptIntent intent)
    {
        if (intent == PromptIntent.Instruction
            && TryExtractTimeSkipDirective(promptText, out var label))
        {
            return label;
        }

        return null;
    }

    private static void AppendTimeSkipGuidance(StringBuilder sb, string label, string currentPhase)
    {
        sb.AppendLine("Narrative Time Skip:");
        sb.AppendLine($"- Time advance: {label}");
        sb.AppendLine("- The current scene moment ends here. Open the next passage with a brief orienting sentence that anchors the new time and place.");
        sb.AppendLine("- Do not narrate the passage of time in detail — cut directly to the new moment.");
        sb.AppendLine("- Characters react naturally to the new setting; do not recap prior events.");
        sb.AppendLine($"- Maintain narrative phase continuity: current phase is {currentPhase}.");
        sb.AppendLine("- After the time-skip opening, continue the narrative from the new moment forward as normal.");
    }

    private static string ResolvePromptActorLabel(RolePlaySession session, string? actorIdOrName)
    {
        if (string.IsNullOrWhiteSpace(actorIdOrName))
        {
            return "Unknown";
        }

        var token = actorIdOrName.Trim();
        if (!string.IsNullOrWhiteSpace(session.PersonaName)
            && string.Equals(token, session.PersonaName, StringComparison.OrdinalIgnoreCase))
        {
            return session.PersonaName;
        }

        var perspective = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterId, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.CharacterName, token, StringComparison.OrdinalIgnoreCase));
        if (perspective is not null && !string.IsNullOrWhiteSpace(perspective.CharacterName))
        {
            return perspective.CharacterName;
        }

        return token;
    }

    private static Dictionary<string, int>? ResolvePromptActorStats(RolePlaySession session, string actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            return null;
        }

        var direct = session.AdaptiveState.CharacterStats
            .FirstOrDefault(x => string.Equals(x.Key, actorName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(direct.Key))
        {
            return CharacterStatProfileV2Accessor.GetAllStats(direct.Value);
        }

        var byCharacterId = session.AdaptiveState.CharacterStats.Values.FirstOrDefault(x =>
            string.Equals(x.CharacterId, actorName, StringComparison.OrdinalIgnoreCase));
        return byCharacterId is null ? null : CharacterStatProfileV2Accessor.GetAllStats(byCharacterId);
    }

    private static int ResolveStat(IReadOnlyDictionary<string, int>? stats, string statName, double fallback)
    {
        if (stats is not null && stats.TryGetValue(statName, out var value))
        {
            return value;
        }

        return (int)Math.Round(fallback, MidpointRounding.AwayFromZero);
    }

    private async Task AppendPositionListAsync(
        StringBuilder sb,
        RolePlaySession session,
        string currentPhase,
        PromptIntent intent,
        CancellationToken cancellationToken)
    {
        if (_rpThemeService is null) return;
        if (intent == PromptIntent.Instruction) return;
        if (!string.Equals(currentPhase, "Approaching", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(currentPhase, "Climax", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            // Only scenario-bound characters (those with a CharacterRole) contribute to stat averages.
            var trackedStatsForPositions = session.AdaptiveState.CharacterStats.Values
                .Where(x => !string.IsNullOrEmpty(x.CharacterRole))
                .ToList();
            var avgDesire = trackedStatsForPositions.Count == 0 ? 50.0
                : trackedStatsForPositions.Average(x => CharacterStatProfileV2Accessor.GetStatOrDefault(x, "Desire", 50));
            var avgDominance = trackedStatsForPositions.Count == 0 ? 50.0
                : trackedStatsForPositions.Average(x => CharacterStatProfileV2Accessor.GetStatOrDefault(x, "Dominance", 50));
            var otherManDominance = trackedStatsForPositions.Count == 0 ? avgDominance
                : trackedStatsForPositions.Average(x =>
                {
                    var allStats = CharacterStatProfileV2Accessor.GetAllStats(x);
                    if (allStats.TryGetValue("OtherManDominance", out var d)) return (double)d;
                    if (allStats.TryGetValue("OtherManDom", out var d2)) return (double)d2;
                    if (allStats.TryGetValue("RivalDominance", out var d3)) return (double)d3;
                    if (allStats.TryGetValue("BullDominance", out var d4)) return (double)d4;
                    return avgDominance;
                });

            var tier = DerivePositionEscalationTier(avgDesire, otherManDominance);
            var all = await _rpThemeService.ListPositionsAsync(cancellationToken);
            var available = all.Where(p => PositionTierRank(p.EscalationTier) <= PositionTierRank(tier))
                               .OrderBy(p => p.SortOrder)
                               .ToList();

            if (available.Count == 0) return;

            sb.AppendLine($"Available Positions (tier: {tier}):");
            foreach (var pos in available)
            {
                sb.AppendLine($"- {pos.Name}: {pos.ShortDescription}");
            }

            sb.AppendLine("Use positions from this list when describing physical acts. Do not invent positions not on this list.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to load position list for continuation prompt.");
        }
    }

    private static string DerivePositionEscalationTier(double desire, double otherManDominance)
    {
        if (otherManDominance >= 70 || desire >= 70) return "High";
        if (otherManDominance >= 40 || desire >= 40) return "Medium";
        return "Low";
    }

    private static int PositionTierRank(string? tier) => (tier ?? "Low") switch
    {
        "High" => 3,
        "Medium" => 2,
        _ => 1
    };

    private static void AppendScenarioPriorities(
        StringBuilder sb,
        IReadOnlyList<string> goals,
        IReadOnlyList<string> conflicts,
        IReadOnlyList<string> guidelines)
    {
        if (goals.Count == 0 && conflicts.Count == 0 && guidelines.Count == 0)
        {
            return;
        }

        sb.AppendLine("Scenario Priorities For The Next Response:");
        foreach (var goal in goals)
        {
            sb.AppendLine($"- Higher priority: move toward this goal when it fits naturally: {goal}");
        }

        foreach (var conflict in conflicts)
        {
            sb.AppendLine($"- Higher priority: keep this conflict active, meaningful, or unresolved unless a natural scene turn changes it: {conflict}");
        }

        foreach (var guideline in guidelines)
        {
            sb.AppendLine($"- Lower priority than goals/conflicts, but still prefer this when it fits naturally: {guideline}");
        }

        sb.AppendLine("Treat goals and conflicts as higher-level soft priorities than narrative guidelines. Advance them when the scene allows, but do not force abrupt jumps or resolve everything immediately. Ignore any of these only when the current instruction, scene reality, or hard safety constraints require otherwise.");
    }

    private async Task<string> GenerateNarrativeWithValidationAsync(
        RolePlaySession session,
        string prompt,
        ResolvedModel resolved,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var climaxMode = session.AdaptiveState.CurrentPhase == NarrativePhase.Climax;

        var firstOutput = await _completionClient.GenerateAsync(prompt, resolved, cancellationToken);
        var firstAnalysis = AnalyzeNarrativeOutput(firstOutput, climaxMode);
        await WriteNarrativeValidationEventAsync(session, correlationId, 0, firstAnalysis, cancellationToken);

        if (!firstAnalysis.ShouldRetry)
        {
            return string.IsNullOrWhiteSpace(firstOutput) ? "(No output generated)" : firstOutput.Trim();
        }

        var retryPrompt = BuildNarrativeCorrectionPrompt(prompt, firstAnalysis);
        var bestOutput = firstOutput;
        var bestAnalysis = firstAnalysis;

        for (var attempt = 1; attempt <= NarrativeValidationRetryLimit; attempt++)
        {
            var retryOutput = await _completionClient.GenerateAsync(retryPrompt, resolved, cancellationToken);
            var retryAnalysis = AnalyzeNarrativeOutput(retryOutput, climaxMode);
            await WriteNarrativeValidationEventAsync(session, correlationId, attempt, retryAnalysis, cancellationToken);

            if (retryAnalysis.Score <= bestAnalysis.Score)
            {
                bestOutput = retryOutput;
                bestAnalysis = retryAnalysis;
            }

            if (!retryAnalysis.ShouldRetry)
            {
                break;
            }
        }

        if (bestAnalysis.HasViolation)
        {
            _logger.LogWarning(
                "Narrative validation retained best-effort output in session {SessionId}; score={Score}, quotedBlocks={QuotedBlocks}",
                session.Id,
                bestAnalysis.Score,
                bestAnalysis.QuotedBlockCount);
        }

        return string.IsNullOrWhiteSpace(bestOutput) ? "(No output generated)" : bestOutput.Trim();
    }

    private async Task WriteNarrativeValidationEventAsync(
        RolePlaySession session,
        string correlationId,
        int attempt,
        NarrativeValidationResult analysis,
        CancellationToken cancellationToken)
    {
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            CorrelationId = correlationId,
            EventKind = "NarrativeValidation",
            Severity = analysis.HasViolation ? "Warning" : "Info",
            ActorName = "Narrative",
            Summary = analysis.HasViolation
                ? $"Narrative output flagged on attempt {attempt}"
                : $"Narrative output accepted on attempt {attempt}",
            MetadataJson = JsonSerializer.Serialize(new
            {
                attempt,
                analysis.Score,
                analysis.HasViolation,
                analysis.ShouldRetry,
                analysis.QuotedBlockCount,
                analysis.QuotedTextRatio,
                analysis.DialogueAttributionCount,
                analysis.FirstPersonLeakCount,
                analysis.CharacterInteriorityCount
            })
        }, cancellationToken);
    }

    private static string? NarrativeLocationLabel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        // Strip subtitle after the first separator (em-dash, en-dash, hyphen, or colon with spaces).
        foreach (var sep in new[] { " \u2014 ", " \u2013 ", " - ", " : " })
        {
            var idx = raw.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
            {
                return raw[..idx].Trim();
            }
        }

        return raw.Trim();
    }

    private static string BuildNarrativeCorrectionPrompt(string originalPrompt, NarrativeValidationResult analysis)
    {
        var sb = new StringBuilder();
        sb.Append(originalPrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Revision required: rewrite as pure scene narration.");

        if (analysis.QuotedBlockCount >= NarrativeQuotedBlockRetryThreshold)
        {
            sb.AppendLine($"Found {analysis.QuotedBlockCount} quoted blocks — reduce to zero. Do not write any dialogue.");
        }

        if (analysis.FirstPersonLeakCount > 0)
        {
            sb.AppendLine("Found first-person pronoun in narrator body — write in third person throughout; do not use 'I', 'me', 'my', 'mine', or 'myself' outside of a quoted fragment.");
        }

        if (analysis.CharacterInteriorityCount > 0)
        {
            sb.AppendLine("Found inner-thought phrases — remove sentences about what characters thought, felt, wondered, realized, or decided; describe only externally observable actions, positions, and states.");
        }

        sb.Append("Rewrite focusing on physical scene: positions, surroundings, sensations, and movement.");
        return sb.ToString();
    }

    private static NarrativeValidationResult AnalyzeNarrativeOutput(string? output, bool climaxMode = false)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new NarrativeValidationResult(HasViolation: false, ShouldRetry: false, Score: 0, QuotedBlockCount: 0, QuotedTextRatio: 0d, DialogueAttributionCount: 0, FirstPersonLeakCount: 0, CharacterInteriorityCount: 0);
        }

        var text = output.Trim();
        var quotedMatches = QuotedBlockRegex.Matches(text);
        var quotedLength = quotedMatches.Cast<Match>().Sum(m => m.Length);
        var quotedRatio = text.Length == 0 ? 0d : (double)quotedLength / text.Length;
        var quotedCount = quotedMatches.Count;
        var quotedThreshold = climaxMode ? 1 : NarrativeQuotedBlockRetryThreshold;
        var attributionCount = DialogueAttributionRegex.Matches(text).Count;
        var narratorBodyOnly = QuotedBlockRegex.Replace(text, string.Empty);
        var firstPersonCount = FirstPersonLeakRegex.Matches(narratorBodyOnly).Count;
        var interiorityCount = CharacterInteriorityRegex.Matches(text).Count;

        var score = 0;
        if (quotedCount >= quotedThreshold)
        {
            score += 3;
        }

        if (quotedCount >= NarrativeQuotedBlockHardViolationThreshold)
        {
            score += 3;
        }

        if (quotedRatio >= NarrativeQuotedTextRatioRetryThreshold)
        {
            score += 2;
        }

        if (attributionCount >= 2 && quotedCount >= 2)
        {
            score += 2;
        }

        if (firstPersonCount > 0)
        {
            score += 2;
        }

        if (interiorityCount > 0)
        {
            score += 2;
        }

        var hasViolation = score > 0;
        var shouldRetry = quotedCount >= quotedThreshold
            || quotedRatio >= NarrativeQuotedTextRatioRetryThreshold
            || (attributionCount >= 2 && quotedCount >= 2)
            || firstPersonCount > 0
            || interiorityCount > 0;

        return new NarrativeValidationResult(
            HasViolation: hasViolation,
            ShouldRetry: shouldRetry,
            Score: score,
            QuotedBlockCount: quotedCount,
            QuotedTextRatio: Math.Round(quotedRatio, 4),
            DialogueAttributionCount: attributionCount,
            FirstPersonLeakCount: firstPersonCount,
            CharacterInteriorityCount: interiorityCount);
    }

    private static IReadOnlyList<ScenarioCharacter> BuildCharactersWithPersona(
        IReadOnlyList<ScenarioCharacter>? scenarioCharacters,
        RolePlaySession session)
    {
        var list = scenarioCharacters is not null
            ? new List<ScenarioCharacter>(scenarioCharacters)
            : [];

        // The persona may already be present when the scenario's Characters list includes
        // an IsPersona character (unified persona-as-character refactor). Only inject the
        // legacy synthetic "__persona__" entry when no existing entry matches the persona
        // name — this preserves the frame-generator resolution path for old sessions that
        // still rely on the agg-slot token.
        var personaName = !string.IsNullOrWhiteSpace(session.PersonaName) ? session.PersonaName : "Persona";
        var alreadyPresent = list.Any(c =>
            string.Equals(c.Name, personaName, StringComparison.OrdinalIgnoreCase));
        if (alreadyPresent)
        {
            return list;
        }

        var personaRole = !string.IsNullOrWhiteSpace(session.PersonaRole) ? session.PersonaRole : string.Empty;
        list.Add(new ScenarioCharacter("__persona__", personaName, personaRole));

        return list;
    }

    private sealed record NarrativeValidationResult(
        bool HasViolation,
        bool ShouldRetry,
        int Score,
        int QuotedBlockCount,
        double QuotedTextRatio,
        int DialogueAttributionCount,
        int FirstPersonLeakCount,
        int CharacterInteriorityCount);

    // ── Intimate behavioral text injection helpers ──────────────────────────

    private static void InjectCharacterBehavioralTexts(
        StringBuilder sb,
        DreamGenClone.Web.Domain.Scenarios.Character character,
        System.Collections.Generic.IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character> allCharacters,
        RolePlaySession session)
    {
        if (character.PhysicalAttributes is null) return;

        // 1. Self-awareness text for every character with intimate attributes
        var selfAwareness = IntimateBehavioralTextBuilder.BuildSelfAwarenessText(
            character.PhysicalAttributes, character.Gender, awarenessLevel: null, character.Name);
        if (!string.IsNullOrEmpty(selfAwareness))
            sb.AppendLine($"    {selfAwareness}");

        // 2. Partner perspective: for female characters related to male persona/character
        var isFemale = string.Equals(character.Gender, "Female", StringComparison.OrdinalIgnoreCase);
        if (!isFemale || session.PersonaPhysicalAttributes is null) return;

        var personaIsMale = string.Equals(session.PersonaGender, "Male", StringComparison.OrdinalIgnoreCase);
        if (!personaIsMale) return;

        // Check if this female character is related to the male persona
        var hasRelationToPersona = HasRelationToPersona(character, session, allCharacters);
        if (!hasRelationToPersona) return;

        // Partner perspective: this female character → persona
        var partnerPerspective = IntimateBehavioralTextBuilder.BuildPartnerPerspectiveText(
            session.PersonaPhysicalAttributes, session.PersonaGender,
            character.PhysicalAttributes, character.Gender,
            session.PersonaName, character.Name!);
        if (!string.IsNullOrEmpty(partnerPerspective))
            sb.AppendLine($"    {partnerPerspective}");

        // 3. Partner perspective + comparison: for other male characters in scene
        var otherMales = allCharacters
            .Where(c => c != character
                && string.Equals(c.Gender, "Male", StringComparison.OrdinalIgnoreCase)
                && c.PhysicalAttributes is not null
                && HasAnyIntimateFields(c.PhysicalAttributes))
            .ToList();

        string? firstOtherMaleName = null;
        DreamGenClone.Domain.Templates.PhysicalAttributes? firstOtherMaleAttrs = null;

        foreach (var otherMale in otherMales)
        {
            // B-058 Phase 6.2 gate: knowledge of the other man's intimate attributes now
            // depends on whether an EncounterCompletion record exists for him. Pre-encounter
            //   → attraction without knowledge (BuildPartnerPreEncounterText)
            // Post-encounter → full partner perspective (BuildPartnerPerspectiveText)
            // Comparison text is gated post-encounter only.
            //
            // B-058 Phase 6.3 EXCEPTION: the husband (or any male character related to this
            // female character) has an established intimate history — the encounter gate is
            // designed for new partners (the "other man"), not the spouse. Always treat
            // related males as post-encounter.
            var isRelatedMale = !string.IsNullOrWhiteSpace(otherMale.RelationTargetId)
                && string.Equals(otherMale.RelationTargetId, character.Name, StringComparison.OrdinalIgnoreCase);

            var hasEncounterCompletion = isRelatedMale
                || IntimateBehavioralTextBuilder.HasEncounterCompletionForCharacter(
                    otherMale.Id,
                    session.AdaptiveState.EncounterSummaries,
                    session.PersonaName);

            if (!hasEncounterCompletion)
            {
                var anticipation = IntimateBehavioralTextBuilder.BuildPartnerPreEncounterText(
                    otherMale.Name!, otherMale.Gender,
                    otherMale.PhysicalAttributes,
                    character.Name!, character.Gender);
                if (!string.IsNullOrEmpty(anticipation))
                    sb.AppendLine($"    {anticipation}");
                // Pre-encounter: do NOT register as first-other-male — comparison is gated post-encounter.
                continue;
            }

            var otherPerspective = IntimateBehavioralTextBuilder.BuildPartnerPerspectiveText(
                otherMale.PhysicalAttributes!, otherMale.Gender,
                character.PhysicalAttributes, character.Gender,
                otherMale.Name!, character.Name!);
            if (!string.IsNullOrEmpty(otherPerspective))
                sb.AppendLine($"    {otherPerspective}");

            if (firstOtherMaleName is null)
            {
                firstOtherMaleName = otherMale.Name;
                firstOtherMaleAttrs = otherMale.PhysicalAttributes;
            }
        }

        // 4. Comparison text: when there are two male partners
        if (firstOtherMaleName is not null && firstOtherMaleAttrs is not null)
        {
            var comparison = IntimateBehavioralTextBuilder.BuildComparisonText(
                session.PersonaPhysicalAttributes, session.PersonaName,
                firstOtherMaleAttrs, firstOtherMaleName,
                character.PhysicalAttributes, character.Name!);
            if (!string.IsNullOrEmpty(comparison))
                sb.AppendLine($"    {comparison}");
        }
    }

    private static bool HasRelationToPersona(
        DreamGenClone.Web.Domain.Scenarios.Character character,
        RolePlaySession session,
        System.Collections.Generic.IReadOnlyList<DreamGenClone.Web.Domain.Scenarios.Character> allCharacters)
    {
        // Direct relation: character's RelationTargetId points to persona
        if (!string.IsNullOrWhiteSpace(character.RelationTargetId))
        {
            // Check if the target is the persona (by name match or special token)
            if (string.Equals(character.RelationTargetId, session.PersonaName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Role-based: Wife role character and Husband role persona
        var charRole = CharacterRoleCatalog.Normalize(character.Role);
        var personaRole = CharacterRoleCatalog.Normalize(session.PersonaRole);
        if (string.Equals(charRole, "Wife", StringComparison.OrdinalIgnoreCase)
            && string.Equals(personaRole, "Husband", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static int? ResolvePersonaAwarenessLevel(RolePlaySession session)
    {
        // Try to find a CharacterEncounterProfileId for the persona by matching name or role
        foreach (var kvp in session.AdaptiveState.CharacterEncounterProfileIds)
        {
            if (string.Equals(kvp.Key, session.PersonaName, StringComparison.OrdinalIgnoreCase))
                return ResolveAwarenessFromProfileId(kvp.Value);
        }

        // Fallback: match by persona role
        foreach (var kvp in session.AdaptiveState.CharacterEncounterProfileIds)
        {
            if (string.Equals(kvp.Key, session.PersonaRole, StringComparison.OrdinalIgnoreCase))
                return ResolveAwarenessFromProfileId(kvp.Value);
        }

        return null;
    }

    private static int? ResolveAwarenessFromProfileId(string? profileId)
    {
        // Awareness is derived from CharacterProfile dimensions.
        // Without ICharacterProfileService injected into static helpers,
        // we return null. The caller handles null by omitting awareness framing.
        // Future enhancement: wire up ICharacterProfileService for full resolution.
        return null;
    }

    private static bool HasAnyIntimateFields(DreamGenClone.Domain.Templates.PhysicalAttributes attrs)
    {
        return !string.IsNullOrWhiteSpace(attrs.Scent)
            || !string.IsNullOrWhiteSpace(attrs.SexualDrive)
            || !string.IsNullOrWhiteSpace(attrs.SexualConfidence)
            || !string.IsNullOrWhiteSpace(attrs.SexualSkill)
            || !string.IsNullOrWhiteSpace(attrs.OralSkill)
            || !string.IsNullOrWhiteSpace(attrs.EndowmentLength)
            || !string.IsNullOrWhiteSpace(attrs.EndowmentGirth)
            || !string.IsNullOrWhiteSpace(attrs.Stamina)
            || !string.IsNullOrWhiteSpace(attrs.Recovery)
            || !string.IsNullOrWhiteSpace(attrs.EjaculationIntensity)
            || !string.IsNullOrWhiteSpace(attrs.VaginalTightness)
            || !string.IsNullOrWhiteSpace(attrs.Sensitivity)
            || !string.IsNullOrWhiteSpace(attrs.Lubrication)
            || !string.IsNullOrWhiteSpace(attrs.OrgasmicCapacity);
    }

}
