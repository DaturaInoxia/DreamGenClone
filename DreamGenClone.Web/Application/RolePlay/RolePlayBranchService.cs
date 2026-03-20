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
            source.PersonaName, source.PersonaDescription, source.PersonaTemplateId,
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

    private static RolePlayInteraction CloneInteraction(RolePlayInteraction interaction)
    {
        return new RolePlayInteraction
        {
            InteractionType = interaction.InteractionType,
            ActorName = interaction.ActorName,
            Content = interaction.Content,
            CreatedAt = interaction.CreatedAt
        };
    }
}
