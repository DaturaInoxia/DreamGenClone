using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatCatalogueContractTests
{
    private const string ValidResponse = """
        {
          "schemaVersion": 1,
          "beats": [
            {
              "beatId": "b1",
              "order": 1,
              "label": "Arrival at the doorway",
              "beatSynopsis": "Becky enters the hall and draws Dean's attention.",
              "primaryLocation": "entry hall",
              "participants": [
                { "name": "Becky", "involvement": "active" },
                { "name": "Dean", "involvement": "observer" }
              ],
              "evidenceKeys": ["n0", "c2"]
            }
          ]
        }
        """;

    private readonly SceneBeatCatalogueSnapshotBuilder _snapshotBuilder = new();

    [Fact]
    public void BuildMessages_EmitsVersionedStrictBoundedSchemaAndCompactPrompt()
    {
        var contract = CreateContract();
        var snapshot = CreateSnapshot();

        var messages = contract.BuildMessages(snapshot, 6);

        Assert.Equal(SceneBeatCatalogueContract.ContractVersion, messages.ContractVersion);
        Assert.Equal(SceneBeatCatalogueContract.ResponseSchemaName, messages.ResponseSchemaName);
        Assert.Contains("1 to 6", messages.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("[n0] Narrative", messages.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("[p0] Becky", messages.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("narrative-id", messages.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("character-becky", messages.UserPrompt, StringComparison.Ordinal);

        var schema = messages.ResponseSchema;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var beats = schema.GetProperty("properties").GetProperty("beats");
        Assert.Equal(6, beats.GetProperty("maxItems").GetInt32());
        Assert.False(beats.GetProperty("items").GetProperty("additionalProperties").GetBoolean());
        Assert.False(beats.GetProperty("items").GetProperty("properties").GetProperty("participants")
            .GetProperty("items").GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Parse_ResolvesEvidenceAndProducesPersistenceEntries()
    {
        var entry = Assert.Single(CreateContract().Parse("catalogue-1", ValidResponse, CreateSnapshot(), 6));

        Assert.Equal("catalogue-1", entry.CatalogueId);
        Assert.Equal("b1", entry.BeatId);
        Assert.Equal("Arrival at the doorway", entry.Label);
        Assert.Equal("entry hall", entry.PrimaryLocation);
        Assert.Equal("[]", entry.ContentTagsJson);
        Assert.Equal(["narrative-id", "becky-id"], JsonSerializer.Deserialize<string[]>(entry.EvidenceInteractionIdsJson));
        using var participants = JsonDocument.Parse(entry.ParticipantSummaryJson);
        Assert.Equal("Becky", participants.RootElement[0].GetProperty("name").GetString());
        Assert.Equal("active", participants.RootElement[0].GetProperty("involvement").GetString());
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"beats\":[],\"extra\":true}", "unknown, missing, or duplicate")]
    [InlineData("{\"schemaVersion\":2,\"beats\":[]}", "unsupported schemaVersion")]
    [InlineData("not json", "malformed JSON")]
    public void Parse_RejectsInvalidRootContract(string response, string expectedMessage)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            CreateContract().Parse("catalogue-1", response, CreateSnapshot(), 6));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsUnknownEvidenceKey()
    {
        var response = ValidResponse.Replace("\"c2\"", "\"missing\"");

        var error = Assert.Throws<InvalidOperationException>(() =>
            CreateContract().Parse("catalogue-1", response, CreateSnapshot(), 6));

        Assert.Contains("Unknown evidence keys: missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RequiresNarrativeEvidence()
    {
        var response = ValidResponse.Replace("[\"n0\", \"c2\"]", "[\"c2\"]");

        var error = Assert.Throws<InvalidOperationException>(() =>
            CreateContract().Parse("catalogue-1", response, CreateSnapshot(), 6));

        Assert.Contains("must cite Narrative evidence key n0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsUnknownOrObserverOnlyParticipants()
    {
        var unknown = ValidResponse.Replace("\"Becky\"", "\"Unknown\"");
        var unknownError = Assert.Throws<InvalidOperationException>(() =>
            CreateContract().Parse("catalogue-1", unknown, CreateSnapshot(), 6));
        Assert.Contains("unknown participant", unknownError.Message, StringComparison.OrdinalIgnoreCase);

        var observerOnly = ValidResponse.Replace("\"active\"", "\"observer\"");
        var observerError = Assert.Throws<InvalidOperationException>(() =>
            CreateContract().Parse("catalogue-1", observerOnly, CreateSnapshot(), 6));
        Assert.Contains("at least one active participant", observerError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsCompoundLocationAndConfiguredMaximumOverflow()
    {
        var compound = ValidResponse.Replace("entry hall", "entry hall and kitchen");
        var locationError = Assert.Throws<InvalidOperationException>(() =>
            CreateContract().Parse("catalogue-1", compound, CreateSnapshot(), 6));
        Assert.Contains("exactly one physical location", locationError.Message, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(ValidResponse);
        var beat = document.RootElement.GetProperty("beats")[0].GetRawText();
        var overflow = "{\"schemaVersion\":1,\"beats\":[" + beat + ","
            + beat.Replace("\"b1\"", "\"b2\"").Replace("\"order\": 1", "\"order\": 2") + "]}";
        var maximumError = Assert.Throws<InvalidOperationException>(() =>
            CreateContract().Parse("catalogue-1", overflow, CreateSnapshot(), 1));
        Assert.Contains("between 1 and 1 beats", maximumError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsUnknownNestedFieldsAndDuplicateIdentity()
    {
        var extraParticipantField = ValidResponse.Replace(
            "\"involvement\": \"active\"",
            "\"involvement\": \"active\", \"profileId\": \"invented\"");
        var fieldError = Assert.Throws<InvalidOperationException>(() =>
            CreateContract().Parse("catalogue-1", extraParticipantField, CreateSnapshot(), 6));
        Assert.Contains("unknown, missing, or duplicate fields", fieldError.Message, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(ValidResponse);
        var beat = document.RootElement.GetProperty("beats")[0].GetRawText();
        var duplicate = "{\"schemaVersion\":1,\"beats\":[" + beat + ","
            + beat.Replace("\"order\": 1", "\"order\": 2") + "]}";
        var duplicateError = Assert.Throws<InvalidOperationException>(() =>
            CreateContract().Parse("catalogue-1", duplicate, CreateSnapshot(), 6));
        Assert.Contains("duplicate beat ids", duplicateError.Message, StringComparison.Ordinal);
    }

    private SceneBeatCatalogueContract CreateContract() => new(_snapshotBuilder);

    private static SceneBeatCatalogueInputSnapshot CreateSnapshot()
        => new(
            SceneBeatCatalogueSnapshotBuilder.CurrentSchemaVersion,
            "session-1",
            "turn-1",
            1,
            "SubmitPrompt",
            new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 31, 10, 0, 5, DateTimeKind.Utc),
            new string('A', 64),
            [
                new("n0", 2, "narrative-id", "Narrative", "System", "Narrative synthesis", new DateTime(2026, 8, 31, 10, 0, 3, DateTimeKind.Utc), new string('B', 64)),
                new("c1", 0, "dean-id", "Dean", "User", "Dean speaks", new DateTime(2026, 8, 31, 10, 0, 1, DateTimeKind.Utc), new string('C', 64)),
                new("c2", 1, "becky-id", "Becky", "Npc", "Becky enters", new DateTime(2026, 8, 31, 10, 0, 2, DateTimeKind.Utc), new string('D', 64))
            ],
            [
                new("p0", "character-becky", "Becky", "Wife", "Female", "", "", "", false, new string('E', 64)),
                new("p1", "character-dean", "Dean", "Husband", "Male", "", "", "", true, new string('F', 64))
            ]);
}