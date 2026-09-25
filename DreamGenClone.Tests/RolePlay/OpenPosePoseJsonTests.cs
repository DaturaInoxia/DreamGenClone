using System.Text.Json.Nodes;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The OpenPose reader/writer is the contract between the imported pack, the stored preset and the renderer,
/// so its refusals matter as much as its successes.
/// </summary>
public sealed class OpenPosePoseJsonTests
{
    [Fact]
    public void Parse_ReadsThePackDocumentShape()
    {
        var json = PackDocument();

        var person = OpenPosePoseJson.Parse(json, "pack sample");

        Assert.Equal(PosePerson.BodyJointCount, person.Body.Count);
        Assert.Empty(person.LeftHand);
        Assert.Equal(100, person.Body[0].X, 3);
        Assert.Equal(1.0, person.Body[0].Confidence, 3);
    }

    [Fact]
    public void Parse_ReadsTheStoredBarePersonShape()
    {
        var stored = OpenPosePoseJson.Serialize(OpenPosePoseJson.Parse(PackDocument(), "pack sample"));

        var person = OpenPosePoseJson.Parse(stored, "stored sample");

        Assert.Equal(PosePerson.BodyJointCount, person.Body.Count);
    }

    [Fact]
    public void Serialize_ThenParse_RoundTripsIdentically()
    {
        var first = OpenPosePoseJson.Parse(PackDocument(), "pack sample");
        var once = OpenPosePoseJson.Serialize(first);
        var second = OpenPosePoseJson.Parse(once, "round trip");
        var twice = OpenPosePoseJson.Serialize(second);

        Assert.Equal(once, twice);
        for (var i = 0; i < PosePerson.BodyJointCount; i++)
        {
            Assert.Equal(first.Body[i].X, second.Body[i].X, 6);
            Assert.Equal(first.Body[i].Y, second.Body[i].Y, 6);
            Assert.Equal(first.Body[i].Confidence, second.Body[i].Confidence, 6);
        }
    }

    [Fact]
    public void Parse_WrongBodyCount_NamesTheCounts()
    {
        var json = """
            { "people": [ { "pose_keypoints_2d": [1, 2, 1, 3, 4, 1] } ] }
            """;

        var error = Assert.Throws<InvalidOperationException>(() => OpenPosePoseJson.Parse(json, "too few"));

        Assert.Contains("18", error.Message, StringComparison.Ordinal);
        Assert.Contains("too few", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TwoPeople_IsRefusedRatherThanSilentlyTruncated()
    {
        var json = $$"""
            { "people": [ {{PersonObject()}}, {{PersonObject()}} ] }
            """;

        var error = Assert.Throws<InvalidOperationException>(() => OpenPosePoseJson.Parse(json, "pair"));

        Assert.Contains("exactly one person", error.Message, StringComparison.Ordinal);
        Assert.Contains("2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MissingBodyKeypoints_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            OpenPosePoseJson.Parse("""{ "people": [ { "hand_left_keypoints_2d": [] } ] }""", "no body"));

        Assert.Contains("pose_keypoints_2d", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireHeadKeypoints_AcceptsACompleteHead()
    {
        var person = OpenPosePoseJson.Parse(PackDocument(), "pack sample");

        OpenPosePoseJson.RequireHeadKeypoints(person, "pack sample");
    }

    [Fact]
    public void RequireHeadKeypoints_NamesTheMissingJoints()
    {
        var person = OpenPosePoseJson.Parse(PackDocument(noseConfidence: 0.0, leftShoulderConfidence: 0.05), "faceless");

        var error = Assert.Throws<InvalidOperationException>(() =>
            OpenPosePoseJson.RequireHeadKeypoints(person, "faceless"));

        Assert.Contains("nose", error.Message, StringComparison.Ordinal);
        Assert.Contains("left shoulder", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("right shoulder", error.Message, StringComparison.Ordinal);
    }

    internal static string PackDocument(
        double noseConfidence = 1.0,
        double leftShoulderConfidence = 1.0,
        double rightShoulderConfidence = 1.0)
    {
        var body = BodyValues(noseConfidence, leftShoulderConfidence, rightShoulderConfidence);
        var person = new JsonObject
        {
            ["pose_keypoints_2d"] = new JsonArray(body.Select(value => (JsonNode)value).ToArray()),
            ["hand_left_keypoints_2d"] = new JsonArray(),
            ["hand_right_keypoints_2d"] = new JsonArray()
        };

        var document = new JsonObject
        {
            ["version"] = 1.0,
            ["people"] = new JsonArray(person),
            ["canvas_width"] = 512,
            ["canvas_height"] = 768
        };

        return document.ToJsonString();
    }

    private static string PersonObject() => $$"""
        { "pose_keypoints_2d": [{{string.Join(", ", BodyValues(1.0, 1.0, 1.0))}}] }
        """;

    /// <summary>
    /// A plausible upright figure: nose high, neck below it, shoulders either side, then limbs. Coordinates are
    /// only required to be ordered and distinct so the fit and the pair predicate have something real to work on.
    /// </summary>
    private static double[] BodyValues(
        double noseConfidence, double leftShoulderConfidence, double rightShoulderConfidence)
    {
        var points = new (double X, double Y, double C)[]
        {
            (100, 40, noseConfidence),      // 0 nose
            (100, 60, 1.0),                 // 1 neck
            (80, 70, rightShoulderConfidence),  // 2 right shoulder
            (70, 110, 1.0),                 // 3 right elbow
            (60, 150, 1.0),                 // 4 right wrist
            (120, 70, leftShoulderConfidence),  // 5 left shoulder
            (130, 110, 1.0),                // 6 left elbow
            (140, 150, 1.0),                // 7 left wrist
            (85, 130, 1.0),                 // 8 right hip
            (85, 200, 1.0),                 // 9 right knee
            (85, 270, 1.0),                 // 10 right ankle
            (115, 130, 1.0),                // 11 left hip
            (115, 200, 1.0),                // 12 left knee
            (115, 270, 1.0),                // 13 left ankle
            (95, 38, 1.0),                  // 14 right eye
            (105, 38, 1.0),                 // 15 left eye
            (90, 42, 1.0),                  // 16 right ear
            (110, 42, 1.0)                  // 17 left ear
        };

        return points.SelectMany(p => new[] { p.X, p.Y, p.C }).ToArray();
    }
}
