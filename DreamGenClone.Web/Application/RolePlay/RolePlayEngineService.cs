using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayEngineService : IRolePlayEngineService
{
    private static readonly TimeSpan DecisionPointContextCooldown = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<string, RolePlaySession> Sessions = new();
    private const bool EnableRolePlayStreaming = false;
    private static readonly IReadOnlyDictionary<string, (string Label, IReadOnlyDictionary<string, int> Deltas)> DecisionOptionCatalog =
        new Dictionary<string, (string Label, IReadOnlyDictionary<string, int> Deltas)>(StringComparer.OrdinalIgnoreCase)
        {
            ["lean-in"] = ("Lean In", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 6,
                ["Tension"] = 4,
                ["Restraint"] = -3
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
                ["Restraint"] = -2,
                ["Tension"] = 3
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
    private readonly IRolePlayV2StateRepository _v2StateRepository;
    private readonly IThemePreferenceService? _themePreferenceService;
    private readonly IRPThemeService? _rpThemeService;
    private readonly RolePlayPromptComposer _promptComposer;
    private readonly RolePlaySessionCompatibilityService? _compatibilityService;
    private readonly ILogger<RolePlayEngineService> _logger;
    private readonly bool _useRpThemeSubsystem;
    private readonly bool _useRpThemeSubsystemForNewSessionsOnly;

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
        IRolePlayV2StateRepository? v2StateRepository = null,
        IThemePreferenceService? themePreferenceService = null,
        IRPThemeService? rpThemeService = null,
        RolePlayPromptComposer? promptComposer = null,
        RolePlaySessionCompatibilityService? compatibilityService = null,
        IOptions<StoryAnalysisOptions>? storyAnalysisOptions = null)
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
        _v2StateRepository = v2StateRepository ?? new NullRolePlayV2StateRepository();
        _themePreferenceService = themePreferenceService;
        _rpThemeService = rpThemeService;
        _promptComposer = promptComposer ?? new RolePlayPromptComposer();
        _compatibilityService = compatibilityService;
        _logger = logger;
        _useRpThemeSubsystem = storyAnalysisOptions?.Value.UseRpThemeSubsystem ?? true;
        _useRpThemeSubsystemForNewSessionsOnly = storyAnalysisOptions?.Value.UseRpThemeSubsystemForNewSessionsOnly ?? true;
    }

    public async Task<RolePlaySession> CreateSessionAsync(
        string title,
        string? scenarioId = null,
        string personaName = "You",
        string personaDescription = "",
        string? personaTemplateId = null,
        CancellationToken cancellationToken = default)
    {
        var session = new RolePlaySession
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled Role-Play" : title.Trim(),
            ScenarioId = scenarioId,
            PersonaName = string.IsNullOrWhiteSpace(personaName) ? "You" : personaName.Trim(),
            PersonaDescription = personaDescription ?? string.Empty,
            PersonaTemplateId = personaTemplateId,
            PersonaPerspectiveMode = CharacterPerspectiveMode.FirstPersonInternalMonologue,
            UseRpThemeSubsystem = _useRpThemeSubsystem
        };

        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(scenarioId);
            if (scenario is not null)
            {
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
            return session;
        }

        session = await _sessionService.LoadRolePlaySessionAsync(sessionId, cancellationToken);
        if (session is not null)
        {
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

        session.Interactions.Add(interaction);
        session.Status = RolePlaySessionStatus.InProgress;
        session.ModifiedAt = DateTime.UtcNow;

        session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
        await RunRolePlayV2PipelinesAsync(session, DecisionTrigger.InteractionStart, cancellationToken);

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
        return interaction;
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

        if (!_behaviorModeService.IsContinuationAllowed(session.BehaviorMode, actor))
        {
            throw new InvalidOperationException($"Actor '{actor}' is not allowed in mode '{session.BehaviorMode}'.");
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var promptText = string.IsNullOrWhiteSpace(instruction)
            ? "Continue the scene naturally."
            : instruction.Trim();

        var interaction = await _continuationService.ContinueAsync(
            session,
            actor,
            customActorName,
            PromptIntent.Narrative,
            promptText,
            null,
            cancellationToken);

        session.Interactions.Add(interaction);
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

        return interaction;
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
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, userPromptInteraction, cancellationToken);

            if (submission.SubmittedVia == SubmissionSource.PlusButton)
            {
                interaction = userPromptInteraction;
            }
            else
            {
                interaction = await _continuationService.ContinueAsync(
                    session,
                    identity.Actor,
                    selectedActorName,
                    submission.Intent,
                    BuildContinuationPromptText(submission.Intent, submission.PromptText),
                    effectiveOnChunk,
                    cancellationToken);

                session.Interactions.Add(interaction);
                session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
            }
        }

        if (submission.Intent == PromptIntent.Instruction)
        {
            session.Interactions.Add(interaction);
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

        await RunRolePlayV2PipelinesAsync(session, DecisionTrigger.InteractionStart, cancellationToken);

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

        var isOverflowContinue = request.TriggeredBy == SubmissionSource.MainOverflowContinue;

        // --- OPENING NARRATIVE ---
        // If no interactions yet, always generate a scene-setting narrative FIRST
        var isOpeningScene = session.Interactions.Count(i => !i.IsExcluded) == 0;
        if (isOpeningScene && session.AutoNarrative)
        {
            var openingNarrative = await _continuationService.ContinueAsync(
                session,
                ContinueAsActor.Npc,
                "Narrative",
                PromptIntent.Narrative,
                "Set the opening scene. Establish the setting, atmosphere, and the characters present. " +
                "Describe the environment and the initial situation the characters find themselves in.",
                effectiveOnChunk,
                cancellationToken);

            openingNarrative.InteractionType = InteractionType.System;
            openingNarrative.ActorName = "Narrative";
            result.NarrativeOutput = openingNarrative;
            session.Interactions.Add(openingNarrative);
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, openingNarrative, cancellationToken);
        }

        if (selectedIdentityOptions.Count > 0)
        {
            // Explicit identity selections — generate sequentially, accumulating context
            foreach (var option in selectedIdentityOptions)
            {
                var actorName = ResolveOptionActorName(option, request.CustomIdentityName);
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
                session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
            }
        }
        else if (isOverflowContinue)
        {
            // --- MULTI-ACTOR OVERFLOW CONTINUE ---
            // Determine which scene characters should naturally respond,
            // generate sequentially so each sees the prior output in context.
            var sceneActors = await ResolveSceneContinueActorsAsync(session, cancellationToken);
            var preferredOverflowActor = ResolveDefaultContinueActor(session);
            if (preferredOverflowActor == ContinueAsActor.You)
            {
                var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
                sceneActors.RemoveAll(x => x.Actor == ContinueAsActor.You);
                sceneActors.Insert(0, (ContinueAsActor.You, personaName));
            }

            var batchSize = Math.Max(1, Math.Min(session.SceneContinueBatchSize, sceneActors.Count));

            for (var i = 0; i < batchSize; i++)
            {
                var (actor, actorName) = sceneActors[i];
                var promptText = i == 0
                    ? "Continue the scene naturally with the next character response."
                    : "Continue the conversation naturally, building on the previous response.";

                var interaction = await _continuationService.ContinueAsync(
                    session, actor, actorName, PromptIntent.Message, promptText, effectiveOnChunk, cancellationToken);

                result.ParticipantOutputs.Add(interaction);
                // Append to session so next iteration's prompt sees this interaction
                session.Interactions.Add(interaction);
                session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
            }
        }
        else
        {
            // Fallback: single actor default
            var fallbackActor = ResolveDefaultContinueActor(session);
            var fallbackActorName = ResolveActorName(fallbackActor, request.CustomIdentityName);
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
            session.AdaptiveState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);
        }

        // --- AUTO-NARRATIVE ---
        // Include narrative if explicitly requested OR if AutoNarrative is on for overflow continues
        // Skip if we already generated the opening narrative above
        var shouldIncludeNarrative = !isOpeningScene
            && (request.IncludeNarrative
                || (isOverflowContinue && session.AutoNarrative && ShouldAutoNarrate(session)));

        if (shouldIncludeNarrative)
        {
            var narrativePrompt = DetermineNarrativePrompt(session);
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

        return result;
    }

    public async Task<RolePlayPendingDecisionPrompt?> GetPendingDecisionPromptAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var points = await _v2StateRepository.LoadDecisionPointsAsync(sessionId, 30, cancellationToken);
        if (points.Count == 0)
        {
            return null;
        }

        var appliedIds = session.AppliedDecisionPointIds ??= [];
        var pending = points
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefault(x => !appliedIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase));
        if (pending is null)
        {
            return null;
        }

        var options = await _v2StateRepository.LoadDecisionOptionsAsync(pending.DecisionPointId, cancellationToken);
        return new RolePlayPendingDecisionPrompt
        {
            DecisionPoint = pending,
            Options = options
        };
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
        var targetActorId = ResolveDecisionTargetActorId(state, session.PersonaName);
        var outcome = await _decisionPointService.ApplyDecisionAsync(
            state,
            new DecisionSubmission
            {
                DecisionPointId = decisionPointId,
                OptionId = optionId,
                CustomResponseText = customResponseText,
                ActorName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName,
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

        session.Interactions.Add(new RolePlayInteraction
        {
            InteractionType = InteractionType.System,
            ActorName = "Decision Outcome",
            Content = BuildDecisionOutcomePrompt(outcome)
        });

        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-decision-applied");
        return outcome;
    }

    /// <summary>
    /// Resolves the scene characters that should naturally continue the conversation.
    /// Looks at the scenario character list and recent interaction history to pick
    /// the most relevant characters in a natural conversation order.
    /// </summary>
    private async Task<List<(ContinueAsActor Actor, string Name)>> ResolveSceneContinueActorsAsync(
        RolePlaySession session,
        CancellationToken cancellationToken)
    {
        var actors = new List<(ContinueAsActor, string)>();

        // Gather scenario characters (excluding the POV persona)
        var sceneCharacterNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                foreach (var character in scenario.Characters)
                {
                    if (!string.IsNullOrWhiteSpace(character.Name)
                        && !string.Equals(character.Name.Trim(), session.PersonaName, StringComparison.OrdinalIgnoreCase))
                    {
                        sceneCharacterNames.Add(character.Name.Trim());
                    }
                }
            }
        }

        if (sceneCharacterNames.Count == 0)
        {
            // No scenario characters — fall back to default single actor
            var fallback = ResolveDefaultContinueActor(session);
            actors.Add((fallback, ResolveActorName(fallback, null)));
            return actors;
        }

        // Determine conversation order by recency: characters who haven't spoken recently,
        // or never spoke at all, go first.
        var recentActors = session.Interactions
            .Where(i => (i.InteractionType == InteractionType.Npc || i.InteractionType == InteractionType.Custom) && !i.IsExcluded)
            .TakeLast(6)
            .Select(i => i.ActorName?.Trim())
            .ToList();

        var ordered = sceneCharacterNames
            .Select((name, scenarioOrder) => new
            {
                Name = name,
                ScenarioOrder = scenarioOrder,
                LastSeenIndex = recentActors.FindLastIndex(actorName => string.Equals(actorName, name, StringComparison.OrdinalIgnoreCase))
            })
            .OrderBy(x => x.LastSeenIndex < 0 ? int.MinValue : x.LastSeenIndex)
            .ThenBy(x => x.ScenarioOrder)
            .Select(x => x.Name)
            .ToList();

        foreach (var name in ordered)
        {
            actors.Add((ContinueAsActor.Npc, name));
        }

        return actors;
    }

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

    private async Task RunRolePlayV2PipelinesAsync(RolePlaySession session, DecisionTrigger trigger, CancellationToken cancellationToken)
    {
        var previousV2State = await _v2StateRepository.LoadAdaptiveStateAsync(session.Id, cancellationToken);
        var v2State = HydrateV2State(session, previousV2State);
        v2State.InteractionCountInPhase = Math.Max(0, v2State.InteractionCountInPhase) + 1;
        var candidates = await BuildScenarioCandidatesAsync(session, cancellationToken);

        var evaluations = await _scenarioSelectionService.EvaluateCandidatesAsync(v2State, candidates, cancellationToken);
        await _v2StateRepository.SaveCandidateEvaluationsAsync(evaluations, cancellationToken);

        var commitResult = await _scenarioSelectionService.TryCommitScenarioAsync(v2State, evaluations, cancellationToken);
        v2State.ConsecutiveLeadCount = commitResult.UpdatedConsecutiveLeadCount;
        if (commitResult.Committed && !string.IsNullOrWhiteSpace(commitResult.ScenarioId))
        {
            v2State.ActiveScenarioId = commitResult.ScenarioId;
            v2State.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed;
            v2State.InteractionCountInPhase = 0;
        }

        var lifecycle = await _scenarioLifecycleService.EvaluateTransitionAsync(
            v2State,
            new LifecycleInputs
            {
                InteractionsSinceCommitment = v2State.InteractionCountInPhase,
                ActiveScenarioConfidence = commitResult.SelectedEvaluation?.Confidence ?? 0m,
                ActiveScenarioFitScore = commitResult.SelectedEvaluation?.FitScore ?? 0m,
                EvidenceSummary = commitResult.Reason
            },
            cancellationToken);

        if (lifecycle.Transitioned)
        {
            v2State.CurrentPhase = lifecycle.TargetPhase;
            v2State.InteractionCountInPhase = 0;
            if (lifecycle.TransitionEvent is not null)
            {
                await _v2StateRepository.SaveTransitionEventAsync(lifecycle.TransitionEvent, cancellationToken);
            }

            if (lifecycle.TargetPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Reset)
            {
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
                await _v2StateRepository.SaveCompletionMetadataAsync(completion, cancellationToken);
                v2State = await _scenarioLifecycleService.ExecuteResetAsync(v2State, ResetReason.Completion, cancellationToken);
            }
        }

        var significantStatChange = HasSignificantStatChange(previousV2State, v2State);
        var conceptCandidates = BuildConceptCandidates(session);
        var conceptTriggers = _promptComposer.ResolveConceptInjectionTriggers(trigger, lifecycle.Transitioned, significantStatChange);
        foreach (var conceptTrigger in conceptTriggers)
        {
            var conceptContext = _promptComposer.BuildConceptContext(conceptCandidates, conceptTrigger);
            var conceptResult = await _conceptInjectionService.BuildGuidanceAsync(v2State, conceptContext, cancellationToken);
            await _v2StateRepository.SaveConceptInjectionAsync(session.Id, conceptResult, cancellationToken);
        }

        var effectiveDecisionTrigger = lifecycle.Transitioned
            ? DecisionTrigger.PhaseChanged
            : trigger;

        var hasPendingDecision = await HasPendingDecisionPointAsync(session, cancellationToken);
        var isInDecisionCooldown = hasPendingDecision
            ? false
            : await HasRecentDecisionPointForContextAsync(session, v2State, effectiveDecisionTrigger, cancellationToken);
        var decisionContext = BuildDecisionGenerationContext(session, v2State, effectiveDecisionTrigger);
        var decisionPoint = (hasPendingDecision || isInDecisionCooldown)
            ? null
            : await _decisionPointService.TryCreateDecisionPointAsync(v2State, effectiveDecisionTrigger, decisionContext, cancellationToken);
        if (decisionPoint is not null)
        {
            var options = decisionPoint.OptionIds.Select(optionId => new DreamGenClone.Domain.RolePlay.DecisionOption
            {
                OptionId = optionId,
                DecisionPointId = decisionPoint.DecisionPointId,
                DisplayText = ResolveDecisionOptionDisplayText(optionId),
                VisibilityMode = decisionPoint.TransparencyMode,
                Prerequisites = "{}",
                StatDeltaMap = ResolveDecisionOptionDeltaMap(optionId),
                IsCustomResponseFallback = string.Equals(optionId, "custom", StringComparison.OrdinalIgnoreCase)
            }).ToList();

            await _v2StateRepository.SaveDecisionPointAsync(decisionPoint, options, cancellationToken);

            // Surface decision points directly in the story so users can see them when they trigger.
            session.Interactions.Add(new RolePlayInteraction
            {
                InteractionType = InteractionType.System,
                ActorName = "Decision",
                Content = BuildDecisionQuestionPrompt(decisionPoint)
            });
        }

        await _v2StateRepository.SaveFormulaVersionReferenceAsync(
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

        await _v2StateRepository.SaveAdaptiveStateAsync(v2State, cancellationToken);
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

        mapped.ActiveScenarioId = previousState.ActiveScenarioId ?? mapped.ActiveScenarioId;
        mapped.ActiveVariantId = previousState.ActiveVariantId ?? mapped.ActiveVariantId;
        mapped.CurrentPhase = previousState.CurrentPhase;
        mapped.InteractionCountInPhase = Math.Max(0, previousState.InteractionCountInPhase);
        mapped.ConsecutiveLeadCount = Math.Max(0, previousState.ConsecutiveLeadCount);
        mapped.CycleIndex = Math.Max(mapped.CycleIndex, previousState.CycleIndex);
        mapped.ActiveFormulaVersion = string.IsNullOrWhiteSpace(previousState.ActiveFormulaVersion)
            ? mapped.ActiveFormulaVersion
            : previousState.ActiveFormulaVersion;
        mapped.LastEvaluationUtc = previousState.LastEvaluationUtc;
        return mapped;
    }

    private static void SyncSessionAdaptiveStateFromV2(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState v2State)
    {
        session.AdaptiveState.ActiveScenarioId = v2State.ActiveScenarioId;
        session.AdaptiveState.ActiveVariantId = v2State.ActiveVariantId;
        session.AdaptiveState.CurrentNarrativePhase = MapStoryPhase(v2State.CurrentPhase);

        var interactionCount = Math.Max(0, v2State.InteractionCountInPhase);
        session.AdaptiveState.InteractionsSinceCommitment = v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp
            ? 0
            : interactionCount;
        session.AdaptiveState.InteractionsInApproaching = v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching
            ? interactionCount
            : 0;

        if (v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Committed
            && session.AdaptiveState.ScenarioCommitmentTimeUtc is null)
        {
            session.AdaptiveState.ScenarioCommitmentTimeUtc = DateTime.UtcNow;
        }
        else if (v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp)
        {
            session.AdaptiveState.ScenarioCommitmentTimeUtc = null;
        }
    }

    private async Task<bool> HasPendingDecisionPointAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        var points = await _v2StateRepository.LoadDecisionPointsAsync(session.Id, 30, cancellationToken);
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

        var points = await _v2StateRepository.LoadDecisionPointsAsync(session.Id, 10, cancellationToken);
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

    private static string BuildDecisionQuestionPrompt(DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint)
    {
        var phaseLabel = decisionPoint.Phase.ToString();
        var optionsText = decisionPoint.OptionIds.Count == 0
            ? "Respond in-character with your next choice."
            : string.Join(" | ", decisionPoint.OptionIds.Select(x => x.Trim()).Where(x => x.Length > 0));

        return $"Decision point ({phaseLabel}): what does your character choose next?\nOptions: {optionsText}";
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

    private static void ApplyDecisionOutcomeToSessionState(RolePlaySession session, DecisionOutcome outcome)
    {
        if (outcome.AppliedStatDeltas.Count == 0 || session.AdaptiveState.CharacterStats.Count == 0)
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

        foreach (var statBlock in session.AdaptiveState.CharacterStats.Values)
        {
            ApplyDeltasToStatBlock(statBlock, outcome.AppliedStatDeltas);
        }
    }

    private static void ApplyDeltasToStatBlock(
        CharacterStatBlock statBlock,
        IReadOnlyDictionary<string, int> deltas)
    {
        foreach (var (statName, delta) in deltas)
        {
            var current = statBlock.Stats.TryGetValue(statName, out var currentValue)
                ? currentValue
                : AdaptiveStatCatalog.DefaultValue;
            statBlock.Stats[statName] = Math.Clamp(current + delta, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue);
        }

        statBlock.UpdatedUtc = DateTime.UtcNow;
    }

    private static string? ResolveDecisionTargetActorId(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string? actorName)
    {
        if (!string.IsNullOrWhiteSpace(actorName))
        {
            var match = state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, actorName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.CharacterId;
            }
        }

        return state.CharacterSnapshots.Count == 1
            ? state.CharacterSnapshots[0].CharacterId
            : null;
    }

    private static DecisionGenerationContext BuildDecisionGenerationContext(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        DecisionTrigger trigger)
    {
        var snippet = session.Interactions
            .TakeLast(4)
            .Select(x => x.Content)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .FirstOrDefault();

        return new DecisionGenerationContext
        {
            ScenarioId = session.ScenarioId,
            TriggerSource = trigger.ToString(),
            Phase = state.CurrentPhase,
            PromptSnippet = snippet,
            AskingActorName = string.IsNullOrWhiteSpace(session.PersonaName) ? null : session.PersonaName,
            TargetActorId = ResolveDecisionTargetActorId(state, session.PersonaName),
            RelevantActors = state.CharacterSnapshots
        };
    }

    private static string BuildDecisionOutcomePrompt(DecisionOutcome outcome)
    {
        if (outcome.AppliedStatDeltas.Count == 0)
        {
            return $"Decision applied: {outcome.OptionId}.";
        }

        var deltas = string.Join(", ",
            outcome.AppliedStatDeltas.Select(x => $"{x.Key} {(x.Value >= 0 ? "+" : string.Empty)}{x.Value}"));
        return $"Decision applied: {outcome.OptionId}. Stat changes: {deltas}.";
    }

    private static DreamGenClone.Domain.RolePlay.AdaptiveScenarioState MapToV2State(RolePlaySession session)
    {
        var snapshots = session.AdaptiveState.CharacterStats.Select(x =>
        {
            var stats = x.Value.Stats;
            return new DreamGenClone.Domain.RolePlay.CharacterStatProfileV2
            {
                CharacterId = string.IsNullOrWhiteSpace(x.Value.CharacterId) ? x.Key : x.Value.CharacterId,
                Desire = stats.TryGetValue("Desire", out var desire) ? desire : 50,
                Restraint = stats.TryGetValue("Restraint", out var restraint) ? restraint : 50,
                Tension = stats.TryGetValue("Tension", out var tension) ? tension : 50,
                Connection = stats.TryGetValue("Connection", out var connection) ? connection : 50,
                Dominance = stats.TryGetValue("Dominance", out var dominance) ? dominance : 50,
                Loyalty = stats.TryGetValue("Loyalty", out var loyalty) ? loyalty : 50,
                SelfRespect = stats.TryGetValue("SelfRespect", out var selfRespect) ? selfRespect : 50,
                SnapshotUtc = DateTime.UtcNow
            };
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
            HusbandAwarenessProfileId = session.AdaptiveState.HusbandAwarenessProfileId,
            CharacterSnapshots = snapshots
        };
    }

    private async Task<List<ScenarioDefinition>> BuildScenarioCandidatesAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        var rankedThemes = session.AdaptiveState.ThemeTracker.Themes.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ThemeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_rpThemeService is not null && ShouldUseRpThemeSubsystem(session) && !string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
        {
            var assignments = await _rpThemeService.ListProfileAssignmentsAsync(session.SelectedRPThemeProfileId, cancellationToken);
            var themes = await _rpThemeService.ListThemesAsync(includeDisabled: false, cancellationToken: cancellationToken);
            var themesById = themes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

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

                    return new ScenarioDefinition(
                        theme.Id,
                        theme.Label,
                        Priority: Math.Max(1, 5 - index),
                        NarrativeEvidenceScore: preferencePriority,
                        PreferencePriorityScore: preferencePriority);
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
                    .Where(x => allowedCatalogIds.Contains(x.ThemeId))
                    .Take(5)
                    .Select((theme, index) => new ScenarioDefinition(
                        theme.ThemeId,
                        theme.ThemeName,
                        Priority: 5 - index,
                        NarrativeEvidenceScore: NormalizeThemeScore(theme.Score),
                        PreferencePriorityScore: NormalizePreferencePriority(5 - index)))
                    .ToList();

                if (profileCandidates.Count > 0)
                {
                    return profileCandidates;
                }
            }
        }

        var candidates = rankedThemes
            .Take(5)
            .Select((theme, index) => new ScenarioDefinition(
                theme.ThemeId,
                theme.ThemeName,
                Priority: 5 - index,
                NarrativeEvidenceScore: NormalizeThemeScore(theme.Score),
                PreferencePriorityScore: NormalizePreferencePriority(5 - index)))
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

    private bool ShouldUseRpThemeSubsystem(RolePlaySession session)
    {
        if (!_useRpThemeSubsystem)
        {
            return false;
        }

        if (!_useRpThemeSubsystemForNewSessionsOnly)
        {
            return true;
        }

        return session.UseRpThemeSubsystem;
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
        var allowedActors = _behaviorModeService.GetAllowedActors(session.BehaviorMode);
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

    private sealed class NullRolePlayV2StateRepository : IRolePlayV2StateRepository
    {
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
