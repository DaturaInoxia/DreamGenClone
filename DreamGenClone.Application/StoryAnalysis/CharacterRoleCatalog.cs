namespace DreamGenClone.Application.StoryAnalysis;

public static class CharacterRoleCatalog
{
    public const string Wife = "Wife";
    public const string Husband = "Husband";
    public const string TheOtherMan = "The Other Man";
    public const string BackgroundCharacters = "Background Characters";
    public const string Unknown = "Unknown";

    public static readonly IReadOnlyList<string> ProfileRoleOptions = [Wife, Husband, TheOtherMan];

    public static readonly IReadOnlyList<string> CharacterRoleOptions = [Wife, Husband, TheOtherMan, BackgroundCharacters];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Unknown;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals(Wife, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Female Lead", StringComparison.OrdinalIgnoreCase))
        {
            return Wife;
        }

        if (trimmed.Equals(Husband, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Male Lead", StringComparison.OrdinalIgnoreCase))
        {
            return Husband;
        }

        if (trimmed.Equals(TheOtherMan, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Other Man", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("The Other Guy", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Bull", StringComparison.OrdinalIgnoreCase))
        {
            return TheOtherMan;
        }

        if (trimmed.Equals(BackgroundCharacters, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Background Character", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Background", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Extra", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Extras", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("NPC", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("NPCs", StringComparison.OrdinalIgnoreCase))
        {
            return BackgroundCharacters;
        }

        if (trimmed.Equals(Unknown, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Unspecified", StringComparison.OrdinalIgnoreCase))
        {
            return Unknown;
        }

        return trimmed;
    }

    public static bool IsProfileApplicableToCharacter(string? profileRole, string? characterRole)
    {
        var normalizedProfile = Normalize(profileRole);
        var normalizedCharacter = Normalize(characterRole);
        if (string.Equals(normalizedProfile, Unknown, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedCharacter, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(normalizedProfile, normalizedCharacter, StringComparison.OrdinalIgnoreCase);
    }
}