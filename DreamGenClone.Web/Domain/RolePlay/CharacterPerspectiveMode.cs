namespace DreamGenClone.Web.Domain.RolePlay;

public enum CharacterPerspectiveMode
{
    FirstPersonInternalMonologue = 1,
    FirstPersonExternalOnly = 2,
    ThirdPersonLimited = 3,
    ThirdPersonExternalOnly = 4
}

public sealed class CharacterPerspectiveModeDefinition
{
    public CharacterPerspectiveMode Mode { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Example { get; init; } = string.Empty;

    public string RuleOfThumb { get; init; } = string.Empty;
}

public static class CharacterPerspectiveModes
{
    public static IReadOnlyList<CharacterPerspectiveModeDefinition> All { get; } =
    [
        new CharacterPerspectiveModeDefinition
        {
            Mode = CharacterPerspectiveMode.FirstPersonInternalMonologue,
            DisplayName = "First Person with Internal Monologue",
            Description = "Uses 'I' and allows direct access to the character's private thoughts, feelings, urges, and bodily sensations.",
            Example = "I smiled at him, but my pulse kicked harder than I wanted to admit. I should have stepped back. Instead, I stayed where I was.",
            RuleOfThumb = "Use this for the most intimate POV when the character's inner conflict matters as much as their dialogue."
        },
        new CharacterPerspectiveModeDefinition
        {
            Mode = CharacterPerspectiveMode.FirstPersonExternalOnly,
            DisplayName = "First Person External Only",
            Description = "Uses 'I' but stays on what the character says, does, notices, and physically experiences without directly explaining their inner reasoning.",
            Example = "I stayed close to him and felt the heat rise under my skin, even though I kept my expression steady.",
            RuleOfThumb = "Use this when you want first-person immediacy without heavy introspection."
        },
        new CharacterPerspectiveModeDefinition
        {
            Mode = CharacterPerspectiveMode.ThirdPersonLimited,
            DisplayName = "Third Person Limited",
            Description = "Uses the character's name or pronouns while staying anchored to that one character's inner experience and interpretation of the scene.",
            Example = "Becky knew she should have stepped back, but the thrill of staying close kept her rooted in place.",
            RuleOfThumb = "Use this when you want internal depth without switching to 'I'."
        },
        new CharacterPerspectiveModeDefinition
        {
            Mode = CharacterPerspectiveMode.ThirdPersonExternalOnly,
            DisplayName = "Third Person External Only",
            Description = "Uses the character's name or pronouns and only includes observable dialogue, actions, body language, and externally visible details.",
            Example = "Becky stayed close instead of stepping back, her expression steady even as color rose in her cheeks.",
            RuleOfThumb = "Use this for cinematic, ambiguous, or multi-character scenes where private thoughts would clutter the flow."
        }
    ];

    public static CharacterPerspectiveModeDefinition Get(CharacterPerspectiveMode mode)
        => All.First(x => x.Mode == mode);
}