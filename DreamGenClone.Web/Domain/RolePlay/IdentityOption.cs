namespace DreamGenClone.Web.Domain.RolePlay;

public enum IdentityOptionSource
{
    SceneCharacter = 1,
    Persona = 2,
    CustomCharacter = 3
}

public sealed class IdentityOption
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public IdentityOptionSource SourceType { get; set; }

    public ContinueAsActor Actor { get; set; }

    public bool IsAvailable { get; set; } = true;

    public string? AvailabilityReason { get; set; }
}
