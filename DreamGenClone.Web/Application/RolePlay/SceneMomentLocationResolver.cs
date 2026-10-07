using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The D14 resolution rule: prefix-match the moment's composite <c>location</c> string against
/// <see cref="Scenario.Locations"/>' names; the <c>"- &lt;spot&gt;"</c> suffix is a spot suggestion, not a second
/// container. A non-match is a legitimate "this place is new" branch (ad-hoc), never a value fallback.
/// </summary>
public static class SceneMomentLocationResolver
{
    /// <summary>
    /// Prefix-matches <paramref name="momentLocation"/> against the scenario locations, preferring the LONGEST matching
    /// name (a moment in "Husband and Wife Trailer — Shared Private Space" must not match the shorter "Husband and Wife
    /// Trailer" if a longer name exists). Returns <see cref="SceneMomentLocationMatch.Matched"/> false when nothing
    /// matches or when the moment carries no location text.
    /// </summary>
    public static SceneMomentLocationMatch Match(
        string? momentLocation,
        IReadOnlyList<Location> scenarioLocations)
    {
        ArgumentNullException.ThrowIfNull(scenarioLocations);

        if (string.IsNullOrWhiteSpace(momentLocation))
        {
            return new SceneMomentLocationMatch(Matched: false, Location: null, SpotSuggestion: null);
        }

        Location? best = null;
        foreach (var location in scenarioLocations)
        {
            if (string.IsNullOrWhiteSpace(location.Name))
            {
                continue;
            }

            if (!momentLocation.StartsWith(location.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (best is null || location.Name.Length > best.Name!.Length)
            {
                best = location;
            }
        }

        if (best is null)
        {
            return new SceneMomentLocationMatch(Matched: false, Location: null, SpotSuggestion: null);
        }

        var remainder = momentLocation[best.Name.Length..].Trim();
        remainder = remainder.TrimStart('-', '—', ':', '–');
        remainder = remainder.Trim();

        return new SceneMomentLocationMatch(
            Matched: true,
            Location: best,
            SpotSuggestion: string.IsNullOrWhiteSpace(remainder) ? null : remainder);
    }
}

/// <summary>The outcome of the D14 prefix-match. A suggestion the operator always confirms, never a lock.</summary>
public sealed record SceneMomentLocationMatch(
    bool Matched,
    Location? Location,
    string? SpotSuggestion);

/// <summary>
/// The D4 place-only seed: a rendering-oriented description of the PLACE, with the action, people and nudity stripped.
///
/// <para>
/// A location is a place, not the scene happening in it: the cast and anything they wear or bring belong to the
/// COMPOSITION, and a backdrop text that names a character or describes their shorts is wrong for every other moment
/// at that place. So the cast contributes nothing, objects a character is wearing are removed (matched against their
/// own recorded clothing, not guessed), and character names are scrubbed out of the place prose.
/// </para>
///
/// <para>
/// Everything removed is REPORTED rather than silently dropped, so the operator can see what was left out and add
/// anything back by hand.
/// </para>
/// </summary>
public static class SceneMomentPlaceSeedBuilder
{
    public static SceneMomentPlaceSeed Build(SceneMomentFrozenStateContract frozenState)
    {
        ArgumentNullException.ThrowIfNull(frozenState);

        var characters = frozenState.Characters ?? [];
        var names = characters
            .Select(character => character.Name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var clothingBlob = " " + string.Join(" ", characters.Select(character => Normalize(character.Clothing))) + " ";
        var worn = new List<string>();
        var fixtures = new List<string>();
        foreach (var item in frozenState.Objects ?? [])
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var trimmed = item.Trim();
            if (IsWorn(trimmed, clothingBlob))
            {
                worn.Add(trimmed);
                continue;
            }

            if (!fixtures.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                fixtures.Add(trimmed);
            }
        }

        var lines = new List<string>();
        var environment = ScrubNames(frozenState.Environment, names);
        if (!string.IsNullOrWhiteSpace(environment))
            lines.Add(environment);
        if (!string.IsNullOrWhiteSpace(frozenState.TimeOfDay))
            lines.Add($"Time of day: {frozenState.TimeOfDay.Trim()}");
        if (!string.IsNullOrWhiteSpace(frozenState.Lighting))
            lines.Add($"Lighting: {ScrubNames(frozenState.Lighting, names)}");

        // The cast's own positions are deliberately absent: people are added to the scene, not to the place.
        if (fixtures.Count > 0)
            lines.Add($"Objects: {string.Join(", ", fixtures)}");

        return new SceneMomentPlaceSeed(string.Join("\n", lines), worn, names);
    }

    /// <summary>
    /// Whether an object is something a character is WEARING, decided from that character's own recorded clothing —
    /// which is the only part of "what they bring" the enrichment records structurally. "tank top" and "cutoffs" are
    /// removed because they appear in Becky's clothing; "clothesline" is kept because nothing wears it.
    /// </summary>
    private static bool IsWorn(string item, string clothingBlob)
    {
        var normalized = Normalize(item);
        return normalized.Length > 0 && clothingBlob.Contains($" {normalized} ", StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.ToLowerInvariant();
        var builder = new System.Text.StringBuilder(lowered.Length);
        foreach (var character in lowered)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Removes character names from the place prose: "separating Becky and Dean's open slider" becomes "separating the
    /// open slider". A possessive group collapses to "the"; a bare name becomes "they". The text stays the operator's
    /// to edit, and the names removed are reported.
    /// </summary>
    private static string ScrubNames(string? text, IReadOnlyList<string> names)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text.Trim();
        if (names.Count == 0)
        {
            return result;
        }

        var alternation = string.Join("|", names.Select(System.Text.RegularExpressions.Regex.Escape));
        result = System.Text.RegularExpressions.Regex.Replace(
            result, $@"\b(?:{alternation})(?:\s+and\s+(?:{alternation}))*(?:'s|’s)\b", "the",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(
            result, $@"\b(?:{alternation})\b", "they",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ").Trim();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Deserializes the frozen-state contract, used by the bind service when only the JSON is in hand.</summary>
    public static SceneMomentFrozenStateContract? TryReadFrozenState(string? frozenStateContractJson)
    {
        if (string.IsNullOrWhiteSpace(frozenStateContractJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SceneMomentFrozenStateContract>(frozenStateContractJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The place-only seed, plus what was deliberately left out so nothing disappears silently: objects a character was
/// wearing, and the character names scrubbed from the place prose.
/// </summary>
public sealed record SceneMomentPlaceSeed(
    string Description,
    IReadOnlyList<string> WornObjectsRemoved,
    IReadOnlyList<string> CharacterNamesRemoved);
