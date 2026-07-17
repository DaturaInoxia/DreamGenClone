using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Prompts;

/// <summary>
/// Resolves <see cref="ActorProfile"/> from <see cref="ContinueAsActor"/> + <see cref="PromptIntent"/>
/// at prompt-build time. Uses <see cref="RolePlayScenePresenceHelper"/> for NPC presence determination.
/// Fail-fast on unknown actor (Edge Case: Actor profile mismatch).
/// </summary>
public sealed class ActorProfileResolver
{
    /// <summary>
    /// Resolves the actor profile for the given continuation parameters.
    /// </summary>
    /// <param name="actor">The actor the user chose to continue as.</param>
    /// <param name="customActorName">Custom actor name (only relevant when actor == Custom).</param>
    /// <param name="intent">The prompt intent (Message, Narrative, Instruction).</param>
    /// <param name="session">The roleplay session.</param>
    /// <param name="roster">All scenario characters.</param>
    /// <returns>Resolved actor profile.</returns>
    /// <exception cref="InvalidOperationException">Thrown when actor is not found in the character roster.</exception>
    public ActorProfile Resolve(
        ContinueAsActor actor,
        string? customActorName,
        PromptIntent intent,
        RolePlaySession session,
        IReadOnlyList<ScenarioCharacter> roster)
    {
        // Narrative intent always maps to Narrative profile.
        if (intent == PromptIntent.Narrative)
        {
            return new ActorProfile
            {
                Kind = ActorProfileKind.Narrative,
                ActorName = "omniscient narrator",
                ActorRole = "narrator",
                PresentCharacterIds = roster.Select(c => c.Id).ToList(),
                AllCharacterIds = roster.Select(c => c.Id).ToList(),
            };
        }

        var allIds = roster.Select(c => c.Id).ToList();

        switch (actor)
        {
            case ContinueAsActor.You:
                var personaCharacter = roster.FirstOrDefault(c =>
                    string.Equals(c.Id, session.PersonaCharacterId, StringComparison.OrdinalIgnoreCase));
                return new ActorProfile
                {
                    Kind = ActorProfileKind.Player,
                    ActorName = session.PersonaName,
                    ActorRole = session.PersonaRole,
                    PresentCharacterIds = allIds,
                    AllCharacterIds = allIds,
                };

            case ContinueAsActor.Npc:
                return ResolveNpcProfile(session, roster, allIds);

            case ContinueAsActor.Custom:
                var customName = customActorName?.Trim();
                if (string.IsNullOrWhiteSpace(customName))
                {
                    throw new InvalidOperationException(
                        $"ActorProfileResolver: Custom actor requested but customActorName is null/empty for session '{session.Id}'.");
                }
                return new ActorProfile
                {
                    Kind = ActorProfileKind.Custom,
                    ActorName = customName,
                    ActorRole = "custom",
                    PresentCharacterIds = allIds,
                    AllCharacterIds = allIds,
                };

            default:
                throw new InvalidOperationException(
                    $"ActorProfileResolver: Unknown ContinueAsActor value '{actor}' for session '{session.Id}'.");
        }
    }

    private ActorProfile ResolveNpcProfile(
        RolePlaySession session,
        IReadOnlyList<ScenarioCharacter> roster,
        IReadOnlyList<string> allIds)
    {
        // Get the NPC actor name from the session's current turn state or last selected NPC.
        // The caller provides this via the actor name resolved from the session.
        var actorName = session.CurrentTurnState == TurnState.Any
            ? ResolveOpeningNpcName(session, roster)
            : ResolveNpcNameFromSession(session);

        var sceneCharacter = roster.FirstOrDefault(c =>
            string.Equals(c.Name, actorName, StringComparison.OrdinalIgnoreCase));

        if (sceneCharacter is null)
        {
            throw new InvalidOperationException(
                $"ActorProfileResolver: NPC actor '{actorName}' not found in character roster for session '{session.Id}'. " +
                $"Roster: {string.Join(", ", roster.Select(c => $"{c.Name}({c.Id})"))}");
        }

        var inScene = RolePlayScenePresenceHelper.IsActorInScene(session, actorName) ?? false;
        var presentIds = inScene
            ? allIds
            : allIds.Where(id =>
                string.Equals(id, sceneCharacter.Id, StringComparison.OrdinalIgnoreCase)).ToList();

        return new ActorProfile
        {
            Kind = inScene ? ActorProfileKind.NpcPresent : ActorProfileKind.NpcNonPresent,
            ActorName = actorName,
            ActorRole = sceneCharacter.Role,
            PresentCharacterIds = presentIds,
            AllCharacterIds = allIds,
        };
    }

    private static string ResolveNpcNameFromSession(RolePlaySession session)
    {
        // Return the most recently acting NPC name from interactions.
        // Skip Narrative/System interactions — they are not character actors.
        for (int i = session.Interactions.Count - 1; i >= 0; i--)
        {
            var interaction = session.Interactions[i];
            if (!string.IsNullOrWhiteSpace(interaction.ActorName) &&
                !string.Equals(interaction.ActorName, "Narrative", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(interaction.ActorName, session.PersonaName, StringComparison.OrdinalIgnoreCase) &&
                interaction.InteractionType != Domain.RolePlay.InteractionType.System)
            {
                return interaction.ActorName;
            }
        }

        throw new InvalidOperationException(
            $"ActorProfileResolver: Cannot resolve NPC actor name for session '{session.Id}'. " +
            "No NPC interactions found in session history.");
    }

    /// <summary>
    /// Resolves the NPC actor name for the opening turn (no prior interactions exist).
    /// Finds the spouse character — the NPC whose RelationTargetId points to the persona.
    /// Never falls back to <see cref="RolePlaySession.PersonaName"/> — the persona is not an NPC.
    /// </summary>
    private static string ResolveOpeningNpcName(
        RolePlaySession session, IReadOnlyList<ScenarioCharacter> roster)
    {
        var personaId = session.PersonaCharacterId;

        // Find the spouse: NPC whose RelationTargetId points to the persona's character ID.
        if (!string.IsNullOrWhiteSpace(personaId))
        {
            var spouse = roster.FirstOrDefault(c =>
                string.Equals(c.Id, personaId, StringComparison.OrdinalIgnoreCase) == false &&
                !string.Equals(c.Name, session.PersonaName, StringComparison.OrdinalIgnoreCase));
            if (spouse is not null)
                return spouse.Name;
        }

        // Fallback: first character that isn't the persona.
        var firstNpc = roster.FirstOrDefault(c =>
            !string.Equals(c.Name, session.PersonaName, StringComparison.OrdinalIgnoreCase));
        if (firstNpc is not null)
            return firstNpc.Name;

        // Absolute last resort — should not happen in valid scenarios.
        throw new InvalidOperationException(
            $"ActorProfileResolver: Cannot resolve opening NPC actor name for session '{session.Id}'. " +
            $"Roster contains no non-persona characters. Roster: {string.Join(", ", roster.Select(c => c.Name))}");
    }
}
