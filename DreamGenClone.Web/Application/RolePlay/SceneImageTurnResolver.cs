using DreamGenClone.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolves an interaction id to its full-turn context (CR-006 P2). A turn is one user submission
/// cycle recorded in <c>RolePlayV2Turns</c> with <c>OutputInteractionIds</c> (all interactions
/// generated in that turn). Given a selected interaction, this finds its turn and loads all sibling
/// interactions so the image prompt can be built from the whole turn (including the Narrative
/// omniscient synthesis) rather than a single actor's slice.
///
/// Fallback: when no turn row exists (legacy data), returns the single interaction plus nearby
/// Narrative interactions in a small window.
/// </summary>
public sealed class SceneImageTurnResolver
{
    private readonly IRolePlayStateRepository _stateRepository;

    public SceneImageTurnResolver(IRolePlayStateRepository stateRepository)
    {
        _stateRepository = stateRepository;
    }

    /// <summary>
    /// Resolves the full-turn context for the given interaction. The session's interactions are
    /// used as the source of sibling interactions; the turn row (if any) provides the authoritative
    /// membership via <c>OutputInteractionIds</c>.
    /// </summary>
    public async Task<FullTurnContext> ResolveAsync(
        RolePlaySession session,
        string interactionId,
        CancellationToken cancellationToken = default)
    {
        var selected = session.Interactions.FirstOrDefault(x => string.Equals(x.Id, interactionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Interaction '{interactionId}' was not found in session '{session.Id}'.");

        // Find the turn that produced this interaction (match InputInteractionId or membership in OutputInteractionIds).
        var turns = await _stateRepository.LoadTurnsAsync(session.Id, take: 200, cancellationToken);
        var turn = turns.FirstOrDefault(t =>
            string.Equals(t.InputInteractionId, interactionId, StringComparison.OrdinalIgnoreCase)
            || t.OutputInteractionIds.Any(id => string.Equals(id, interactionId, StringComparison.OrdinalIgnoreCase)));

        if (turn is not null)
        {
            var turnInteractionIds = turn.OutputInteractionIds
                .Concat(turn.InputInteractionId is null ? [] : [turn.InputInteractionId])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var interactions = session.Interactions
                .Where(x => turnInteractionIds.Contains(x.Id))
                .OrderBy(x => x.CreatedAt)
                .ToList();

            // If the turn's output ids don't resolve to any loaded interactions (e.g. stale ids),
            // fall back to the selected interaction alone.
            if (interactions.Count == 0)
            {
                interactions = [selected];
            }

            return new FullTurnContext
            {
                Turn = turn,
                Interactions = interactions,
                SelectedInteraction = selected,
                NarrativeInteraction = interactions.FirstOrDefault(x => IsNarrative(x))
            };
        }

        // Legacy fallback: the single interaction + nearby Narrative interactions in a small window.
        var fallback = BuildLegacyFallback(session, selected);
        return fallback;
    }

    private static FullTurnContext BuildLegacyFallback(RolePlaySession session, RolePlayInteraction selected)
    {
        var index = session.Interactions.IndexOf(selected);
        if (index < 0)
        {
            return new FullTurnContext
            {
                Interactions = [selected],
                SelectedInteraction = selected,
                NarrativeInteraction = null
            };
        }

        // Window of up to 3 interactions before and after the selected one.
        var start = Math.Max(0, index - 3);
        var end = Math.Min(session.Interactions.Count - 1, index + 3);
        var window = session.Interactions.Skip(start).Take(end - start + 1).ToList();

        return new FullTurnContext
        {
            Interactions = window,
            SelectedInteraction = selected,
            NarrativeInteraction = window.FirstOrDefault(x => IsNarrative(x))
        };
    }

    private static bool IsNarrative(RolePlayInteraction interaction)
        => string.Equals(interaction.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase);
}