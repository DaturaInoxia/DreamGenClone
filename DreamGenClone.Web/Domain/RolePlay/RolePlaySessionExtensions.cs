namespace DreamGenClone.Web.Domain.RolePlay;

public static class RolePlaySessionExtensions
{
    /// <summary>
    /// Returns true when the given actor name or ID matches the session's persona character.
    /// Checks PersonaCharacterId first (stable), falls back to PersonaName.
    /// </summary>
    public static bool IsPersonaActor(this RolePlaySession session, string? actorNameOrId)
    {
        if (string.IsNullOrWhiteSpace(actorNameOrId)) return false;

        // Primary: match by PersonaCharacterId (survives character name changes)
        if (!string.IsNullOrWhiteSpace(session.PersonaCharacterId)
            && string.Equals(actorNameOrId.Trim(), session.PersonaCharacterId.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        // Fallback: match by PersonaName (exclude "You" to avoid false positives)
        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        if (string.Equals(personaName, "You", StringComparison.OrdinalIgnoreCase))
            return false;

        return string.Equals(actorNameOrId.Trim(), personaName, StringComparison.OrdinalIgnoreCase);
    }

    public static CharacterPerspectiveMode ResolvePerspectiveMode(this RolePlaySession session, ContinueAsActor actor, string actorName)
    {
        if (actor == ContinueAsActor.You || string.Equals(actorName, session.PersonaName, StringComparison.OrdinalIgnoreCase))
        {
            return CharacterPerspectiveMode.FirstPersonInternalMonologue;
        }

        return ResolveCharacterPerspectiveMode(session, actorName);
    }

    public static CharacterPerspectiveMode ResolvePerspectiveMode(this RolePlaySession session, InteractionType interactionType, string actorName)
    {
        if (interactionType == InteractionType.User || string.Equals(actorName, session.PersonaName, StringComparison.OrdinalIgnoreCase))
        {
            return CharacterPerspectiveMode.FirstPersonInternalMonologue;
        }

        return ResolveCharacterPerspectiveMode(session, actorName);
    }

    private static CharacterPerspectiveMode ResolveCharacterPerspectiveMode(RolePlaySession session, string actorName)
    {
        if (!string.IsNullOrWhiteSpace(actorName))
        {
            var configured = session.CharacterPerspectives.FirstOrDefault(x =>
                string.Equals(x.CharacterName, actorName, StringComparison.OrdinalIgnoreCase));
            if (configured is not null)
            {
                return Enum.IsDefined(configured.PerspectiveMode)
                    ? configured.PerspectiveMode
                    : CharacterPerspectiveMode.ThirdPersonExternalOnly;
            }
        }

        return CharacterPerspectiveMode.ThirdPersonExternalOnly;
    }

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
