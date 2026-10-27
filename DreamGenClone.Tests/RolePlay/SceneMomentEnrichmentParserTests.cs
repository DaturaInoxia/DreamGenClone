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
          "schemaVersion": 2,
          "catalogueBeatId": "b1",
          "momentId": "m2",
          "visualDescription": "Becky and Dean hold one shared look in the entry hall.",
          "characters": [
            {
              "name": "Becky", "profileKey": "p0", "involvement": "active",
              "physicalLocation": "entry hall", "position": "just inside the doorway",
              "actionOrObservation": "holds Dean's gaze", "sightline": "toward Dean",
              "visibleCharacterNames": ["Dean"], "clothing": "blue shirt",
              "at": { "anchor": "doorway", "posture": "standing just inside" },
              "facing": { "toward": "p1" }
            },
            {
              "name": "Dean", "profileKey": "p1", "involvement": "observer",
              "physicalLocation": "entry hall", "position": "seated across the hall",
              "actionOrObservation": "looks up at Becky", "sightline": "toward Becky",
              "visibleCharacterNames": ["Becky"], "clothing": "dark lounge clothes",
              "at": { "anchor": "armchair", "posture": "seated" },
              "facing": { "toward": "p0" }
            }
          ],
          "location": "entry hall",
          "timeOfDay": "evening",
          "lighting": "warm ceiling light",
          "environment": "narrow entry hall with the open door behind the doorway",
          "mood": "expectant",
          "objects": ["open door"],
          "event": { "anchor": "doorway", "line": "Becky holds Dean's gaze across the entry hall." },
          "contacts": [],
          "instantaneousSoundCueKeys": ["s1"],
          "videoKeyState": { "roles": ["VideoEnd"], "stateChangeAllowed": false }
        }
        """;

    [Fact]
    public void Parse_CanonicalizesAFacingTargetWrappedInAPreposition()
    {
        // The contract asks for a profile key but the model can wrap it ("toward p0", "facing Becky"). Stored verbatim,
        // the phrase was printed into the frame block and the caption writer copied the raw key into the image prompt
        // (live 2026-10-27, prompt record 8655924e).
        var response = Mutate(root =>
        {
            root["characters"]![0]!["facing"]!["toward"] = "toward p1";
            root["characters"]![1]!["facing"]!["toward"] = "facing Becky";
        });

        var frozen = JsonSerializer.Deserialize<SceneMomentFrozenStateContract>(
            Parse(response).FrozenStateContractJson, JsonOptions)!;

        Assert.Equal(["p1", "p0"], frozen.Characters.Select(item => item.Facing.Toward));
    }

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

    [Fact]
    public void Parse_AllowsSpatialBeforePhraseInFrozenAction()
    {
        var response = Mutate(root => root["characters"]![0]!["actionOrObservation"] =
            "Unaware of the door opening, standing bare before the shower stall");

        var result = Parse(response);

        Assert.Contains("standing bare before the shower stall", result.FrozenStateContractJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ProjectsWorldFactsOntoTheFrozenState()
    {
        var response = Mutate(root =>
        {
            root["characters"]![0]!["at"] = new JsonObject { ["anchor"] = "doorway", ["posture"] = "standing just inside" };
            root["characters"]![0]!["facing"] = new JsonObject { ["toward"] = "p1" };
            root["event"] = new JsonObject { ["anchor"] = "doorway", ["line"] = "Becky holds Dean's gaze across the entry hall." };
            root["contacts"] = new JsonArray(new JsonObject
            {
                ["from"] = "p0", ["part"] = "hands", ["to"] = "p1", ["targetPart"] = "chest", ["manner"] = "rest"
            });
        });

        var result = Parse(response);

        var frozen = JsonSerializer.Deserialize<SceneMomentFrozenStateContract>(result.FrozenStateContractJson, JsonOptions)!;
        Assert.Equal(new SceneMomentBodyPosition("doorway", "standing just inside"), frozen.Characters[0].At);
        Assert.Equal(new SceneMomentFacing("p1"), frozen.Characters[0].Facing);
        Assert.Equal("Becky holds Dean's gaze across the entry hall.", frozen.Event!.Line);
        var contact = Assert.Single(frozen.Contacts!);
        Assert.Equal("p0", contact.From);
        Assert.Equal("chest", contact.TargetPart);
    }

    [Fact]
    public void Parse_RefusesACastNameInsideThePlace()
    {
        var response = Mutate(root => root["location"] = "entry hall where Becky waits");

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("place only", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Becky", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RefusesAContactThatNamesSomeoneOutsideTheCast()
    {
        var response = Mutate(root => root["contacts"] = new JsonArray(new JsonObject
        {
            ["from"] = "p0", ["part"] = "hands", ["to"] = "p9", ["targetPart"] = "chest", ["manner"] = "rest"
        }));

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("unknown profile key 'p9'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AcceptsASelfContactAndDeduplicatesARepeatedOne()
    {
        // A contact with one's OWN body is a real fact (a hand on one's own thigh), and it is the only way a first-person
        // frame learns about the camera holder's own body. Refusing it blocked a legitimate enrichment outright
        // (reported live 2026-10-24: "contact 0 pairs 'p0' with itself").
        var self = Mutate(root => root["contacts"] = new JsonArray(new JsonObject
        {
            ["from"] = "p0", ["part"] = "hands", ["to"] = "p0", ["targetPart"] = "thighs", ["manner"] = "press"
        }));

        var frozen = JsonSerializer.Deserialize<SceneMomentFrozenStateContract>(
            Parse(self).FrozenStateContractJson, JsonOptions)!;
        var contact = Assert.Single(frozen.Contacts!);
        Assert.Equal("p0", contact.From);
        Assert.Equal("p0", contact.To);
        Assert.Equal("thighs", contact.TargetPart);

        // The same contact stated twice is the same fact: the first occurrence wins rather than failing the record.
        var duplicate = Mutate(root => root["contacts"] = new JsonArray(
            new JsonObject { ["from"] = "p0", ["part"] = "hands", ["to"] = "p1", ["targetPart"] = "chest", ["manner"] = "rest" },
            new JsonObject { ["from"] = "p0", ["part"] = "hands", ["to"] = "p1", ["targetPart"] = "chest", ["manner"] = "press" }));

        var deduped = JsonSerializer.Deserialize<SceneMomentFrozenStateContract>(
            Parse(duplicate).FrozenStateContractJson, JsonOptions)!;
        Assert.Equal("rest", Assert.Single(deduped.Contacts!).Manner);
    }

    [Fact]
    public void Parse_RefusesASequentialEventLine()
    {
        var response = Mutate(root => root["event"]!["line"] = "Becky turns away, then looks back at Dean.");

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("event line", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sequential", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_StripsANegatedGarmentFromTheWardrobe()
    {
        // Reported 2026-10-24: "Naked; no tank or cutoffs on." An image model renders what the text names, so the
        // negated garments would put a tank and cutoffs in the picture. The affirmative state is what remains.
        var response = Mutate(root =>
            root["characters"]![0]!["clothing"] = "Naked; no tank or cutoffs on.");

        var frozen = JsonSerializer.Deserialize<SceneMomentFrozenStateContract>(
            Parse(response).FrozenStateContractJson, JsonOptions)!;

        Assert.Equal("Naked", frozen.Characters[0].Clothing);
        Assert.DoesNotContain("tank", frozen.Characters[0].Clothing, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cutoffs", frozen.Characters[0].Clothing, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Nude, no shoes", "Nude")]
    [InlineData("Completely naked without any clothing", "Completely naked")]
    [InlineData("Barefoot, not wearing her tank top", "Barefoot")]
    [InlineData("Jeans round her knees, no shirt", "Jeans round her knees")]
    public void NormalizeWardrobe_KeepsTheAffirmativePart(string clothing, string expected)
        => Assert.Equal(expected, SceneMomentEnrichmentParser.NormalizeWardrobe(clothing, "Becky"));

    [Fact]
    public void NormalizeWardrobe_LeavesAnAffirmativeStatementAlone()
    {
        Assert.Equal("Open blue shirt, jeans round the ankles",
            SceneMomentEnrichmentParser.NormalizeWardrobe("Open blue shirt, jeans round the ankles", "Becky"));
        Assert.Equal("Nude", SceneMomentEnrichmentParser.NormalizeWardrobe("Nude", "Becky"));
        // "nothing on" states nudity affirmatively; it names no garment, so it is not a negation to strip.
        Assert.Equal("Nothing on", SceneMomentEnrichmentParser.NormalizeWardrobe("Nothing on", "Becky"));
    }

    [Fact]
    public void NormalizeWardrobe_RefusesAValueThatIsOnlyANegation()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SceneMomentEnrichmentParser.NormalizeWardrobe("No tank top", "Becky"));

        Assert.Contains("Becky", error.Message, StringComparison.Ordinal);
        Assert.Contains("affirmatively", error.Message, StringComparison.Ordinal);
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