using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Logging;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayEngineService : IRolePlayEngineService
{
    private static readonly TimeSpan DecisionPointContextCooldown = TimeSpan.FromMinutes(5);
    private const int ManualOverrideSelectionLockInteractions = 8;

    private static readonly string[] GenericLocationNames =
    [
        "Living Room",
        "Game Room",
        "Guest Room",
        "Guest Bedroom",
        "Kitchen",
        "Bedroom",
        "Bathroom",
        "Office",
        "Study",
        "Garden",
        "Patio",
        "Balcony",
        "Hall",
        "Hallway",
        "Corridor",
        "Lounge",
        "Bar",
        "Club",
        "Restaurant",
        "Cafe",
        "Coffee Shop",
        "Outside",
        "Outdoors",
        "Park",
        "Street",
        "Car",
        "Parking Lot",
        "Backyard",
        "Garage",
        "Dining Room",
        "Pool",
        "Library"
    ];

    private static readonly ConcurrentDictionary<string, RolePlaySession> Sessions = new();
    private const bool EnableRolePlayStreaming = false;
    private static readonly IReadOnlyDictionary<string, (string Label, IReadOnlyDictionary<string, int> Deltas)> DecisionOptionCatalog =
        new Dictionary<string, (string Label, IReadOnlyDictionary<string, int> Deltas)>(StringComparer.OrdinalIgnoreCase)
        {
            ["lean-in"] = ("Lean In", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 6,
                ["Tension"] = 4,
                ["Restraint"] = -20
            }),
            ["tempt-answer"] = ("Tempted Answer", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 8,
                ["Loyalty"] = -6,
                ["Tension"] = 3,
                ["Restraint"] = -18
            }),
            ["hold-back"] = ("Hold Back", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Restraint"] = 5,
                ["Tension"] = -2,
                ["SelfRespect"] = 2
            }),
            ["seek-connection"] = ("Seek Connection", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Connection"] = 5,
                ["Loyalty"] = 3
            }),
            ["test-boundary"] = ("Test Boundary", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 5,
                ["Restraint"] = -20,
                ["Tension"] = 3
            }),
            ["escalate"] = ("Escalate", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 4,
                ["Tension"] = 4,
                ["Restraint"] = -25
            }),
            ["redirect"] = ("Redirect", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Restraint"] = 4,
                ["Connection"] = 2
            }),
            ["observe"] = ("Observe", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tension"] = 1,
                ["Restraint"] = 2
            }),
            ["husband-observes"] = ("Let Him Observe", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tension"] = 2,
                ["Restraint"] = -22
            }),
            ["custom"] = ("Custom Response", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
        };

    private readonly IRolePlayContinuationService _continuationService;
    private readonly IBehaviorModeService _behaviorModeService;
    private readonly IRolePlayPromptRouter _promptRouter;
    private readonly IRolePlayIdentityOptionsService _identityOptionsService;
    private readonly IRolePlayAdaptiveStateService _adaptiveStateService;
    private readonly IRolePlayCommandValidator _commandValidator;
    private readonly ISessionService _sessionService;
    private readonly IScenarioService _scenarioService;
    private readonly IBaseStatProfileService _baseStatProfileService;
    private readonly AutoSaveCoordinator _autoSaveCoordinator;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly IScenarioSelectionService _scenarioSelectionService;
    private readonly IScenarioLifecycleService _scenarioLifecycleService;
    private readonly IConceptInjectionService _conceptInjectionService;
    private readonly IDecisionPointService _decisionPointService;
    private readonly IRolePlayStateRepository _stateRepository;
    private readonly ICompletionClient? _completionClient;
    private readonly IModelResolutionService? _modelResolutionService;
    private readonly IThemePreferenceService? _themePreferenceService;
    private readonly IRPThemeService? _rpThemeService;
    private readonly ITemplateService? _templateService;
    private readonly RolePlayPromptComposer _promptComposer;
    private readonly RolePlaySessionCompatibilityService? _compatibilityService;
    private readonly ILogger<RolePlayEngineService> _logger;
    private readonly decimal _completedScenarioRepeatPenaltyPerRun;
    private readonly decimal _completedScenarioRepeatPenaltyFloor;
    private readonly decimal _completedScenarioRecentPenaltyMultiplier;
    private readonly decimal _completedScenarioThemeScorePenalty;
    private readonly bool _suppressNarrativeAfterDecision;
    private readonly bool _suppressNarrativeAfterPhaseChange;
    private readonly bool _enablePhaseChangeDecisionPrompts;
    private readonly bool _enableSceneLocationDecisionPrompts;

    public RolePlayEngineService(
        IRolePlayContinuationService continuationService,
        IBehaviorModeService behaviorModeService,
        IRolePlayPromptRouter promptRouter,
        IRolePlayIdentityOptionsService identityOptionsService,
        IRolePlayAdaptiveStateService adaptiveStateService,
        IRolePlayCommandValidator commandValidator,
        ISessionService sessionService,
        IScenarioService scenarioService,
        IBaseStatProfileService baseStatProfileService,
        AutoSaveCoordinator autoSaveCoordinator,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayEngineService> logger,
        IScenarioSelectionService? scenarioSelectionService = null,
        IScenarioLifecycleService? scenarioLifecycleService = null,
        IConceptInjectionService? conceptInjectionService = null,
        IDecisionPointService? decisionPointService = null,
        IRolePlayStateRepository? stateRepository = null,
        ICompletionClient? completionClient = null,
        IModelResolutionService? modelResolutionService = null,
        IThemePreferenceService? themePreferenceService = null,
        IRPThemeService? rpThemeService = null,
        RolePlayPromptComposer? promptComposer = null,
        RolePlaySessionCompatibilityService? compatibilityService = null,
        IOptions<StoryAnalysisOptions>? storyAnalysisOptions = null,
        IOptions<RolePlayDecisionOptions>? rolePlayDecisionOptions = null,
        ITemplateService? templateService = null)
    {
        _continuationService = continuationService;
        _behaviorModeService = behaviorModeService;
        _promptRouter = promptRouter;
        _identityOptionsService = identityOptionsService;
        _adaptiveStateService = adaptiveStateService;
        _commandValidator = commandValidator;
        _sessionService = sessionService;
        _scenarioService = scenarioService;
        _baseStatProfileService = baseStatProfileService;
        _autoSaveCoordinator = autoSaveCoordinator;
        _debugEventSink = debugEventSink;
        _scenarioSelectionService = scenarioSelectionService ?? new NullScenarioSelectionService();
        _scenarioLifecycleService = scenarioLifecycleService ?? new NullScenarioLifecycleService();
        _conceptInjectionService = conceptInjectionService ?? new NullConceptInjectionService();
        _decisionPointService = decisionPointService ?? new NullDecisionPointService();
        _stateRepository = stateRepository ?? new NullRolePlayStateRepository();
        _completionClient = completionClient;
        _modelResolutionService = modelResolutionService;
        _themePreferenceService = themePreferenceService;
        _rpThemeService = rpThemeService;
        _promptComposer = promptComposer ?? new RolePlayPromptComposer();
        _compatibilityService = compatibilityService;
        _templateService = templateService;
        _logger = logger;
        _completedScenarioRepeatPenaltyPerRun = (decimal)Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioRepeatPenaltyPerRun ?? 0.20, 0d, 1d);
        _completedScenarioRepeatPenaltyFloor = (decimal)Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioRepeatPenaltyFloor ?? 0.40, 0d, 1d);
        _completedScenarioRecentPenaltyMultiplier = (decimal)Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioRecentPenaltyMultiplier ?? 0.65, 0d, 1d);
        _completedScenarioThemeScorePenalty = Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioThemeScorePenalty ?? 10, 0, 100);
        _suppressNarrativeAfterDecision = rolePlayDecisionOptions?.Value.SuppressNarrativeAfterDecision ?? false;
        _suppressNarrativeAfterPhaseChange = rolePlayDecisionOptions?.Value.SuppressNarrativeAfterPhaseChange ?? false;
        _enablePhaseChangeDecisionPrompts = rolePlayDecisionOptions?.Value.EnablePhaseChangeDecisionPrompts ?? false;
        _enableSceneLocationDecisionPrompts = rolePlayDecisionOptions?.Value.EnableSceneLocationDecisionPrompts ?? false;
    }

    public async Task<RolePlaySession> CreateSessionAsync(
        string title,
        string? scenarioId = null,
        string personaName = "You",
        string personaDescription = "",
        string? personaTemplateId = null,
        string personaGender = "Unknown",
        string personaRole = "Unknown",
        string? personaRelationTargetId = null,
        CancellationToken cancellationToken = default)
    {
        var session = new RolePlaySession
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled Role-Play" : title.Trim(),
            ScenarioId = scenarioId,
            PersonaName = string.IsNullOrWhiteSpace(personaName) ? "You" : personaName.Trim(),
            PersonaDescription = personaDescription ?? string.Empty,
            PersonaTemplateId = personaTemplateId,
            PersonaGender = CharacterGenderCatalog.NormalizeForCharacter(personaGender),
            PersonaRole = CharacterRoleCatalog.Normalize(personaRole),
            PersonaRelationTargetId = CharacterRelationCatalog.NormalizeTargetId(personaRelationTargetId),
            PersonaPerspectiveMode = CharacterPerspectiveMode.FirstPersonInternalMonologue,
        };

        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(scenarioId);
            if (scenario is not null)
            {
                if (string.IsNullOrWhiteSpace(session.PersonaRelationTargetId))
                {
                    var personaRelationSource = scenario.Characters.FirstOrDefault(character =>
                    {
                        var relationTargetId = CharacterRelationCatalog.NormalizeTargetId(character.RelationTargetId);
                        if (CharacterRelationCatalog.IsPersonaTarget(relationTargetId))
                        {
                            return true;
                        }

                        var targetPersonaTemplateId = CharacterRelationCatalog.TryGetPersonaTemplateId(relationTargetId);
                        return !string.IsNullOrWhiteSpace(targetPersonaTemplateId)
                            && string.Equals(targetPersonaTemplateId, session.PersonaTemplateId, StringComparison.OrdinalIgnoreCase);
                    });

                    if (personaRelationSource is not null)
                    {
                        session.PersonaRelationTargetId = personaRelationSource.Id;
                    }
                }

                session.PersonaPerspectiveMode = scenario.DefaultPersonaPerspectiveMode;
                session.SelectedThemeProfileId = scenario.DefaultThemeProfileId;
                session.SelectedRPThemeProfileId = scenario.DefaultRPThemeProfileId;
                session.SelectedIntensityProfileId = scenario.DefaultIntensityProfileId;
                session.AdaptiveIntensityProfileId = scenario.DefaultIntensityProfileId;
                session.SelectedSteeringProfileId = scenario.DefaultSteeringProfileId;
                session.IntensityFloorOverride = scenario.DefaultIntensityFloor;
                session.IntensityCeilingOverride = scenario.DefaultIntensityCeiling;

                var resolvedBaseStats = AdaptiveStatCatalog.NormalizePartial(scenario.ResolvedBaseStats);
                if (!string.IsNullOrWhiteSpace(scenario.BaseStatProfileId))
                {
                    var baseStatProfile = await _baseStatProfileService.GetAsync(scenario.BaseStatProfileId, cancellationToken);
                    if (baseStatProfile is not null)
                    {
                        resolvedBaseStats = AdaptiveStatCatalog.NormalizeComplete(baseStatProfile.DefaultStats);
                        scenario.ResolvedBaseStats = new Dictionary<string, int>(resolvedBaseStats, StringComparer.OrdinalIgnoreCase);
                    }
                }

                session.CharacterPerspectives = scenario.Characters
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .Select(x => new RolePlayCharacterPerspective
                    {
                        CharacterId = x.Id,
                        CharacterName = x.Name!.Trim(),
                        PerspectiveMode = x.PerspectiveMode
                    })
                    .ToList();

                foreach (var character in scenario.Characters)
                {
                    if (string.IsNullOrWhiteSpace(character.Name))
                    {
                        continue;
                    }

                    var mergedStats = new Dictionary<string, int>(resolvedBaseStats, StringComparer.OrdinalIgnoreCase);
                    var normalizedCharacterOverrides = AdaptiveStatCatalog.NormalizePartial(character.BaseStats);
                    foreach (var (statName, statValue) in normalizedCharacterOverrides)
                    {
                        mergedStats[statName] = statValue;
                    }

                    if (mergedStats.Count == 0)
                    {
                        continue;
                    }

                    session.AdaptiveState.CharacterStats[character.Name.Trim()] = new CharacterStatBlock
                    {
                        CharacterId = character.Id,
                        Stats = mergedStats
                    };
                }

                await _adaptiveStateService.SeedFromScenarioAsync(session, scenario, cancellationToken);
            }
        }

        await SeedPersonaStatsFromTemplateAsync(session, cancellationToken);
        EnsurePersonaCharacterState(session);
        Sessions[session.Id] = session;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-session-created");
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            EventKind = "SessionCreated",
            Severity = "Info",
            ActorName = session.PersonaName,
            Summary = "Role-play session created",
            MetadataJson = JsonSerializer.Serialize(new
            {
                session.Id,
                session.Title,
                session.PersonaName,
                session.ScenarioId
            })
        }, cancellationToken);

        _logger.LogInformation("Role-play session created: {SessionId} ({Title}), Persona={PersonaName}",
            session.Id, session.Title, session.PersonaName);
        return session;
    }

    public async Task<IReadOnlyList<RolePlaySession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsurePersistedSessionsLoadedAsync(cancellationToken);

        IReadOnlyList<RolePlaySession> results = Sessions.Values
            .OrderByDescending(x => x.ModifiedAt)
            .ToList();

        _logger.LogInformation("Retrieved {Count} role-play sessions", results.Count);
        return results;
    }

    public async Task<RolePlaySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (Sessions.TryGetValue(sessionId, out var session))
        {
            if (EnsurePersonaCharacterState(session))
            {
                session.ModifiedAt = DateTime.UtcNow;
                _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-persona-character-normalized");
            }

            return session;
        }

        session = await _sessionService.LoadRolePlaySessionAsync(sessionId, cancellationToken);
        if (session is not null)
        {
            if (EnsurePersonaCharacterState(session))
            {
                session.ModifiedAt = DateTime.UtcNow;
                _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-persona-character-normalized");
            }

            Sessions[session.Id] = session;
        }

        return session;
    }

    public async Task<RolePlaySession> OpenSessionAsync(
        string sessionId,
        RolePlaySessionOpenAction action,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");

        switch (action)
        {
            case RolePlaySessionOpenAction.Start when session.Status != RolePlaySessionStatus.NotStarted:
                throw new InvalidOperationException($"Session '{sessionId}' cannot be started because it is {session.Status}.");

            case RolePlaySessionOpenAction.Continue when session.Status != RolePlaySessionStatus.InProgress:
                throw new InvalidOperationException($"Session '{sessionId}' cannot be continued because it is {session.Status}.");

            case RolePlaySessionOpenAction.Start:
                session.Status = RolePlaySessionStatus.InProgress;
                session.ModifiedAt = DateTime.UtcNow;
                _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-session-started");
                break;
        }

        _logger.LogInformation(
            SessionLogEvents.OpenRolePlaySession,
            "Role-play session opened: {SessionId}, action={Action}, status={Status}",
            session.Id,
            action,
            session.Status);

        return session;
    }

    public Task<RolePlaySession> SaveSessionAsync(RolePlaySession session, CancellationToken cancellationToken = default)
    {
        session.ModifiedAt = DateTime.UtcNow;
        Sessions[session.Id] = session;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-session-updated");
        _logger.LogInformation("Role-play session saved: {SessionId}, interactions={Count}, mode={Mode}", session.Id, session.Interactions.Count, session.BehaviorMode);
        return Task.FromResult(session);
    }

    public async Task<RolePlaySession> RebuildAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");

        await RebuildAdaptiveStateInternalAsync(session, cancellationToken);
        await SaveSessionAsync(session, cancellationToken);
        return session;
    }

    public async Task<RolePlaySession> OverrideAdaptiveThemeAsync(
        string sessionId,
        string requestedThemeId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");

        if (string.IsNullOrWhiteSpace(requestedThemeId))
        {
            throw new ArgumentException("Requested theme id is required.", nameof(requestedThemeId));
        }

        var applied = await _adaptiveStateService.ApplyManualScenarioOverrideAsync(session, requestedThemeId, cancellationToken);
        if (!applied)
        {
            throw new InvalidOperationException($"Theme '{requestedThemeId}' is not available for manual override.");
        }

        session.ModifiedAt = DateTime.UtcNow;
        await SaveSessionAsync(session, cancellationToken);

        _logger.LogInformation(
            "Manual adaptive theme override applied for session {SessionId}: requestedThemeId={ThemeId}",
            sessionId,
            requestedThemeId);

        return session;
    }

    public async Task<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var removedFromCache = Sessions.TryRemove(sessionId, out _);
        var deletedPersisted = await _sessionService.DeleteAsync(sessionId, cancellationToken);
        var deleted = removedFromCache || deletedPersisted;

        if (deleted)
        {
            _logger.LogInformation(SessionLogEvents.DeleteRolePlaySession, "Role-play session hard-deleted: {SessionId}", sessionId);
        }
        else
        {
            _logger.LogWarning("Role-play session delete requested for missing session: {SessionId}", sessionId);
        }

        return deleted;
    }

    public async Task<bool> UpdateBehaviorModeAsync(string sessionId, BehaviorMode mode, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        _behaviorModeService.SetMode(session, mode);
        await SaveSessionAsync(session, cancellationToken);
        return true;
    }

    public async Task<RolePlayInteraction> AddInteractionAsync(
        string sessionId,
        ContinueAsActor actor,
        string content,
        string? customActorName = null,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var interaction = new RolePlayInteraction
        {
            InteractionType = ToInteractionType(actor),
            ActorName = ResolveActorName(actor, customActorName),
            Content = content.Trim()
        };

        var persistedTurn = await _stateRepository.StartTurnAsync(
            session.Id,
            "AddInteraction",
            actor.ToString(),
            interaction.ActorName,
            null,
            cancellationToken);

        var outputInteractionIds = new List<string>();
        var turnSucceeded = false;
        string? turnFailureReason = null;
        try
        {
            session.Interactions.Add(interaction);
            outputInteractionIds.Add(interaction.Id);
            session.Status = RolePlaySessionStatus.InProgress;
            session.ModifiedAt = DateTime.UtcNow;

            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
            await RunRolePlayV2PipelinesAsync(
                session,
                DecisionTrigger.InteractionStart,
                cancellationToken);

            // Reset turn tracking if user acted manually
            if (actor == ContinueAsActor.You)
            {
                session.ConsecutiveNpcTurns = 0;
                session.CurrentTurnState = TurnState.NpcTurn;
            }

            _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-interaction-added");
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                EventKind = "InteractionPersisted",
                Severity = "Info",
                ActorName = interaction.ActorName,
                Summary = "Manual interaction added",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    interaction.Id,
                    interaction.ActorName,
                    interaction.InteractionType,
                    interaction.Content
                })
            }, cancellationToken);

            _logger.LogInformation("Manual role-play interaction appended to session {SessionId} as {Actor}", sessionId, interaction.ActorName);
            turnSucceeded = true;
            return interaction;
        }
        catch (Exception ex)
        {
            turnFailureReason = ex.Message;
            throw;
        }
        finally
        {
            await _stateRepository.CompleteTurnAsync(
                session.Id,
                persistedTurn.TurnId,
                outputInteractionIds,
                turnSucceeded,
                turnFailureReason,
                cancellationToken);
        }
    }

    public async Task<RolePlayInteraction> ContinueAsync(
        string sessionId,
        ContinueAsActor actor,
        string? customActorName = null,
        string? instruction = null,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");
        }

        if (!_behaviorModeService.IsContinuationAllowed(session.BehaviorMode, actor, explicitSelection: true))
        {
            throw new InvalidOperationException($"Actor '{actor}' is not allowed in mode '{session.BehaviorMode}'.");
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var promptText = string.IsNullOrWhiteSpace(instruction)
            ? "Continue the scene naturally."
            : instruction.Trim();
        var persistedTurn = await _stateRepository.StartTurnAsync(
            session.Id,
            "Continue",
            actor.ToString(),
            ResolveActorName(actor, customActorName),
            null,
            cancellationToken);

        var outputInteractionIds = new List<string>();
        var turnSucceeded = false;
        string? turnFailureReason = null;
        try
        {
            await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);

            var interaction = await _continuationService.ContinueAsync(
                session,
                actor,
                customActorName,
                PromptIntent.Narrative,
                promptText,
                null,
                cancellationToken);

            session.Interactions.Add(interaction);
            outputInteractionIds.Add(interaction.Id);
            session.Status = RolePlaySessionStatus.InProgress;
            session.ModifiedAt = DateTime.UtcNow;
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
            await RunRolePlayV2PipelinesAsync(session, DecisionTrigger.InteractionStart, cancellationToken);
            _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-continue-generated");
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                EventKind = "InteractionPersisted",
                Severity = "Info",
                ActorName = interaction.ActorName,
                ModelIdentifier = interaction.GeneratedByModelId,
                ProviderName = interaction.GeneratedByProvider,
                Summary = "Continuation interaction persisted",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    interaction.Id,
                    interaction.ActorName,
                    interaction.InteractionType,
                    interaction.GeneratedByCommand,
                    interaction.GeneratedByModelId,
                    interaction.GeneratedByProvider
                })
            }, cancellationToken);

            _logger.LogInformation(
                "Role-play continuation generated for session {SessionId} as {Actor} in mode {Mode}",
                sessionId,
                interaction.ActorName,
                session.BehaviorMode);

            turnSucceeded = true;
            return interaction;
        }
        catch (Exception ex)
        {
            turnFailureReason = ex.Message;
            throw;
        }
        finally
        {
            await _stateRepository.CompleteTurnAsync(
                session.Id,
                persistedTurn.TurnId,
                outputInteractionIds,
                turnSucceeded,
                turnFailureReason,
                cancellationToken);
        }
    }

    public async Task<RolePlayInteraction> SubmitPromptAsync(
        UnifiedPromptSubmission submission,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (!_commandValidator.ValidateSubmission(submission, out var validationError))
        {
            throw new ArgumentException(validationError, nameof(submission));
        }

        var session = await GetSessionAsync(submission.SessionId, cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException($"Role-play session '{submission.SessionId}' not found.");
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        IdentityOption identity;
        string? customName;
        if (submission.Intent == PromptIntent.Instruction)
        {
            identity = new IdentityOption
            {
                Id = "system:instruction",
                DisplayName = "Instruction",
                SourceType = IdentityOptionSource.Persona,
                Actor = ContinueAsActor.Npc,
                IsAvailable = true
            };
            customName = null;
        }
        else
        {
            var options = await _identityOptionsService.GetIdentityOptionsAsync(session, cancellationToken);
            identity = options.FirstOrDefault(x =>
                x.SourceType == submission.SelectedIdentityType &&
                string.Equals(x.Id, submission.SelectedIdentityId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Identity option '{submission.SelectedIdentityId}' is not available for this session.");

            if (submission.SubmittedVia != SubmissionSource.PlusButton && !identity.IsAvailable)
            {
                throw new InvalidOperationException(identity.AvailabilityReason ?? "The selected identity is not available.");
            }

            customName = identity.SourceType == IdentityOptionSource.CustomCharacter
                ? (string.IsNullOrWhiteSpace(submission.CustomIdentityName) ? null : submission.CustomIdentityName.Trim())
                : null;
        }

        var initiatedByActorName = submission.Intent == PromptIntent.Instruction
            ? "Instruction"
            : (identity.SourceType == IdentityOptionSource.CustomCharacter
                ? (string.IsNullOrWhiteSpace(customName) ? identity.DisplayName : customName)
                : identity.DisplayName);

        var persistedTurn = await _stateRepository.StartTurnAsync(
            session.Id,
            "SubmitPrompt",
            submission.SubmittedVia.ToString(),
            initiatedByActorName,
            null,
            cancellationToken);
        var outputInteractionIds = new List<string>();

        var route = _promptRouter.Resolve(submission.Intent);
        _logger.LogInformation(
            "Unified prompt route selected for session {SessionId}: intent={Intent}, command={Command}, identity={IdentityId}",
            submission.SessionId,
            submission.Intent,
            route.TargetCommand,
            identity.Id);

        var effectiveOnChunk = EnableRolePlayStreaming ? onChunk : null;

        RolePlayInteraction interaction;
        if (submission.Intent == PromptIntent.Instruction)
        {
            interaction = new RolePlayInteraction
            {
                InteractionType = InteractionType.System,
                ActorName = "Instruction",
                Content = submission.PromptText.Trim()
            };
        }
        else
        {
            if (submission.SubmittedVia != SubmissionSource.PlusButton
                && !_identityOptionsService.IsIdentityAvailableForIntent(session, identity, submission.Intent, out var availabilityReason))
            {
                throw new InvalidOperationException(availabilityReason ?? "The selected identity is not available for this action.");
            }

            var selectedActorName = identity.SourceType == IdentityOptionSource.CustomCharacter
                ? (string.IsNullOrWhiteSpace(customName) ? identity.DisplayName : customName)
                : identity.DisplayName;

            var userPromptInteraction = new RolePlayInteraction
            {
                InteractionType = ToInteractionType(identity.Actor),
                ActorName = selectedActorName,
                Content = submission.PromptText.Trim()
            };

            session.Interactions.Add(userPromptInteraction);
            outputInteractionIds.Add(userPromptInteraction.Id);
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, userPromptInteraction, cancellationToken);

            if (submission.SubmittedVia == SubmissionSource.PlusButton)
            {
                interaction = userPromptInteraction;
            }
            else
            {
                await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);

                interaction = await _continuationService.ContinueAsync(
                    session,
                    identity.Actor,
                    selectedActorName,
                    submission.Intent,
                    BuildContinuationPromptText(submission.Intent, submission.PromptText),
                    effectiveOnChunk,
                    cancellationToken);

                session.Interactions.Add(interaction);
                outputInteractionIds.Add(interaction.Id);
                session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
            }
        }

        if (submission.Intent == PromptIntent.Instruction)
        {
            session.Interactions.Add(interaction);
            outputInteractionIds.Add(interaction.Id);
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
        }

        session.Status = RolePlaySessionStatus.InProgress;
        session.BehaviorMode = submission.BehaviorModeAtSubmit;
        session.ModifiedAt = DateTime.UtcNow;

        // Reset turn tracking when user submits any message — it's no longer "their turn"
        if (submission.Intent != PromptIntent.Instruction)
        {
            session.ConsecutiveNpcTurns = 0;
            session.CurrentTurnState = TurnState.NpcTurn;
        }

        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-unified-prompt-submitted");
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            EventKind = "PromptSubmitted",
            Severity = "Info",
            ActorName = interaction.ActorName,
            ModelIdentifier = interaction.GeneratedByModelId,
            ProviderName = interaction.GeneratedByProvider,
            Summary = "Unified prompt submission completed",
            MetadataJson = JsonSerializer.Serialize(new
            {
                submission.Intent,
                submission.SubmittedVia,
                submission.SelectedIdentityId,
                submission.SelectedIdentityType,
                interaction.Id,
                interaction.ActorName
            })
        }, cancellationToken);

        _logger.LogInformation(
            "Unified prompt executed for session {SessionId}: actor={Actor}, mode={Mode}",
            session.Id,
            interaction.ActorName,
            session.BehaviorMode);

        var steerDirective = string.Empty;
        var steerCommandRequested = submission.Intent == PromptIntent.Instruction
            && TryExtractSteerDirective(submission.PromptText, out steerDirective);
        if (steerCommandRequested)
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                EventKind = "SteerCommandApplied",
                Severity = "Info",
                ActorName = interaction.ActorName,
                Summary = "Steer command applied without phase progression",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    command = "/steer",
                    directive = steerDirective,
                    currentPhase = session.AdaptiveState.CurrentNarrativePhase.ToString(),
                    activeThemeId = session.AdaptiveState.ActiveScenarioId,
                    currentSceneLocation = session.AdaptiveState.CurrentSceneLocation
                })
            }, cancellationToken);

            await _stateRepository.CompleteTurnAsync(
                session.Id,
                persistedTurn.TurnId,
                outputInteractionIds,
                succeeded: true,
                cancellationToken: cancellationToken);

            return interaction;
        }

        var nextPhaseCommandRequested = submission.Intent == PromptIntent.Instruction
            && ContainsNextPhaseCommand(submission.PromptText);
        var explicitClimaxCompletionRequested = ContainsClimaxCompletionCommand(submission.PromptText);
        // Always align V2 state into AdaptiveState immediately before resolving the manual phase target.
        // This prevents V1 pipeline mutations (from UpdateFromInteractionAsync above) from polluting
        // the phase used by ResolveManualPhaseAdvanceTarget, which must reflect the V2 canonical state.
        await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
        var phaseBeforePipeline = session.AdaptiveState.CurrentNarrativePhase;
        var activeScenarioBeforePipeline = session.AdaptiveState.ActiveScenarioId;
        var manualPhaseAdvanceTarget = ResolveManualPhaseAdvanceTarget(submission.PromptText, phaseBeforePipeline);

        if (nextPhaseCommandRequested || explicitClimaxCompletionRequested)
        {
            _logger.LogInformation(
                "Phase command received: SessionId={SessionId} CommandText={CommandText} CurrentPhase={CurrentPhase} ManualTarget={ManualTarget} ClimaxCompletion={ClimaxCompletion}",
                session.Id,
                submission.PromptText,
                phaseBeforePipeline,
                manualPhaseAdvanceTarget?.ToString() ?? "(none)",
                explicitClimaxCompletionRequested);
        }

        await RunRolePlayV2PipelinesAsync(
            session,
            DecisionTrigger.InteractionStart,
            cancellationToken,
            explicitClimaxCompletionRequested,
            manualPhaseAdvanceTarget);

        if (nextPhaseCommandRequested || explicitClimaxCompletionRequested)
        {
            var phaseAfterPipeline = session.AdaptiveState.CurrentNarrativePhase;
            var activeScenarioAfterPipeline = session.AdaptiveState.ActiveScenarioId;
            var phaseChanged = phaseAfterPipeline != phaseBeforePipeline;

            if (!phaseChanged)
            {
                _logger.LogWarning(
                    "Phase command completed without phase change: SessionId={SessionId} CommandText={CommandText} Phase={Phase} ManualTarget={ManualTarget} ClimaxCompletion={ClimaxCompletion} ActiveScenarioBefore={ActiveScenarioBefore} ActiveScenarioAfter={ActiveScenarioAfter}",
                    session.Id,
                    submission.PromptText,
                    phaseAfterPipeline,
                    manualPhaseAdvanceTarget?.ToString() ?? "(none)",
                    explicitClimaxCompletionRequested,
                    activeScenarioBeforePipeline ?? string.Empty,
                    activeScenarioAfterPipeline ?? string.Empty);
            }
            else
            {
                _logger.LogInformation(
                    "Phase command advanced phase: SessionId={SessionId} CommandText={CommandText} FromPhase={FromPhase} ToPhase={ToPhase} ActiveScenario={ActiveScenario}",
                    session.Id,
                    submission.PromptText,
                    phaseBeforePipeline,
                    phaseAfterPipeline,
                    activeScenarioAfterPipeline ?? string.Empty);
            }
        }

        await _stateRepository.CompleteTurnAsync(
            session.Id,
            persistedTurn.TurnId,
            outputInteractionIds,
            succeeded: true,
            cancellationToken: cancellationToken);

        return interaction;
    }

    public async Task<ContinueAsResult> ContinueAsAsync(
        ContinueAsRequest request,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await GetSessionAsync(request.SessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{request.SessionId}' not found.");

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        if (!_commandValidator.ValidateContinueRequest(request, session.BehaviorMode, out var validationError))
        {
            return new ContinueAsResult
            {
                Success = false,
                ValidationError = validationError,
                IsClearResult = request.IsClearAction
            };
        }

        if (request.IsClearAction)
        {
            _logger.LogInformation("Continue As selections cleared for session {SessionId}", request.SessionId);
            return new ContinueAsResult { Success = true, IsClearResult = true };
        }

        // If it was the user's turn but they clicked continue, reset turn tracking and proceed
        if (session.BehaviorMode == BehaviorMode.TakeTurns
            && session.CurrentTurnState == TurnState.UserTurn
            && request.TriggeredBy == SubmissionSource.MainOverflowContinue)
        {
            session.ConsecutiveNpcTurns = 0;
            session.CurrentTurnState = TurnState.NpcTurn;
        }

        var effectiveOnChunk = EnableRolePlayStreaming ? onChunk : null;
        var selectedIdentityOptions = await ResolveSelectedIdentityOptionsAsync(session, request, cancellationToken);
        var result = new ContinueAsResult { Success = true };
        var persistedTurn = await _stateRepository.StartTurnAsync(
            session.Id,
            "ContinueAs",
            request.TriggeredBy.ToString(),
            string.IsNullOrWhiteSpace(request.CustomIdentityName) ? session.PersonaName : request.CustomIdentityName,
            null,
            cancellationToken);
        var outputInteractionIds = new List<string>();

        var isOverflowContinue = request.TriggeredBy == SubmissionSource.MainOverflowContinue;

        // --- OPENING NARRATIVE ---
        // If no interactions yet, always generate a scene-setting narrative FIRST
        var isOpeningScene = session.Interactions.Count(i => !i.IsExcluded) == 0;
        if (isOpeningScene && session.AutoNarrative)
        {
            var openingPrompt = await BuildOpeningNarrativePromptAsync(session, cancellationToken);
            await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
            var openingNarrative = await _continuationService.ContinueAsync(
                session,
                ContinueAsActor.Npc,
                "Narrative",
                PromptIntent.Narrative,
                openingPrompt,
                effectiveOnChunk,
                cancellationToken);

            openingNarrative.InteractionType = InteractionType.System;
            openingNarrative.ActorName = "Narrative";
            result.NarrativeOutput = openingNarrative;
            session.Interactions.Add(openingNarrative);
            outputInteractionIds.Add(openingNarrative.Id);
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, openingNarrative, cancellationToken);
        }

        if (selectedIdentityOptions.Count > 0)
        {
            // Explicit identity selections — generate sequentially, accumulating context
            foreach (var option in selectedIdentityOptions)
            {
                var actorName = ResolveOptionActorName(option, request.CustomIdentityName);
                await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
                var interaction = await _continuationService.ContinueAsync(
                    session,
                    option.Actor,
                    actorName,
                    PromptIntent.Message,
                    "Continue role-play for the selected character.",
                    effectiveOnChunk,
                    cancellationToken);

                result.ParticipantOutputs.Add(interaction);
                // Add to session immediately so the next generation sees this in its context
                session.Interactions.Add(interaction);
                outputInteractionIds.Add(interaction.Id);
                session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
            }
        }
        else if (isOverflowContinue)
        {
            // --- MULTI-ACTOR OVERFLOW CONTINUE ---
            // Determine which scene characters should naturally respond,
            // generate sequentially so each sees the prior output in context.
            var sceneActors = await ResolveSceneContinueActorsAsync(session, cancellationToken);

            var batchSize = Math.Max(1, Math.Min(session.SceneContinueBatchSize, sceneActors.Count));
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "OverflowActorSelection",
                Severity = "Info",
                ActorName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName,
                Summary = $"Overflow actor auto-selection resolved ({batchSize} of {sceneActors.Count} candidates).",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    source = request.TriggeredBy.ToString(),
                    mode = session.BehaviorMode.ToString(),
                    batchSize,
                    candidates = sceneActors.Select((x, index) => new
                    {
                        rank = index + 1,
                        actor = x.Actor.ToString(),
                        name = x.Name,
                        reason = x.Reason,
                        selected = index < batchSize
                    }).ToList()
                })
            }, cancellationToken);

            for (var i = 0; i < batchSize; i++)
            {
                var candidate = sceneActors[i];
                var actor = candidate.Actor;
                var actorName = candidate.Name;
                var promptText = i == 0
                    ? "Continue the scene naturally with the next character response."
                    : "Continue the conversation naturally, building on the previous response.";

                await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
                var interaction = await _continuationService.ContinueAsync(
                    session, actor, actorName, PromptIntent.Message, promptText, effectiveOnChunk, cancellationToken);

                result.ParticipantOutputs.Add(interaction);
                // Append to session so next iteration's prompt sees this interaction
                session.Interactions.Add(interaction);
                outputInteractionIds.Add(interaction.Id);
                session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
            }
        }
        else
        {
            // Fallback: single actor default
            var fallbackActor = ResolveDefaultContinueActor(session);
            var fallbackActorName = ResolveActorName(fallbackActor, request.CustomIdentityName);
            await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
            var interaction = await _continuationService.ContinueAsync(
                session,
                fallbackActor,
                fallbackActorName,
                PromptIntent.Message,
                "Continue naturally with the next interaction that best fits recent context.",
                effectiveOnChunk,
                cancellationToken);

            result.ParticipantOutputs.Add(interaction);
            session.Interactions.Add(interaction);
            outputInteractionIds.Add(interaction.Id);
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
        }

        // --- AUTO-NARRATIVE ---
        // Include narrative if explicitly requested OR if AutoNarrative is on for overflow continues
        // Skip if we already generated the opening narrative above
        var suppressNarrativeForDecisionTurn = session.SuppressNextNarrativeAfterDecision;
        if (suppressNarrativeForDecisionTurn)
        {
            session.SuppressNextNarrativeAfterDecision = false;
        }

        var shouldIncludeNarrative = !isOpeningScene
            && !suppressNarrativeForDecisionTurn
            && (request.IncludeNarrative
                || (isOverflowContinue && session.AutoNarrative && ShouldAutoNarrate(session)));

        if (suppressNarrativeForDecisionTurn && _debugEventSink is not null)
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "DecisionPostTurnNarrativeSuppressed",
                Severity = "Info",
                ActorName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName,
                Summary = "Narrative was suppressed for the immediate post-decision continuation turn.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    source = request.TriggeredBy.ToString(),
                    includeNarrativeRequested = request.IncludeNarrative,
                    isOverflowContinue
                })
            }, cancellationToken);
        }

        if (shouldIncludeNarrative)
        {
            var narrativePrompt = DetermineNarrativePrompt(session);
            await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
            var narrative = await _continuationService.ContinueAsync(
                session,
                ContinueAsActor.Npc,
                "Narrative",
                PromptIntent.Narrative,
                narrativePrompt,
                effectiveOnChunk,
                cancellationToken);

            narrative.InteractionType = InteractionType.System;
            narrative.ActorName = "Narrative";
            result.NarrativeOutput = narrative;
            session.Interactions.Add(narrative);
            outputInteractionIds.Add(narrative.Id);
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, narrative, cancellationToken);
        }

        // --- TURN-TAKING ENFORCEMENT ---
        // Count consecutive NPC turns and signal user turn if threshold reached
        UpdateTurnTracking(session, result);

        session.Status = RolePlaySessionStatus.InProgress;
        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-continueas-generated");

        _logger.LogInformation(
            "Continue As executed for session {SessionId}: participants={ParticipantCount}, includeNarrative={IncludeNarrative}, source={Source}, isUserTurn={IsUserTurn}",
            session.Id,
            result.ParticipantOutputs.Count,
            shouldIncludeNarrative,
            request.TriggeredBy,
            result.IsUserTurn);

        await RunRolePlayV2PipelinesAsync(session, DecisionTrigger.InteractionStart, cancellationToken);
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-continueas-v2-processed");

        await _stateRepository.CompleteTurnAsync(
            session.Id,
            persistedTurn.TurnId,
            outputInteractionIds,
            succeeded: true,
            cancellationToken: cancellationToken);

        return result;
    }

    public async Task<RolePlayPendingDecisionPrompt?> GetPendingDecisionPromptAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var points = await _stateRepository.LoadDecisionPointsAsync(sessionId, 30, cancellationToken);
        if (points.Count == 0)
        {
            return null;
        }

        var appliedIds = session.AppliedDecisionPointIds ??= [];
        var deferredIds = session.DeferredDecisionPointIds ??= [];
        var pending = points
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefault(x =>
                !appliedIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase)
                && !deferredIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase));
        if (pending is null)
        {
            return null;
        }

        var options = await _stateRepository.LoadDecisionOptionsAsync(pending.DecisionPointId, cancellationToken);
        options = ApplyTransparencyToDecisionOptions(options, pending.TransparencyMode);
        return new RolePlayPendingDecisionPrompt
        {
            DecisionPoint = pending,
            Options = options
        };
    }

    public async Task<IReadOnlyList<RolePlayPendingDecisionPrompt>> GetDeferredDecisionPromptsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return [];
        }

        var points = await _stateRepository.LoadDecisionPointsAsync(sessionId, 60, cancellationToken);
        if (points.Count == 0)
        {
            return [];
        }

        var appliedIds = session.AppliedDecisionPointIds ??= [];
        var deferredIds = session.DeferredDecisionPointIds ??= [];
        if (deferredIds.Count == 0)
        {
            return [];
        }

        var deferredPoints = points
            .Where(x =>
                deferredIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase)
                && !appliedIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedUtc)
            .ToList();

        var prompts = new List<RolePlayPendingDecisionPrompt>(deferredPoints.Count);
        foreach (var point in deferredPoints)
        {
            var options = await _stateRepository.LoadDecisionOptionsAsync(point.DecisionPointId, cancellationToken);
            options = ApplyTransparencyToDecisionOptions(options, point.TransparencyMode);
            prompts.Add(new RolePlayPendingDecisionPrompt
            {
                DecisionPoint = point,
                Options = options
            });
        }

        return prompts;
    }

    public async Task<DecisionOutcome?> ApplyDecisionAsync(
        string sessionId,
        string decisionPointId,
        string optionId,
        string? customResponseText = null,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var state = MapToV2State(session);
        var decisionPoint = await ResolveDecisionPointAsync(sessionId, decisionPointId, cancellationToken);
        var askingActorId = !string.IsNullOrWhiteSpace(decisionPoint?.AskingActorName)
            ? decisionPoint!.AskingActorName
            : ResolveDecisionActorId(state, session, session.PersonaName);
        var targetActorId = !string.IsNullOrWhiteSpace(decisionPoint?.TargetActorId)
            ? decisionPoint!.TargetActorId
            : ResolveDecisionTargetActorId(state, askingActorId);
        var responderActorId = !string.IsNullOrWhiteSpace(targetActorId)
            ? targetActorId
            : askingActorId;
        var outcome = await _decisionPointService.ApplyDecisionAsync(
            state,
            new DecisionSubmission
            {
                DecisionPointId = decisionPointId,
                OptionId = optionId,
                CustomResponseText = customResponseText,
                ActorName = string.IsNullOrWhiteSpace(responderActorId)
                    ? (string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName)
                    : responderActorId,
                TargetActorId = targetActorId
            },
            targetActorId,
            cancellationToken);

        if (!outcome.Applied)
        {
            return outcome;
        }

        ApplyDecisionOutcomeToSessionState(session, outcome);
        session.AppliedDecisionPointIds ??= [];
        if (!session.AppliedDecisionPointIds.Contains(decisionPointId, StringComparer.OrdinalIgnoreCase))
        {
            session.AppliedDecisionPointIds.Add(decisionPointId);
        }

        session.DeferredDecisionPointIds ??= [];
        session.DeferredDecisionPointIds.RemoveAll(x => string.Equals(x, decisionPointId, StringComparison.OrdinalIgnoreCase));
        session.SuppressNextNarrativeAfterDecision = _suppressNarrativeAfterDecision;

        var selectedOption = await ResolveAppliedDecisionOptionAsync(decisionPointId, optionId, cancellationToken);
        var (selectedDialogue, selectedDialogueSource) = ResolveSelectedDecisionDialogueWithSource(selectedOption, customResponseText);
        if (string.IsNullOrWhiteSpace(selectedDialogue))
        {
            selectedDialogue = ResolveFallbackDecisionDialogue(optionId);
            selectedDialogueSource = "fallback-option-label";
        }

        var steeringInstruction = BuildDecisionSteeringInstruction(selectedDialogue);
        if (!string.IsNullOrWhiteSpace(steeringInstruction))
        {
            var instructionActorName = BuildDecisionInstructionActorName(session, targetActorId);
            session.Interactions.Add(new RolePlayInteraction
            {
                InteractionType = InteractionType.System,
                ActorName = instructionActorName,
                Content = steeringInstruction
            });

            if (_debugEventSink is not null)
            {
                await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                {
                    SessionId = session.Id,
                    EventKind = "DecisionInstructionInjected",
                    Severity = "Info",
                    ActorName = instructionActorName,
                    Summary = $"Decision instruction injected for {optionId}.",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        decisionPointId,
                        optionId,
                        trigger = decisionPoint?.TriggerSource,
                        targetActorId,
                        askingActorId,
                        selectedDialogue,
                        selectedDialogueSource,
                        injectedInstruction = steeringInstruction,
                        responsePreview = selectedOption?.ResponsePreview,
                        displayText = selectedOption?.DisplayText
                    })
                }, cancellationToken);
            }
        }
        else if (_debugEventSink is not null)
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "DecisionInstructionSkipped",
                Severity = "Warning",
                ActorName = ResolveLocationActorLabel(session, targetActorId),
                Summary = $"Decision instruction skipped for {optionId}: no dialogue resolved.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    decisionPointId,
                    optionId,
                    trigger = decisionPoint?.TriggerSource,
                    targetActorId,
                    askingActorId,
                    selectedDialogue,
                    selectedDialogueSource,
                    responsePreview = selectedOption?.ResponsePreview,
                    displayText = selectedOption?.DisplayText
                })
            }, cancellationToken);
        }

        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-decision-applied");
        return outcome;
    }

    public async Task<bool> DeferDecisionPointAsync(
        string sessionId,
        string decisionPointId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(decisionPointId))
        {
            return false;
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var decisionPoint = await ResolveDecisionPointAsync(sessionId, decisionPointId, cancellationToken);
        if (decisionPoint is null)
        {
            return false;
        }

        session.AppliedDecisionPointIds ??= [];
        if (session.AppliedDecisionPointIds.Contains(decisionPointId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        session.DeferredDecisionPointIds ??= [];
        if (!session.DeferredDecisionPointIds.Contains(decisionPointId, StringComparer.OrdinalIgnoreCase))
        {
            session.DeferredDecisionPointIds.Add(decisionPointId);
        }

        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-decision-deferred");
        return true;
    }

    public async Task<bool> RestoreDeferredDecisionPointAsync(
        string sessionId,
        string decisionPointId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(decisionPointId))
        {
            return false;
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        session.DeferredDecisionPointIds ??= [];
        var removed = session.DeferredDecisionPointIds.RemoveAll(x => string.Equals(x, decisionPointId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
        {
            return false;
        }

        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-decision-restored");
        return true;
    }

    public async Task<bool> SkipDecisionPointAsync(
        string sessionId,
        string decisionPointId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(decisionPointId))
        {
            return false;
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var decisionPoint = await ResolveDecisionPointAsync(sessionId, decisionPointId, cancellationToken);
        if (decisionPoint is null)
        {
            return false;
        }

        session.AppliedDecisionPointIds ??= [];
        if (!session.AppliedDecisionPointIds.Contains(decisionPointId, StringComparer.OrdinalIgnoreCase))
        {
            session.AppliedDecisionPointIds.Add(decisionPointId);
        }

        session.DeferredDecisionPointIds ??= [];
        session.DeferredDecisionPointIds.RemoveAll(x => string.Equals(x, decisionPointId, StringComparison.OrdinalIgnoreCase));

        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-decision-skipped");
        return true;
    }

    /// <summary>
    /// Resolves the scene characters that should naturally continue the conversation.
    /// Looks at the scenario character list and recent interaction history to pick
    /// the most relevant characters in a natural conversation order.
    /// </summary>
    private async Task<List<OverflowActorCandidate>> ResolveSceneContinueActorsAsync(
        RolePlaySession session,
        CancellationToken cancellationToken)
    {
        var actors = new List<OverflowActorCandidate>();
        var autoAllowedActors = _behaviorModeService.GetAllowedActors(session.BehaviorMode, explicitSelection: false).ToHashSet();

        // Gather scenario characters.
        var sceneCharacterNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                foreach (var character in scenario.Characters)
                {
                    if (!string.IsNullOrWhiteSpace(character.Name))
                    {
                        sceneCharacterNames.Add(character.Name.Trim());
                    }
                }
            }
        }

        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        sceneCharacterNames = sceneCharacterNames
            .Where(name => !string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sceneCharacterNames.Count == 0 && !autoAllowedActors.Contains(ContinueAsActor.You))
        {
            // No scenario characters — fall back to default single actor
            var fallback = ResolveDefaultContinueActor(session);
            actors.Add(new OverflowActorCandidate(
                fallback,
                ResolveActorName(fallback, null),
                "Fallback actor because no scenario characters were available for automatic continuation."));
            return actors;
        }

        // Determine conversation order by recency: characters who haven't spoken recently,
        // or never spoke at all, go first.
        var recentActors = session.Interactions
            .Where(i => (i.InteractionType == InteractionType.Npc || i.InteractionType == InteractionType.Custom) && !i.IsExcluded)
            .TakeLast(6)
            .Select(i => i.ActorName?.Trim())
            .ToList();

        var currentSceneLocation = session.AdaptiveState.CurrentSceneLocation;
        var ordered = sceneCharacterNames
            .Select((name, scenarioOrder) => new
            {
                Name = name,
                ScenarioOrder = scenarioOrder,
                LastSeenIndex = recentActors.FindLastIndex(actorName => string.Equals(actorName, name, StringComparison.OrdinalIgnoreCase)),
                InScene = IsActorInCurrentScene(session, name, currentSceneLocation)
            })
            .OrderByDescending(x => x.InScene)
            .ThenBy(x => x.LastSeenIndex < 0 ? int.MinValue : x.LastSeenIndex)
            .ThenBy(x => x.ScenarioOrder)
            .Select(x => x.Name)
            .ToList();

        if (autoAllowedActors.Contains(ContinueAsActor.Npc))
        {
            foreach (var name in ordered)
            {
                var inScene = IsActorInCurrentScene(session, name, currentSceneLocation);
                var recencyIndex = recentActors.FindLastIndex(actorName => string.Equals(actorName, name, StringComparison.OrdinalIgnoreCase));
                var recencyReason = recencyIndex < 0 ? "not recently active" : $"recent-index={recencyIndex}";
                var sceneReason = inScene ? "in-scene" : "out-of-scene";
                actors.Add(new OverflowActorCandidate(
                    ContinueAsActor.Npc,
                    name,
                    $"NPC auto candidate ({sceneReason}, {recencyReason})."));
            }
        }

        if (autoAllowedActors.Contains(ContinueAsActor.You)
            && ShouldIncludePersonaInAutoRotation(session, personaName, currentSceneLocation))
        {
            var insertIndex = session.SceneContinueBatchSize <= 1
                ? 0
                : Math.Min(actors.Count, 1);
            var personaInScene = IsActorInCurrentScene(session, personaName, currentSceneLocation);
            var personaReason = personaInScene
                ? "Persona auto candidate (in-scene; injected into mixed rotation)."
                : "Persona auto candidate (out-of-scene allowed by recency context).";
            actors.Insert(insertIndex, new OverflowActorCandidate(ContinueAsActor.You, personaName, personaReason));
        }

        if (actors.Count == 0)
        {
            var fallback = ResolveDefaultContinueActor(session);
            var fallbackName = fallback == ContinueAsActor.You ? personaName : ResolveActorName(fallback, null);
            actors.Add(new OverflowActorCandidate(
                fallback,
                fallbackName,
                "Fallback actor because automatic candidate list was empty after mode filtering."));
        }

        return actors;
    }

    private static bool IsActorInCurrentScene(RolePlaySession session, string actorName, string? currentSceneLocation)
    {
        if (string.IsNullOrWhiteSpace(actorName) || string.IsNullOrWhiteSpace(currentSceneLocation))
        {
            return false;
        }

        var location = session.AdaptiveState.CharacterLocations.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(x.CharacterId)
            && string.Equals(x.CharacterId, actorName, StringComparison.OrdinalIgnoreCase));

        if (location is null || string.IsNullOrWhiteSpace(location.TrueLocation))
        {
            return false;
        }

        return string.Equals(location.TrueLocation.Trim(), currentSceneLocation.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIncludePersonaInAutoRotation(RolePlaySession session, string personaName, string? currentSceneLocation)
    {
        var personaInScene = IsActorInCurrentScene(session, personaName, currentSceneLocation);
        var recent = session.Interactions
            .Where(x => !x.IsExcluded && x.InteractionType != InteractionType.System)
            .TakeLast(4)
            .ToList();

        if (recent.Count == 0)
        {
            return personaInScene;
        }

        var personaSpokeVeryRecently = recent.TakeLast(2).Any(x =>
            x.InteractionType == InteractionType.User
            || string.Equals(x.ActorName, personaName, StringComparison.OrdinalIgnoreCase));
        if (personaSpokeVeryRecently)
        {
            return false;
        }

        var npcSpokeRecently = recent.TakeLast(2).Any(x => x.InteractionType is InteractionType.Npc or InteractionType.Custom);
        if (!npcSpokeRecently)
        {
            return false;
        }

        // Permit occasional out-of-scene persona turns, but bias toward in-scene inclusion.
        return personaInScene || recent.Count >= 3;
    }

    private sealed record OverflowActorCandidate(ContinueAsActor Actor, string Name, string Reason);

    private async Task RebuildAdaptiveStateInternalAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        session.AdaptiveState = new RolePlayAdaptiveState();

        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                await SeedAdaptiveStateFromScenarioAsync(session, scenario, cancellationToken);
            }
        }

        foreach (var interaction in session.Interactions.Where(x => !x.IsExcluded))
        {
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
        }

        _logger.LogInformation(
            "Adaptive state rebuilt for session {SessionId}: interactionsReplayed={InteractionCount}, primaryTheme={PrimaryTheme}, secondaryTheme={SecondaryTheme}",
            session.Id,
            session.Interactions.Count(x => !x.IsExcluded),
            session.AdaptiveState.ThemeTracker.PrimaryThemeId ?? "(none)",
            session.AdaptiveState.ThemeTracker.SecondaryThemeId ?? "(none)");
    }

    private async Task SeedAdaptiveStateFromScenarioAsync(RolePlaySession session, DreamGenClone.Web.Domain.Scenarios.Scenario scenario, CancellationToken cancellationToken)
    {
        var resolvedBaseStats = AdaptiveStatCatalog.NormalizePartial(scenario.ResolvedBaseStats);
        if (!string.IsNullOrWhiteSpace(scenario.BaseStatProfileId))
        {
            var baseStatProfile = await _baseStatProfileService.GetAsync(scenario.BaseStatProfileId, cancellationToken);
            if (baseStatProfile is not null)
            {
                resolvedBaseStats = AdaptiveStatCatalog.NormalizeComplete(baseStatProfile.DefaultStats);
                scenario.ResolvedBaseStats = new Dictionary<string, int>(resolvedBaseStats, StringComparer.OrdinalIgnoreCase);
            }
        }

        session.CharacterPerspectives = scenario.Characters
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new RolePlayCharacterPerspective
            {
                CharacterId = x.Id,
                CharacterName = x.Name!.Trim(),
                PerspectiveMode = x.PerspectiveMode
            })
            .ToList();

        foreach (var character in scenario.Characters)
        {
            if (string.IsNullOrWhiteSpace(character.Name))
            {
                continue;
            }

            var mergedStats = new Dictionary<string, int>(resolvedBaseStats, StringComparer.OrdinalIgnoreCase);
            var normalizedCharacterOverrides = AdaptiveStatCatalog.NormalizePartial(character.BaseStats);
            foreach (var (statName, statValue) in normalizedCharacterOverrides)
            {
                mergedStats[statName] = statValue;
            }

            if (mergedStats.Count == 0)
            {
                continue;
            }

            session.AdaptiveState.CharacterStats[character.Name.Trim()] = new CharacterStatBlock
            {
                CharacterId = character.Id,
                Stats = mergedStats
            };
        }

        await _adaptiveStateService.SeedFromScenarioAsync(session, scenario, cancellationToken);
    }

    /// <summary>
    /// Determines whether auto-narrative should fire based on recent interaction patterns.
    /// Narrative is warranted after user actions, scene transitions, or long character-only exchanges.
    /// </summary>
    private static bool ShouldAutoNarrate(RolePlaySession session)
    {
        var recent = session.Interactions
            .Where(i => !i.IsExcluded)
            .TakeLast(6)
            .ToList();

        if (recent.Count == 0)
            return true; // Opening scene — always narrate

        var lastInteraction = recent[^1];

        // Narrate after a user action (Ken stepped away → describe what happens)
        if (lastInteraction.InteractionType == InteractionType.User)
            return true;

        // Narrate after an instruction (scene direction → describe the result)
        if (lastInteraction.InteractionType == InteractionType.System && lastInteraction.ActorName == "Instruction")
            return true;

        // Count consecutive character messages without narrative
        var consecutiveMessages = 0;
        for (var i = recent.Count - 1; i >= 0; i--)
        {
            if (recent[i].InteractionType is InteractionType.Npc or InteractionType.Custom)
                consecutiveMessages++;
            else
                break;
        }

        // Insert narrative after 2+ character messages without one
        return consecutiveMessages >= 2;
    }

    private async Task<string> BuildOpeningNarrativePromptAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        const string basePrompt = "Set the opening scene. Establish the setting, atmosphere, and the characters present. Describe the environment and the initial situation the characters find themselves in.";

        if (string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            return basePrompt + " In the opening paragraph, explicitly state where the scene is happening and where key characters are in relation to each other.";
        }

        var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
        var locationNames = scenario?.Locations
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList() ?? [];

        if (locationNames.Count == 0)
        {
            return basePrompt + " In the opening paragraph, explicitly state where the scene is happening and where key characters are in relation to each other.";
        }

        return basePrompt
            + $" In the first paragraph, explicitly ground the scene in one clear location using one of these names: {string.Join(", ", locationNames)}."
            + " Also establish where the major characters are relative to each other (for example beside, across, near the doorway, across the room)."
            + " Keep this grounding natural and immersive, not bullet points.";
    }

    /// <summary>
    /// Builds a context-aware narrative prompt based on recent session state.
    /// </summary>
    private static string DetermineNarrativePrompt(RolePlaySession session)
    {
        var lastInteraction = session.Interactions
            .Where(i => !i.IsExcluded)
            .LastOrDefault();

        if (lastInteraction is null)
            return "Set the scene and establish the atmosphere.";

        return lastInteraction.InteractionType switch
        {
            InteractionType.User => $"Describe what happens after {session.PersonaName}'s action. Include scene details, other characters' reactions, internal thoughts, and sensory details.",
            InteractionType.System when lastInteraction.ActorName == "Instruction" => "Follow the instruction. Describe the scene in detail with environment, body language, and atmosphere.",
            _ => "Describe the scene between the characters: body language, internal thoughts, sensory details, and atmosphere. Bridge the dialogue with vivid narrative prose."
        };
    }

    /// <summary>
    /// Updates consecutive NPC turn counter and signals user turn if threshold reached.
    /// </summary>
    private static void UpdateTurnTracking(RolePlaySession session, ContinueAsResult result)
    {
        if (session.BehaviorMode != BehaviorMode.TakeTurns)
            return;

        // Count the NPC outputs just generated
        var npcCount = result.ParticipantOutputs.Count(i => i.InteractionType is InteractionType.Npc or InteractionType.Custom);
        if (result.NarrativeOutput is not null)
            npcCount++;

        session.ConsecutiveNpcTurns += npcCount;

        if (session.ConsecutiveNpcTurns >= session.TurnTakingThreshold)
        {
            session.CurrentTurnState = TurnState.UserTurn;
            result.IsUserTurn = true;
        }
        else
        {
            session.CurrentTurnState = TurnState.NpcTurn;
        }
    }

    private static string BuildContinuationPromptText(PromptIntent intent, string promptText)
    {
        var trimmed = promptText.Trim();
        return intent switch
        {
            PromptIntent.Message => $"Respond in-character and follow this direction for tone/mood/action: {trimmed}",
            PromptIntent.Narrative => $"Expand this into narrative from the selected character POV: {trimmed}",
            _ => trimmed
        };
    }

    private async Task EnsurePersistedSessionsLoadedAsync(CancellationToken cancellationToken)
    {
        var persisted = await _sessionService.GetSessionsByTypeAsync(SessionService.RolePlaySessionType, cancellationToken);
        foreach (var item in persisted)
        {
            if (Sessions.ContainsKey(item.Id))
            {
                continue;
            }

            var loaded = await _sessionService.LoadRolePlaySessionAsync(item.Id, cancellationToken);
            if (loaded is not null)
            {
                if (loaded.Status == RolePlaySessionStatus.NotStarted && loaded.Interactions.Count > 0)
                {
                    loaded.Status = RolePlaySessionStatus.InProgress;
                }

                Sessions.TryAdd(loaded.Id, loaded);
            }
        }
    }

    private async Task ValidateSessionCompatibilityOrThrowAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        if (_compatibilityService is null)
        {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(session);
        var compatibilityError = await _compatibilityService.ValidateSessionPayloadAsync(session.Id, payloadJson, cancellationToken);
        if (compatibilityError is not null)
        {
            throw new InvalidOperationException($"Session '{session.Id}' is not compatible with RolePlay v2 ({compatibilityError.ErrorCode}).");
        }
    }

    private async Task RunRolePlayV2PipelinesAsync(
        RolePlaySession session,
        DecisionTrigger trigger,
        CancellationToken cancellationToken,
        bool explicitClimaxCompletionRequested = false,
        DreamGenClone.Domain.RolePlay.NarrativePhase? manualPhaseAdvanceTarget = null)
    {
        var previousV2State = await _stateRepository.LoadAdaptiveStateAsync(session.Id, cancellationToken);
        var v2State = HydrateV2State(session, previousV2State);
        NormalizePhaseOverrideLock(v2State);
        var climaxCompletionRequested = explicitClimaxCompletionRequested || IsClimaxCompletionRequested(session);

        // Count actual NPC/narrative interactions generated since the last pipeline evaluation,
        // so batch ContinueAs calls (which may generate 2-3 interactions per button click) advance
        // the counter correctly instead of always adding +1 regardless of batch size.
        var totalGeneratedInteractions = session.Interactions.Count(x =>
            x.InteractionType is InteractionType.Npc or InteractionType.Custom or InteractionType.System);

        var previousPhaseInteractionCount = Math.Max(0, v2State.InteractionCountInPhase);
        var generatedSinceLastEval = previousV2State?.LastEvaluationUtc is { } lastEval
            ? session.Interactions.Count(x =>
                x.CreatedAt > lastEval
                && x.InteractionType is InteractionType.Npc or InteractionType.Custom or InteractionType.System)
            : Math.Max(0, totalGeneratedInteractions - previousPhaseInteractionCount);

        var proposedPhaseInteractionCount = previousPhaseInteractionCount + Math.Max(0, generatedSinceLastEval);
        var invariantPhaseInteractionCount = Math.Min(proposedPhaseInteractionCount, totalGeneratedInteractions);
        v2State.InteractionCountInPhase = invariantPhaseInteractionCount;

        if (invariantPhaseInteractionCount != proposedPhaseInteractionCount)
        {
            _logger.LogWarning(
                "RolePlayV2 phase interaction count invariant clamp applied: SessionId={SessionId} PreviousCount={PreviousCount} Delta={Delta} ProposedCount={ProposedCount} ClampedCount={ClampedCount} TotalGeneratedInteractions={TotalGeneratedInteractions} LastEvaluationUtc={LastEvaluationUtc}",
                session.Id,
                previousPhaseInteractionCount,
                generatedSinceLastEval,
                proposedPhaseInteractionCount,
                invariantPhaseInteractionCount,
                totalGeneratedInteractions,
                previousV2State?.LastEvaluationUtc);

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "PhaseInteractionCountMismatchDetected",
                Severity = "Warning",
                Summary = "Phase interaction count clamped by invariant to prevent overcount drift.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    phase = v2State.CurrentPhase.ToString(),
                    previousCount = previousPhaseInteractionCount,
                    delta = generatedSinceLastEval,
                    proposedCount = proposedPhaseInteractionCount,
                    clampedCount = invariantPhaseInteractionCount,
                    totalGeneratedInteractions,
                    lastEvaluationUtc = previousV2State?.LastEvaluationUtc
                })
            }, cancellationToken);
        }

        var candidates = await BuildScenarioCandidatesAsync(session, cancellationToken);
        var (linkedNarrativeGateProfileId, linkedNarrativeGateRules) = await ResolveThemeNarrativeGateConfigAsync(session, v2State, cancellationToken);
        v2State.SelectedNarrativeGateProfileId = linkedNarrativeGateProfileId;

        var manualOverrideLockActive = IsManualThemeOverrideLockActive(session);

        var evaluations = manualOverrideLockActive
            ? Array.Empty<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>()
            : await _scenarioSelectionService.EvaluateCandidatesAsync(v2State, candidates, cancellationToken);
        await _stateRepository.SaveCandidateEvaluationsAsync(evaluations, cancellationToken);

        var inResetPhase = v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Reset;
        var commitResult = (manualOverrideLockActive || inResetPhase)
            ? new ScenarioCommitResult
            {
                Committed = false,
                ScenarioId = v2State.ActiveScenarioId,
                UpdatedConsecutiveLeadCount = v2State.ConsecutiveLeadCount,
                Reason = manualOverrideLockActive ? "ManualOverrideLockActive" : "ResetPhase"
            }
            : await _scenarioSelectionService.TryCommitScenarioAsync(v2State, evaluations, cancellationToken);

        if (v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp)
        {
            var gateSnapshot = ParseBuildUpGateAudit(commitResult.AuditMetadataJson);
            var gateSummary = gateSnapshot.Passed switch
            {
                true => "passed",
                false => "blocked",
                null => "not-configured"
            };

            _logger.LogInformation(
                "RolePlayV2 BuildUp commit gate {GateSummary}: SessionId={SessionId} ProfileId={ProfileId} ProfileName={ProfileName} Configured={Configured} Committed={Committed} CandidateScenarioId={CandidateScenarioId} InteractionCount={InteractionCount} CandidateCount={CandidateCount} Reason={Reason}",
                gateSummary,
                session.Id,
                gateSnapshot.ProfileId,
                gateSnapshot.ProfileName,
                gateSnapshot.Configured,
                commitResult.Committed,
                commitResult.ScenarioId,
                v2State.InteractionCountInPhase,
                evaluations.Count,
                commitResult.Reason);

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "AdaptiveCommitGateEvaluated",
                Severity = gateSnapshot.Passed == false ? "Warning" : "Info",
                Summary = gateSnapshot.Passed == false
                    ? "BuildUp commit blocked by gate rules"
                    : gateSnapshot.Passed == true
                        ? "BuildUp commit gate passed"
                        : "BuildUp commit gate not configured",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    phase = v2State.CurrentPhase.ToString(),
                    interactionCount = v2State.InteractionCountInPhase,
                    candidateCount = evaluations.Count,
                    selectedScenarioId = commitResult.ScenarioId,
                    committed = commitResult.Committed,
                    reason = commitResult.Reason,
                    gateAudit = commitResult.AuditMetadataJson
                })
            }, cancellationToken);
        }

        v2State.ConsecutiveLeadCount = commitResult.UpdatedConsecutiveLeadCount;
        var commitApplied = false;
        if (commitResult.Committed && !string.IsNullOrWhiteSpace(commitResult.ScenarioId))
        {
            var currentPhase = v2State.CurrentPhase;
            var sameScenarioAlreadyActive = string.Equals(
                v2State.ActiveScenarioId,
                commitResult.ScenarioId,
                StringComparison.OrdinalIgnoreCase);
            var enteringArc = currentPhase is DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp
                or DreamGenClone.Domain.RolePlay.NarrativePhase.Reset;
            var hasActiveScenario = !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId);
            var suppressMidArcSwitch = hasActiveScenario && !sameScenarioAlreadyActive && !enteringArc;

            if (suppressMidArcSwitch)
            {
                _logger.LogInformation(
                    "RolePlayV2 recommit suppressed to preserve phase continuity: SessionId={SessionId} Phase={Phase} ActiveScenarioId={ActiveScenarioId} CandidateScenarioId={CandidateScenarioId}",
                    session.Id,
                    currentPhase,
                    v2State.ActiveScenarioId,
                    commitResult.ScenarioId);
            }
            else
            {
                if (!sameScenarioAlreadyActive)
                {
                    ClearPhaseOverrideLock(v2State);
                    await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                    {
                        SessionId = session.Id,
                        EventKind = "PhaseOverrideLockCleared",
                        Severity = "Info",
                        Summary = "Phase override lock cleared due scenario switch",
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            reason = "ScenarioSwitch",
                            scenarioId = commitResult.ScenarioId
                        })
                    }, cancellationToken);
                }

                v2State.ActiveScenarioId = commitResult.ScenarioId;
                if (!sameScenarioAlreadyActive || enteringArc)
                {
                    v2State.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed;

                    // Reset phase interaction cadence only when a new arc is entered.
                    v2State.InteractionCountInPhase = 0;
                }

                commitApplied = true;
            }
        }

        // BuildUp always needs a selected scenario/theme even before commit gates allow phase promotion.
        if (v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp
            && string.IsNullOrWhiteSpace(v2State.ActiveScenarioId))
        {
            var inferredScenarioId = !string.IsNullOrWhiteSpace(commitResult.ScenarioId)
                ? commitResult.ScenarioId
                : evaluations.FirstOrDefault(x => x.StageBEligible)?.ScenarioId
                    ?? evaluations.FirstOrDefault()?.ScenarioId
                    ?? candidates.FirstOrDefault()?.ScenarioId;

            if (!string.IsNullOrWhiteSpace(inferredScenarioId))
            {
                v2State.ActiveScenarioId = inferredScenarioId;
                _logger.LogInformation(
                    "RolePlayV2 BuildUp active scenario backfilled: SessionId={SessionId} ScenarioId={ScenarioId} Reason={Reason}",
                    session.Id,
                    inferredScenarioId,
                    commitResult.Reason);
            }
        }

        var activeScenarioEvaluation = evaluations.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId)
            && string.Equals(x.ScenarioId, v2State.ActiveScenarioId, StringComparison.OrdinalIgnoreCase));

        var lifecycleConfidence = commitApplied
            ? (commitResult.SelectedEvaluation?.Confidence ?? activeScenarioEvaluation?.Confidence ?? 0m)
            : (activeScenarioEvaluation?.Confidence ?? 0m);
        // Use the unpenalized score for lifecycle gate evaluation — the gate penalty is for scenario
        // selection competition only. Phase transitions should use the true narrative fit score.
        var lifecycleFitScore = commitApplied
            ? (commitResult.SelectedEvaluation?.UnpenalizedFitScore ?? activeScenarioEvaluation?.UnpenalizedFitScore ?? 0m)
            : (activeScenarioEvaluation?.UnpenalizedFitScore ?? 0m);

        var lifecycle = await _scenarioLifecycleService.EvaluateTransitionAsync(
            v2State,
            new LifecycleInputs
            {
                InteractionsSinceCommitment = v2State.InteractionCountInPhase,
                ClimaxCompletionRequested = climaxCompletionRequested,
                ManualAdvanceTargetPhase = manualPhaseAdvanceTarget,
                NarrativeGateProfileId = linkedNarrativeGateProfileId,
                NarrativeGateRules = linkedNarrativeGateRules,
                SkipDefaultNarrativeGateProfileFallback = linkedNarrativeGateRules.Count > 0 || string.IsNullOrWhiteSpace(linkedNarrativeGateProfileId),
                ActiveScenarioConfidence = lifecycleConfidence,
                ActiveScenarioFitScore = lifecycleFitScore,
                EvidenceSummary = commitResult.Reason
            },
            cancellationToken);

        if (lifecycle.Transitioned)
        {
            var transitionSourcePhase = v2State.CurrentPhase;
            v2State.CurrentPhase = lifecycle.TargetPhase;
            v2State.InteractionCountInPhase = 0;

            if (_suppressNarrativeAfterPhaseChange)
            {
                session.SuppressNextNarrativeAfterDecision = true;
            }

            if (manualPhaseAdvanceTarget.HasValue
                && lifecycle.TargetPhase == manualPhaseAdvanceTarget.Value
                && IsForwardPhaseTransition(transitionSourcePhase, lifecycle.TargetPhase)
                && !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId))
            {
                v2State.PhaseOverrideFloor = lifecycle.TargetPhase;
                v2State.PhaseOverrideScenarioId = v2State.ActiveScenarioId;
                v2State.PhaseOverrideCycleIndex = v2State.CycleIndex;
                v2State.PhaseOverrideSource = "/nextphase";
                v2State.PhaseOverrideAppliedUtc = DateTime.UtcNow;
                await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                {
                    SessionId = session.Id,
                    EventKind = "PhaseOverrideLockApplied",
                    Severity = "Info",
                    Summary = "Phase override lock applied via /nextphase",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        fromPhase = transitionSourcePhase.ToString(),
                        toPhase = lifecycle.TargetPhase.ToString(),
                        floorPhase = v2State.PhaseOverrideFloor?.ToString(),
                        scenarioId = v2State.PhaseOverrideScenarioId,
                        cycleIndex = v2State.PhaseOverrideCycleIndex,
                        source = v2State.PhaseOverrideSource
                    })
                }, cancellationToken);
            }

            if (lifecycle.TransitionEvent is not null)
            {
                await _stateRepository.SaveTransitionEventAsync(lifecycle.TransitionEvent, cancellationToken);
            }

            if (lifecycle.TargetPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Reset)
            {
                ClearPhaseOverrideLock(v2State);
                await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                {
                    SessionId = session.Id,
                    EventKind = "PhaseOverrideLockCleared",
                    Severity = "Info",
                    Summary = "Phase override lock cleared due reset",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        reason = "Reset",
                        resetReason = lifecycle.Reason
                    })
                }, cancellationToken);
                var completedScenarioId = v2State.ActiveScenarioId;
                var completion = new DreamGenClone.Domain.RolePlay.ScenarioCompletionMetadata
                {
                    SessionId = session.Id,
                    CycleIndex = v2State.CycleIndex,
                    ScenarioId = v2State.ActiveScenarioId ?? string.Empty,
                    PeakPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
                    ResetReason = lifecycle.Reason,
                    StartedUtc = session.AdaptiveState.ScenarioCommitmentTimeUtc ?? DateTime.UtcNow,
                    CompletedUtc = DateTime.UtcNow
                };
                await _stateRepository.SaveCompletionMetadataAsync(completion, cancellationToken);
                if (!string.IsNullOrWhiteSpace(completedScenarioId))
                {
                    var scenarioHistory = session.AdaptiveState.ScenarioHistory;
                    scenarioHistory ??= [];

                    scenarioHistory.Add(new DreamGenClone.Domain.StoryAnalysis.ScenarioMetadata
                    {
                        ScenarioId = completedScenarioId,
                        CompletedAtUtc = completion.CompletedUtc,
                        InteractionCount = Math.Max(0, v2State.InteractionCountInPhase),
                        PeakThemeScore = session.AdaptiveState.ThemeTracker.Themes.TryGetValue(completedScenarioId, out var completedTheme)
                            ? (int)Math.Round(Math.Clamp(completedTheme.Score, 0d, 100d), MidpointRounding.AwayFromZero)
                            : 0,
                        PeakDesireLevel = v2State.CharacterSnapshots.Count == 0
                            ? 0
                            : v2State.CharacterSnapshots.Max(x => Math.Clamp(x.Desire, 0, 100)),
                        AverageRestraintLevel = v2State.CharacterSnapshots.Count == 0
                            ? 0
                            : Math.Round(v2State.CharacterSnapshots.Average(x => Math.Clamp(x.Restraint, 0, 100)), 2),
                        Notes = lifecycle.Reason
                    });

                    session.AdaptiveState.CompletedScenarios = Math.Max(
                        session.AdaptiveState.CompletedScenarios,
                        scenarioHistory.Count);
                }
                v2State = await _scenarioLifecycleService.ExecuteResetAsync(v2State, ResetReason.Completion, cancellationToken);
                ApplyThemeSemiReset(session.AdaptiveState.ThemeTracker, completedScenarioId);
            }
        }

        if (TryGetActivePhaseOverrideFloor(v2State, out var phaseFloor)
            && IsForwardPhaseTransition(v2State.CurrentPhase, phaseFloor))
        {
            v2State.CurrentPhase = phaseFloor;
        }

        var significantStatChange = HasSignificantStatChange(previousV2State, v2State);
        var conceptCandidates = BuildConceptCandidates(session);
        var conceptTriggers = _promptComposer.ResolveConceptInjectionTriggers(trigger, lifecycle.Transitioned, significantStatChange);
        foreach (var conceptTrigger in conceptTriggers)
        {
            var conceptContext = _promptComposer.BuildConceptContext(conceptCandidates, conceptTrigger);
            var conceptResult = await _conceptInjectionService.BuildGuidanceAsync(v2State, conceptContext, cancellationToken);
            await _stateRepository.SaveConceptInjectionAsync(session.Id, conceptResult, cancellationToken);
        }

        var effectiveDecisionTrigger = lifecycle.Transitioned && _enablePhaseChangeDecisionPrompts
            ? DecisionTrigger.PhaseChanged
            : trigger;

        var directQuestionSignal = TryDetectDirectQuestionSignal(session, v2State);
        var sceneLocationSignal = await DetectSceneLocationSignalAsync(session, v2State, cancellationToken);

        if (sceneLocationSignal.Changed)
        {
            if (_enableSceneLocationDecisionPrompts)
            {
                effectiveDecisionTrigger = DecisionTrigger.SceneLocationChanged;
            }

            _logger.LogInformation(
                RolePlayV2LogEvents.SceneLocationChangedDetected,
                session.Id,
                sceneLocationSignal.PreviousLocation ?? string.Empty,
                sceneLocationSignal.CurrentLocation ?? string.Empty);
        }

        if (directQuestionSignal.IsDetected)
        {
            effectiveDecisionTrigger = DecisionTrigger.CharacterDirectQuestion;
            _logger.LogInformation(
                RolePlayV2LogEvents.DirectQuestionDetected,
                session.Id,
                directQuestionSignal.AskingActorId ?? string.Empty,
                directQuestionSignal.TargetActorId ?? string.Empty);
        }

        var hasPendingDecision = await HasPendingDecisionPointAsync(session, cancellationToken);
        var isInDecisionCooldown = hasPendingDecision
            ? false
            : await HasRecentDecisionPointForContextAsync(session, v2State, effectiveDecisionTrigger, cancellationToken);
        var decisionSkipReasons = new List<string>();
        var evaluatedContextCount = 0;
        var createdDecisionCount = 0;
        var triggerEligibleForDecisionCreation = IsDecisionTriggerEligible(effectiveDecisionTrigger, v2State);
        var bypassActiveScenarioRequirement = effectiveDecisionTrigger is DecisionTrigger.CharacterDirectQuestion or DecisionTrigger.SceneLocationChanged;
        var hasActiveScenarioForDecisionCreation = !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId) || bypassActiveScenarioRequirement;

        if (hasPendingDecision)
        {
            decisionSkipReasons.Add("PendingDecisionExists");
        }

        if (!hasPendingDecision && isInDecisionCooldown)
        {
            decisionSkipReasons.Add("ContextCooldownActive");
        }

        if (!hasPendingDecision && !isInDecisionCooldown && !triggerEligibleForDecisionCreation)
        {
            decisionSkipReasons.Add("TriggerCadenceNotReached");
        }

        if (!hasPendingDecision && !isInDecisionCooldown && triggerEligibleForDecisionCreation && !hasActiveScenarioForDecisionCreation)
        {
            decisionSkipReasons.Add("NoActiveScenario");
        }

        if (!hasPendingDecision && !isInDecisionCooldown && triggerEligibleForDecisionCreation && hasActiveScenarioForDecisionCreation)
        {
            var decisionContexts = BuildDecisionGenerationContexts(
                session,
                v2State,
                effectiveDecisionTrigger,
                directQuestionSignal,
                sceneLocationSignal.CurrentLocation);
            evaluatedContextCount = decisionContexts.Count;
            foreach (var decisionContext in decisionContexts)
            {
                var decisionPoint = await _decisionPointService.TryCreateDecisionPointAsync(
                    v2State,
                    effectiveDecisionTrigger,
                    decisionContext,
                    cancellationToken);
                if (decisionPoint is null)
                {
                    continue;
                }

                createdDecisionCount++;

                var options = decisionPoint.OptionIds
                    .Select(optionId => BuildDecisionOptionForContext(session, decisionPoint, optionId))
                    .ToList();
                var rewriteResult = await TryApplyAiDecisionOptionAnswersAsync(session, decisionPoint, options, cancellationToken);
                options = rewriteResult.Options;

                await _stateRepository.SaveDecisionPointAsync(decisionPoint, options, cancellationToken);

                if (_debugEventSink is not null)
                {
                    await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                    {
                        SessionId = session.Id,
                        EventKind = "DecisionPromptCreated",
                        Severity = "Info",
                        ActorName = ResolveLocationActorLabel(session, decisionPoint.TargetActorId),
                        Summary = $"Decision prompt created ({decisionPoint.TriggerSource}) for {ResolveLocationActorLabel(session, decisionPoint.TargetActorId)}.",
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            decisionPointId = decisionPoint.DecisionPointId,
                            trigger = decisionPoint.TriggerSource,
                            phase = decisionPoint.Phase.ToString(),
                            askingActor = ResolveLocationActorLabel(session, decisionPoint.AskingActorName),
                            targetActor = ResolveLocationActorLabel(session, decisionPoint.TargetActorId),
                            contextSummary = decisionPoint.ContextSummary,
                            rewriteApplied = rewriteResult.UsedAiRewrite,
                            rewriteStatus = rewriteResult.Status,
                            rewriteReason = rewriteResult.Reason,
                            options = options.Select(x => new
                            {
                                optionId = x.OptionId,
                                displayText = x.DisplayText,
                                responsePreview = x.ResponsePreview,
                                visibilityMode = x.VisibilityMode.ToString(),
                                statDeltaMap = x.StatDeltaMap
                            })
                        })
                    }, cancellationToken);
                }

                _logger.LogInformation(
                    RolePlayV2LogEvents.DecisionPointCreated,
                    session.Id,
                    decisionPoint.DecisionPointId,
                    effectiveDecisionTrigger,
                    decisionPoint.AskingActorName ?? string.Empty,
                    decisionPoint.TargetActorId ?? string.Empty);

            }

            if (createdDecisionCount == 0)
            {
                decisionSkipReasons.Add("NoEligibleDecisionGenerated");
            }
        }

        _logger.LogInformation(
            RolePlayV2LogEvents.DecisionAttemptEvaluated,
            session.Id,
            effectiveDecisionTrigger,
            hasPendingDecision,
            isInDecisionCooldown,
            evaluatedContextCount,
            createdDecisionCount,
            decisionSkipReasons.Count == 0 ? "None" : string.Join(",", decisionSkipReasons));

        if (_debugEventSink is not null)
        {
            var debugActor = ResolveLocationActorLabel(session, directQuestionSignal.AskingActorId);
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "LocationStateUpdated",
                Severity = "Info",
                ActorName = string.IsNullOrWhiteSpace(debugActor) ? directQuestionSignal.AskingActorId : debugActor,
                Summary = $"Location state refreshed ({v2State.CharacterLocations.Count} truth rows, {v2State.CharacterLocationPerceptions.Count} perception rows)",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    trigger = effectiveDecisionTrigger.ToString(),
                    sceneLocationChanged = sceneLocationSignal.Changed,
                    previousSceneLocation = sceneLocationSignal.PreviousLocation,
                    currentSceneLocation = v2State.CurrentSceneLocation,
                    characterLocations = v2State.CharacterLocations
                        .OrderBy(x => x.CharacterId, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new
                        {
                            characterId = x.CharacterId,
                            characterLabel = ResolveLocationActorLabel(session, x.CharacterId),
                            characterType = ResolveLocationActorType(session, x.CharacterId),
                            trueLocation = x.TrueLocation,
                            isHidden = x.IsHidden,
                            updatedUtc = x.UpdatedUtc
                        }),
                    characterLocationPerceptions = v2State.CharacterLocationPerceptions
                        .OrderBy(x => x.ObserverCharacterId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.TargetCharacterId, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new
                        {
                            observerCharacterId = x.ObserverCharacterId,
                            observerLabel = ResolveLocationActorLabel(session, x.ObserverCharacterId),
                            observerType = ResolveLocationActorType(session, x.ObserverCharacterId),
                            targetCharacterId = x.TargetCharacterId,
                            targetLabel = ResolveLocationActorLabel(session, x.TargetCharacterId),
                            targetType = ResolveLocationActorType(session, x.TargetCharacterId),
                            perceivedLocation = x.PerceivedLocation,
                            confidence = x.Confidence,
                            hasLineOfSight = x.HasLineOfSight,
                            isInProximity = x.IsInProximity,
                            knowledgeSource = x.KnowledgeSource,
                            updatedUtc = x.UpdatedUtc
                        })
                })
            }, cancellationToken);
        }

        await _stateRepository.SaveFormulaVersionReferenceAsync(
            session.Id,
            new DreamGenClone.Domain.RolePlay.FormulaConfigVersion
            {
                FormulaVersionId = "rpv2-default",
                Name = "RolePlay V2 Default",
                ParameterPayload = "{\"nearTieThreshold\":0.8,\"requiredLeadCount\":2}",
                EffectiveFromUtc = DateTime.UtcNow,
                IsDefault = true
            },
            v2State.CycleIndex,
            cancellationToken);

        // Refresh evaluation watermark so generatedSinceLastEval only counts interactions
        // created after this pipeline execution.
        v2State.LastEvaluationUtc = DateTime.UtcNow;

        await _stateRepository.SaveAdaptiveStateAsync(v2State, cancellationToken);
        SyncSessionAdaptiveStateFromV2(session, v2State);
    }

    private static DreamGenClone.Domain.RolePlay.AdaptiveScenarioState HydrateV2State(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState? previousState)
    {
        var mapped = MapToV2State(session);
        if (previousState is null)
        {
            return mapped;
        }

        var manualOverrideLocked = IsManualThemeOverrideLockActive(session);
        if (manualOverrideLocked)
        {
            mapped.ActiveScenarioId = session.AdaptiveState.ActiveScenarioId;
            mapped.ActiveVariantId = session.AdaptiveState.ActiveVariantId;
            mapped.CurrentPhase = MapPhase(session.AdaptiveState.CurrentNarrativePhase);
            mapped.InteractionCountInPhase = Math.Max(0, session.AdaptiveState.InteractionsSinceCommitment);
        }
        else
        {
            mapped.ActiveScenarioId = previousState.ActiveScenarioId ?? mapped.ActiveScenarioId;
            mapped.ActiveVariantId = previousState.ActiveVariantId ?? mapped.ActiveVariantId;
            mapped.CurrentPhase = previousState.CurrentPhase;
            mapped.InteractionCountInPhase = Math.Max(0, previousState.InteractionCountInPhase);
        }

        mapped.ConsecutiveLeadCount = Math.Max(0, previousState.ConsecutiveLeadCount);
        mapped.CycleIndex = Math.Max(mapped.CycleIndex, previousState.CycleIndex);
        mapped.ActiveFormulaVersion = string.IsNullOrWhiteSpace(previousState.ActiveFormulaVersion)
            ? mapped.ActiveFormulaVersion
            : previousState.ActiveFormulaVersion;
        mapped.SelectedNarrativeGateProfileId = previousState.SelectedNarrativeGateProfileId ?? mapped.SelectedNarrativeGateProfileId;
        mapped.PhaseOverrideFloor = previousState.PhaseOverrideFloor ?? mapped.PhaseOverrideFloor;
        mapped.PhaseOverrideScenarioId = previousState.PhaseOverrideScenarioId ?? mapped.PhaseOverrideScenarioId;
        mapped.PhaseOverrideCycleIndex = previousState.PhaseOverrideCycleIndex ?? mapped.PhaseOverrideCycleIndex;
        mapped.PhaseOverrideSource = previousState.PhaseOverrideSource ?? mapped.PhaseOverrideSource;
        mapped.PhaseOverrideAppliedUtc = previousState.PhaseOverrideAppliedUtc ?? mapped.PhaseOverrideAppliedUtc;
        mapped.LastEvaluationUtc = previousState.LastEvaluationUtc;
        mapped.CurrentSceneLocation = previousState.CurrentSceneLocation;
        mapped.CharacterLocations = previousState.CharacterLocations
            .Select(x => new DreamGenClone.Domain.RolePlay.CharacterLocationState
            {
                CharacterId = x.CharacterId,
                TrueLocation = x.TrueLocation,
                IsHidden = x.IsHidden,
                UpdatedUtc = x.UpdatedUtc
            })
            .ToList();
        mapped.CharacterLocationPerceptions = previousState.CharacterLocationPerceptions
            .Select(x => new DreamGenClone.Domain.RolePlay.CharacterLocationPerceptionState
            {
                ObserverCharacterId = x.ObserverCharacterId,
                TargetCharacterId = x.TargetCharacterId,
                PerceivedLocation = x.PerceivedLocation,
                Confidence = x.Confidence,
                HasLineOfSight = x.HasLineOfSight,
                IsInProximity = x.IsInProximity,
                KnowledgeSource = x.KnowledgeSource,
                UpdatedUtc = x.UpdatedUtc
            })
            .ToList();
        return mapped;
    }

    private static bool IsManualThemeOverrideLockActive(RolePlaySession session)
    {
        if (!string.Equals(session.AdaptiveState.ThemeTracker.ThemeSelectionRule, "ManualOverride", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(session.AdaptiveState.ActiveScenarioId))
        {
            return false;
        }

        var interactionCount = Math.Max(0, session.AdaptiveState.InteractionsSinceCommitment);
        return interactionCount < ManualOverrideSelectionLockInteractions;
    }

    private async Task AlignPromptNarrativeStateWithV2Async(
        RolePlaySession session,
        CancellationToken cancellationToken)
    {
        var snapshot = await _stateRepository.LoadAdaptiveStateAsync(session.Id, cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        session.AdaptiveState.ActiveScenarioId = snapshot.ActiveScenarioId;
        session.AdaptiveState.ActiveVariantId = snapshot.ActiveVariantId;
        session.AdaptiveState.CurrentNarrativePhase = MapStoryPhase(snapshot.CurrentPhase);
        session.AdaptiveState.PhaseOverrideFloor = snapshot.PhaseOverrideFloor is null
            ? null
            : MapStoryPhase(snapshot.PhaseOverrideFloor.Value);
        session.AdaptiveState.PhaseOverrideScenarioId = snapshot.PhaseOverrideScenarioId;
        session.AdaptiveState.PhaseOverrideCycleIndex = snapshot.PhaseOverrideCycleIndex;
        session.AdaptiveState.PhaseOverrideSource = snapshot.PhaseOverrideSource;
        session.AdaptiveState.PhaseOverrideAppliedUtc = snapshot.PhaseOverrideAppliedUtc;

        var interactionCount = Math.Max(0, snapshot.InteractionCountInPhase);
        session.AdaptiveState.InteractionsSinceCommitment = snapshot.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp
            ? 0
            : interactionCount;
        session.AdaptiveState.InteractionsInApproaching = snapshot.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching
            ? interactionCount
            : 0;
    }

    private static void SyncSessionAdaptiveStateFromV2(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState v2State)
    {
        session.AdaptiveState.ActiveScenarioId = v2State.ActiveScenarioId;
        session.AdaptiveState.ActiveVariantId = v2State.ActiveVariantId;
        session.AdaptiveState.CurrentNarrativePhase = MapStoryPhase(v2State.CurrentPhase);
        session.AdaptiveState.PhaseOverrideFloor = v2State.PhaseOverrideFloor is null
            ? null
            : MapStoryPhase(v2State.PhaseOverrideFloor.Value);
        session.AdaptiveState.PhaseOverrideScenarioId = v2State.PhaseOverrideScenarioId;
        session.AdaptiveState.PhaseOverrideCycleIndex = v2State.PhaseOverrideCycleIndex;
        session.AdaptiveState.PhaseOverrideSource = v2State.PhaseOverrideSource;
        session.AdaptiveState.PhaseOverrideAppliedUtc = v2State.PhaseOverrideAppliedUtc;

        var interactionCount = Math.Max(0, v2State.InteractionCountInPhase);
        session.AdaptiveState.InteractionsSinceCommitment = v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp
            ? 0
            : interactionCount;
        session.AdaptiveState.InteractionsInApproaching = v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching
            ? interactionCount
            : 0;
        session.AdaptiveState.SelectedNarrativeGateProfileId = v2State.SelectedNarrativeGateProfileId;
        session.AdaptiveState.CurrentSceneLocation = v2State.CurrentSceneLocation;
        session.AdaptiveState.CharacterLocations = v2State.CharacterLocations
            .Select(x => new RolePlayCharacterLocationState
            {
                CharacterId = x.CharacterId,
                TrueLocation = x.TrueLocation,
                IsHidden = x.IsHidden,
                UpdatedUtc = x.UpdatedUtc
            })
            .ToList();
        session.AdaptiveState.CharacterLocationPerceptions = v2State.CharacterLocationPerceptions
            .Select(x => new RolePlayCharacterLocationPerceptionState
            {
                ObserverCharacterId = x.ObserverCharacterId,
                TargetCharacterId = x.TargetCharacterId,
                PerceivedLocation = x.PerceivedLocation,
                Confidence = x.Confidence,
                HasLineOfSight = x.HasLineOfSight,
                IsInProximity = x.IsInProximity,
                KnowledgeSource = x.KnowledgeSource,
                UpdatedUtc = x.UpdatedUtc
            })
            .ToList();

        if (v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Committed
            && session.AdaptiveState.ScenarioCommitmentTimeUtc is null)
        {
            session.AdaptiveState.ScenarioCommitmentTimeUtc = DateTime.UtcNow;
        }
        else if (v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp)
        {
            session.AdaptiveState.ScenarioCommitmentTimeUtc = null;
        }

        if (v2State.CharacterSnapshots.Count > 0)
        {
            foreach (var snapshot in v2State.CharacterSnapshots)
            {
                if (string.IsNullOrWhiteSpace(snapshot.CharacterId))
                {
                    continue;
                }

                var mappedKey = ResolveAdaptiveCharacterStatsKey(session, snapshot.CharacterId);
                if (!session.AdaptiveState.CharacterStats.TryGetValue(mappedKey, out var statBlock))
                {
                    statBlock = new CharacterStatBlock
                    {
                        CharacterId = snapshot.CharacterId,
                        Stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                        LastStatDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    };
                    session.AdaptiveState.CharacterStats[mappedKey] = statBlock;
                }

                var previousStats = statBlock.Stats is null
                    ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(statBlock.Stats, StringComparer.OrdinalIgnoreCase);

                statBlock.CharacterId = snapshot.CharacterId;
                var syncedStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = Math.Clamp(snapshot.Desire, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue),
                    ["Restraint"] = Math.Clamp(snapshot.Restraint, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue),
                    ["Tension"] = Math.Clamp(snapshot.Tension, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue),
                    ["Connection"] = Math.Clamp(snapshot.Connection, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue),
                    ["Dominance"] = Math.Clamp(snapshot.Dominance, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue),
                    ["Loyalty"] = Math.Clamp(snapshot.Loyalty, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue),
                    ["SelfRespect"] = Math.Clamp(snapshot.SelfRespect, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue)
                };
                statBlock.Stats = syncedStats;

                var lastDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var (statName, currentValue) in syncedStats)
                {
                    var previousValue = previousStats.TryGetValue(statName, out var value)
                        ? value
                        : currentValue;
                    var delta = currentValue - previousValue;
                    if (delta != 0)
                    {
                        lastDeltas[statName] = delta;
                    }
                }

                statBlock.LastStatDeltas = lastDeltas;
                statBlock.LastStatDeltaUpdatedUtc = DateTime.UtcNow;
                statBlock.UpdatedUtc = DateTime.UtcNow;

                // Clean up historical duplicate keys where snapshots were previously materialized by GUID key.
                if (!string.Equals(mappedKey, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase)
                    && session.AdaptiveState.CharacterStats.TryGetValue(snapshot.CharacterId, out var duplicateById)
                    && string.Equals(duplicateById.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase))
                {
                    session.AdaptiveState.CharacterStats.Remove(snapshot.CharacterId);
                }
            }
        }
    }

    private static string ResolveAdaptiveCharacterStatsKey(RolePlaySession session, string characterId)
    {
        var existing = session.AdaptiveState.CharacterStats
            .FirstOrDefault(x => string.Equals(x.Value.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing.Key))
        {
            return existing.Key;
        }

        var perspectiveMatch = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterId, characterId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.CharacterName));
        if (perspectiveMatch is not null)
        {
            return perspectiveMatch.CharacterName.Trim();
        }

        return characterId;
    }

    private void ApplyThemeSemiReset(ThemeTrackerState tracker, string? completedScenarioId)
    {
        tracker.RecentEvidence.Clear();
        tracker.PrimaryThemeId = null;
        tracker.SecondaryThemeId = null;
        tracker.ThemeSelectionRule = "Top2Blend";
        tracker.UpdatedUtc = DateTime.UtcNow;

        foreach (var item in tracker.Themes.Values)
        {
            item.Score = Math.Round(Math.Max(0, item.Score * 0.15), 4);
            item.Breakdown.ChoiceSignal = 0;
            item.Breakdown.CharacterStateSignal = 0;
            item.Breakdown.InteractionEvidenceSignal = 0;
            item.Breakdown.ScenarioPhaseSignal = 0;
            item.Intensity = ResolveResetIntensity(item.Score);
        }

        if (!string.IsNullOrWhiteSpace(completedScenarioId)
            && tracker.Themes.TryGetValue(completedScenarioId, out var completedTheme))
        {
            completedTheme.Score = Math.Round(Math.Max(0, completedTheme.Score - (double)_completedScenarioThemeScorePenalty), 4);
            completedTheme.Intensity = ResolveResetIntensity(completedTheme.Score);
        }
    }

    private static bool IsClimaxCompletionRequested(RolePlaySession session)
    {
        var latest = session.Interactions
            .Where(x => x.ParentInteractionId is null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (latest is null || string.IsNullOrWhiteSpace(latest.Content))
        {
            return false;
        }

        return ContainsClimaxCompletionCommand(latest.Content);
    }

    private static bool ContainsClimaxCompletionCommand(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return content.Contains("/completeclimax", StringComparison.OrdinalIgnoreCase)
            || content.Contains("/endclimax", StringComparison.OrdinalIgnoreCase)
            || content.Contains("/end-climax", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(content, @"\b(complete|end)\s+climax\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static DreamGenClone.Domain.RolePlay.NarrativePhase? ResolveManualPhaseAdvanceTarget(
        string? content,
        DreamGenClone.Domain.StoryAnalysis.NarrativePhase currentPhase)
    {
        if (!ContainsNextPhaseCommand(content))
        {
            return null;
        }

        // Climax exits ONLY via /endclimax — /nextphase is intentionally blocked here.
        return currentPhase switch
        {
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.BuildUp => DreamGenClone.Domain.RolePlay.NarrativePhase.Committed,
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Committed => DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching,
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Approaching => DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
            _ => null
        };
    }

    private static bool ContainsNextPhaseCommand(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var tokens = content
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Any(token => string.Equals(token, "/nextphase", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSteerCommand(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var tokens = content
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Any(token => string.Equals(token, "/steer", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryExtractSteerDirective(string? content, out string directive)
    {
        directive = string.Empty;
        if (!ContainsSteerCommand(content))
        {
            return false;
        }

        var raw = content?.Trim() ?? string.Empty;
        var markerIndex = raw.IndexOf("/steer", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            directive = "Steer the scene in a meaningful, phase-consistent direction.";
            return true;
        }

        var remaining = raw[(markerIndex + "/steer".Length)..].Trim();
        directive = string.IsNullOrWhiteSpace(remaining)
            ? "Steer the scene in a meaningful, phase-consistent direction."
            : remaining;
        return true;
    }

    private static bool TryGetActivePhaseOverrideFloor(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        out DreamGenClone.Domain.RolePlay.NarrativePhase floor)
    {
        floor = DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp;
        if (!state.PhaseOverrideFloor.HasValue
            || !state.PhaseOverrideCycleIndex.HasValue
            || string.IsNullOrWhiteSpace(state.PhaseOverrideScenarioId)
            || string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return false;
        }

        if (state.PhaseOverrideCycleIndex.Value != state.CycleIndex
            || !string.Equals(state.PhaseOverrideScenarioId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        floor = state.PhaseOverrideFloor.Value;
        return true;
    }

    private static void NormalizePhaseOverrideLock(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        if (TryGetActivePhaseOverrideFloor(state, out _))
        {
            return;
        }

        ClearPhaseOverrideLock(state);
    }

    private static void ClearPhaseOverrideLock(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        state.PhaseOverrideFloor = null;
        state.PhaseOverrideScenarioId = null;
        state.PhaseOverrideCycleIndex = null;
        state.PhaseOverrideSource = null;
        state.PhaseOverrideAppliedUtc = null;
    }

    private static bool IsForwardPhaseTransition(
        DreamGenClone.Domain.RolePlay.NarrativePhase from,
        DreamGenClone.Domain.RolePlay.NarrativePhase to)
        => GetPhaseOrder(to) > GetPhaseOrder(from);

    private static int GetPhaseOrder(DreamGenClone.Domain.RolePlay.NarrativePhase phase)
        => phase switch
        {
            DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp => 0,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Committed => 1,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching => 2,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Climax => 3,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Reset => 4,
            _ => 0
        };

    private static string ResolveResetIntensity(double score)
    {
        if (score >= 45)
        {
            return "Moderate";
        }

        if (score >= 15)
        {
            return "Minor";
        }

        return "None";
    }

    private async Task<bool> HasPendingDecisionPointAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        var points = await _stateRepository.LoadDecisionPointsAsync(session.Id, 30, cancellationToken);
        if (points.Count == 0)
        {
            return false;
        }

        var appliedIds = session.AppliedDecisionPointIds ??= [];
        return points.Any(x => !appliedIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<bool> HasRecentDecisionPointForContextAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        DecisionTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return false;
        }

        var points = await _stateRepository.LoadDecisionPointsAsync(session.Id, 10, cancellationToken);
        if (points.Count == 0)
        {
            return false;
        }

        var latest = points[^1];
        var sameContext = string.Equals(latest.ScenarioId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase)
            && latest.Phase == state.CurrentPhase
            && string.Equals(latest.TriggerSource, trigger.ToString(), StringComparison.OrdinalIgnoreCase);

        if (!sameContext)
        {
            return false;
        }

        return DateTime.UtcNow - latest.CreatedUtc < DecisionPointContextCooldown;
    }

    private bool IsDecisionTriggerEligible(
        DecisionTrigger trigger,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        if (trigger == DecisionTrigger.PhaseChanged && !_enablePhaseChangeDecisionPrompts)
        {
            return false;
        }

        if (trigger == DecisionTrigger.SceneLocationChanged && !_enableSceneLocationDecisionPrompts)
        {
            return false;
        }

        return trigger == DecisionTrigger.PhaseChanged
            || trigger == DecisionTrigger.SignificantStatChange
            || trigger == DecisionTrigger.CharacterDirectQuestion
            || trigger == DecisionTrigger.SceneLocationChanged
            || (trigger == DecisionTrigger.InteractionStart
                && state.InteractionCountInPhase > 0
                && state.InteractionCountInPhase % 3 == 0)
            || trigger == DecisionTrigger.ManualOverride;
    }

    private static bool HasSignificantStatChange(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState? previous,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState current,
        int threshold = 10)
    {
        if (previous is null || previous.CharacterSnapshots.Count == 0)
        {
            return false;
        }

        var previousByCharacter = previous.CharacterSnapshots
            .GroupBy(x => x.CharacterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in current.CharacterSnapshots)
        {
            if (!previousByCharacter.TryGetValue(snapshot.CharacterId, out var old))
            {
                continue;
            }

            if (Math.Abs(snapshot.Desire - old.Desire) >= threshold
                || Math.Abs(snapshot.Restraint - old.Restraint) >= threshold
                || Math.Abs(snapshot.Tension - old.Tension) >= threshold
                || Math.Abs(snapshot.Connection - old.Connection) >= threshold
                || Math.Abs(snapshot.Dominance - old.Dominance) >= threshold
                || Math.Abs(snapshot.Loyalty - old.Loyalty) >= threshold
                || Math.Abs(snapshot.SelfRespect - old.SelfRespect) >= threshold)
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveDecisionOptionDisplayText(string optionId)
    {
        if (DecisionOptionCatalog.TryGetValue(optionId, out var details))
        {
            return details.Label;
        }

        return optionId;
    }

    private static string ResolveDecisionOptionDeltaMap(string optionId)
    {
        if (DecisionOptionCatalog.TryGetValue(optionId, out var details) && details.Deltas.Count > 0)
        {
            return JsonSerializer.Serialize(details.Deltas);
        }

        return "{}";
    }

    private static string ResolveDecisionOptionPrerequisites(string optionId)
    {
        return optionId switch
        {
            "lean-in" => "{\"min\":{\"Desire\":55}}",
            "test-boundary" => "{\"min\":{\"Desire\":65},\"max\":{\"Loyalty\":70}}",
            "husband-observes" => "{\"min\":{\"Desire\":60}}",
            _ => "{}"
        };
    }

    private DreamGenClone.Domain.RolePlay.DecisionOption BuildDecisionOptionForContext(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        string optionId)
    {
        var displayText = BuildDecisionAnswerChoiceText(optionId, decisionPoint);
        var deltaMap = ResolveDecisionOptionDeltaMap(optionId);
        var baseDeltas = ParseDeltaMap(deltaMap);
        var deltas = AdjustDecisionDeltasForContext(session, decisionPoint.TargetActorId, baseDeltas);
        var adjustedDeltaMap = JsonSerializer.Serialize(deltas);
        var topThemes = ResolveTopThemeNames(session, 2);
        var targetActorLabel = ResolveDecisionActorDisplayLabel(session, decisionPoint.TargetActorId);
        var askingActorLabel = ResolveDecisionActorDisplayLabel(session, decisionPoint.AskingActorName);
        var (highestStatName, highestStatValue) = ResolveHighestStat(session, decisionPoint.TargetActorId);
        var deescalating = IsDeescalatingChoice(deltas, highestStatName);

        return new DreamGenClone.Domain.RolePlay.DecisionOption
        {
            OptionId = optionId,
            DecisionPointId = decisionPoint.DecisionPointId,
            DisplayText = displayText,
            ResponsePreview = BuildDecisionResponsePreview(
                optionId,
                decisionPoint,
                targetActorLabel,
                askingActorLabel,
                topThemes),
            BehaviorStyleHint = BuildBehaviorStyleHint(deescalating, highestStatName, highestStatValue),
            CharacterDirectionInstruction = BuildCharacterDirectionInstruction(
                optionId,
                decisionPoint,
                targetActorLabel,
                askingActorLabel,
                topThemes,
                highestStatName,
                highestStatValue,
                deescalating),
            ChatInstruction = BuildChatInstruction(
                optionId,
                decisionPoint,
                topThemes,
                highestStatName,
                highestStatValue,
                deescalating),
            VisibilityMode = decisionPoint.TransparencyMode,
            Prerequisites = ResolveDecisionOptionPrerequisites(optionId),
            StatDeltaMap = adjustedDeltaMap,
            IsCustomResponseFallback = string.Equals(optionId, "custom", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyDictionary<string, int> AdjustDecisionDeltasForContext(
        RolePlaySession session,
        string? targetActorId,
        IReadOnlyDictionary<string, int> baseDeltas)
    {
        if (!baseDeltas.TryGetValue("Restraint", out var restraintDelta) || restraintDelta >= 0)
        {
            return baseDeltas;
        }

        var currentRestraint = ResolveDecisionActorStatValue(session, targetActorId, "Restraint", AdaptiveStatCatalog.DefaultValue);
        var scale = currentRestraint switch
        {
            >= 90 => 0.80,
            >= 80 => 0.65,
            >= 65 => 0.45,
            _ => 0.30
        };

        var adjustedRestraintDelta = (int)Math.Round(restraintDelta * scale, MidpointRounding.AwayFromZero);
        adjustedRestraintDelta = Math.Min(-1, adjustedRestraintDelta);

        if (adjustedRestraintDelta == restraintDelta)
        {
            return baseDeltas;
        }

        var mutable = new Dictionary<string, int>(baseDeltas, StringComparer.OrdinalIgnoreCase)
        {
            ["Restraint"] = adjustedRestraintDelta
        };

        return mutable;
    }

    private static int ResolveDecisionActorStatValue(
        RolePlaySession session,
        string? actorId,
        string statName,
        int fallback)
    {
        CharacterStatBlock? statBlock = null;
        if (!string.IsNullOrWhiteSpace(actorId))
        {
            statBlock = session.AdaptiveState.CharacterStats.Values
                .FirstOrDefault(x => string.Equals(x.CharacterId, actorId, StringComparison.OrdinalIgnoreCase));
        }

        statBlock ??= session.AdaptiveState.CharacterStats.Values.FirstOrDefault();
        if (statBlock is null)
        {
            return fallback;
        }

        return statBlock.Stats.TryGetValue(statName, out var value)
            ? value
            : fallback;
    }

    private async Task<DecisionOptionRewriteResult> TryApplyAiDecisionOptionAnswersAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        List<DreamGenClone.Domain.RolePlay.DecisionOption> options,
        CancellationToken cancellationToken)
    {
        if (_completionClient is null || _modelResolutionService is null)
        {
            return new DecisionOptionRewriteResult(options, false, "skipped", "model-services-unavailable");
        }

        var rewriteCandidates = options
            .Where(x => !string.Equals(x.OptionId, "custom", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (rewriteCandidates.Count == 0)
        {
            return new DecisionOptionRewriteResult(options, false, "skipped", "no-rewrite-candidates");
        }

        try
        {
            var resolved = await _modelResolutionService.ResolveAsync(
                AppFunction.RolePlayGeneration,
                cancellationToken: cancellationToken);

            var targetActor = ResolveDecisionActorDisplayLabel(session, decisionPoint.TargetActorId);
            var askingActor = ResolveDecisionActorDisplayLabel(session, decisionPoint.AskingActorName);
            var statsSummary = BuildDecisionActorStatSummary(session, decisionPoint.TargetActorId);
            var topThemes = ResolveTopThemeNames(session, 3);
            var activeScenarioId = session.AdaptiveState.ActiveScenarioId;
            var activeTheme = ResolveDecisionThemeContextLabel(session, activeScenarioId);
            var topThemeText = topThemes.Count == 0 ? "(none)" : string.Join(", ", topThemes);

            var systemMessage =
                "You generate context-aware, in-character decision answer options. " +
                "Output ONLY strict JSON with schema: {\"options\":[{\"optionId\":\"id\",\"answer\":\"quoted spoken line\",\"preview\":\"short plain summary\"}]}. " +
                "Never include markdown, explanations, or extra keys.";

            var userMessage = $"""
Scene context:
- Trigger: {decisionPoint.TriggerSource}
- Phase: {decisionPoint.Phase}
- Target actor: {targetActor}
- Asking actor: {askingActor}
- Prompt/context: {decisionPoint.ContextSummary}
- Target actor stats: {statsSummary}
- Active theme/scenario: {activeTheme}
- Top adaptive themes: {topThemeText}

Options to rewrite (keep optionId unchanged):
{JsonSerializer.Serialize(rewriteCandidates.Select(x => new { x.OptionId, baseText = x.DisplayText, basePreview = x.ResponsePreview }))}

Requirements:
1) Keep each option's intent distinct and coherent with the context.
2) answer must be a natural spoken response line in double quotes.
3) preview must be one short non-technical sentence.
4) Do not mention stats, deltas, system prompts, or metadata.
5) Return all provided optionIds exactly once.
6) Keep the response aligned with active scene/theme guidance.
""";

            var modelOutput = await _completionClient.GenerateAsync(systemMessage, userMessage, resolved, cancellationToken);
            var generated = ParseGeneratedDecisionAnswers(modelOutput, rewriteCandidates.Select(x => x.OptionId));
            if (generated.Count == 0)
            {
                return new DecisionOptionRewriteResult(options, false, "fallback", "empty-or-invalid-model-output");
            }

            for (var i = 0; i < options.Count; i++)
            {
                var current = options[i];
                if (!generated.TryGetValue(current.OptionId, out var rewritten))
                {
                    continue;
                }

                options[i] = new DreamGenClone.Domain.RolePlay.DecisionOption
                {
                    OptionId = current.OptionId,
                    DecisionPointId = current.DecisionPointId,
                    DisplayText = rewritten.Answer,
                    ResponsePreview = string.IsNullOrWhiteSpace(rewritten.Preview) ? current.ResponsePreview : rewritten.Preview,
                    BehaviorStyleHint = current.BehaviorStyleHint,
                    CharacterDirectionInstruction = current.CharacterDirectionInstruction,
                    ChatInstruction = current.ChatInstruction,
                    VisibilityMode = current.VisibilityMode,
                    Prerequisites = current.Prerequisites,
                    StatDeltaMap = current.StatDeltaMap,
                    IsCustomResponseFallback = current.IsCustomResponseFallback
                };
            }

            return new DecisionOptionRewriteResult(options, true, "applied", "ai-rewrite-applied");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AI decision option rewrite failed for session {SessionId}, decisionPointId={DecisionPointId}", session.Id, decisionPoint.DecisionPointId);
            return new DecisionOptionRewriteResult(options, false, "fallback", "model-call-failed");
        }
    }

    private static string BuildDecisionActorStatSummary(RolePlaySession session, string? actorId)
    {
        if (session.AdaptiveState.CharacterStats.Count == 0)
        {
            return "(no stats)";
        }

        CharacterStatBlock? target = null;
        if (!string.IsNullOrWhiteSpace(actorId))
        {
            target = session.AdaptiveState.CharacterStats.Values.FirstOrDefault(x =>
                string.Equals(x.CharacterId, actorId, StringComparison.OrdinalIgnoreCase));
        }

        target ??= session.AdaptiveState.CharacterStats.Values.FirstOrDefault();
        if (target is null || target.Stats.Count == 0)
        {
            return "(no stats)";
        }

        return string.Join(", ", AdaptiveStatCatalog.CanonicalStatNames
            .Select(stat => $"{stat}={GetCharacterStatValue(target, stat)}"));
    }

    private static int GetCharacterStatValue(CharacterStatBlock block, string statName)
    {
        if (block.Stats.TryGetValue(statName, out var value))
        {
            return value;
        }

        return 50;
    }

    private static Dictionary<string, GeneratedDecisionAnswer> ParseGeneratedDecisionAnswers(
        string modelOutput,
        IEnumerable<string> optionIds)
    {
        var allowed = optionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(modelOutput))
        {
            return new Dictionary<string, GeneratedDecisionAnswer>(StringComparer.OrdinalIgnoreCase);
        }

        if (TryParseGeneratedDecisionAnswersJson(modelOutput, allowed, out var parsed))
        {
            return parsed;
        }

        var firstBrace = modelOutput.IndexOf('{');
        var lastBrace = modelOutput.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            var jsonSlice = modelOutput.Substring(firstBrace, lastBrace - firstBrace + 1);
            if (TryParseGeneratedDecisionAnswersJson(jsonSlice, allowed, out parsed))
            {
                return parsed;
            }
        }

        return new Dictionary<string, GeneratedDecisionAnswer>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryParseGeneratedDecisionAnswersJson(
        string json,
        HashSet<string> allowed,
        out Dictionary<string, GeneratedDecisionAnswer> parsed)
    {
        parsed = new Dictionary<string, GeneratedDecisionAnswer>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("options", out var optionsElement)
                || optionsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var entry in optionsElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("optionId", out var optionIdElement)
                    || optionIdElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var optionId = optionIdElement.GetString() ?? string.Empty;
                if (!allowed.Contains(optionId))
                {
                    continue;
                }

                var answer = entry.TryGetProperty("answer", out var answerElement) && answerElement.ValueKind == JsonValueKind.String
                    ? answerElement.GetString() ?? string.Empty
                    : string.Empty;
                var preview = entry.TryGetProperty("preview", out var previewElement) && previewElement.ValueKind == JsonValueKind.String
                    ? previewElement.GetString() ?? string.Empty
                    : string.Empty;

                answer = answer.Trim();
                preview = preview.Trim();
                if (string.IsNullOrWhiteSpace(answer))
                {
                    continue;
                }

                if (!answer.StartsWith('"'))
                {
                    answer = $"\"{answer.Trim('"')}\"";
                }

                if (answer.Length > 180)
                {
                    answer = answer[..180].TrimEnd();
                    if (!answer.EndsWith('"'))
                    {
                        answer += '"';
                    }
                }

                if (preview.Length > 220)
                {
                    preview = preview[..220].TrimEnd();
                }

                parsed[optionId] = new GeneratedDecisionAnswer(answer, preview);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record GeneratedDecisionAnswer(string Answer, string Preview);
    private sealed record DecisionOptionRewriteResult(
        List<DreamGenClone.Domain.RolePlay.DecisionOption> Options,
        bool UsedAiRewrite,
        string Status,
        string Reason);

    private async Task<DreamGenClone.Domain.RolePlay.DecisionOption?> ResolveAppliedDecisionOptionAsync(
        string decisionPointId,
        string optionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decisionPointId) || string.IsNullOrWhiteSpace(optionId))
        {
            return null;
        }

        var options = await _stateRepository.LoadDecisionOptionsAsync(decisionPointId, cancellationToken);
        return options.FirstOrDefault(x => string.Equals(x.OptionId, optionId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ResolveTopThemeNames(RolePlaySession session, int take)
    {
        return session.AdaptiveState.ThemeTracker.Themes.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ThemeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => string.IsNullOrWhiteSpace(x.ThemeName) ? x.ThemeId : x.ThemeName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(Math.Clamp(take, 1, 4))
            .ToList();
    }

    private static (string StatName, int Value) ResolveHighestStat(RolePlaySession session, string? actorId)
    {
        CharacterStatBlock? statBlock = null;

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            statBlock = session.AdaptiveState.CharacterStats.Values
                .FirstOrDefault(x => string.Equals(x.CharacterId, actorId, StringComparison.OrdinalIgnoreCase));

            if (statBlock is null && session.AdaptiveState.CharacterStats.TryGetValue(actorId, out var keyedBlock))
            {
                statBlock = keyedBlock;
            }
        }

        statBlock ??= session.AdaptiveState.CharacterStats.Values.FirstOrDefault();
        if (statBlock is null || statBlock.Stats.Count == 0)
        {
            return (string.Empty, 0);
        }

        var highest = statBlock.Stats.OrderByDescending(x => x.Value).First();
        return (highest.Key, highest.Value);
    }

    private static bool IsDeescalatingChoice(IReadOnlyDictionary<string, int> deltas, string highestStatName)
    {
        if (!string.IsNullOrWhiteSpace(highestStatName)
            && deltas.TryGetValue(highestStatName, out var highestDelta)
            && highestDelta < 0)
        {
            return true;
        }

        return (deltas.TryGetValue("Tension", out var tensionDelta) && tensionDelta < 0)
               || (deltas.TryGetValue("Restraint", out var restraintDelta) && restraintDelta > 0);
    }

    private static string ResolveDecisionActorDisplayLabel(RolePlaySession session, string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return "You";
        }

        var resolved = ResolveLocationActorLabel(session, actorId);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return actorId;
        }

        var trimmed = resolved.Trim();
        var friendlyMatch = Regex.Match(trimmed, "^(.*)\\s+\\([0-9a-fA-F-]{36}\\)$", RegexOptions.CultureInvariant);
        if (friendlyMatch.Success)
        {
            var friendly = friendlyMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(friendly))
            {
                return friendly;
            }
        }

        return trimmed;
    }

    private static string ResolveDecisionThemeContextLabel(RolePlaySession session, string? scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            return "(none)";
        }

        if (session.AdaptiveState.ThemeTracker.Themes.TryGetValue(scenarioId, out var theme)
            && !string.IsNullOrWhiteSpace(theme.ThemeName))
        {
            return $"{theme.ThemeName} ({scenarioId})";
        }

        return scenarioId;
    }

    private static string BuildDecisionResponsePreview(
        string optionId,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        string targetActorLabel,
        string askingActorLabel,
        IReadOnlyList<string> topThemes)
    {
        if (string.Equals(decisionPoint.TriggerSource, DecisionTrigger.CharacterDirectQuestion.ToString(), StringComparison.OrdinalIgnoreCase)
            && LooksLikeInvitationPrompt(decisionPoint.ContextSummary))
        {
            return optionId switch
            {
                "tempt-answer" => "Enthusiastic acceptance that raises attraction and speeds up chemistry.",
                "lean-in" => "Warm acceptance that keeps things casual while opening connection.",
                "hold-back" => "Polite delay that acknowledges interest without committing right now.",
                "seek-connection" => "Boundary-focused refusal that reinforces loyalty to the relationship.",
                "redirect" => "Neutral refusal that exits the invitation without emotional escalation.",
                "observe" => "Minimal non-commitment while gathering more social context.",
                "custom" => "Write your own in-character answer for this specific question.",
                _ => "Respond in character while preserving social realism and continuity."
            };
        }

        var themeTail = topThemes.Count > 0
            ? $" with a {topThemes[0]} undertone"
            : string.Empty;

        var promptTail = string.Equals(decisionPoint.TriggerSource, "CharacterDirectQuestion", StringComparison.OrdinalIgnoreCase)
            ? $" to {askingActorLabel}"
            : string.Empty;

        return optionId switch
        {
            "lean-in" => $"{targetActorLabel} answers warmly{promptTail} and steps closer{themeTail}.",
            "tempt-answer" => $"{targetActorLabel} gives a daring answer{promptTail}, leaning into forbidden chemistry{themeTail}.",
            "hold-back" => $"{targetActorLabel} answers politely{promptTail}, but sets a calmer boundary{themeTail}.",
            "seek-connection" => $"{targetActorLabel} gives a sincere answer{promptTail} and emphasizes trust{themeTail}.",
            "test-boundary" => $"{targetActorLabel} replies playfully{promptTail}, probing limits without committing{themeTail}.",
            "escalate" => $"{targetActorLabel} responds directly{promptTail}, pushing intensity higher{themeTail}.",
            "redirect" => $"{targetActorLabel} acknowledges the point{promptTail} and redirects toward safer ground{themeTail}.",
            "observe" => $"{targetActorLabel} gives a minimal answer{promptTail} and watches the room{themeTail}.",
            "husband-observes" => $"{targetActorLabel} allows visibility while answering carefully{promptTail}{themeTail}.",
            "custom" => "Write a custom in-character response for this moment.",
            _ => $"{targetActorLabel} responds in character{promptTail}{themeTail}."
        };
    }

    private static string BuildDecisionAnswerChoiceText(
        string optionId,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint)
    {
        if (string.Equals(optionId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return "Write your own response...";
        }

        var prompt = decisionPoint.ContextSummary;
        var isDirectQuestion = string.Equals(decisionPoint.TriggerSource, DecisionTrigger.CharacterDirectQuestion.ToString(), StringComparison.OrdinalIgnoreCase);

        if (isDirectQuestion && LooksLikeInvitationPrompt(prompt))
        {
            var activity = ResolveInvitationActivity(prompt);
            return optionId switch
            {
                "tempt-answer" => $"\"Definitely, I'd love to {activity}.\"",
                "lean-in" => $"\"Sure, {activity} sounds nice.\"",
                "hold-back" => "\"Maybe in a bit, I have some work to finish first.\"",
                "seek-connection" => "\"I shouldn't, my partner is expecting me.\"",
                "redirect" => "\"Sorry, I'm busy right now.\"",
                "observe" => "\"Maybe another time.\"",
                _ => ResolveDecisionOptionDisplayText(optionId)
            };
        }

        if (isDirectQuestion)
        {
            return optionId switch
            {
                "tempt-answer" => "\"Definitely... yes.\"",
                "lean-in" => "\"Yeah, that sounds good.\"",
                "hold-back" => "\"Not right now, maybe in a bit.\"",
                "seek-connection" => "\"I can't, I need to stay fair to my relationship.\"",
                "redirect" => "\"Let's keep it friendly for now.\"",
                "observe" => "\"Let me think about that.\"",
                _ => ResolveDecisionOptionDisplayText(optionId)
            };
        }

        return ResolveDecisionOptionDisplayText(optionId);
    }

    private static bool LooksLikeInvitationPrompt(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return false;
        }

        return snippet.Contains("coffee", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("drink", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("dinner", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("lunch", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("grab", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("go with", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("join me", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("with me", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveInvitationActivity(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return "go together";
        }

        if (snippet.Contains("coffee", StringComparison.OrdinalIgnoreCase))
        {
            return "grab coffee together";
        }

        if (snippet.Contains("drink", StringComparison.OrdinalIgnoreCase))
        {
            return "grab a drink";
        }

        if (snippet.Contains("dinner", StringComparison.OrdinalIgnoreCase))
        {
            return "go to dinner";
        }

        if (snippet.Contains("lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "grab lunch";
        }

        return "go together";
    }

    private static string BuildBehaviorStyleHint(bool deescalating, string highestStatName, int highestStatValue)
    {
        var style = deescalating
            ? "Style: calm, affirming, and de-escalating."
            : "Style: intentional and emotionally clear, but controlled.";

        if (string.IsNullOrWhiteSpace(highestStatName) || highestStatValue < 65)
        {
            return style;
        }

        return $"{style} Keep {highestStatName} stable (current {highestStatValue}).";
    }

    private static string BuildCharacterDirectionInstruction(
        string optionId,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        string targetActorLabel,
        string askingActorLabel,
        IReadOnlyList<string> topThemes,
        string highestStatName,
        int highestStatValue,
        bool deescalating)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Character Direction ({targetActorLabel})");
        builder.AppendLine($"Phase: {decisionPoint.Phase}. Trigger: {decisionPoint.TriggerSource}.");

        if (topThemes.Count > 0)
        {
            builder.AppendLine($"Anchor tone to: {string.Join(", ", topThemes)}.");
        }

        if (!string.IsNullOrWhiteSpace(askingActorLabel))
        {
            builder.AppendLine($"Address {askingActorLabel} directly with clear intent.");
        }

        if (!string.IsNullOrWhiteSpace(highestStatName) && highestStatValue >= 65)
        {
            var pressureGuidance = deescalating
                ? "Actively lower pressure and avoid spikes."
                : "Do not spike pressure abruptly; modulate pacing.";
            builder.AppendLine($"High stat signal: {highestStatName}={highestStatValue}. {pressureGuidance}");
        }

        builder.Append(optionId switch
        {
            "lean-in" => "Use receptive language, short affirmations, and consent-forward escalation.",
            "tempt-answer" => "Answer with provocative subtext and attraction-forward language while preserving scene coherence.",
            "hold-back" => "Use respectful boundaries, slower cadence, and emotionally steady wording.",
            "seek-connection" => "Prioritize reassurance, shared goals, and relational clarity.",
            "test-boundary" => "Keep it playful but non-coercive; signal limits before pressure.",
            "escalate" => "Increase intensity through subtext, not blunt force; keep coherence with scene logic.",
            "redirect" => "Acknowledge the request, then steer toward safer and more sustainable momentum.",
            "observe" => "Stay concise, gather cues, and avoid committing to heavy directional moves.",
            "husband-observes" => "Balance transparency and tact; preserve composure under observation.",
            _ => "Respond naturally in character while preserving continuity and scene intent."
        });

        return builder.ToString().Trim();
    }

    private static string BuildChatInstruction(
        string optionId,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        IReadOnlyList<string> topThemes,
        string highestStatName,
        int highestStatValue,
        bool deescalating)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Chat Instruction");
        builder.AppendLine($"For the next 1-2 assistant turns, reflect a {decisionPoint.Phase} cadence and preserve continuity from the chosen decision.");

        if (topThemes.Count > 0)
        {
            builder.AppendLine($"Emphasize themes: {string.Join(", ", topThemes)}.");
        }

        if (!string.IsNullOrWhiteSpace(highestStatName) && highestStatValue >= 65)
        {
            builder.AppendLine(deescalating
                ? $"Bias toward alignment/de-escalation to bring {highestStatName} down from {highestStatValue}."
                : $"Keep {highestStatName} from escalating too sharply from {highestStatValue}.");
        }

        builder.Append(optionId switch
        {
            "custom" => "Honor the user-provided custom response exactly, then continue scene progression naturally.",
            _ => "Carry the selected option's intent forward with coherent emotional follow-through."
        });

        return builder.ToString().Trim();
    }

    private static string BuildDecisionSteeringInstruction(string? selectedDialogue)
    {
        if (!string.IsNullOrWhiteSpace(selectedDialogue))
        {
            return selectedDialogue.Trim();
        }

        return string.Empty;
    }

    private static string BuildDecisionInstructionActorName(RolePlaySession session, string? targetActorId)
    {
        var actorLabel = ResolveLocationActorLabel(session, targetActorId);
        if (string.IsNullOrWhiteSpace(actorLabel)
            || string.Equals(actorLabel, "You", StringComparison.OrdinalIgnoreCase))
        {
            return "Instruction";
        }

        return $"{actorLabel} Instruction";
    }

    private static string? ResolveSelectedDecisionDialogue(
        DreamGenClone.Domain.RolePlay.DecisionOption? selectedOption,
        string? customResponseText)
    {
        if (selectedOption is null)
        {
            return null;
        }

        if (string.Equals(selectedOption.OptionId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(customResponseText)
                ? null
                : customResponseText.Trim();
        }

        var display = selectedOption.DisplayText?.Trim();
        if (!string.IsNullOrWhiteSpace(display)
            && !string.Equals(display, "Write your own response...", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(display, "Custom Response", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDecisionDialogue(display);
        }

        var preview = selectedOption.ResponsePreview?.Trim();
        if (!string.IsNullOrWhiteSpace(preview))
        {
            return NormalizeDecisionDialogue(preview);
        }

        return null;
    }

    private static (string? Dialogue, string Source) ResolveSelectedDecisionDialogueWithSource(
        DreamGenClone.Domain.RolePlay.DecisionOption? selectedOption,
        string? customResponseText)
    {
        if (selectedOption is null)
        {
            return (null, "none");
        }

        if (string.Equals(selectedOption.OptionId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            var custom = string.IsNullOrWhiteSpace(customResponseText)
                ? null
                : NormalizeDecisionDialogue(customResponseText);
            return (custom, custom is null ? "custom-empty" : "custom-input");
        }

        var display = selectedOption.DisplayText?.Trim();
        if (!string.IsNullOrWhiteSpace(display)
            && !string.Equals(display, "Write your own response...", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(display, "Custom Response", StringComparison.OrdinalIgnoreCase))
        {
            return (NormalizeDecisionDialogue(display), "display-text");
        }

        var preview = selectedOption.ResponsePreview?.Trim();
        if (!string.IsNullOrWhiteSpace(preview))
        {
            return (NormalizeDecisionDialogue(preview), "response-preview");
        }

        return (null, "none");
    }

    private static string ResolveFallbackDecisionDialogue(string optionId)
    {
        var fallback = ResolveDecisionOptionDisplayText(optionId);
        return NormalizeDecisionDialogue(fallback);
    }

    private static string NormalizeDecisionDialogue(string rawText)
    {
        var trimmed = rawText.Trim();
        if (trimmed.Length == 0)
        {
            return "\"...\"";
        }

        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            return trimmed;
        }

        return $"\"{trimmed.Trim('"')}\"";
    }

    private static void ApplyDecisionOutcomeToSessionState(RolePlaySession session, DecisionOutcome outcome)
    {
        if (session.AdaptiveState.CharacterStats.Count == 0)
        {
            return;
        }

        if (outcome.PerActorStatDeltas.Count > 0)
        {
            foreach (var (actorId, actorDeltas) in outcome.PerActorStatDeltas)
            {
                var actorEntry = session.AdaptiveState.CharacterStats
                    .FirstOrDefault(x => string.Equals(x.Value.CharacterId, actorId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(actorEntry.Key))
                {
                    ApplyDeltasToStatBlock(actorEntry.Value, actorDeltas);
                }
            }

            return;
        }

        if (outcome.AppliedStatDeltas.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(outcome.TargetActorId))
        {
            var targetEntry = session.AdaptiveState.CharacterStats
                .FirstOrDefault(x => string.Equals(x.Value.CharacterId, outcome.TargetActorId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(targetEntry.Key))
            {
                ApplyDeltasToStatBlock(targetEntry.Value, outcome.AppliedStatDeltas);
                return;
            }
        }

        var first = session.AdaptiveState.CharacterStats.Values.FirstOrDefault();
        if (first is not null)
        {
            ApplyDeltasToStatBlock(first, outcome.AppliedStatDeltas);
        }
    }

    private static void ApplyDeltasToStatBlock(
        CharacterStatBlock statBlock,
        IReadOnlyDictionary<string, int> deltas)
    {
        var appliedDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (statName, delta) in deltas)
        {
            var current = statBlock.Stats.TryGetValue(statName, out var currentValue)
                ? currentValue
                : AdaptiveStatCatalog.DefaultValue;
            statBlock.Stats[statName] = Math.Clamp(current + delta, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue);
            if (delta != 0)
            {
                appliedDeltas[statName] = delta;
            }
        }

        if (appliedDeltas.Count > 0)
        {
            statBlock.LastStatDeltas = appliedDeltas;
            statBlock.LastStatDeltaUpdatedUtc = DateTime.UtcNow;
        }

        statBlock.UpdatedUtc = DateTime.UtcNow;
    }

    private static string? ResolveDecisionTargetActorId(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string? askingActorId)
    {
        if (!string.IsNullOrWhiteSpace(askingActorId))
        {
            var asking = state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, askingActorId, StringComparison.OrdinalIgnoreCase));
            if (asking is not null)
            {
                return asking.CharacterId;
            }

            var nonAsking = state.CharacterSnapshots.FirstOrDefault(x =>
                !string.Equals(x.CharacterId, askingActorId, StringComparison.OrdinalIgnoreCase));
            if (nonAsking is not null)
            {
                return nonAsking.CharacterId;
            }
        }

        return state.CharacterSnapshots.Count == 1
            ? state.CharacterSnapshots[0].CharacterId
            : null;
    }

    private static string? ResolveDecisionActorId(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        RolePlaySession session,
        string? actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            return null;
        }

        var byId = state.CharacterSnapshots.FirstOrDefault(x =>
            string.Equals(x.CharacterId, actorName, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return byId.CharacterId;
        }

        var byPerspectiveName = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterName, actorName, StringComparison.OrdinalIgnoreCase));
        if (byPerspectiveName is not null)
        {
            var perspectiveMatch = state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, byPerspectiveName.CharacterId, StringComparison.OrdinalIgnoreCase));
            if (perspectiveMatch is not null)
            {
                return perspectiveMatch.CharacterId;
            }
        }

        return null;
    }

    private static IReadOnlyList<DecisionGenerationContext> BuildDecisionGenerationContexts(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        DecisionTrigger trigger,
        DirectQuestionSignal directQuestionSignal,
        string? currentSceneLocation)
    {
        var snippet = session.Interactions
            .TakeLast(4)
            .Select(x => x.Content)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .FirstOrDefault();

        var actorIds = state.CharacterSnapshots
            .Select(x => x.CharacterId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (trigger == DecisionTrigger.CharacterDirectQuestion
            && directQuestionSignal.IsDetected
            && !string.IsNullOrWhiteSpace(directQuestionSignal.TargetActorId))
        {
            return
            [
                new DecisionGenerationContext
                {
                    ScenarioId = session.ScenarioId,
                    TriggerSource = trigger.ToString(),
                    Phase = state.CurrentPhase,
                    Who = InferDecisionWho(directQuestionSignal.PromptSnippet ?? snippet),
                    What = InferDecisionWhat(directQuestionSignal.PromptSnippet ?? snippet),
                    PromptSnippet = directQuestionSignal.PromptSnippet ?? snippet,
                    AskingActorName = directQuestionSignal.AskingActorId,
                    TargetActorId = directQuestionSignal.TargetActorId,
                    IsDirectQuestionContext = true,
                    CurrentSceneLocation = currentSceneLocation,
                    TransparencyOverride = DreamGenClone.Domain.RolePlay.TransparencyMode.Explicit,
                    RelevantActors = state.CharacterSnapshots
                }
            ];
        }

        if (trigger == DecisionTrigger.SceneLocationChanged && actorIds.Count > 0)
        {
            return actorIds.Select(actorId => new DecisionGenerationContext
            {
                ScenarioId = session.ScenarioId,
                TriggerSource = trigger.ToString(),
                Phase = state.CurrentPhase,
                Who = InferDecisionWho(snippet),
                What = InferDecisionWhat(snippet),
                PromptSnippet = snippet,
                AskingActorName = actorId,
                TargetActorId = actorId,
                CurrentSceneLocation = currentSceneLocation,
                TransparencyOverride = DreamGenClone.Domain.RolePlay.TransparencyMode.Explicit,
                RelevantActors = state.CharacterSnapshots
            }).ToList();
        }

        if (actorIds.Count == 0)
        {
            var fallback = ResolveDecisionActorsFromStoryContext(session, state);
            return
            [
                new DecisionGenerationContext
                {
                    ScenarioId = session.ScenarioId,
                    TriggerSource = trigger.ToString(),
                    Phase = state.CurrentPhase,
                    Who = InferDecisionWho(snippet),
                    What = InferDecisionWhat(snippet),
                    PromptSnippet = snippet,
                    AskingActorName = fallback.AskingActorId,
                    TargetActorId = fallback.TargetActorId,
                    CurrentSceneLocation = currentSceneLocation,
                    RelevantActors = state.CharacterSnapshots
                }
            ];
        }

        return actorIds.Select(actorId => new DecisionGenerationContext
        {
            ScenarioId = session.ScenarioId,
            TriggerSource = trigger.ToString(),
            Phase = state.CurrentPhase,
            Who = InferDecisionWho(snippet),
            What = InferDecisionWhat(snippet),
            PromptSnippet = snippet,
            AskingActorName = actorId,
            TargetActorId = actorId,
            CurrentSceneLocation = currentSceneLocation,
            RelevantActors = state.CharacterSnapshots
        }).ToList();
    }

    private DirectQuestionSignal TryDetectDirectQuestionSignal(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        var lastInteraction = session.Interactions.LastOrDefault(x =>
            x.InteractionType != InteractionType.System
            && !string.IsNullOrWhiteSpace(x.Content));
        if (lastInteraction is null)
        {
            return DirectQuestionSignal.None;
        }

        var content = lastInteraction.Content.Trim();
        if (!LooksLikeDirectQuestion(content))
        {
            return DirectQuestionSignal.None;
        }

        var askingActorId = ResolveDecisionActorId(state, session, lastInteraction.ActorName);
        if (string.IsNullOrWhiteSpace(askingActorId))
        {
            return DirectQuestionSignal.None;
        }

        var targetActorId = ResolveQuestionTargetActorId(session, state, askingActorId, content)
            ?? ResolveDecisionTargetActorId(state, askingActorId);
        if (string.IsNullOrWhiteSpace(targetActorId))
        {
            return DirectQuestionSignal.None;
        }

        return new DirectQuestionSignal(true, askingActorId, targetActorId, content);
    }

    private async Task<SceneLocationSignal> DetectSceneLocationSignalAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        CancellationToken cancellationToken)
    {
        EnsureCharacterLocationRows(state);

        var scenarioLocationNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null && scenario.Locations.Count > 0)
            {
                scenarioLocationNames = scenario.Locations
                    .Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        string? latestLocation = null;
        var previousLocation = state.CurrentSceneLocation;
        var matchedInteraction = default(RolePlayInteraction);

        foreach (var interaction in session.Interactions
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .Reverse())
        {
            var matched = MatchScenarioLocation(interaction.Content, scenarioLocationNames);
            if (string.IsNullOrWhiteSpace(matched))
            {
                matched = MatchGenericLocation(interaction.Content);
            }

            if (string.IsNullOrWhiteSpace(matched))
            {
                continue;
            }

            latestLocation = matched;
            matchedInteraction = interaction;
            break;
        }

        if (string.IsNullOrWhiteSpace(latestLocation))
        {
            var fallbackLocation = previousLocation;

            if (!string.IsNullOrWhiteSpace(fallbackLocation))
            {
                state.CurrentSceneLocation = fallbackLocation;

                foreach (var snapshot in state.CharacterSnapshots)
                {
                    var existing = state.CharacterLocations.FirstOrDefault(x =>
                        string.Equals(x.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase));
                    if (existing is null || string.IsNullOrWhiteSpace(existing.TrueLocation))
                    {
                        UpsertTrueLocation(state, snapshot.CharacterId, fallbackLocation, sourceIsHidden: false);
                    }
                }
            }

            UpdatePerceivedLocationsFromTruth(state);
            return new SceneLocationSignal(false, previousLocation, state.CurrentSceneLocation);
        }

        var changed = !string.Equals(previousLocation, latestLocation, StringComparison.OrdinalIgnoreCase);
        state.CurrentSceneLocation = latestLocation;

        var actorId = ResolveDecisionActorId(state, session, matchedInteraction?.ActorName);
        if (matchedInteraction is not null && matchedInteraction.InteractionType == InteractionType.System)
        {
            foreach (var snapshot in state.CharacterSnapshots)
            {
                UpsertTrueLocation(state, snapshot.CharacterId, latestLocation, sourceIsHidden: false);
            }
        }
        else if (!string.IsNullOrWhiteSpace(actorId))
        {
            UpsertTrueLocation(state, actorId, latestLocation, sourceIsHidden: false);
        }
        else
        {
            foreach (var snapshot in state.CharacterSnapshots)
            {
                var existing = state.CharacterLocations.FirstOrDefault(x => string.Equals(x.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase));
                if (existing is null || string.IsNullOrWhiteSpace(existing.TrueLocation))
                {
                    UpsertTrueLocation(state, snapshot.CharacterId, latestLocation, sourceIsHidden: false);
                }
            }
        }

        UpdatePerceivedLocationsFromTruth(state);

        if (!changed)
        {
            return new SceneLocationSignal(false, previousLocation, latestLocation);
        }

        return new SceneLocationSignal(true, previousLocation, latestLocation);
    }

    private static void EnsureCharacterLocationRows(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        foreach (var snapshot in state.CharacterSnapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.CharacterId))
            {
                continue;
            }

            if (!state.CharacterLocations.Any(x => string.Equals(x.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase)))
            {
                state.CharacterLocations.Add(new DreamGenClone.Domain.RolePlay.CharacterLocationState
                {
                    CharacterId = snapshot.CharacterId,
                    TrueLocation = null,
                    UpdatedUtc = DateTime.UtcNow
                });
            }
        }
    }

    private static void UpsertTrueLocation(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string characterId,
        string? trueLocation,
        bool sourceIsHidden)
    {
        var row = state.CharacterLocations.FirstOrDefault(x =>
            string.Equals(x.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            row = new DreamGenClone.Domain.RolePlay.CharacterLocationState
            {
                CharacterId = characterId
            };
            state.CharacterLocations.Add(row);
        }

        row.TrueLocation = trueLocation;
        row.IsHidden = sourceIsHidden;
        row.UpdatedUtc = DateTime.UtcNow;
    }

    private static void UpdatePerceivedLocationsFromTruth(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        var truthByActor = state.CharacterLocations
            .Where(x => !string.IsNullOrWhiteSpace(x.CharacterId))
            .ToDictionary(x => x.CharacterId, x => x, StringComparer.OrdinalIgnoreCase);
        if (truthByActor.Count == 0)
        {
            return;
        }

        foreach (var observer in truthByActor.Values)
        {
            foreach (var target in truthByActor.Values)
            {
                var row = state.CharacterLocationPerceptions.FirstOrDefault(x =>
                    string.Equals(x.ObserverCharacterId, observer.CharacterId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.TargetCharacterId, target.CharacterId, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    row = new DreamGenClone.Domain.RolePlay.CharacterLocationPerceptionState
                    {
                        ObserverCharacterId = observer.CharacterId,
                        TargetCharacterId = target.CharacterId
                    };
                    state.CharacterLocationPerceptions.Add(row);
                }

                if (string.Equals(observer.CharacterId, target.CharacterId, StringComparison.OrdinalIgnoreCase))
                {
                    row.PerceivedLocation = observer.TrueLocation;
                    row.Confidence = 100;
                    row.HasLineOfSight = true;
                    row.IsInProximity = true;
                    row.KnowledgeSource = "self";
                    row.UpdatedUtc = DateTime.UtcNow;
                    continue;
                }

                var sameLocation = !string.IsNullOrWhiteSpace(observer.TrueLocation)
                    && string.Equals(observer.TrueLocation, target.TrueLocation, StringComparison.OrdinalIgnoreCase);
                if (sameLocation && !target.IsHidden)
                {
                    row.PerceivedLocation = target.TrueLocation;
                    row.Confidence = 100;
                    row.HasLineOfSight = true;
                    row.IsInProximity = true;
                    row.KnowledgeSource = "line-of-sight";
                    row.UpdatedUtc = DateTime.UtcNow;
                    continue;
                }

                row.HasLineOfSight = false;
                row.IsInProximity = false;
                if (string.IsNullOrWhiteSpace(row.PerceivedLocation))
                {
                    if (string.IsNullOrWhiteSpace(target.TrueLocation))
                    {
                        row.Confidence = 0;
                        row.KnowledgeSource = "unknown";
                        row.UpdatedUtc = DateTime.UtcNow;
                        continue;
                    }

                    row.PerceivedLocation = target.TrueLocation;
                    row.Confidence = 35;
                    row.KnowledgeSource = "assumed";
                }
                else
                {
                    row.Confidence = Math.Clamp(row.Confidence - 15, 20, 85);
                    row.KnowledgeSource = "last-known";
                }

                row.UpdatedUtc = DateTime.UtcNow;
            }
        }
    }

    private static string? ResolveQuestionTargetActorId(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string askingActorId,
        string content)
    {
        foreach (var perspective in session.CharacterPerspectives)
        {
            if (string.IsNullOrWhiteSpace(perspective.CharacterName))
            {
                continue;
            }

            if (!ContainsWholeWord(content, perspective.CharacterName))
            {
                continue;
            }

            var candidateId = ResolveDecisionActorId(state, session, perspective.CharacterName);
            if (!string.IsNullOrWhiteSpace(candidateId)
                && !string.Equals(candidateId, askingActorId, StringComparison.OrdinalIgnoreCase))
            {
                return candidateId;
            }
        }

        if (!string.IsNullOrWhiteSpace(session.PersonaName)
            && ContainsWholeWord(content, "you"))
        {
            var personaId = ResolveDecisionActorId(state, session, session.PersonaName);
            if (!string.IsNullOrWhiteSpace(personaId)
                && !string.Equals(personaId, askingActorId, StringComparison.OrdinalIgnoreCase))
            {
                return personaId;
            }
        }

        return null;
    }

    private static bool LooksLikeDirectQuestion(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        // Require explicit question punctuation to prevent narrative prose from being treated as a direct question.
        return content.Contains('?', StringComparison.Ordinal);
    }

    private static string? MatchScenarioLocation(string content, IEnumerable<string> locationNames)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var ordered = locationNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderByDescending(x => x.Length)
            .ToList();
        foreach (var name in ordered)
        {
            if (ContainsWholeWord(content, name))
            {
                return name.Trim();
            }
        }

        return null;
    }

    private static string? MatchGenericLocation(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        foreach (var genericName in GenericLocationNames.OrderByDescending(x => x.Length))
        {
            if (ContainsWholeWord(content, genericName))
            {
                return genericName;
            }
        }

        return null;
    }

    private static bool ContainsWholeWord(string content, string token)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var pattern = $@"\b{Regex.Escape(token.Trim())}\b";
        return Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ResolveLocationActorLabel(RolePlaySession session, string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return "Unknown";
        }

        var token = actorId.Trim();
        if (!string.IsNullOrWhiteSpace(session.PersonaName)
            && string.Equals(token, session.PersonaName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{session.PersonaName} (Persona)";
        }

        var perspective = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterId, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.CharacterName, token, StringComparison.OrdinalIgnoreCase));
        if (perspective is null)
        {
            return token;
        }

        if (!string.IsNullOrWhiteSpace(perspective.CharacterName)
            && string.Equals(perspective.CharacterId, token, StringComparison.OrdinalIgnoreCase))
        {
            return $"{perspective.CharacterName} ({perspective.CharacterId})";
        }

        return string.IsNullOrWhiteSpace(perspective.CharacterName)
            ? token
            : perspective.CharacterName;
    }

    private static string ResolveLocationActorType(RolePlaySession session, string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return "unknown";
        }

        var token = actorId.Trim();
        if (!string.IsNullOrWhiteSpace(session.PersonaName)
            && string.Equals(token, session.PersonaName, StringComparison.OrdinalIgnoreCase))
        {
            return "persona";
        }

        return session.CharacterPerspectives.Any(x =>
            string.Equals(x.CharacterId, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.CharacterName, token, StringComparison.OrdinalIgnoreCase))
            ? "character"
            : "unknown";
    }

    private readonly record struct DirectQuestionSignal(
        bool IsDetected,
        string? AskingActorId,
        string? TargetActorId,
        string? PromptSnippet)
    {
        public static DirectQuestionSignal None => new(false, null, null, null);
    }

    private readonly record struct SceneLocationSignal(
        bool Changed,
        string? PreviousLocation,
        string? CurrentLocation)
    {
        public static SceneLocationSignal None => new(false, null, null);
    }

    private static (string? AskingActorId, string? TargetActorId) ResolveDecisionActorsFromStoryContext(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        var recentActorIds = session.Interactions
            .Where(x => x.InteractionType != InteractionType.System)
            .TakeLast(8)
            .Select(x => ResolveDecisionActorId(state, session, x.ActorName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Reverse()
            .ToList();

        var askingActorId = recentActorIds.FirstOrDefault();
        askingActorId ??= ResolveDecisionActorId(state, session, session.PersonaName);

        var targetActorId = ResolveDecisionTargetActorId(state, askingActorId);

        return (askingActorId, targetActorId);
    }

    private async Task<DreamGenClone.Domain.RolePlay.DecisionPoint?> ResolveDecisionPointAsync(
        string sessionId,
        string decisionPointId,
        CancellationToken cancellationToken)
    {
        var points = await _stateRepository.LoadDecisionPointsAsync(sessionId, 30, cancellationToken);
        return points.FirstOrDefault(x => string.Equals(x.DecisionPointId, decisionPointId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? InferDecisionWho(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return null;
        }

        if (snippet.Contains("husband", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("partner", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("spouse", StringComparison.OrdinalIgnoreCase))
        {
            return "husband";
        }

        if (snippet.Contains("coworker", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("colleague", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("boss", StringComparison.OrdinalIgnoreCase))
        {
            return "coworker";
        }

        if (snippet.Contains("friend", StringComparison.OrdinalIgnoreCase))
        {
            return "friend";
        }

        if (snippet.Contains("stranger", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "stranger";
        }

        return null;
    }

    private static string? InferDecisionWhat(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return null;
        }

        if (snippet.Contains("coffee", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("drink", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("dinner", StringComparison.OrdinalIgnoreCase))
        {
            return "invitation";
        }

        if (snippet.Contains("flirt", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("tempt", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("attract", StringComparison.OrdinalIgnoreCase))
        {
            return "temptation";
        }

        if (snippet.Contains("risk", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("public", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("caught", StringComparison.OrdinalIgnoreCase))
        {
            return "risk";
        }

        if (snippet.Contains("trust", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("boundary", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("relationship", StringComparison.OrdinalIgnoreCase))
        {
            return "boundary";
        }

        return null;
    }

    private static IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption> ApplyTransparencyToDecisionOptions(
        IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption> options,
        DreamGenClone.Domain.RolePlay.TransparencyMode mode)
    {
        if (mode == DreamGenClone.Domain.RolePlay.TransparencyMode.Explicit)
        {
            return options;
        }

        var transformed = new List<DreamGenClone.Domain.RolePlay.DecisionOption>(options.Count);
        foreach (var option in options)
        {
            var map = ParseDeltaMap(option.StatDeltaMap);

            var transformedMap = mode switch
            {
                DreamGenClone.Domain.RolePlay.TransparencyMode.Hidden => "{}",
                DreamGenClone.Domain.RolePlay.TransparencyMode.Directional => JsonSerializer.Serialize(
                    map.ToDictionary(x => x.Key, x => x.Value >= 0 ? 1 : -1, StringComparer.OrdinalIgnoreCase)),
                _ => option.StatDeltaMap
            };

            transformed.Add(new DreamGenClone.Domain.RolePlay.DecisionOption
            {
                OptionId = option.OptionId,
                DecisionPointId = option.DecisionPointId,
                DisplayText = option.DisplayText,
                ResponsePreview = option.ResponsePreview,
                BehaviorStyleHint = option.BehaviorStyleHint,
                CharacterDirectionInstruction = option.CharacterDirectionInstruction,
                ChatInstruction = option.ChatInstruction,
                VisibilityMode = option.VisibilityMode,
                Prerequisites = option.Prerequisites,
                StatDeltaMap = transformedMap,
                IsCustomResponseFallback = option.IsCustomResponseFallback
            });
        }

        return transformed;
    }

    private static IReadOnlyDictionary<string, int> ParseDeltaMap(string deltaMap)
    {
        if (string.IsNullOrWhiteSpace(deltaMap) || deltaMap == "{}")
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(deltaMap)
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static DreamGenClone.Domain.RolePlay.AdaptiveScenarioState MapToV2State(RolePlaySession session)
    {
        EnsurePersonaCharacterState(session);

        var snapshots = session.AdaptiveState.CharacterStats.Select(x =>
        {
            var characterId = string.IsNullOrWhiteSpace(x.Value.CharacterId) ? x.Key : x.Value.CharacterId;
            var snapshot = CharacterStatProfileV2Accessor.CreateFromStats(characterId, x.Value.Stats);
            snapshot.SnapshotUtc = DateTime.UtcNow;
            return snapshot;
        }).ToList();

        return new DreamGenClone.Domain.RolePlay.AdaptiveScenarioState
        {
            SessionId = session.Id,
            ActiveScenarioId = session.AdaptiveState.ActiveScenarioId,
            ActiveVariantId = session.AdaptiveState.ActiveVariantId,
            CurrentPhase = MapPhase(session.AdaptiveState.CurrentNarrativePhase),
            InteractionCountInPhase = session.AdaptiveState.InteractionsSinceCommitment,
            ConsecutiveLeadCount = 0,
            LastEvaluationUtc = DateTime.UtcNow,
            CycleIndex = session.AdaptiveState.CompletedScenarios,
            ActiveFormulaVersion = "rpv2-default",
            SelectedWillingnessProfileId = session.AdaptiveState.SelectedWillingnessProfileId,
            SelectedNarrativeGateProfileId = session.AdaptiveState.SelectedNarrativeGateProfileId,
            HusbandAwarenessProfileId = session.AdaptiveState.HusbandAwarenessProfileId,
            PhaseOverrideFloor = session.AdaptiveState.PhaseOverrideFloor is null
                ? null
                : MapPhase(session.AdaptiveState.PhaseOverrideFloor.Value),
            PhaseOverrideScenarioId = session.AdaptiveState.PhaseOverrideScenarioId,
            PhaseOverrideCycleIndex = session.AdaptiveState.PhaseOverrideCycleIndex,
            PhaseOverrideSource = session.AdaptiveState.PhaseOverrideSource,
            PhaseOverrideAppliedUtc = session.AdaptiveState.PhaseOverrideAppliedUtc,
            CharacterSnapshots = snapshots,
            CurrentSceneLocation = session.AdaptiveState.CurrentSceneLocation,
            CharacterLocations = session.AdaptiveState.CharacterLocations
                .Select(x => new DreamGenClone.Domain.RolePlay.CharacterLocationState
                {
                    CharacterId = x.CharacterId,
                    TrueLocation = x.TrueLocation,
                    IsHidden = x.IsHidden,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToList(),
            CharacterLocationPerceptions = session.AdaptiveState.CharacterLocationPerceptions
                .Select(x => new DreamGenClone.Domain.RolePlay.CharacterLocationPerceptionState
                {
                    ObserverCharacterId = x.ObserverCharacterId,
                    TargetCharacterId = x.TargetCharacterId,
                    PerceivedLocation = x.PerceivedLocation,
                    Confidence = x.Confidence,
                    HasLineOfSight = x.HasLineOfSight,
                    IsInProximity = x.IsInProximity,
                    KnowledgeSource = x.KnowledgeSource ?? string.Empty,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToList()
        };
    }

    private async Task SeedPersonaStatsFromTemplateAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        if (_templateService is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.PersonaTemplateId)
            || !Guid.TryParse(session.PersonaTemplateId, out var personaTemplateGuid))
        {
            return;
        }

        var personaTemplate = await _templateService.GetByIdAsync(personaTemplateGuid, cancellationToken);
        if (personaTemplate is null || personaTemplate.BaseStats.Count == 0)
        {
            return;
        }

        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        var normalizedStats = AdaptiveStatCatalog.NormalizeComplete(personaTemplate.BaseStats);
        session.AdaptiveState.CharacterStats[personaName] = new CharacterStatBlock
        {
            CharacterId = personaName,
            Stats = normalizedStats,
            UpdatedUtc = DateTime.UtcNow
        };

        _logger.LogDebug(
            "Seeded persona '{PersonaName}' stats from template '{TemplateId}' for session {SessionId}",
            personaName, session.PersonaTemplateId, session.Id);
    }

    private static bool EnsurePersonaCharacterState(RolePlaySession session)
    {
        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        if (string.IsNullOrWhiteSpace(personaName))
        {
            return false;
        }

        var existing = session.AdaptiveState.CharacterStats.Any(entry =>
            string.Equals(entry.Key, personaName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Value.CharacterId, personaName, StringComparison.OrdinalIgnoreCase));
        if (existing)
        {
            return false;
        }

        var seedStats = session.AdaptiveState.CharacterStats.Values.FirstOrDefault()?.Stats;
        var normalizedStats = AdaptiveStatCatalog.NormalizeComplete(seedStats ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        session.AdaptiveState.CharacterStats[personaName] = new CharacterStatBlock
        {
            CharacterId = personaName,
            Stats = normalizedStats,
            UpdatedUtc = DateTime.UtcNow
        };

        return true;
    }

    private async Task<List<ScenarioDefinition>> BuildScenarioCandidatesAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        var completionCounts = session.AdaptiveState.ScenarioHistory
            .Where(x => !string.IsNullOrWhiteSpace(x.ScenarioId))
            .GroupBy(x => x.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        var mostRecentCompletedScenarioId = session.AdaptiveState.ScenarioHistory
            .LastOrDefault(x => !string.IsNullOrWhiteSpace(x.ScenarioId))
            ?.ScenarioId;

        decimal ApplyCompletedScenarioPenalty(string scenarioId, decimal value)
        {
            var normalized = Math.Clamp(value, 0m, 1m);
            if (_completedScenarioRepeatPenaltyPerRun <= 0m
                || completionCounts.Count == 0
                || !completionCounts.TryGetValue(scenarioId, out var completedCount)
                || completedCount <= 0)
            {
                return normalized;
            }

            var multiplier = Math.Max(
                _completedScenarioRepeatPenaltyFloor,
                1m - (_completedScenarioRepeatPenaltyPerRun * completedCount));

            if (!string.IsNullOrWhiteSpace(mostRecentCompletedScenarioId)
                && string.Equals(scenarioId, mostRecentCompletedScenarioId, StringComparison.OrdinalIgnoreCase)
                && _completedScenarioRecentPenaltyMultiplier > 0m
                && _completedScenarioRecentPenaltyMultiplier < 1m)
            {
                multiplier *= _completedScenarioRecentPenaltyMultiplier;
            }

            return Math.Clamp(decimal.Round(normalized * multiplier, 4, MidpointRounding.AwayFromZero), 0m, 1m);
        }

        ScenarioDefinition ApplyRepeatPenalty(ScenarioDefinition candidate)
            => candidate with
            {
                NarrativeEvidenceScore = ApplyCompletedScenarioPenalty(candidate.ScenarioId, candidate.NarrativeEvidenceScore),
                PreferencePriorityScore = ApplyCompletedScenarioPenalty(candidate.ScenarioId, candidate.PreferencePriorityScore)
            };

        var rankedThemes = session.AdaptiveState.ThemeTracker.Themes.Values
            .Select(theme => new
            {
                Theme = theme,
                PenalizedScore = ApplyCompletedScenarioPenalty(theme.ThemeId, NormalizeThemeScore(theme.Score))
            })
            .OrderByDescending(x => x.PenalizedScore)
            .ThenBy(x => x.Theme.ThemeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_rpThemeService is not null && !string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
        {
            var assignments = await _rpThemeService.ListProfileAssignmentsAsync(session.SelectedRPThemeProfileId, cancellationToken);
            var themes = await _rpThemeService.ListThemesAsync(includeDisabled: false, cancellationToken: cancellationToken);
            var themesById = themes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var roleCharacterBindings = await BuildRoleCharacterBindingsAsync(session, cancellationToken);

            var rpCandidates = assignments
                .Where(x => x.IsEnabled && x.Tier != DreamGenClone.Domain.RolePlay.RPThemeTier.HardDealBreaker)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Tier)
                .Select((assignment, index) =>
                {
                    if (!themesById.TryGetValue(assignment.ThemeId, out var theme))
                    {
                        return null;
                    }

                    var preferencePriority = assignment.Tier switch
                    {
                        DreamGenClone.Domain.RolePlay.RPThemeTier.MustHave => 1m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.StronglyPrefer => 0.8m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.NiceToHave => 0.6m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.Neutral => 0.5m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.Discouraged => 0.2m,
                        _ => 0.5m
                    };

                    if (assignment.Weight > 0m)
                    {
                        preferencePriority = Math.Clamp(assignment.Weight, 0m, 1m);
                    }

                    var fitRulesJson = RPThemeFitRulesConverter.BuildScenarioFitRulesJson(theme, roleCharacterBindings);

                    return ApplyRepeatPenalty(new ScenarioDefinition(
                        theme.Id,
                        theme.Label,
                        Priority: Math.Max(1, 5 - index),
                        NarrativeEvidenceScore: preferencePriority,
                        PreferencePriorityScore: preferencePriority,
                        ScenarioFitRulesJson: fitRulesJson,
                        ScenarioFitRuleSource: "rp-theme"));
                })
                .Where(x => x is not null)
                .Select(x => x!)
                .Take(5)
                .ToList();

            if (rpCandidates.Count > 0)
            {
                return rpCandidates;
            }
        }

        if (_themePreferenceService is not null && !string.IsNullOrWhiteSpace(session.SelectedThemeProfileId))
        {
            var preferences = await _themePreferenceService.ListByProfileAsync(session.SelectedThemeProfileId, cancellationToken);
            var allowedCatalogIds = preferences
                .Where(x => x.Tier != ThemeTier.HardDealBreaker && !string.IsNullOrWhiteSpace(x.CatalogId))
                .Select(x => x.CatalogId.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (allowedCatalogIds.Count > 0)
            {
                var profileCandidates = rankedThemes
                    .Where(x => allowedCatalogIds.Contains(x.Theme.ThemeId))
                    .Take(5)
                    .Select((theme, index) => ApplyRepeatPenalty(new ScenarioDefinition(
                        theme.Theme.ThemeId,
                        theme.Theme.ThemeName,
                        Priority: 5 - index,
                        NarrativeEvidenceScore: theme.PenalizedScore,
                        PreferencePriorityScore: NormalizePreferencePriority(5 - index))))
                    .ToList();

                if (profileCandidates.Count > 0)
                {
                    return profileCandidates;
                }
            }
        }

        var candidates = rankedThemes
            .Take(5)
            .Select((theme, index) => ApplyRepeatPenalty(new ScenarioDefinition(
                theme.Theme.ThemeId,
                theme.Theme.ThemeName,
                Priority: 5 - index,
                NarrativeEvidenceScore: theme.PenalizedScore,
                PreferencePriorityScore: NormalizePreferencePriority(5 - index))))
            .ToList();

        if (candidates.Count == 0)
        {
            candidates.Add(new ScenarioDefinition(
                session.ScenarioId ?? "default-scenario",
                "Default Scenario",
                Priority: 1,
                NarrativeEvidenceScore: 0.4m,
                PreferencePriorityScore: 0.5m));
        }

        return candidates;
    }

    private async Task<(string? ProfileId, IReadOnlyList<NarrativeGateRule> Rules)> ResolveThemeNarrativeGateConfigAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        CancellationToken cancellationToken)
    {
        if (_rpThemeService is null
            || string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return (null, []);
        }

        var theme = await _rpThemeService.GetThemeAsync(state.ActiveScenarioId, cancellationToken);
        if (theme is null)
        {
            return (null, []);
        }

        if (theme.NarrativeGateRules.Count > 0)
        {
            return (null, theme.NarrativeGateRules);
        }

        if (string.IsNullOrWhiteSpace(theme.NarrativeGateProfileId))
        {
            return (null, []);
        }

        return (theme.NarrativeGateProfileId.Trim(), []);
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildRoleCharacterBindingsAsync(
        RolePlaySession session,
        CancellationToken cancellationToken)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(session.PersonaRole)
            && !string.IsNullOrWhiteSpace(session.PersonaName))
        {
            var personaRole = CharacterRoleCatalog.Normalize(session.PersonaRole);
            if (!string.IsNullOrWhiteSpace(personaRole)
                && !string.Equals(personaRole, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                bindings[personaRole] = session.PersonaName.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            return bindings;
        }

        var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
        if (scenario is null || scenario.Characters.Count == 0)
        {
            return bindings;
        }

        var seenRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in scenario.Characters)
        {
            var roleName = CharacterRoleCatalog.Normalize(character.Role);
            if (string.IsNullOrWhiteSpace(roleName)
                || string.Equals(roleName, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase)
                || seenRoles.Contains(roleName)
                || string.IsNullOrWhiteSpace(character.Id))
            {
                continue;
            }

            seenRoles.Add(roleName);
            var boundCharacterId = ResolveFitBindingCharacterId(session, character);
            bindings.TryAdd(roleName, boundCharacterId);
        }

        return bindings;
    }

    private static string ResolveFitBindingCharacterId(RolePlaySession session, DreamGenClone.Web.Domain.Scenarios.Character scenarioCharacter)
    {
        var scenarioCharacterId = (scenarioCharacter.Id ?? string.Empty).Trim();
        var scenarioCharacterName = (scenarioCharacter.Name ?? string.Empty).Trim();

        // Candidate identifiers that may represent this actor in adaptive snapshots.
        var identityCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(scenarioCharacterId))
        {
            identityCandidates.Add(scenarioCharacterId);
        }

        if (!string.IsNullOrWhiteSpace(scenarioCharacterName))
        {
            identityCandidates.Add(scenarioCharacterName);
        }

        var perspective = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterId, scenarioCharacterId, StringComparison.OrdinalIgnoreCase));
        var perspectiveName = (perspective?.CharacterName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(perspectiveName))
        {
            identityCandidates.Add(perspectiveName);
        }

        foreach (var entry in session.AdaptiveState.CharacterStats)
        {
            var key = (entry.Key ?? string.Empty).Trim();
            var blockCharacterId = (entry.Value?.CharacterId ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(blockCharacterId) && identityCandidates.Contains(blockCharacterId))
            {
                return blockCharacterId;
            }

            if (!string.IsNullOrWhiteSpace(key) && identityCandidates.Contains(key))
            {
                return key;
            }
        }

        if (!string.IsNullOrWhiteSpace(perspectiveName))
        {
            return perspectiveName;
        }

        return scenarioCharacterId;
    }

    private static decimal NormalizeThemeScore(double score)
    {
        var normalized = decimal.Round((decimal)score / 100m, 4, MidpointRounding.AwayFromZero);
        return Math.Clamp(normalized, 0m, 1m);
    }

    private static decimal NormalizePreferencePriority(int priority)
    {
        var normalized = decimal.Round(priority / 5m, 4, MidpointRounding.AwayFromZero);
        return Math.Clamp(normalized, 0m, 1m);
    }

    private static BuildUpGateSnapshot ParseBuildUpGateAudit(string? auditMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(auditMetadataJson))
        {
            return BuildUpGateSnapshot.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(auditMetadataJson);
            var root = doc.RootElement;
            return new BuildUpGateSnapshot(
                Passed: ReadNullableBool(root, "passed"),
                Configured: ReadNullableBool(root, "configured") ?? false,
                ProfileId: ReadString(root, "profileId"),
                ProfileName: ReadString(root, "profileName"));
        }
        catch
        {
            return BuildUpGateSnapshot.Empty;
        }
    }

    private static bool? ReadNullableBool(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record BuildUpGateSnapshot(bool? Passed, bool Configured, string? ProfileId, string? ProfileName)
    {
        public static BuildUpGateSnapshot Empty { get; } = new(null, false, null, null);
    }

    private static List<DreamGenClone.Domain.RolePlay.BehavioralConcept> BuildConceptCandidates(RolePlaySession session)
    {
        var list = new List<DreamGenClone.Domain.RolePlay.BehavioralConcept>();
        if (!string.IsNullOrWhiteSpace(session.AdaptiveState.ThemeTracker.PrimaryThemeId))
        {
            list.Add(new DreamGenClone.Domain.RolePlay.BehavioralConcept
            {
                ConceptId = $"theme:{session.AdaptiveState.ThemeTracker.PrimaryThemeId}",
                Category = "Scenario",
                Priority = 100,
                GuidanceText = "Maintain primary-theme continuity.",
                TriggerConditions = "{}",
                IsEnabled = true
            });
        }

        list.Add(new DreamGenClone.Domain.RolePlay.BehavioralConcept
        {
            ConceptId = "willingness:balance",
            Category = "Willingness",
            Priority = 80,
            GuidanceText = "Balance desire and restraint progression.",
            TriggerConditions = "{}",
            IsEnabled = true
        });

        return list;
    }

    private static DreamGenClone.Domain.RolePlay.NarrativePhase MapPhase(DreamGenClone.Domain.StoryAnalysis.NarrativePhase current)
    {
        return current switch
        {
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.BuildUp => DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp,
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Committed => DreamGenClone.Domain.RolePlay.NarrativePhase.Committed,
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Approaching => DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching,
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Climax => DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
            _ => DreamGenClone.Domain.RolePlay.NarrativePhase.Reset
        };
    }

    private static DreamGenClone.Domain.StoryAnalysis.NarrativePhase MapStoryPhase(DreamGenClone.Domain.RolePlay.NarrativePhase phase)
    {
        return phase switch
        {
            DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.BuildUp,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Committed => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Committed,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Approaching,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Climax => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Climax,
            _ => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Reset
        };
    }

    private static InteractionType ToInteractionType(ContinueAsActor actor)
    {
        return actor switch
        {
            ContinueAsActor.You => InteractionType.User,
            ContinueAsActor.Npc => InteractionType.Npc,
            ContinueAsActor.Custom => InteractionType.Custom,
            _ => InteractionType.System
        };
    }

    private static string ResolveActorName(ContinueAsActor actor, string? customActorName)
    {
        return actor switch
        {
            ContinueAsActor.You => "You",
            ContinueAsActor.Npc => "NPC",
            ContinueAsActor.Custom => string.IsNullOrWhiteSpace(customActorName) ? "Custom" : customActorName.Trim(),
            _ => "System"
        };
    }

    private async Task<IReadOnlyList<IdentityOption>> ResolveSelectedIdentityOptionsAsync(
        RolePlaySession session,
        ContinueAsRequest request,
        CancellationToken cancellationToken)
    {
        var identityOptions = await _identityOptionsService.GetIdentityOptionsAsync(session, cancellationToken);
        if (identityOptions.Count == 0)
        {
            return [];
        }

        var selectedById = request.SelectedIdentityIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedById.Count > 0)
        {
            return identityOptions
                .Where(x => x.IsAvailable && selectedById.Contains(x.Id))
                .ToList();
        }

        var selectedActors = ContinueAsOrdering.OrderDistinct(request.SelectedParticipants).ToHashSet();
        if (selectedActors.Count == 0)
        {
            return [];
        }

        return identityOptions
            .Where(x => x.IsAvailable && selectedActors.Contains(x.Actor))
            .ToList();
    }

    private ContinueAsActor ResolveDefaultContinueActor(RolePlaySession session)
    {
        var allowedActors = _behaviorModeService.GetAllowedActors(session.BehaviorMode, explicitSelection: false);
        if (allowedActors.Count == 0)
        {
            return ContinueAsActor.Npc;
        }

        var lastInteraction = session.Interactions.LastOrDefault();
        var preferred = lastInteraction?.InteractionType switch
        {
            InteractionType.User => ContinueAsActor.Npc,
            InteractionType.Custom => ContinueAsActor.Npc,
            InteractionType.Npc => ContinueAsActor.You,
            InteractionType.System => ContinueAsActor.Npc,
            _ => ContinueAsActor.Npc
        };

        if (allowedActors.Contains(preferred))
        {
            return preferred;
        }

        if (allowedActors.Contains(ContinueAsActor.Npc))
        {
            return ContinueAsActor.Npc;
        }

        if (allowedActors.Contains(ContinueAsActor.You))
        {
            return ContinueAsActor.You;
        }

        if (allowedActors.Contains(ContinueAsActor.Custom))
        {
            return ContinueAsActor.Custom;
        }

        return allowedActors.First();
    }

    private static string? ResolveOptionActorName(IdentityOption option, string? customActorName)
    {
        if (option.SourceType == IdentityOptionSource.CustomCharacter)
        {
            return string.IsNullOrWhiteSpace(customActorName) ? option.DisplayName : customActorName.Trim();
        }

        return option.DisplayName;
    }

    private sealed class NullScenarioSelectionService : IScenarioSelectionService
    {
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>> EvaluateCandidatesAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            IReadOnlyList<ScenarioDefinition> candidates,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>>([]);

        public Task<ScenarioCommitResult> TryCommitScenarioAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation> evaluations,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ScenarioCommitResult { Committed = false, UpdatedConsecutiveLeadCount = 0, Reason = "Selection disabled." });
    }

    private sealed class NullScenarioLifecycleService : IScenarioLifecycleService
    {
        public Task<PhaseTransitionResult> EvaluateTransitionAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            LifecycleInputs inputs,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PhaseTransitionResult { Transitioned = false, TargetPhase = state.CurrentPhase, Reason = "Lifecycle disabled." });

        public Task<DreamGenClone.Domain.RolePlay.AdaptiveScenarioState> ExecuteResetAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            ResetReason reason,
            CancellationToken cancellationToken = default)
            => Task.FromResult(state);
    }

    private sealed class NullConceptInjectionService : IConceptInjectionService
    {
        public Task<ConceptInjectionResult> BuildGuidanceAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            ConceptInjectionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ConceptInjectionResult { SelectedConcepts = [], BudgetCap = context.BudgetCap, BudgetUsed = 0, Rationale = "Concept injection disabled." });
    }

    private sealed class NullDecisionPointService : IDecisionPointService
    {
        public Task<DreamGenClone.Domain.RolePlay.DecisionPoint?> TryCreateDecisionPointAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            DecisionTrigger trigger,
            DecisionGenerationContext? context = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<DreamGenClone.Domain.RolePlay.DecisionPoint?>(null);

        public Task<DecisionOutcome> ApplyDecisionAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            DecisionSubmission submission,
            string? targetActorId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new DecisionOutcome { Applied = false, DecisionPointId = submission.DecisionPointId, OptionId = submission.OptionId, Summary = "Decision service disabled." });
    }

    private sealed class NullRolePlayStateRepository : IRolePlayStateRepository
    {
        public Task<DreamGenClone.Domain.RolePlay.RolePlayTurn> StartTurnAsync(string sessionId, string turnKind, string triggerSource, string? initiatedByActorName, string? inputInteractionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new DreamGenClone.Domain.RolePlay.RolePlayTurn
            {
                TurnId = Guid.NewGuid().ToString("N"),
                SessionId = sessionId,
                TurnKind = turnKind,
                TriggerSource = triggerSource,
                InitiatedByActorName = initiatedByActorName,
                InputInteractionId = inputInteractionId,
                StartedUtc = DateTime.UtcNow,
                Status = DreamGenClone.Domain.RolePlay.RolePlayTurnStatus.Started
            });
        public Task CompleteTurnAsync(string sessionId, string turnId, IReadOnlyList<string> outputInteractionIds, bool succeeded, string? failureReason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.RolePlayTurn>>([]);
        public Task SaveAdaptiveStateAsync(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DreamGenClone.Domain.RolePlay.AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<DreamGenClone.Domain.RolePlay.AdaptiveScenarioState?>(null);
        public Task SaveCandidateEvaluationsAsync(IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>>([]);
        public Task SaveTransitionEventAsync(DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent>>([]);
        public Task SaveCompletionMetadataAsync(DreamGenClone.Domain.RolePlay.ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDecisionPointAsync(DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint, IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption> options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionPoint>>([]);
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption>>([]);
        public Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveFormulaVersionReferenceAsync(string sessionId, DreamGenClone.Domain.RolePlay.FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveUnsupportedSessionErrorAsync(DreamGenClone.Domain.RolePlay.UnsupportedSessionError error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.UnsupportedSessionError>>([]);
    }
}
