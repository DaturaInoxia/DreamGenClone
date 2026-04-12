namespace DreamGenClone.Domain.RolePlay;

public sealed class FormulaConfigVersion
{
    public string FormulaVersionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ParameterPayload { get; set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
    public bool IsDefault { get; set; }
}
