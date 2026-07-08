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
    /// </summary>
    /// <param name="session">The roleplay session containing adaptive state.</param>
    /// <param name="actorName">The actor's name to check.</param>
    /// <returns>
    ///   true if the actor is confirmed at the current scene location;
    ///   false if the actor is confirmed elsewhere;
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

        if (location is null || string.IsNullOrWhiteSpace(location.TrueLocation))
        {
            return null;
        }

        return string.Equals(location.TrueLocation.Trim(), currentSceneLocation.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
