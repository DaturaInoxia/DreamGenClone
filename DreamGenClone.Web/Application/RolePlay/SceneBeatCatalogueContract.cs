using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record SceneBeatCatalogueContractMessages(
    string ContractVersion,
    string SystemPrompt,
    string UserPrompt,
    string ResponseSchemaName,
    JsonElement ResponseSchema);

public sealed class SceneBeatCatalogueContract
{
    public const string ContractVersion = "scene-beat-catalogue-v1";
    public const string ResponseSchemaName = "scene_beat_catalogue";
    public const int LabelMaxLength = 80;
    public const int BeatSynopsisMaxLength = 400;
    public const int PrimaryLocationMaxLength = 120;
    public const int ParticipantNameMaxLength = 100;

    private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "beats"
    };

    private static readonly HashSet<string> BeatFields = new(StringComparer.Ordinal)
    {
        "beatId", "order", "label", "beatSynopsis", "primaryLocation", "participants", "evidenceKeys"
    };

    private static readonly HashSet<string> ParticipantFields = new(StringComparer.Ordinal)
    {
        "name", "involvement"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SceneBeatCatalogueSnapshotBuilder _snapshotBuilder;

    public SceneBeatCatalogueContract(SceneBeatCatalogueSnapshotBuilder snapshotBuilder)
    {
        _snapshotBuilder = snapshotBuilder;
    }

    public SceneBeatCatalogueContractMessages BuildMessages(
        SceneBeatCatalogueInputSnapshot snapshot,
        int maximumEntries)
    {
        ValidateInputs(snapshot, maximumEntries);

        var user = new StringBuilder();
        user.AppendLine("AUTHORITATIVE TURN EVIDENCE:");
        foreach (var evidence in snapshot.Evidence.OrderBy(item => item.SourceOrder))
        {
            user.AppendLine($"[{evidence.Key}] {evidence.ActorName} ({evidence.InteractionType}, {evidence.CreatedAt:O}):");
            user.AppendLine(evidence.Content);
        }

        user.AppendLine("KNOWN PARTICIPANTS:");
        foreach (var profile in snapshot.Profiles)
        {
            user.AppendLine($"[{profile.Key}] {profile.Name} | role={profile.Role} | gender={profile.Gender}");
        }

        return new SceneBeatCatalogueContractMessages(
            ContractVersion,
            BuildSystemPrompt(maximumEntries),
            user.ToString().TrimEnd(),
            ResponseSchemaName,
            CreateResponseSchema(maximumEntries));
    }

    public IReadOnlyList<SceneBeatCatalogueEntry> Parse(
        string catalogueId,
        string rawResponse,
        SceneBeatCatalogueInputSnapshot snapshot,
        int maximumEntries)
    {
        if (string.IsNullOrWhiteSpace(catalogueId))
            throw new ArgumentException("Catalogue id is required.", nameof(catalogueId));
        if (string.IsNullOrWhiteSpace(rawResponse))
            throw new InvalidOperationException("Beat Catalogue returned empty output.");
        ValidateInputs(snapshot, maximumEntries);

        try
        {
            using var document = JsonDocument.Parse(rawResponse);
            var root = document.RootElement;
            RequireExactFields(root, RootFields, "Beat Catalogue response");
            var schemaVersion = RequiredInt(root, "schemaVersion", "Beat Catalogue response");
            if (schemaVersion != SceneBeatCatalogueSnapshotBuilder.CurrentSchemaVersion)
                throw new InvalidOperationException($"Beat Catalogue returned unsupported schemaVersion {schemaVersion}.");

            var beatsElement = Required(root, "beats", "Beat Catalogue response");
            if (beatsElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Beat Catalogue response beats must be an array.");
            var beatElements = beatsElement.EnumerateArray().ToList();
            if (beatElements.Count is < 1 || beatElements.Count > maximumEntries)
                throw new InvalidOperationException($"Beat Catalogue must contain between 1 and {maximumEntries} beats.");

            var knownNames = snapshot.Profiles.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var entries = beatElements.Select(element => ParseBeat(catalogueId.Trim(), element, snapshot, knownNames)).ToList();
            if (entries.Select(item => item.BeatId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
                throw new InvalidOperationException("Beat Catalogue contains duplicate beat ids.");
            if (entries.Select(item => item.Order).Distinct().Count() != entries.Count)
                throw new InvalidOperationException("Beat Catalogue contains duplicate beat order values.");

            return entries.OrderBy(item => item.Order).ToList();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Beat Catalogue returned malformed JSON.", ex);
        }
    }

    public static JsonElement CreateResponseSchema(int maximumEntries)
    {
        if (maximumEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries), "Maximum Beat Catalogue entries must be positive.");

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("schemaVersion", "beats"),
            ["properties"] = new JsonObject
            {
                ["schemaVersion"] = new JsonObject { ["const"] = SceneBeatCatalogueSnapshotBuilder.CurrentSchemaVersion },
                ["beats"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = maximumEntries,
                    ["items"] = CreateBeatSchema()
                }
            }
        };
        return JsonSerializer.SerializeToElement(schema);
    }

    private SceneBeatCatalogueEntry ParseBeat(
        string catalogueId,
        JsonElement element,
        SceneBeatCatalogueInputSnapshot snapshot,
        HashSet<string> knownNames)
    {
        RequireExactFields(element, BeatFields, "Beat Catalogue beat");
        var beatId = RequiredBoundedString(element, "beatId", 40, "Beat Catalogue beat");
        if (beatId[0] != 'b' || beatId.Length == 1 || !beatId.AsSpan(1).ToString().All(char.IsAsciiDigit) || beatId[1] == '0')
            throw new InvalidOperationException($"Beat Catalogue beat id '{beatId}' must match b followed by a positive integer.");

        var order = RequiredInt(element, "order", $"Beat Catalogue beat '{beatId}'");
        var label = RequiredBoundedString(element, "label", LabelMaxLength, $"Beat Catalogue beat '{beatId}'");
        var synopsis = RequiredBoundedString(element, "beatSynopsis", BeatSynopsisMaxLength, $"Beat Catalogue beat '{beatId}'");
        var location = RequiredBoundedString(element, "primaryLocation", PrimaryLocationMaxLength, $"Beat Catalogue beat '{beatId}'");
        ValidateAtomicLocation(location, beatId);

        var participantsElement = Required(element, "participants", $"Beat Catalogue beat '{beatId}'");
        if (participantsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Beat Catalogue beat '{beatId}' participants must be an array.");
        var participants = participantsElement.EnumerateArray().Select(item => ParseParticipant(item, beatId, knownNames)).ToList();
        if (participants.Count == 0)
            throw new InvalidOperationException($"Beat Catalogue beat '{beatId}' must contain at least one participant.");
        if (participants.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != participants.Count)
            throw new InvalidOperationException($"Beat Catalogue beat '{beatId}' contains duplicate participants.");
        if (!participants.Any(item => string.Equals(item.Involvement, "active", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Beat Catalogue beat '{beatId}' must contain at least one active participant.");

        var evidenceKeys = RequiredStringArray(element, "evidenceKeys", $"Beat Catalogue beat '{beatId}'");
        if (!evidenceKeys.Contains("n0", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Beat Catalogue beat '{beatId}' must cite Narrative evidence key n0.");
        var interactionIds = _snapshotBuilder.ResolveEvidenceInteractionIds(snapshot, evidenceKeys);

        return new SceneBeatCatalogueEntry
        {
            CatalogueId = catalogueId,
            BeatId = beatId,
            Order = order,
            Label = label,
            BeatSynopsis = synopsis,
            PrimaryLocation = location,
            ParticipantSummaryJson = JsonSerializer.Serialize(participants, JsonOptions),
            EvidenceInteractionIdsJson = JsonSerializer.Serialize(interactionIds, JsonOptions),
            ContentTagsJson = "[]"
        };
    }

    private static Participant ParseParticipant(JsonElement element, string beatId, HashSet<string> knownNames)
    {
        RequireExactFields(element, ParticipantFields, $"Beat Catalogue beat '{beatId}' participant");
        var name = RequiredBoundedString(element, "name", ParticipantNameMaxLength, $"Beat Catalogue beat '{beatId}' participant");
        if (!knownNames.Contains(name))
            throw new InvalidOperationException($"Beat Catalogue beat '{beatId}' references unknown participant '{name}'.");
        var involvement = RequiredBoundedString(element, "involvement", 8, $"Beat Catalogue beat '{beatId}' participant").ToLowerInvariant();
        if (involvement is not ("active" or "observer"))
            throw new InvalidOperationException($"Beat Catalogue beat '{beatId}' participant involvement must be active or observer.");
        return new Participant(name, involvement);
    }

    private static JsonObject CreateBeatSchema()
        => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("beatId", "order", "label", "beatSynopsis", "primaryLocation", "participants", "evidenceKeys"),
            ["properties"] = new JsonObject
            {
                ["beatId"] = new JsonObject { ["type"] = "string", ["pattern"] = "^b[1-9][0-9]*$", ["maxLength"] = 40 },
                ["order"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
                ["label"] = StringSchema(LabelMaxLength),
                ["beatSynopsis"] = StringSchema(BeatSynopsisMaxLength),
                ["primaryLocation"] = StringSchema(PrimaryLocationMaxLength),
                ["participants"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("name", "involvement"),
                        ["properties"] = new JsonObject
                        {
                            ["name"] = StringSchema(ParticipantNameMaxLength),
                            ["involvement"] = new JsonObject { ["enum"] = new JsonArray("active", "observer") }
                        }
                    }
                },
                ["evidenceKeys"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["uniqueItems"] = true,
                    ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 }
                }
            }
        };

    private static JsonObject StringSchema(int maxLength)
        => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = maxLength };

    private static string BuildSystemPrompt(int maximumEntries)
        => $$"""
            You are a narrative production analyst. Read one complete authoritative role-play turn and identify 1 to {{maximumEntries}} distinct narrative Beats for later production planning.

            A Beat is a concise narrative development that may include movement through time. It is not a frozen image and must not contain camera prompts, detailed blocking, clothing inventories, sightlines, lighting design, provider syntax, or generation settings.

            Treat [n0] Narrative as the authoritative chronology and shared-scene synthesis. Use [c#] evidence only to support that chronology. Merge parallel accounts of the same development. Start a new Beat only for a material change in action, arrangement, location, clothing state, time, or scene purpose.

            Every Beat must cite n0 and every supporting evidence key. Use only supplied evidence keys and known participant names. Every Beat requires at least one active participant; people who only watch or notice are observers. primaryLocation must name exactly one physical event location.

            Keep labels under {{LabelMaxLength}} characters, synopses under {{BeatSynopsisMaxLength}} characters, and locations under {{PrimaryLocationMaxLength}} characters. Return only JSON matching the supplied schema. Do not use markdown fences or explanatory text.
            """;

    private static void ValidateInputs(SceneBeatCatalogueInputSnapshot snapshot, int maximumEntries)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximumEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries), "Maximum Beat Catalogue entries must be positive.");
        if (snapshot.SchemaVersion != SceneBeatCatalogueSnapshotBuilder.CurrentSchemaVersion)
            throw new InvalidOperationException($"Beat Catalogue input snapshot schemaVersion {snapshot.SchemaVersion} is unsupported.");
        if (!snapshot.Evidence.Any(item => string.Equals(item.Key, "n0", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Beat Catalogue input snapshot is missing Narrative evidence key n0.");
    }

    private static void ValidateAtomicLocation(string location, string beatId)
    {
        string[] separators = [" and ", " & ", " / ", ";", "|"];
        if (separators.Any(separator => location.Contains(separator, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Beat Catalogue beat '{beatId}' primaryLocation must contain exactly one physical location.");
    }

    private static void RequireExactFields(JsonElement element, HashSet<string> expected, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{context} must be an object.");
        var properties = element.EnumerateObject().ToList();
        if (properties.Count != expected.Count || properties.Any(property => !expected.Contains(property.Name)))
            throw new InvalidOperationException($"{context} has unknown, missing, or duplicate fields.");
    }

    private static JsonElement Required(JsonElement element, string name, string context)
        => element.TryGetProperty(name, out var property)
            ? property
            : throw new InvalidOperationException($"{context} is missing {name}.");

    private static int RequiredInt(JsonElement element, string name, string context)
        => Required(element, name, context).TryGetInt32(out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"{context} has invalid {name}.");

    private static string RequiredBoundedString(JsonElement element, string name, int maxLength, string context)
    {
        var property = Required(element, name, context);
        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
            throw new InvalidOperationException($"{context} has invalid {name}.");
        var value = property.GetString()!.Trim();
        if (value.Length > maxLength)
            throw new InvalidOperationException($"{context} {name} exceeds {maxLength} characters.");
        return value;
    }

    private static IReadOnlyList<string> RequiredStringArray(JsonElement element, string name, string context)
    {
        var property = Required(element, name, context);
        if (property.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"{context} {name} must be an array.");
        var values = property.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())
                ? item.GetString()!.Trim()
                : throw new InvalidOperationException($"{context} {name} contains an invalid value.")).ToList();
        if (values.Count == 0 || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new InvalidOperationException($"{context} {name} must contain unique non-empty values.");
        return values;
    }

    private sealed record Participant(string Name, string Involvement);
}