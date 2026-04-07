using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class InteractionCommandService : IInteractionCommandService
{
    private readonly IRolePlayEngineService _engineService;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly ILogger<InteractionCommandService> _logger;

    public InteractionCommandService(
        IRolePlayEngineService engineService,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<InteractionCommandService> logger)
    {
        _engineService = engineService;
        _debugEventSink = debugEventSink;
        _logger = logger;
    }

    public async Task<bool> ToggleFlagAsync(
        RolePlaySession session,
        string interactionId,
        InteractionFlag flag,
        CancellationToken cancellationToken = default)
    {
        var interaction = session.Interactions.FirstOrDefault(i => i.Id == interactionId)
            ?? throw new ArgumentException($"Interaction {interactionId} not found in session {session.Id}.", nameof(interactionId));

        var newValue = flag switch
        {
            InteractionFlag.Excluded => interaction.IsExcluded = !interaction.IsExcluded,
            InteractionFlag.Hidden => interaction.IsHidden = !interaction.IsHidden,
            InteractionFlag.Pinned => interaction.IsPinned = !interaction.IsPinned,
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown flag.")
        };

        await _engineService.SaveSessionAsync(session, cancellationToken);
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            EventKind = "CommandExecuted",
            Severity = "Info",
            ActorName = interaction.ActorName,
            Summary = $"Toggled {flag} to {newValue}",
            MetadataJson = JsonSerializer.Serialize(new { flag, newValue, interactionId })
        }, cancellationToken);

        _logger.LogInformation(
            "Toggled {Flag} to {Value} on interaction {InteractionId} in session {SessionId}",
            flag, newValue, interactionId, session.Id);

        return newValue;
    }

    public async Task UpdateContentAsync(
        RolePlaySession session,
        string interactionId,
        string newContent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newContent))
        {
            throw new ArgumentException("Content cannot be empty.", nameof(newContent));
        }

        var interaction = session.Interactions.FirstOrDefault(i => i.Id == interactionId)
            ?? throw new ArgumentException($"Interaction {interactionId} not found in session {session.Id}.", nameof(interactionId));

        interaction.Content = newContent.Trim();

        await _engineService.SaveSessionAsync(session, cancellationToken);
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            EventKind = "CommandExecuted",
            Severity = "Info",
            ActorName = interaction.ActorName,
            Summary = "Interaction content updated",
            MetadataJson = JsonSerializer.Serialize(new { interactionId, contentLength = interaction.Content.Length })
        }, cancellationToken);

        _logger.LogInformation(
            "Updated content of interaction {InteractionId} in session {SessionId}",
            interactionId, session.Id);
    }

    public async Task DeleteInteractionAsync(
        RolePlaySession session,
        string interactionId,
        bool deleteBelow,
        CancellationToken cancellationToken = default)
    {
        var interaction = session.Interactions.FirstOrDefault(i => i.Id == interactionId)
            ?? throw new ArgumentException($"Interaction {interactionId} not found in session {session.Id}.", nameof(interactionId));

        // Resolve to original if this is an alternative
        var original = interaction.ParentInteractionId is not null
            ? session.Interactions.FirstOrDefault(i => i.Id == interaction.ParentInteractionId) ?? interaction
            : interaction;

        // Find the position index among originals
        var originals = session.Interactions.Where(i => i.ParentInteractionId is null).ToList();
        var targetIndex = originals.IndexOf(original);

        // Collect IDs to remove
        var idsToRemove = new HashSet<string>();

        if (deleteBelow)
        {
            // Remove target and all originals after it, plus all their alternatives
            for (var i = targetIndex; i < originals.Count; i++)
            {
                idsToRemove.Add(originals[i].Id);
            }
        }
        else
        {
            // Remove only the target original
            idsToRemove.Add(original.Id);
        }

        // Also collect all alternatives of originals being removed
        var allIdsToRemove = new HashSet<string>(idsToRemove);
        foreach (var alt in session.Interactions.Where(i => i.ParentInteractionId is not null && idsToRemove.Contains(i.ParentInteractionId)))
        {
            allIdsToRemove.Add(alt.Id);
        }

        var removedCount = session.Interactions.RemoveAll(i => allIdsToRemove.Contains(i.Id));

        await _engineService.SaveSessionAsync(session, cancellationToken);
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            InteractionId = interactionId,
            EventKind = "CommandExecuted",
            Severity = "Info",
            ActorName = interaction.ActorName,
            Summary = "Interaction delete command executed",
            MetadataJson = JsonSerializer.Serialize(new { interactionId, deleteBelow, removedCount })
        }, cancellationToken);

        _logger.LogInformation(
            "Deleted interaction {InteractionId} (deleteBelow={DeleteBelow}) from session {SessionId}, removed {Count} entries",
            interactionId, deleteBelow, session.Id, removedCount);
    }

    public async Task<int> NavigateAlternativeAsync(
        RolePlaySession session,
        string interactionId,
        int direction,
        CancellationToken cancellationToken = default)
    {
        var interaction = session.Interactions.FirstOrDefault(i => i.Id == interactionId)
            ?? throw new ArgumentException($"Interaction {interactionId} not found in session {session.Id}.", nameof(interactionId));

        // Resolve to original
        var original = interaction.ParentInteractionId is not null
            ? session.Interactions.FirstOrDefault(i => i.Id == interaction.ParentInteractionId) ?? interaction
            : interaction;

        // Count alternatives (0 = original, 1..N = alternatives)
        var maxIndex = session.Interactions
            .Where(i => i.ParentInteractionId == original.Id)
            .Select(i => i.AlternativeIndex)
            .DefaultIfEmpty(0)
            .Max();

        var newIndex = original.ActiveAlternativeIndex + direction;
        newIndex = Math.Clamp(newIndex, 0, maxIndex);

        original.ActiveAlternativeIndex = newIndex;

        await _engineService.SaveSessionAsync(session, cancellationToken);
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            InteractionId = original.Id,
            EventKind = "CommandExecuted",
            Severity = "Info",
            ActorName = original.ActorName,
            Summary = "Alternative navigation command executed",
            MetadataJson = JsonSerializer.Serialize(new { original.Id, direction, newIndex, maxIndex })
        }, cancellationToken);

        _logger.LogInformation(
            "Navigated to alternative {Index} of {Max} for interaction {InteractionId} in session {SessionId}",
            newIndex, maxIndex, original.Id, session.Id);

        return newIndex;
    }

    public async Task DeleteAlternativeAsync(
        RolePlaySession session,
        string interactionId,
        CancellationToken cancellationToken = default)
    {
        var interaction = session.Interactions.FirstOrDefault(i => i.Id == interactionId)
            ?? throw new ArgumentException($"Interaction {interactionId} not found in session {session.Id}.", nameof(interactionId));

        // Must be an alternative (has parent), not the original
        if (interaction.ParentInteractionId is null)
        {
            throw new InvalidOperationException("Cannot delete the original interaction as an alternative. Use DeleteInteractionAsync instead.");
        }

        var originalId = interaction.ParentInteractionId;
        var original = session.Interactions.FirstOrDefault(i => i.Id == originalId)
            ?? throw new InvalidOperationException($"Parent interaction {originalId} not found.");

        var deletedIndex = interaction.AlternativeIndex;

        // Remove the alternative
        session.Interactions.Remove(interaction);

        // Renumber remaining alternatives to be sequential
        var remainingAlternatives = session.Interactions
            .Where(i => i.ParentInteractionId == originalId)
            .OrderBy(i => i.AlternativeIndex)
            .ToList();

        for (var i = 0; i < remainingAlternatives.Count; i++)
        {
            remainingAlternatives[i].AlternativeIndex = i + 1;
        }

        // Adjust ActiveAlternativeIndex
        if (remainingAlternatives.Count == 0)
        {
            original.ActiveAlternativeIndex = 0;
        }
        else if (original.ActiveAlternativeIndex == deletedIndex)
        {
            // If we deleted the active one, show the previous or original
            original.ActiveAlternativeIndex = Math.Max(0, deletedIndex - 1);
            if (original.ActiveAlternativeIndex > remainingAlternatives.Count)
            {
                original.ActiveAlternativeIndex = remainingAlternatives.Count;
            }
        }
        else if (original.ActiveAlternativeIndex > deletedIndex)
        {
            // Shift down since we removed one before
            original.ActiveAlternativeIndex--;
        }

        await _engineService.SaveSessionAsync(session, cancellationToken);
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            InteractionId = originalId,
            EventKind = "CommandExecuted",
            Severity = "Info",
            ActorName = original.ActorName,
            Summary = "Alternative deleted",
            MetadataJson = JsonSerializer.Serialize(new { originalId, deletedIndex, remainingCount = remainingAlternatives.Count })
        }, cancellationToken);

        _logger.LogInformation(
            "Deleted alternative {DeletedIndex} for interaction {InteractionId} in session {SessionId}, {Count} alternatives remain",
            deletedIndex, originalId, session.Id, remainingAlternatives.Count);
    }
}
