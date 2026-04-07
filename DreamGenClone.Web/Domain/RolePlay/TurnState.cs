namespace DreamGenClone.Web.Domain.RolePlay;

/// <summary>
/// Indicates whose turn it is in a TakeTurns behavior mode session.
/// </summary>
public enum TurnState
{
    /// <summary>No turn constraint — any actor may continue.</summary>
    Any = 0,

    /// <summary>The user (POV persona) should take their turn next.</summary>
    UserTurn = 1,

    /// <summary>NPCs/characters should continue the scene.</summary>
    NpcTurn = 2
}
