using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Infrastructure.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The wire format of the pose estimator, pinned.
///
/// This file exists because of one measured trap. On 2026-09-26 the local host returned, for a 1024x1536 plate:
/// <c>canvas_width = 1024</c>, <c>canvas_height = 1536</c>, and a nose of <c>0.449, 0.111</c> — the keypoints are
/// FRACTIONS of the canvas, while the app stores keypoints in SOURCE-IMAGE PIXELS (the pack's own JSON holds 373.9 on
/// a 512x768 canvas). Storing the fractions unscaled produces a skeleton the size of a stamp that fits nothing, and it
/// would do so while reporting a successful extraction. So the scaling is asserted on a recorded document rather than
/// left to a live run, and the four refusals are asserted too: each one is a different problem an operator would
/// otherwise have to guess at.
/// </summary>
public sealed class ComfyUIPoseKeypointExtractorTests
{
    [Fact]
    public void ReadPersonInPixels_ScalesCanvasFractionsIntoSourceImagePixels()
    {
        var history = HistoryWith(Person(x: 0.5, y: 0.25), canvasWidth: 1024, canvasHeight: 1536);

        var json = ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-1");

        var person = JsonNode.Parse(json)!.AsObject();
        var body = person["pose_keypoints_2d"]!.AsArray();

        Assert.Equal(54, body.Count);
        Assert.Equal(0.5 * 1024, body[0]!.GetValue<double>(), 6);
        Assert.Equal(0.25 * 1536, body[1]!.GetValue<double>(), 6);

        // The confidence is a probability and is NOT scaled — scaling it would make every joint look visible.
        Assert.Equal(1.0, body[2]!.GetValue<double>(), 6);
    }

    [Fact]
    public void ReadPersonInPixels_KeepsTheHandsAndScalesThemTheSameWay()
    {
        var history = HistoryWith(Person(x: 0.5, y: 0.25, hands: true), canvasWidth: 1024, canvasHeight: 1536);

        var json = ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-1");

        var person = JsonNode.Parse(json)!.AsObject();
        var left = person["hand_left_keypoints_2d"]!.AsArray();
        var right = person["hand_right_keypoints_2d"]!.AsArray();

        Assert.Equal(63, left.Count);
        Assert.Equal(63, right.Count);
        Assert.Equal(0.5 * 1024, left[0]!.GetValue<double>(), 6);
        Assert.Equal(0.25 * 1536, left[1]!.GetValue<double>(), 6);
    }

