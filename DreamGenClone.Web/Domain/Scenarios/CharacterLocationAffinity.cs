namespace DreamGenClone.Web.Domain.Scenarios;

public enum AffinityType
{
    None,
    Preferred,
    Required,
    Excluded
}

public sealed class CharacterLocationAffinity
{
    public string LocationName { get; set; } = string.Empty;
    public AffinityType AffinityType { get; set; }
    public DreamGenClone.Domain.RolePlay.TimeOfDay? TimeOfDay { get; set; }
}
