using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatProductionContractTests
{
    [Fact]
    public void CreateResponseSchema_RequiresEveryCanonicalSectionAndClosesEveryObject()
    {
        var schema = SceneBeatProductionContract.CreateResponseSchema(["p0", "p1"]);
        var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet();

        Assert.Equal(14, required.Count);
        Assert.Contains("timeline", required);
        Assert.Contains("dialogue", required);
        Assert.Contains("ambience", required);
        Assert.Contains("music", required);
        Assert.Contains("videoCoverage", required);
        AssertAllObjectsAreClosed(schema);
    }

    [Fact]
    public void BuildMessages_UsesEvidenceAndProfileKeysWithoutInternalIdsOrProviderDialect()
    {
        var snapshot = CreateSnapshot();

        var messages = new SceneBeatProductionContract().BuildMessages(snapshot);

        Assert.Equal(SceneBeatProductionContract.ContractVersion, messages.ContractVersion);
        Assert.Equal(SceneBeatProductionContract.ResponseSchemaName, messages.ResponseSchemaName);
        Assert.Contains("[n0] Narrative", messages.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("[p0] Becky", messages.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("interaction-1", messages.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("character-1", messages.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("provider-neutral", messages.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not invent Moment IDs", messages.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("actionArc", messages.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("must never be used as profile keys", messages.SystemPrompt, StringComparison.Ordinal);

        var continuity = new SceneBeatProductionContract().BuildContinuityPass(snapshot, "{\"events\":[]}");
        Assert.Equal("continuity", continuity.PassId);
        Assert.Contains("start and end continuity key-states", continuity.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("never suffix them", continuity.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateResponseSchema_RestrictsActionArcReferencesToParticipantProfileKeys()
    {
        var schema = SceneBeatProductionContract.CreateResponseSchema(["p0", "p1"]);
        var actionArc = schema.GetProperty("properties").GetProperty("actionArc")
            .GetProperty("items");

        Assert.Equal(["p0", "p1", null], actionArc.GetProperty("properties").GetProperty("subjectKey")
            .GetProperty("enum").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(["p0", "p1", null], actionArc.GetProperty("properties").GetProperty("targetKey")
            .GetProperty("enum").EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Null ? null : item.GetString()).ToArray());
    }

    [Fact]
    public void CreateResponseSchema_RestrictsEveryParticipantReferenceToProfileKeys()
    {
        var properties = SceneBeatProductionContract.CreateResponseSchema(["p0", "p1"]).GetProperty("properties");
        var dialogue = properties.GetProperty("dialogue").GetProperty("items").GetProperty("properties");
        var narration = properties.GetProperty("narration").GetProperty("items").GetProperty("properties");
        var sound = properties.GetProperty("soundEvents").GetProperty("items").GetProperty("properties");
        var start = properties.GetProperty("startContinuity").GetProperty("properties");
        var end = properties.GetProperty("endContinuity").GetProperty("properties");
        var reference = properties.GetProperty("typedReferences").GetProperty("items").GetProperty("properties");

        AssertEnum(dialogue.GetProperty("speakerKey"), ["p0", "p1", null]);
        AssertEnum(dialogue.GetProperty("performance").GetProperty("properties").GetProperty("speakerKey"), ["p0", "p1", null]);
        AssertEnum(dialogue.GetProperty("addresseeKeys").GetProperty("items"), ["p0", "p1"]);
        AssertEnum(narration.GetProperty("speakerKey"), ["p0", "p1", null]);
        AssertEnum(sound.GetProperty("subjectKey"), ["p0", "p1", null]);
        AssertEnum(reference.GetProperty("subjectKey"), ["p0", "p1", null]);
        AssertEnum(start.GetProperty("characterStates").GetProperty("items").GetProperty("properties").GetProperty("key"), ["p0", "p1"]);
        AssertEnum(start.GetProperty("wardrobeStates").GetProperty("items").GetProperty("properties").GetProperty("key"), ["p0", "p1"]);
        AssertEnum(end.GetProperty("characterStates").GetProperty("items").GetProperty("properties").GetProperty("key"), ["p0", "p1"]);
        AssertEnum(end.GetProperty("wardrobeStates").GetProperty("items").GetProperty("properties").GetProperty("key"), ["p0", "p1"]);
        Assert.Equal("string", start.GetProperty("objectStates").GetProperty("items").GetProperty("properties")
            .GetProperty("key").GetProperty("type").GetString());
    }

    [Fact]
    public void PassSchemas_RestrictSpokenSpeakersAndContinuityCharactersToProfileKeys()
    {
        var contract = new SceneBeatProductionContract();
        var snapshot = CreateSnapshot();

        var spoken = contract.BuildSpokenPass(snapshot, "{\"events\":[]}").ResponseSchema.GetProperty("properties");
        AssertEnum(spoken.GetProperty("dialogue").GetProperty("items").GetProperty("properties").GetProperty("speakerKey"),
            ["p0", null]);
        AssertEnum(spoken.GetProperty("narration").GetProperty("items").GetProperty("properties").GetProperty("speakerKey"),
            ["p0", null]);

        var continuity = contract.BuildContinuityPass(snapshot, "{\"events\":[]}").ResponseSchema.GetProperty("properties")
            .GetProperty("startContinuity").GetProperty("properties");
        AssertEnum(continuity.GetProperty("characterStates").GetProperty("items").GetProperty("properties").GetProperty("key"),
            ["p0"]);

        var soundscape = contract.BuildSoundscapePass(snapshot, "{\"events\":[]}").ResponseSchema.GetProperty("properties");
        AssertEnum(soundscape.GetProperty("soundEvents").GetProperty("items").GetProperty("properties").GetProperty("subjectKey"),
            ["p0", null]);
    }

    private static void AssertEnum(JsonElement schema, string?[] expected)
        => Assert.Equal(expected, schema.GetProperty("enum").EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Null ? null : item.GetString()).ToArray());

    private static void AssertAllObjectsAreClosed(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "object")
            {
                Assert.True(element.TryGetProperty("additionalProperties", out var additional));
                Assert.False(additional.GetBoolean());
            }
            foreach (var property in element.EnumerateObject()) AssertAllObjectsAreClosed(property.Value);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) AssertAllObjectsAreClosed(item);
        }
    }

    private static SceneBeatProductionSourceSnapshot CreateSnapshot()
        => new(
            1,
            "catalogue-1",
            1,
            new SceneBeatProductionBeatSnapshot(
                "b1", 1, "Arrival", "Becky enters.", "entry hall",
                [new("Becky", "active", "p0")], ["n0"]),
            new SceneBeatProductionTurnSnapshot(
                "session-1", "turn-1", 1, "SubmitPrompt", DateTime.UtcNow, DateTime.UtcNow, new string('A', 64)),
            [new("n0", 0, "interaction-1", "Narrative", "System", "Becky enters.", DateTime.UtcNow, new string('B', 64))],
            [new("p0", "character-1", "Becky", "Wife", "Female", "desc", "appearance", "clothing", false, new string('C', 64))]);
}