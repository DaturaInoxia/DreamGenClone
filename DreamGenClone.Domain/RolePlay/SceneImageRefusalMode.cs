namespace DreamGenClone.Domain.RolePlay;

public enum SceneImageRefusalMode
{
    None = 0,
    EmptyOutput = 1,
    PolicyError = 2
}

public static class SceneImageRefusalMessage
{
    public static string ForUser(string modelIdentifier, string providerName, SceneImageRefusalMode mode)
    {
        return mode switch
        {
            SceneImageRefusalMode.EmptyOutput =>
                $"Model '{modelIdentifier}' on provider '{providerName}' returned no image — this often means the model refused this content. Pick a different model and retry.",
            SceneImageRefusalMode.PolicyError =>
                $"Model '{modelIdentifier}' on provider '{providerName}' was blocked by the provider's content policy. Pick a different model and retry.",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown scene image refusal mode.")
        };
    }
}
