namespace DreamGenClone.Web.Domain.RolePlay;

public enum InteractionCommand
{
    ToggleExcluded = 1,
    ToggleHidden = 2,
    TogglePinned = 3,
    Delete = 4,
    DeleteAndBelow = 5,
    MakeEdit = 6,
    Retry = 10,
    RetryWithModel = 11,
    RetryAs = 12,
    MakeLonger = 13,
    MakeShorter = 14,
    AskToRewrite = 15,
    ForkAbove = 20,
    ForkBelow = 21
}
