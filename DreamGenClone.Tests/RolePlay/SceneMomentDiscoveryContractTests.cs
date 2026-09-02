using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentDiscoveryContractTests
{
    [Fact]
    public void CreateResponseSchema_RequiresCompactTwoToFourClosedMoments()
    {
        var schema = SceneMomentDiscoveryContract.CreateResponseSchema();
        var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet();
        var moments = schema.GetProperty("properties").GetProperty("moments");

        Assert.Equal(["schemaVersion", "catalogueBeatId", "recommendedMomentId", "moments"], required);
        Assert.Equal(2, moments.GetProperty("minItems").GetInt32());
        Assert.Equal(4, moments.GetProperty("maxItems").GetInt32());
        AssertAllObjectsAreClosed(schema);
    }

    [Fact]
    public void BuildMessages_UsesCompactKeysWithoutPersistedIdsOrProviderDialect()
    {
        var messages = new SceneMomentDiscoveryContract().BuildMessages(CreateSnapshot());

        Assert.Equal(SceneMomentDiscoveryContract.ContractVersion, messages.ContractVersion);
        Assert.Contains("[p0] Becky", messages.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("[n0] Narrative", messages.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("roles=start,end", messages.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("interaction-1", messages.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("character-1", messages.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("exactly one frozen instant", messages.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ComfyUI", messages.SystemPrompt, StringComparison.OrdinalIgnoreCase);
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

    private static SceneMomentDiscoverySourceSnapshot CreateSnapshot()
        => new(
            1,
            "catalogue-1",
            1,
            "b1",
            "plan-1",
            1,
            "Arrival",
            "Becky enters and meets Dean's gaze.",
            "entry hall",
            "[{\"eventKey\":\"e1\"}]",
            "{\"durationIntent\":\"brief\"}",
            "[{\"eventKey\":\"e1\"}]",
            "{\"stateSummary\":\"outside\"}",
            "{\"stateSummary\":\"inside\"}",
            [new("v1", "MomentTransition", ["e1"], ["start", "end"], ["turn"])],
            [new("n0", 0, "interaction-1", "Narrative", "System", "Becky enters.", new string('A', 64))],
            [new("p0", "character-1", "Becky", "active")]);
}