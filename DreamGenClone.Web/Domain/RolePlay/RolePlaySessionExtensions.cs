namespace DreamGenClone.Web.Domain.RolePlay;

public static class RolePlaySessionExtensions
{
    /// <summary>
    /// Returns a filtered, ordered list of interactions for AI context building:
    /// 1. Selects only original interactions (ParentInteractionId == null)
    /// 2. For each original, resolves the active alternative (or self if index 0)
    /// 3. Excludes interactions with IsExcluded == true
    /// </summary>
    public static List<RolePlayInteraction> GetContextView(this RolePlaySession session)
    {
        var originals = session.Interactions
            .Where(i => i.ParentInteractionId is null)
            .ToList();

        var result = new List<RolePlayInteraction>(originals.Count);

        foreach (var original in originals)
        {
            var active = ResolveActiveAlternative(session, original);

            if (!active.IsExcluded)
            {
                result.Add(active);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a filtered list for UI display (excludes hidden interactions).
    /// </summary>
    public static List<RolePlayInteraction> GetDisplayView(this RolePlaySession session)
    {
        var originals = session.Interactions
            .Where(i => i.ParentInteractionId is null)
            .ToList();

        var result = new List<RolePlayInteraction>(originals.Count);

        foreach (var original in originals)
        {
            var active = ResolveActiveAlternative(session, original);

            if (!active.IsHidden)
            {
                result.Add(active);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves the currently active alternative for an original interaction.
    /// Returns the original itself if ActiveAlternativeIndex is 0 or no matching alternative exists.
    /// </summary>
    public static RolePlayInteraction ResolveActiveAlternative(this RolePlaySession session, RolePlayInteraction original)
    {
        if (original.ActiveAlternativeIndex == 0)
        {
            return original;
        }

        var alternative = session.Interactions.FirstOrDefault(i =>
            i.ParentInteractionId == original.Id &&
            i.AlternativeIndex == original.ActiveAlternativeIndex);

        return alternative ?? original;
    }

    /// <summary>
    /// Gets all sibling alternatives (including the original at index 0) for an interaction.
    /// </summary>
    public static List<RolePlayInteraction> GetAlternatives(this RolePlaySession session, string originalInteractionId)
    {
        var original = session.Interactions.FirstOrDefault(i => i.Id == originalInteractionId && i.ParentInteractionId is null);
        if (original is null)
        {
            return [];
        }

        var alternatives = session.Interactions
            .Where(i => i.ParentInteractionId == originalInteractionId)
            .OrderBy(i => i.AlternativeIndex)
            .ToList();

        alternatives.Insert(0, original);
        return alternatives;
    }

    /// <summary>
    /// Resolves an interaction to its original (parent). If the interaction is already an original, returns it.
    /// </summary>
    public static RolePlayInteraction? ResolveToOriginal(this RolePlaySession session, string interactionId)
    {
        var interaction = session.Interactions.FirstOrDefault(i => i.Id == interactionId);
        if (interaction is null)
        {
            return null;
        }

        if (interaction.ParentInteractionId is null)
        {
            return interaction;
        }

        return session.Interactions.FirstOrDefault(i => i.Id == interaction.ParentInteractionId);
    }
}
