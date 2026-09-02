using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatProductionContractTests
{
    [Fact]
    public void CreateResponseSchema_RequiresEveryCanonicalSectionAndClosesEveryObject()
    {
        var schema = SceneBeatProductionContract.CreateResponseSchema();
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
    }

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