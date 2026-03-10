namespace DreamGenClone.Domain.Templates;

public sealed class TemplateDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public TemplateType TemplateType { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? ImagePath { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
