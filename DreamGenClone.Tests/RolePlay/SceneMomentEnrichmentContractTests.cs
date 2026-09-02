using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentEnrichmentContractTests
{
    [Fact]
    public void CreateResponseSchema_IsExactAndClosesEveryObject()
    {
        var schema = SceneMomentEnrichmentContract.CreateResponseSchema();
        var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet();
        var characterRequired = schema.GetProperty("properties").GetProperty("characters")
            .GetProperty("items").GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet();

        Assert.Equal([
            "schemaVersion", "catalogueBeatId", "momentId", "visualDescription", "characters",
            "location", "timeOfDay", "lighting", "environment", "mood", "objects",
            "instantaneousSoundCueKeys", "videoKeyState"
        ], required);
        Assert.Equal([
            "name", "profileKey", "involvement", "physicalLocation", "position",
            "actionOrObservation", "sightline", "visibleCharacterNames", "clothing"
        ], characterRequired);
        AssertAllObjectsAreClosed(schema);
    }

    [Fact]
    public void BuildMessages_UsesCompactKeysWithoutAuthoritativeIdsOrProviderDialect()
    {
        var snapshot = SceneMomentEnrichmentTestFixture.CreateSnapshot();

        var messages = new SceneMomentEnrichmentContract().BuildMessages(snapshot);

        Assert.Equal(SceneMomentEnrichmentContract.ContractVersion, messages.ContractVersion);
        Assert.Contains("[p0] Becky", messages.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("[s1]", messages.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("[n0] Narrative", messages.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("character-becky", messages.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("interaction-0", messages.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("exactly one instant", messages.SystemPrompt, StringComparison.Ordinal);
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
}