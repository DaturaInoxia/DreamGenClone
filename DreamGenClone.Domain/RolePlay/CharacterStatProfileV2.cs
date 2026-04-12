namespace DreamGenClone.Domain.RolePlay;

public sealed class CharacterStatProfileV2
{
    public string CharacterId { get; set; } = string.Empty;
    public int Desire { get; set; }
    public int Restraint { get; set; }
    public int Tension { get; set; }
    public int Connection { get; set; }
    public int Dominance { get; set; }
    public int Loyalty { get; set; }
    public int SelfRespect { get; set; }
    public DateTime SnapshotUtc { get; set; } = DateTime.UtcNow;
}
