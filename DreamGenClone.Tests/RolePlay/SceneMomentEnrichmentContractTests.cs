using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentEnrichmentContractTests
{
    [Fact]
    public void CreateResponseSchema_IsExactAndClosesEveryObject()
    {
        var schema = SceneMomentEnrichmentContract.CreateResponseSchema(["p0"], ["VideoEnd"]);
        var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet();
        var characterRequired = schema.GetProperty("properties").GetProperty("characters")
            .GetProperty("items").GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet();
        var characters = schema.GetProperty("properties").GetProperty("characters");
        var profileKeys = characters.GetProperty("items").GetProperty("properties").GetProperty("profileKey")
            .GetProperty("enum").EnumerateArray().Select(item => item.GetString()).ToArray();
        var videoRoles = schema.GetProperty("properties").GetProperty("videoKeyState").GetProperty("properties")
            .GetProperty("roles");
        var videoRoleNames = videoRoles.GetProperty("items").GetProperty("enum").EnumerateArray()
            .Select(item => item.GetString()).ToArray();

        Assert.Equal([
            "schemaVersion", "catalogueBeatId", "momentId", "visualDescription", "characters",
            "location", "timeOfDay", "lighting", "environment", "mood", "objects",
            "instantaneousSoundCueKeys", "videoKeyState"
        ], required);
        Assert.Equal([
            "name", "profileKey", "involvement", "physicalLocation", "position",
            "actionOrObservation", "sightline", "visibleCharacterNames", "clothing"
        ], characterRequired);
        Assert.Equal(1, characters.GetProperty("minItems").GetInt32());
        Assert.Equal(1, characters.GetProperty("maxItems").GetInt32());
        Assert.Equal(["p0"], profileKeys);
        Assert.Equal(["VideoEnd"], videoRoleNames);
        Assert.Equal(1, videoRoles.GetProperty("minItems").GetInt32());
        Assert.Equal(1, videoRoles.GetProperty("maxItems").GetInt32());
        AssertAllObjectsAreClosed(schema);
    }

    [Fact]
    public void CreateResponseSchema_RestrictsRolesToExactlyTheSelectedMomentVideoRoles()
    {
        var schema = SceneMomentEnrichmentContract.CreateResponseSchema(["p0"], ["VideoStart"]);

        var videoRoles = schema.GetProperty("properties").GetProperty("videoKeyState").GetProperty("properties")
            .GetProperty("roles");

        Assert.Equal(["VideoStart"], videoRoles.GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(1, videoRoles.GetProperty("minItems").GetInt32());
        Assert.Equal(1, videoRoles.GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public void CreateResponseSchema_RequiresEmptyRolesWhenMomentHasNoVideoRole()
    {
        var schema = SceneMomentEnrichmentContract.CreateResponseSchema(["p0"], Array.Empty<string>());

        var videoRoles = schema.GetProperty("properties").GetProperty("videoKeyState").GetProperty("properties")
            .GetProperty("roles");

        Assert.Equal(0, videoRoles.GetProperty("items").GetProperty("enum").GetArrayLength());
        Assert.Equal(0, videoRoles.GetProperty("minItems").GetInt32());
        Assert.Equal(0, videoRoles.GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public void BuildMessages_DeclaresExactVideoKeyStateConstraintForSelectedMoment()
    {
        var snapshot = SceneMomentEnrichmentTestFixture.CreateSnapshot(); // m2 production roles: StillCandidate, VideoEnd, SoundEventAnchor

        var messages = new SceneMomentEnrichmentContract().BuildMessages(snapshot);

        const string marker = "VIDEO KEY-STATE CONSTRAINT";
        var index = messages.UserPrompt.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, "user prompt must declare the video key-state constraint");
        var constraint = messages.UserPrompt[index..];
        Assert.Contains("videoKeyState.roles must contain exactly the selected Moment's video roles and no others: VideoEnd", constraint, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoStart", constraint, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoInternalKeyframe", constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessages_RequiresEmptyVideoRolesWhenMomentHasNone()
    {
        var snapshot = SceneMomentEnrichmentTestFixture.CreateSnapshot();
        var noVideo = snapshot with
        {
            Moment = snapshot.Moment with
            {
                ProductionRoles = snapshot.Moment.ProductionRoles.Where(role => role != "VideoEnd").ToArray()
            }
        };

        var messages = new SceneMomentEnrichmentContract().BuildMessages(noVideo);

        Assert.Contains("must be an empty array", messages.UserPrompt, StringComparison.Ordinal);
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