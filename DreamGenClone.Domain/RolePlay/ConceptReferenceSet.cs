namespace DreamGenClone.Domain.RolePlay;

public sealed class ConceptReferenceSet
{
    public string ReferenceSetId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public string ScopeId { get; set; } = string.Empty;
    public List<string> ConceptIds { get; set; } = [];
    public DateTime EffectiveUtc { get; set; } = DateTime.UtcNow;
}
