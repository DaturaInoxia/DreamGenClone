using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayBranchService : IRolePlayBranchService
{
    private readonly IRolePlayEngineService _engineService;
    private readonly ILogger<RolePlayBranchService> _logger;

    public RolePlayBranchService(IRolePlayEngineService engineService, ILogger<RolePlayBranchService> logger)
    {
        _engineService = engineService;
        _logger = logger;
    }

    public async Task<RolePlaySession?> ForkSessionAsync(
        string sourceSessionId,
        string branchTitle,
        int fromInteractionIndexInclusive,
        CancellationToken cancellationToken = default)
    {
        var source = await _engineService.GetSessionAsync(sourceSessionId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        var boundedIndex = Math.Min(fromInteractionIndexInclusive, source.Interactions.Count - 1);
        if (boundedIndex < 0)
        {
            boundedIndex = -1;
        }

        var clone = await _engineService.CreateSessionAsync(
            branchTitle, source.ScenarioId,
            source.PersonaName, source.PersonaDescription, source.PersonaTemplateId, source.PersonaGender, source.PersonaRole, source.PersonaRelationTargetId,
            cancellationToken);
        clone.ParentSessionId = source.Id;
        clone.BehaviorMode = source.BehaviorMode;

        if (boundedIndex >= 0)
        {
            clone.Interactions = source.Interactions
                .Take(boundedIndex + 1)
                .Select(CloneInteraction)
                .ToList();
        }

        await _engineService.SaveSessionAsync(clone, cancellationToken);

        _logger.LogInformation(
            "Role-play session forked: source={SourceSessionId}, branch={BranchSessionId}, interactions={Count}",
            sourceSessionId,
            clone.Id,
            clone.Interactions.Count);

        return clone;
    }

    public async Task<RolePlaySession?> ForkAboveAsync(
        string sourceSessionId,
        string interactionId,
        string branchTitle,
        CancellationToken cancellationToken = default)
    {
        var source = await _engineService.GetSessionAsync(sourceSessionId, cancellationToken);
        if (source is null) return null;

        var originals = source.Interactions.Where(i => i.ParentInteractionId is null).ToList();
        var targetIndex = originals.FindIndex(i => i.Id == interactionId);
        if (targetIndex < 0)
        {
            throw new ArgumentException($"Interaction {interactionId} not found as original in session {sourceSessionId}.", nameof(interactionId));
        }

        var clone = await _engineService.CreateSessionAsync(
            branchTitle, source.ScenarioId,
            source.PersonaName, source.PersonaDescription, source.PersonaTemplateId, source.PersonaGender, source.PersonaRole, source.PersonaRelationTargetId,
            cancellationToken);
        clone.ParentSessionId = source.Id;
        clone.BehaviorMode = source.BehaviorMode;

        clone.Interactions = originals
            .Take(targetIndex + 1)
            .Select(o => CloneActiveAlternative(source, o))
            .ToList();

        await _engineService.SaveSessionAsync(clone, cancellationToken);

        _logger.LogInformation(
            "Forked above from session {SourceId} at interaction {InteractionId}, new session {BranchId} with {Count} interactions",
            sourceSessionId, interactionId, clone.Id, clone.Interactions.Count);

        return clone;
    }

    public async Task<RolePlaySession?> ForkBelowAsync(
        string sourceSessionId,
        string interactionId,
        string branchTitle,
        CancellationToken cancellationToken = default)
    {
        var source = await _engineService.GetSessionAsync(sourceSessionId, cancellationToken);
        if (source is null) return null;

        var originals = source.Interactions.Where(i => i.ParentInteractionId is null).ToList();
        var targetIndex = originals.FindIndex(i => i.Id == interactionId);
        if (targetIndex < 0)
        {
            throw new ArgumentException($"Interaction {interactionId} not found as original in session {sourceSessionId}.", nameof(interactionId));
        }

        var clone = await _engineService.CreateSessionAsync(
            branchTitle, source.ScenarioId,
            source.PersonaName, source.PersonaDescription, source.PersonaTemplateId, source.PersonaGender, source.PersonaRole, source.PersonaRelationTargetId,
            cancellationToken);
        clone.ParentSessionId = source.Id;
        clone.BehaviorMode = source.BehaviorMode;

        clone.Interactions = originals
            .Skip(targetIndex)
            .Select(o => CloneActiveAlternative(source, o))
            .ToList();

        await _engineService.SaveSessionAsync(clone, cancellationToken);

        _logger.LogInformation(
            "Forked below from session {SourceId} at interaction {InteractionId}, new session {BranchId} with {Count} interactions",
            sourceSessionId, interactionId, clone.Id, clone.Interactions.Count);

        return clone;
    }

    private static RolePlayInteraction CloneActiveAlternative(RolePlaySession source, RolePlayInteraction original)
    {
        var active = source.ResolveActiveAlternative(original);
        return new RolePlayInteraction
        {
            InteractionType = active.InteractionType,
            ActorName = active.ActorName,
            Content = active.Content,
            CreatedAt = active.CreatedAt,
            IsExcluded = active.IsExcluded,
            IsHidden = active.IsHidden,
            IsPinned = active.IsPinned
        };
    }

    private static RolePlayInteraction CloneInteraction(RolePlayInteraction interaction)
    {
        return new RolePlayInteraction
        {
            InteractionType = interaction.InteractionType,
            ActorName = interaction.ActorName,
            Content = interaction.Content,
            CreatedAt = interaction.CreatedAt,
            IsExcluded = interaction.IsExcluded,
            IsHidden = interaction.IsHidden,
            IsPinned = interaction.IsPinned
        };
    }
}
