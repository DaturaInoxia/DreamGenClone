using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.StoryAnalysis;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayEngineService : IRolePlayEngineService
{
    private static readonly ConcurrentDictionary<string, RolePlaySession> Sessions = new();

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
    private readonly ILogger<RolePlayEngineService> _logger;

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
        ILogger<RolePlayEngineService> logger)
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
        _logger = logger;
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
            PersonaPerspectiveMode = CharacterPerspectiveMode.FirstPersonInternalMonologue
        };

        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(scenarioId);
            if (scenario is not null)
            {
                session.PersonaPerspectiveMode = scenario.DefaultPersonaPerspectiveMode;
                session.SelectedRankingProfileId = scenario.DefaultRankingProfileId;
                session.SelectedToneProfileId = scenario.DefaultToneProfileId ?? scenario.Style.ToneProfileId;
                session.SelectedStyleProfileId = scenario.Style.StyleProfileId;
                session.StyleFloorOverride = scenario.Style.StyleFloor;
                session.StyleCeilingOverride = scenario.Style.StyleCeiling;

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
                    onChunk,
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
                onChunk,
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
                    onChunk,
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
            var batchSize = Math.Max(1, Math.Min(session.SceneContinueBatchSize, sceneActors.Count));

            for (var i = 0; i < batchSize; i++)
            {
                var (actor, actorName) = sceneActors[i];
                var promptText = i == 0
                    ? "Continue the scene naturally with the next character response."
                    : "Continue the conversation naturally, building on the previous response.";

                var interaction = await _continuationService.ContinueAsync(
                    session, actor, actorName, PromptIntent.Message, promptText, onChunk, cancellationToken);

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
                onChunk,
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
                onChunk,
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

        return result;
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

        // Determine conversation order: characters who haven't spoken recently go first,
        // alternate between characters for natural flow
        var recentActors = session.Interactions
            .Where(i => i.InteractionType == InteractionType.Npc && !i.IsExcluded)
            .TakeLast(6)
            .Select(i => i.ActorName)
            .ToList();

        // Find the last speaker to avoid starting with the same character
        var lastNpcSpeaker = recentActors.LastOrDefault();

        // Order: characters who didn't speak last go first
        var ordered = sceneCharacterNames
            .OrderBy(name => string.Equals(name, lastNpcSpeaker, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToList();

        foreach (var name in ordered)
        {
            actors.Add((ContinueAsActor.Npc, name));
        }

        return actors;
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
}
