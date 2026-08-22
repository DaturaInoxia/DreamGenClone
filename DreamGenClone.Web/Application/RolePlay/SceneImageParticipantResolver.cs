using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Resolves which characters are actually present in a scene for image-prompt construction
/// (CR-006 P1). Replaces the preprocessor's naive name-substring matching + always-include-persona
/// behavior with an authoritative presence model grounded in the RP engine's own signals:
///   <see cref="RolePlayScenePresenceHelper.IsActorInScene"/> (location truth + line-of-sight),
///   <see cref="AdaptiveScenarioState.CharacterEncounterStates"/> (having-sex participation),
///   and explicit naming in the story moment text (only as a tie-breaker after the above).
///
/// The persona is only included when it actually participates — it is no longer always injected.
/// </summary>
public static class SceneImageParticipantResolver
{
    /// <summary>Presence classification for a resolved participant.</summary>
    public enum Presence
    {
        /// <summary>The actor of the interaction — the primary subject, always included.</summary>
        Actor,
        /// <summary>Confirmed present at the current scene location (truth match or line-of-sight).</summary>
        InScene,
        /// <summary>Actively participating in the current encounter (having-sex, confirmed or heuristic).</summary>
        InEncounter,
        /// <summary>Named in the story moment text but not confirmed present by the presence model.</summary>
        NamedInText,
        /// <summary>Present but not a subject — e.g. an observer who should not be in-frame.</summary>
        Observer
    }

    /// <summary>A resolved participant with its presence classification and the reason it was included.</summary>
    public sealed record Participant(string Name, Presence Presence, string Reason);

    /// <summary>
    /// Resolves the set of characters present for the given interaction, ordered by presence rank
    /// (actor first, then in-scene, then in-encounter, then named-in-text). The persona is only
    /// included when it actually participates.
    /// </summary>
    public static IReadOnlyList<Participant> ResolveParticipants(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState scenarioState,
        IReadOnlyList<Character>? characters)
    {
        var charactersByName = (characters ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToDictionary(c => c.Name!.Trim(), StringComparer.OrdinalIgnoreCase);

        var actorName = interaction.ActorName?.Trim();
        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();

        var results = new List<Participant>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Rank 1: the actor of the interaction — hard include.
        if (!string.IsNullOrWhiteSpace(actorName))
        {
            results.Add(new Participant(actorName, Presence.Actor, "Actor of the interaction"));
            seen.Add(actorName);
        }

        // Rank 2: present at the current scene location (authoritative presence model).
        foreach (var character in charactersByName.Values)
        {
            var name = character.Name!.Trim();
            if (seen.Contains(name)) continue;
            if (string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase)) continue;

            var inScene = RolePlayScenePresenceHelper.IsActorInScene(session, name);
            if (inScene == true)
            {
                results.Add(new Participant(name, Presence.InScene, "Present at the current scene location"));
                seen.Add(name);
            }
        }

        // Rank 3: actively participating in the current encounter (having-sex).
        foreach (var character in charactersByName.Values)
        {
            var name = character.Name!.Trim();
            if (seen.Contains(name)) continue;
            if (string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase)) continue;

            if (IsInEncounter(scenarioState, name))
            {
                results.Add(new Participant(name, Presence.InEncounter, "Actively participating in the current encounter"));
                seen.Add(name);
            }
        }

        // Rank 4: named in the story moment text (only after ranks 1-3).
        if (!string.IsNullOrWhiteSpace(interaction.Content))
        {
            foreach (var character in charactersByName.Values)
            {
                var name = character.Name!.Trim();
                if (seen.Contains(name)) continue;
                if (string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase)) continue;

                if (interaction.Content.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new Participant(name, Presence.NamedInText, "Named in the story moment text"));
                    seen.Add(name);
                }
            }
        }

        // The persona: only when it actually participates (actor, in-scene, in-encounter, or named).
        if (!seen.Contains(personaName))
        {
            var personaPresence = ResolvePersonaPresence(session, scenarioState, interaction, personaName);
            if (personaPresence is not null)
            {
                results.Add(new Participant(personaName, personaPresence.Value.Presence, personaPresence.Value.Reason));
            }
        }

        return results;
    }

    /// <summary>
    /// Resolves whether the persona participates in the scene. Returns null when the persona is
    /// not present (so it is excluded entirely — fixing the always-include bug).
    /// </summary>
    private static (Presence Presence, string Reason)? ResolvePersonaPresence(
        RolePlaySession session,
        AdaptiveScenarioState scenarioState,
        RolePlayInteraction interaction,
        string personaName)
    {
        var actorName = interaction.ActorName?.Trim();
        if (!string.IsNullOrWhiteSpace(actorName) && string.Equals(personaName, actorName, StringComparison.OrdinalIgnoreCase))
        {
            return (Presence.Actor, "Persona is the actor of the interaction");
        }

        var inScene = RolePlayScenePresenceHelper.IsActorInScene(session, personaName);
        if (inScene == true)
        {
            return (Presence.InScene, "Persona is present at the current scene location");
        }

        if (IsInEncounter(scenarioState, personaName))
        {
            return (Presence.InEncounter, "Persona is actively participating in the current encounter");
        }

        if (!string.IsNullOrWhiteSpace(interaction.Content)
            && interaction.Content.Contains(personaName, StringComparison.OrdinalIgnoreCase))
        {
            return (Presence.NamedInText, "Persona is named in the story moment text");
        }

        // Persona is not present — exclude it (fixes the always-include bug).
        return null;
    }

    private static bool IsInEncounter(AdaptiveScenarioState scenarioState, string characterName)
    {
        return scenarioState.CharacterEncounterStates.TryGetValue(characterName, out var state)
            && (state.IsHavingSexConfirmed || state.IsHavingSex);
    }
}