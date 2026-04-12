namespace DreamGenClone.Domain.RolePlay;

public enum TransitionTriggerType
{
    Threshold = 0,
    InteractionCountGate = 1,
    Override = 2,
    Reset = 3
}

public enum TransparencyMode
{
    Hidden = 0,
    Directional = 1,
    Explicit = 2
}

public enum OverrideActorRole
{
    SessionOwner = 0,
    Operator = 1,
    Admin = 2,
    Unknown = 3
}
