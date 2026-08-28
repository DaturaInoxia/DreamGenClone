using System.Text.Json;
using System.Text.Json.Serialization;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// B-075: One character's four-direction option set from a steering generation response.
/// </summary>
public sealed class SteerCharacterOptionSet
{
    [JsonPropertyName("characterId")]
    public string CharacterId { get; set; } = string.Empty;

    [JsonPropertyName("characterName")]
    public string CharacterName { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("options")]
    public SteerDirectionOptions Options { get; set; } = new();
}

public sealed class SteerDirectionOptions
{
    [JsonPropertyName("away")]
    public string Away { get; set; } = string.Empty;

    [JsonPropertyName("neutral")]
    public string Neutral { get; set; } = string.Empty;

    [JsonPropertyName("towards")]
    public string Towards { get; set; } = string.Empty;

    [JsonPropertyName("hard")]
    public string Hard { get; set; } = string.Empty;
}

/// <summary>
/// B-075: Top-level all-character steering generation response.
/// </summary>
public sealed class SteerGenerationResponse
{
    [JsonPropertyName("characters")]
    public List<SteerCharacterOptionSet> Characters { get; set; } = new();
}

/// <summary>
/// B-075: Parser for the structured all-character steering generation response.
/// </summary>
public static class SteerGenerationParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Tries to parse a raw LLM response into all-character option sets.
    /// Returns null when the response cannot be parsed or contains zero characters.
    /// </summary>
    public static SteerGenerationResponse? TryParse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return null;

        // Extract JSON from possible markdown code fences.
        var json = ExtractJson(rawResponse);
        if (json is null) return null;

        try
        {
            var result = JsonSerializer.Deserialize<SteerGenerationResponse>(json, Options);
            if (result is null || result.Characters.Count == 0) return null;

            // Validate each character has all four directions.
            foreach (var c in result.Characters)
            {
                if (string.IsNullOrWhiteSpace(c.CharacterId)) return null;
                if (string.IsNullOrWhiteSpace(c.Options.Away) ||
                    string.IsNullOrWhiteSpace(c.Options.Neutral) ||
                    string.IsNullOrWhiteSpace(c.Options.Towards) ||
                    string.IsNullOrWhiteSpace(c.Options.Hard))
                    return null;
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractJson(string raw)
    {
        var trimmed = raw.Trim();

        // Strip ```json fences if present.
        if (trimmed.StartsWith("```"))
        {
            var endMarker = trimmed.IndexOf('\n');
            if (endMarker < 0) return null;
            var content = trimmed[(endMarker + 1)..].TrimEnd();
            if (content.EndsWith("```"))
                content = content[..^3].TrimEnd();
            return content.Trim();
        }

        return trimmed;
    }
}
