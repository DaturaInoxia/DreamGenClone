namespace DreamGenClone.Application.StoryAnalysis;

public static class CharacterGenderCatalog
{
    public const string Female = "Female";
    public const string Male = "Male";
    public const string Unknown = "Unknown";

    public static readonly IReadOnlyList<string> ProfileGenders = [Female, Male];

    public static readonly IReadOnlyList<string> CharacterGenders = [Female, Male];

    public static string NormalizeForProfile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Unknown;
        }

        return NormalizeCore(value, Unknown);
    }

    public static string NormalizeForCharacter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Unknown;
        }

        return NormalizeCore(value, Unknown);
    }

    public static bool IsProfileApplicableToCharacter(string? profileGender, string? characterGender)
    {
        var normalizedProfile = NormalizeForProfile(profileGender);
        var normalizedCharacter = NormalizeForCharacter(characterGender);
        if (string.Equals(normalizedProfile, Unknown, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedCharacter, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(normalizedProfile, normalizedCharacter, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCore(string value, string fallback)
    {
        var trimmed = value.Trim();
        if (trimmed.Equals(Female, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Wife", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Woman", StringComparison.OrdinalIgnoreCase))
        {
            return Female;
        }

        if (trimmed.Equals(Male, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Mail", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Husband", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Man", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("OtherMan", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Other Man", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Bull", StringComparison.OrdinalIgnoreCase))
        {
            return Male;
        }

        if (trimmed.Equals(Unknown, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Unspecified", StringComparison.OrdinalIgnoreCase))
        {
            return Unknown;
        }

        return fallback;
    }
}