using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayEngineService : IRolePlayEngineService
{
    private static readonly ConcurrentDictionary<string, RolePlaySession> Sessions = new();

    private readonly IRolePlayContinuationService _continuationService;
    private readonly IBehaviorModeService _behaviorModeService;
    private readonly IRolePlayPromptRouter _promptRouter;
    private readonly IRolePlayIdentityOptionsService _identityOptionsService;
    private readonly ISessionService _sessionService;
    private readonly AutoSaveCoordinator _autoSaveCoordinator;
    private readonly ILogger<RolePlayEngineService> _logger;

    public RolePlayEngineService(
        IRolePlayContinuationService continuationService,
        IBehaviorModeService behaviorModeService,
        IRolePlayPromptRouter promptRouter,
        IRolePlayIdentityOptionsService identityOptionsService,
        ISessionService sessionService,
        AutoSaveCoordinator autoSaveCoordinator,
        ILogger<RolePlayEngineService> logger)
    {
        _continuationService = continuationService;
        _behaviorModeService = behaviorModeService;
        _promptRouter = promptRouter;
        _identityOptionsService = identityOptionsService;
        _sessionService = sessionService;
        _autoSaveCoordinator = autoSaveCoordinator;
        _logger = logger;
    }

    public Task<RolePlaySession> CreateSessionAsync(
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
            PersonaTemplateId = personaTemplateId
        };

        Sessions[session.Id] = session;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-session-created");
        _logger.LogInformation("Role-play session created: {SessionId} ({Title}), Persona={PersonaName}",
            session.Id, session.Title, session.PersonaName);
        return Task.FromResult(session);
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
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-interaction-added");

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
            cancellationToken);

        session.Interactions.Add(interaction);
        session.Status = RolePlaySessionStatus.InProgress;
        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-continue-generated");

        _logger.LogInformation(
            "Role-play continuation generated for session {SessionId} as {Actor} in mode {Mode}",
            sessionId,
            interaction.ActorName,
            session.BehaviorMode);

        return interaction;
    }

    public async Task<RolePlayInteraction> SubmitPromptAsync(
        UnifiedPromptSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (!submission.IsValid(out var validationError))
        {
            throw new ArgumentException(validationError, nameof(submission));
        }

        var session = await GetSessionAsync(submission.SessionId, cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException($"Role-play session '{submission.SessionId}' not found.");
        }

        var options = await _identityOptionsService.GetIdentityOptionsAsync(session, cancellationToken);
        var identity = options.FirstOrDefault(x =>
            x.SourceType == submission.SelectedIdentityType &&
            string.Equals(x.Id, submission.SelectedIdentityId, StringComparison.OrdinalIgnoreCase));

        if (identity is null)
        {
            throw new InvalidOperationException($"Identity option '{submission.SelectedIdentityId}' is not available for this session.");
        }

        if (!identity.IsAvailable)
        {
            throw new InvalidOperationException(identity.AvailabilityReason ?? "The selected identity is not available.");
        }

        var customName = identity.SourceType == IdentityOptionSource.CustomCharacter
            ? (string.IsNullOrWhiteSpace(submission.CustomIdentityName) ? null : submission.CustomIdentityName.Trim())
            : null;

        var route = _promptRouter.Resolve(submission.Intent);
        _logger.LogInformation(
            "Unified prompt route selected for session {SessionId}: intent={Intent}, command={Command}, identity={IdentityId}",
            submission.SessionId,
            submission.Intent,
            route.TargetCommand,
            identity.Id);

        var interaction = await _continuationService.ContinueAsync(
            session,
            identity.Actor,
            customName,
            submission.Intent,
            submission.PromptText,
            cancellationToken);

        session.Interactions.Add(interaction);
        session.Status = RolePlaySessionStatus.InProgress;
        session.BehaviorMode = submission.BehaviorModeAtSubmit;
        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-unified-prompt-submitted");

        _logger.LogInformation(
            "Unified prompt executed for session {SessionId}: actor={Actor}, mode={Mode}",
            session.Id,
            interaction.ActorName,
            session.BehaviorMode);

        return interaction;
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
}
