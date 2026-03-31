using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.Scenarios;

public sealed class AdaptStoryRequest
{
    public string ParsedStoryId { get; set; } = string.Empty;

    public List<CharacterSubstitution> CharacterSubstitutions { get; set; } = [];

    public string? UserGuidance { get; set; }
}

public sealed class CharacterSubstitution
{
    public Guid TemplateId { get; set; }

    public string? TargetRole { get; set; }
}

public sealed class CharacterMapping
{
    public string OriginalName { get; set; } = string.Empty;

    public string SubstitutedName { get; set; } = string.Empty;

    public string? Role { get; set; }
}

public sealed class AdaptStoryResult
{
    public bool Success { get; set; }

    public Scenario? GeneratedScenario { get; set; }

    public string? SourceParsedStoryId { get; set; }

    public string? SourceStoryTitle { get; set; }

    public List<CharacterMapping> CharacterMappings { get; set; } = [];

    public string? AdaptationNotes { get; set; }

    public string? ErrorMessage { get; set; }
}
