using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentDiscoveryParserTests
{
    private const string ValidResponse = """
        {
          "schemaVersion": 1,
          "catalogueBeatId": "b1",
          "recommendedMomentId": "m2",
          "moments": [
            {
              "momentId": "m1", "order": 1, "label": "Threshold",
              "temporalAnchor": "the instant Becky's foot lands inside",
              "frozenState": "Becky is mid-step in the doorway while Dean remains seated.",
              "visibleAction": "crossing the threshold",
              "participants": [{"profileKey":"p0","involvement":"active"},{"profileKey":"p1","involvement":"observer"}],
              "compositionRationale": "The doorway preserves the starting spatial relationship.",
              "productionRoles": ["VideoStart"], "evidenceKeys": ["n0","c1"]
            },
            {
              "momentId": "m2", "order": 2, "label": "Exchanged look",
              "temporalAnchor": "the instant their gazes meet",
              "frozenState": "Becky stands inside the hall and meets Dean's raised gaze.",
              "visibleAction": "holding eye contact",
              "participants": [{"profileKey":"p0","involvement":"active"},{"profileKey":"p1","involvement":"active"}],
              "compositionRationale": "The shared sightline creates a clear emotional center.",
              "productionRoles": ["StillCandidate","VideoEnd"], "evidenceKeys": ["n0","c1"]
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ResolvesFrozenMomentsRecommendationRolesAndEvidence()
    {
        var result = Parse(ValidResponse);

        Assert.Equal("m2", result.RecommendedMomentId);
        Assert.Equal(2, result.Moments.Count);
        Assert.Equal("[\"interaction-0\",\"interaction-1\"]", result.Moments[0].EvidenceInteractionIdsJson);
        Assert.Contains("VideoStart", result.Moments[0].ProductionRolesJson, StringComparison.Ordinal);
        Assert.Contains("StillCandidate", result.Moments[1].ProductionRolesJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsSequentialBeforeAfterState()
    {
        var response = Mutate(root => root["moments"]![0]!["frozenState"] = "Becky is outside before she enters and then faces Dean.");

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("sequential before/after action", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsMissingRequestedVideoRole()
    {
        var response = Mutate(root => root["moments"]![1]!["productionRoles"] = new JsonArray("StillCandidate"));

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("missing required production roles", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VideoEnd", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("participants", "profileKey", "p9", "unknown profile keys")]
    [InlineData("evidenceKeys", null, "c9", "unknown evidence keys")]
    public void Parse_RejectsUnknownAuthoritativeKeys(string collection, string? property, string value, string expected)
    {
        var response = Mutate(root =>
        {
            if (property is null)
                root["moments"]![0]![collection]![1] = value;
            else
                root["moments"]![0]![collection]![0]![property] = value;
        });

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SceneMomentSetData Parse(string response)
        => new SceneMomentDiscoveryParser().Parse("moment-set-1", response, CreateSnapshot());

    private static string Mutate(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(ValidResponse)!.AsObject();
        mutation(root);
        return root.ToJsonString();
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
            [
                new("n0", 0, "interaction-0", "Narrative", "System", "Becky enters.", new string('A', 64)),
                new("c1", 1, "interaction-1", "Dean", "User", "You're still awake.", new string('B', 64))
            ],
            [
                new("p0", "character-becky", "Becky", "active"),
                new("p1", "character-dean", "Dean", "observer")
            ]);
}