    [Fact]
    public void ReadPersonInPixels_RefusesAPersonWithNoBodyKeypoints()
    {
        var history = HistoryWith("""{ "pose_keypoints_2d": [1, 2, 3] }""", canvasWidth: 1024, canvasHeight: 1536);

        var error = Assert.Throws<ImageGenerationException>(
            () => ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-1"));

        // The count is in the message: a partial figure has to be distinguishable from no figure at all.
        Assert.Contains("3 values", error.Message);
        Assert.Contains("18 joints", error.Message);
    }

    [Fact]
    public void ReadPersonInPixels_RefusesSeveralPeople()
    {
        var document = $$"""
            [{ "canvas_width": 1024, "canvas_height": 1536,
               "people": [ {{Person(x: 0.5, y: 0.25)}}, {{Person(x: 0.6, y: 0.26)}} ] }]
            """;
        var history = HistoryWithRaw(document);

        var error = Assert.Throws<ImageGenerationException>(
            () => ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-1"));

        Assert.Contains("found 2 people", error.Message);
    }

    [Fact]
    public void ReadPersonInPixels_RefusesADocumentWithNoPeople()
    {
        var history = HistoryWithRaw("""[{ "canvas_width": 1024, "canvas_height": 1536, "people": [] }]""");

        var error = Assert.Throws<ImageGenerationException>(
            () => ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-1"));

        Assert.Contains("found no person", error.Message);
    }

    /// <summary>
    /// Without the canvas there is no way to put fractions into the pixel space the store uses, and guessing a size
    /// would store a pose whose scale is silently wrong — so the refusal is the correct behaviour, not a gap.
    /// </summary>
    [Fact]
    public void ReadPersonInPixels_RefusesADocumentThatDoesNotSayWhatCanvasTheFractionsAreOf()
    {
        var document = $$"""[{ "people": [ {{Person(x: 0.5, y: 0.25)}} ] }]""";
        var history = HistoryWithRaw(document);

        var error = Assert.Throws<ImageGenerationException>(
            () => ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-1"));

        Assert.Contains("canvas_width", error.Message);
    }

    [Fact]
    public void ReadPersonInPixels_NamesTheMissingNodeWhenTheHostDidNotEmitKeypoints()
    {
        // A cache hit, or a host without the node, comes back through /history with no openpose_json at all.
        var history = JsonNode.Parse("""{ "outputs": { "3": { "images": [] } } }""")!.AsObject();

        var error = Assert.Throws<ImageGenerationException>(
            () => ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-7"));

        Assert.Contains(ComfyUIPoseKeypointExtractor.NodeName, error.Message);
        Assert.Contains("prompt-7", error.Message);
    }

    [Fact]
    public void ReadPersonInPixels_KeepsAndScalesTheFace_WhenAHeadOnlyPoseWasAskedFor()
    {
        var history = HistoryWith(Person(x: 0.5, y: 0.25, face: true), canvasWidth: 1024, canvasHeight: 1536);

        var json = ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-1", keepFace: true);

        var face = JsonNode.Parse(json)!.AsObject()["face_keypoints_2d"]!.AsArray();

        // 70 points, scaled by the SAME canvas as the body: a face scaled differently from its own head would be
        // drawn beside its skull.
        Assert.Equal(210, face.Count);
        Assert.Equal(0.5 * 1024, face[0]!.GetValue<double>(), 6);
        Assert.Equal(0.25 * 1536, face[1]!.GetValue<double>(), 6);
    }

    [Fact]
    public void ReadPersonInPixels_OmitsTheFaceChannel_ForABodyPose()
    {
        var history = HistoryWith(Person(x: 0.5, y: 0.25, face: true), canvasWidth: 1024, canvasHeight: 1536);

        var json = ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-1");

        // The estimator returned a face; a body pose does not store it. Fine face detail at full-body scale is a
        // smear, so "the estimator gave us one" is not a reason to keep it.
        Assert.Null(JsonNode.Parse(json)!.AsObject()["face_keypoints_2d"]);
    }

    [Fact]
    public void ReadPersonInPixels_RefusesAHeadOnlyPoseThatCameBackWithNoFace()
    {
        var history = HistoryWith(Person(x: 0.5, y: 0.25), canvasWidth: 1024, canvasHeight: 1536);

        var error = Assert.Throws<ImageGenerationException>(
            () => ComfyUIPoseKeypointExtractor.ReadPersonInPixels(history, "ComfyUI", "prompt-1", keepFace: true));

        // A head-only pose IS its face, so no face is a refusal rather than a pose that happens to lack one.
        Assert.Contains("head-only", error.Message);
        Assert.Contains("70", error.Message);
    }

    /// <summary>One person as the estimator emits it: fractions of the canvas, confidence unscaled.</summary>
    private static string Person(double x, double y, bool hands = false, bool face = false)
    {
        var body = string.Join(", ", Enumerable.Range(0, 18).SelectMany(_ => new[] { x, y, 1.0 })
            .Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var person = $$"""{ "pose_keypoints_2d": [{{body}}]""";

        if (hands)
        {
            var hand = string.Join(", ", Enumerable.Range(0, 21).SelectMany(_ => new[] { x, y, 1.0 })
                .Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            person += $$""", "hand_left_keypoints_2d": [{{hand}}], "hand_right_keypoints_2d": [{{hand}}]""";
        }

        if (face)
        {
            var points = string.Join(", ", Enumerable.Range(0, 70).SelectMany(_ => new[] { x, y, 1.0 })
                .Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            person += $$""", "face_keypoints_2d": [{{points}}]""";
        }

        return person + " }";
    }

    private static JsonObject HistoryWith(string person, int canvasWidth, int canvasHeight) =>
        HistoryWithRaw($$"""[{ "canvas_width": {{canvasWidth}}, "canvas_height": {{canvasHeight}}, "people": [ {{person}} ] }]""");

    private static JsonObject HistoryWithRaw(string document) =>
        JsonNode.Parse($$"""{ "outputs": { "2": { "openpose_json": [{{JsonValue.Create(document)!.ToJsonString()}}] } } }""")!
            .AsObject();
}
