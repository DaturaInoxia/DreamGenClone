namespace DreamGenClone.Domain.Templates;

public sealed class TemplateDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public TemplateType TemplateType { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Gender { get; set; } = "Unknown";

    public string Role { get; set; } = "Unknown";

    public string? RelationTargetTemplateId { get; set; }

    public Dictionary<string, int> BaseStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? ImagePath { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
