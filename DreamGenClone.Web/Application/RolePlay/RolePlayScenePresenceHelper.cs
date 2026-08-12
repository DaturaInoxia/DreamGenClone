using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Shared static helper for resolving whether an actor is present at the current scene location.
/// Extracted from <see cref="RolePlayEngineService.IsActorInCurrentScene"/> to enable reuse by
/// both the engine service and prompt injection context construction without duplicating logic.
/// 
/// Returns a tri-state value:
///   true  — actor is confirmed in the current scene location
///   false — actor is confirmed NOT in the current scene location
///   null  — unknown (location services off, scene location absent, or actor's truth state not tracked)
/// </summary>
public static class RolePlayScenePresenceHelper
{
    /// <summary>
    /// Determines whether the specified actor is in the current scene location.
    /// 
    /// B-080: presence is now decided by two signals. The primary signal is the
    /// authoritative truth match (<see cref="CharacterLocationState.TrueLocation"/> ==
    /// <see cref="AdaptiveScenarioState.CurrentSceneLocation"/>). The secondary signal uses
    /// <see cref="CharacterLocationPerceptionState"/>: an actor who has line-of-sight to (or is
    /// in proximity with) any character who IS at the current scene location is treated as
    /// present — a character who can see the scene can react to it even if their recorded
    /// TrueLocation differs from CurrentSceneLocation (stale or wrong background location
    /// detection). This prevents the "OtherMan solo turn" bug where the Wife (100% line-of-sight
    /// and proximity) was dropped from actor selection because her TrueLocation did not exactly
    /// match a mis-detected CurrentSceneLocation.
    /// </summary>
    /// <param name="session">The roleplay session containing adaptive state.</param>
    /// <param name="actorName">The actor's name to check.</param>
    /// <returns>
    ///   true if the actor is confirmed at the current scene location (truth match, or
    ///   line-of-sight/proximity to the scene);
    ///   false if the actor is confirmed elsewhere and does not observe the scene;
    ///   null if location data is unavailable or the actor's location is unknown.
    /// </returns>
    public static bool? IsActorInScene(RolePlaySession session, string actorName)
    {
        var currentSceneLocation = session.AdaptiveState.CurrentSceneLocation;

        if (string.IsNullOrWhiteSpace(currentSceneLocation))
        {
            return null;
        }

        var location = session.AdaptiveState.CharacterLocations.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(x.CharacterId)
            && string.Equals(x.CharacterId, actorName, StringComparison.OrdinalIgnoreCase));

        // Primary signal: authoritative truth match with the current scene location.
        if (location is not null
            && !string.IsNullOrWhiteSpace(location.TrueLocation)
            && string.Equals(location.TrueLocation.Trim(), currentSceneLocation.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Secondary signal: the actor observes the current scene. They have line-of-sight to (or
        // are in proximity with) any character who is authoritatively at the current scene
        // location. A perceiver may observe a scene they are not authoritatively placed in.
        var inSceneCharacterIds = session.AdaptiveState.CharacterLocations
            .Where(x => !string.IsNullOrWhiteSpace(x.CharacterId)
                && !string.IsNullOrWhiteSpace(x.TrueLocation)
                && string.Equals(x.TrueLocation.Trim(), currentSceneLocation.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(x => x.CharacterId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var observesCurrentScene = session.AdaptiveState.CharacterLocationPerceptions.Any(p =>
            (p.IsInProximity || p.HasLineOfSight)
            && (
                // Actor perceives (sees/is near) an in-scene character
                (string.Equals(p.ObserverCharacterId, actorName, StringComparison.OrdinalIgnoreCase)
                    && inSceneCharacterIds.Contains(p.TargetCharacterId))
                ||
                // Actor is perceived (seen/approached) by an in-scene character
                (string.Equals(p.TargetCharacterId, actorName, StringComparison.OrdinalIgnoreCase)
                    && inSceneCharacterIds.Contains(p.ObserverCharacterId))
            ));

        if (observesCurrentScene)
        {
            return true;
        }

        if (location is null || string.IsNullOrWhiteSpace(location.TrueLocation))
        {
            return null;
        }

        return false;
    }
}
