using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The prompt and response schema for reading a LOCATION image: what is in it, and where each part sits in the frame.
/// </summary>
/// <remarks>
/// <para>
/// A sibling of <c>SceneImageDescriptionPromptBuilder</c>, which describes a scene for an edit. This one exists because
/// a location image is asked a DIFFERENT question: not "what is happening here" but "what is where", because the answer
/// is what lets a frame plan put a character on the right structure. Measured failure it addresses: a render placed
/// Dean at the left trailer's door when the deck — the place the moment wanted him — is on the right-hand trailer,
/// because nothing had ever told the pipeline what the location image contains.
/// </para>
///
/// <para>
/// The ROLES are fixed (see <see cref="LocationAnchorRole"/>) so a character's action can be matched to a feature; the
/// NAMES are the model's own words, so any place, however worded, can be described.
/// </para>
///
/// <para>
/// The schema forbids an anchor with an empty name, because an unnamed anchor cannot be referred to by anything
/// downstream. It does NOT require a position: "somewhere in the middle distance" is a real answer, and a REQUIRED
/// position would make the model invent one — the exact failure mode this analysis exists to remove.
/// </para>
/// </remarks>
public static class LocationAnchorPromptBuilder
{
    public const string SchemaVersion = "location-anchor-analysis-v1";
    public const string ResponseSchemaName = "location_anchor_analysis";

    /// <summary>The roles the model may choose from, in the schema's own order.</summary>
    public static IReadOnlyList<LocationAnchorRole> Roles { get; } =
    [
        LocationAnchorRole.Deck,
        LocationAnchorRole.Door,
        LocationAnchorRole.Window,
        LocationAnchorRole.Clothesline,
        LocationAnchorRole.Shed,
        LocationAnchorRole.Lawn,
        LocationAnchorRole.Path,
        LocationAnchorRole.Structure,
        LocationAnchorRole.Vegetation,
        LocationAnchorRole.Other
    ];

    public static string BuildSystemMessage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a location scout. Inspect the supplied image of a PLACE and report what is visibly in it and where each part sits.");
        sb.AppendLine();
        sb.AppendLine("Report only what is visible. Do not invent structures, doors, decks, paths or vegetation that cannot be seen, and do not describe people or actions — this image is a place, and the people who will be placed into it are not your concern.");
        sb.AppendLine();
        sb.AppendLine("1. description: 2-4 sentences on the place itself — its structures, its ground, its light, and the direction it is seen from. Write it as a description of a setting, not of a photograph of a setting.");
        sb.AppendLine("2. anchors: every distinct visible feature a person could stand on, walk to, enter, or be described in relation to. For each one give:");
        sb.AppendLine("   - role: the single best-fitting role from the list.");
        sb.AppendLine($"     deck = something stood or walked onto; door = walked through; window = looked through; clothesline = any washing line; shed = an outbuilding; lawn = open ground; path = a route; structure = a building seen as an object; vegetation = trees or hedges; other = anything else.");
        sb.AppendLine("   - name: your own words for it, specific enough that someone reading only the name knows WHICH one you mean when there are several (for example \"the deck on the right-hand trailer\", not \"a deck\").");
        sb.AppendLine("   - horizontal: left, center or right — where it sits across the frame. Omit it when the image does not show it clearly.");
        sb.AppendLine("   - depth: foreground, midground or background — how far into the frame. Omit it when the image does not show it clearly.");
        sb.AppendLine();
        sb.AppendLine("Omit a position rather than guessing one: a guessed side would place a person on the wrong structure, which is worse than no side at all.");
        sb.AppendLine("Name every feature separately. Do not merge two trailers into one anchor, and do not describe one feature twice.");
        sb.AppendLine("Return only JSON matching the supplied schema. Do not use markdown fences or explanatory text.");
        return sb.ToString();
    }

    public static string BuildUserMessage() => "Report what is in this place and where each part sits.";

    public static JsonElement CreateResponseSchema()
    {
        var roleList = string.Join(", ", Roles.Select(role => JsonSerializer.Serialize(role.ToString())));

        using var document = JsonDocument.Parse($$"""
            {
                "type": "object",
                "additionalProperties": false,
                "required": ["description", "anchors"],
                "properties": {
                    "description": { "type": "string", "minLength": 1 },
                    "anchors": {
                        "type": "array",
                        "minItems": 0,
                        "items": {
                            "type": "object",
                            "additionalProperties": false,
                            "required": ["role", "name"],
                            "properties": {
                                "role": { "type": "string", "enum": [{{roleList}}] },
                                "name": { "type": "string", "minLength": 1 },
                                "horizontal": { "type": "string", "enum": ["Left", "Center", "Right"] },
                                "depth": { "type": "string", "enum": ["Foreground", "Midground", "Background"] }
                            }
                        }
                    }
                }
            }
            """);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// The analysis a completion returned, or a refusal naming what was wrong with it.
    /// </summary>
    /// <param name="json">The completion's content, expected to match <see cref="CreateResponseSchema"/>.</param>
    /// <param name="sha256">The analysed image's checksum, recorded so staleness is answerable later.</param>
    /// <param name="analyzedUtc">When the analysis ran.</param>
    /// <remarks>
    /// The schema constrains the model, but a provider that ignores it must not be able to store an unusable analysis:
    /// a blank description or an unnamed anchor is refused HERE, by name, rather than surfacing later as a reference
    /// statement that says nothing.
    /// </remarks>
    public static LocationImageAnalysis Parse(string? json, string sha256, DateTime analyzedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The location analysis completion was empty.");
        }

        LocationAnalysisPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LocationAnalysisPayload>(json, PayloadOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The location analysis completion is not readable as JSON matching the location analysis schema.",
                exception);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Description))
        {
            throw new InvalidOperationException(
                "The location analysis completion carried no description, so it does not describe the place.");
        }

        var anchors = new List<LocationAnchor>();
        foreach (var anchor in payload.Anchors ?? [])
        {
            if (string.IsNullOrWhiteSpace(anchor.Name))
            {
                throw new InvalidOperationException(
                    "The location analysis returned an anchor with no name, and an unnamed anchor cannot be referred "
                    + "to by anything that would use it.");
            }

            if (!Enum.TryParse<LocationAnchorRole>(anchor.Role, ignoreCase: true, out var role)
                || role == LocationAnchorRole.Unknown)
            {
                throw new InvalidOperationException(
                    $"The location analysis returned anchor '{anchor.Name}' with role '{anchor.Role}', which is not one "
                    + $"of the {Roles.Count} known location roles.");
            }

            anchors.Add(new LocationAnchor(role, anchor.Name.Trim(), anchor.Horizontal, anchor.Depth));
        }

        return new LocationImageAnalysis(
            payload.Description.Trim(),
            anchors,
            sha256.Trim(),
            analyzedUtc);
    }

    /// <summary>
    /// Reading options for the completion.
    /// </summary>
    /// <remarks>
    /// The enum converter is REQUIRED, not cosmetic: the schema tells the model to answer with role and position NAMES
    /// ("Deck", "Left"), and without it the reader refuses every well-formed completion over a position it cannot
    /// convert. That failure is what the first run of this builder's tests caught.
    /// </remarks>
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The completion's own shape, which is validated field by field above rather than trusted.</summary>
    private sealed record LocationAnalysisPayload(
        string? Description,
        List<LocationAnchorPayload>? Anchors);

    private sealed record LocationAnchorPayload(
        string? Role,
        string? Name,
        FrameHorizontal? Horizontal,
        FrameDepth? Depth);
}
