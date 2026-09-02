using System.Text.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentEnrichmentParserTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string ValidResponse = """
        {
          "schemaVersion": 1,
          "catalogueBeatId": "b1",
          "momentId": "m2",
          "visualDescription": "Becky and Dean hold one shared look in the entry hall.",
          "characters": [
            {
              "name": "Becky", "profileKey": "p0", "involvement": "active",
              "physicalLocation": "entry hall", "position": "just inside the doorway",
              "actionOrObservation": "holds Dean's gaze", "sightline": "toward Dean",
              "visibleCharacterNames": ["Dean"], "clothing": "blue shirt"
            },
            {
              "name": "Dean", "profileKey": "p1", "involvement": "observer",
              "physicalLocation": "entry hall", "position": "seated across the hall",
              "actionOrObservation": "looks up at Becky", "sightline": "toward Becky",
              "visibleCharacterNames": ["Becky"], "clothing": "dark lounge clothes"
            }
          ],
          "location": "entry hall",
          "timeOfDay": "evening",
          "lighting": "warm ceiling light",
          "environment": "narrow entry hall with the open door behind Becky",
          "mood": "expectant",
          "objects": ["open door"],
          "instantaneousSoundCueKeys": ["s1"],
          "videoKeyState": { "roles": ["VideoEnd"], "stateChangeAllowed": false }
        }
        """;

    [Fact]
    public void Parse_ProjectsCanonicalFrozenCastSoundAndVideoState()
    {
        var result = Parse(ValidResponse);

        var frozen = JsonSerializer.Deserialize<SceneMomentFrozenStateContract>(result.FrozenStateContractJson, JsonOptions)!;
        Assert.Equal("entry hall", frozen.Location);
        Assert.Equal(["character-becky", "character-dean"], frozen.Characters.Select(item => item.CharacterId));
        Assert.Equal("Becky stands inside the hall and meets Dean's raised gaze.", frozen.ContinuityState);
        var sound = Assert.Single(JsonSerializer.Deserialize<SceneMomentInstantaneousSoundEvent[]>(result.InstantaneousSoundEventsJson, JsonOptions)!);
        Assert.Equal("s1", sound.CueKey);
        Assert.Equal("e1", sound.EventKey);
        var video = JsonSerializer.Deserialize<SceneMomentVideoKeyState>(result.VideoKeyStateJson, JsonOptions)!;
        Assert.Equal(["VideoEnd"], video.Roles);
        Assert.False(video.StateChangeAllowed);
    }

    [Fact]
    public void Parse_RejectsUnknownJsonMember()
    {
        var response = Mutate(root => root["extra"] = true);

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("contract-invalid", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("profile", "unknown or non-cast profile keys")]
    [InlineData("sound", "unknown sound cue keys")]
    public void Parse_RejectsUnknownAuthoritativeKeys(string kind, string expected)
    {
        var response = Mutate(root =>
        {
            if (kind == "profile") root["characters"]![0]!["profileKey"] = "p9";
            else root["instantaneousSoundCueKeys"]![0] = "s9";
        });

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsDuplicateProfileKeysAndWrongProfileNames()
    {
        var duplicate = Mutate(root => root["characters"]![1]!["profileKey"] = "p0");
        Assert.Contains("profile keys must be non-empty and unique", Assert.Throws<InvalidOperationException>(() => Parse(duplicate)).Message, StringComparison.OrdinalIgnoreCase);

        var wrongName = Mutate(root => root["characters"]![0]!["name"] = "Not Becky");
        Assert.Contains("does not match authoritative name", Assert.Throws<InvalidOperationException>(() => Parse(wrongName)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsMissingCastAndUnknownVisibleName()
    {
        var missing = Mutate(root => root["characters"]!.AsArray().RemoveAt(1));
        Assert.Contains("missing selected-Moment participants", Assert.Throws<InvalidOperationException>(() => Parse(missing)).Message, StringComparison.OrdinalIgnoreCase);

        var unknownVisible = Mutate(root => root["characters"]![0]!["visibleCharacterNames"]![0] = "Stranger");
        Assert.Contains("unknown visible character names", Assert.Throws<InvalidOperationException>(() => Parse(unknownVisible)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsVideoRoleMismatchAndStateChange()
    {
        var wrongRole = Mutate(root => root["videoKeyState"]!["roles"] = new JsonArray("VideoStart"));
        Assert.Contains("exactly match", Assert.Throws<InvalidOperationException>(() => Parse(wrongRole)).Message, StringComparison.OrdinalIgnoreCase);

        var stateChange = Mutate(root => root["videoKeyState"]!["stateChangeAllowed"] = true);
        Assert.Contains("cannot allow state change", Assert.Throws<InvalidOperationException>(() => Parse(stateChange)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("visualDescription", "Becky looks away before turning back to Dean.")]
    [InlineData("actionOrObservation", "looks down, then raises her gaze")]
    public void Parse_RejectsSequentialActionProse(string field, string value)
    {
        var response = Mutate(root =>
        {
            if (field == "visualDescription") root[field] = value;
            else root["characters"]![0]![field] = value;
        });

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("sequential before/after/then action", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SceneMomentEnrichmentData Parse(string response)
        => new SceneMomentEnrichmentParser().Parse(response, SceneMomentEnrichmentTestFixture.CreateSnapshot());

    private static string Mutate(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(ValidResponse)!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }
}