namespace DreamGenClone.Web.Domain.RolePlay;

public enum PreferredTurnPosition
{
    Auto,
    First,
    Last
}

public sealed class CharacterTurnOverride
{
    public string CharacterName { get; set; } = string.Empty;
    public int? ResponsePriority { get; set; }
    public bool ParticipateInAutoContinue { get; set; } = true;
    public PreferredTurnPosition PreferredPosition { get; set; }
}
