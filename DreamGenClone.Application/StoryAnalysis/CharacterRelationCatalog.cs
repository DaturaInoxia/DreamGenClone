namespace DreamGenClone.Application.StoryAnalysis;

public static class CharacterRelationCatalog
{
    public const string PersonaTargetId = "__persona__";
    public const string PersonaDisplayLabel = "Persona";
    public const string PersonaTemplateTargetPrefix = "persona-template:";

    public static string? NormalizeTargetId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals(PersonaTargetId, StringComparison.OrdinalIgnoreCase))
        {
            return PersonaTargetId;
        }

        if (trimmed.StartsWith(PersonaTemplateTargetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var templateId = trimmed[PersonaTemplateTargetPrefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(templateId)
                ? null
                : PersonaTemplateTargetPrefix + templateId;
        }

        return trimmed;
    }

    public static bool IsPersonaTarget(string? value)
        => string.Equals(NormalizeTargetId(value), PersonaTargetId, StringComparison.OrdinalIgnoreCase);

    public static string CreatePersonaTemplateTargetId(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new ArgumentException("Template id is required.", nameof(templateId));
        }

        return PersonaTemplateTargetPrefix + templateId.Trim();
    }

    public static bool IsPersonaTemplateTarget(string? value)
        => TryGetPersonaTemplateId(value) is not null;

    public static string? TryGetPersonaTemplateId(string? value)
    {
        var normalized = NormalizeTargetId(value);
        if (string.IsNullOrWhiteSpace(normalized)
            || !normalized.StartsWith(PersonaTemplateTargetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var templateId = normalized[PersonaTemplateTargetPrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(templateId) ? null : templateId;
    }
}