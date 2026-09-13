using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The ONE parser for the approved measurement tool's output. The head extent it extracts is what a
/// head-aware crop is placed against, so these tests pin both the extraction and the fact that a run with
/// no face reports no head (rather than inventing one).
/// </summary>
public sealed class CharacterIdentityMeasurementParserTests
{
    [Fact]
    public void Parse_ReadsTheHeadExtentTheToolReports()
    {
        var run = new CharacterIdentityMeasurementRun(0, Json("-0.2", "0.4", "130", null, foreheadTopY: 277, chinY: 820), string.Empty);

        var measurement = CharacterIdentityMeasurementParser.Parse(run);

        Assert.NotNull(measurement.Head);
        Assert.Equal(277, measurement.Head!.ForeheadTopY);
        Assert.Equal(820, measurement.Head.ChinY);
        Assert.Equal(543, measurement.Head.HeadHeightPx);
    }

    [Fact]
    public void Parse_WithoutAFace_ReportsNoHeadInsteadOfGuessingOne()
    {
        var run = new CharacterIdentityMeasurementRun(0, Json(null, null, null, CharacterIdentityMeasurementParser.NoFaceMeshToolError), string.Empty);

        var measurement = CharacterIdentityMeasurementParser.Parse(run);

        Assert.Null(measurement.Head);
        Assert.Equal(CharacterIdentityMeasurementParser.NoFaceMeshToolError, measurement.Error);
    }

    [Fact]
    public void Parse_WithoutHeadPoints_LeavesNoHead()
    {
        // Eye values present but no head points: the crop must refuse rather than derive a head from them.
        var run = new CharacterIdentityMeasurementRun(0, Json("-0.2", "0.4", "130", null), string.Empty);

        var measurement = CharacterIdentityMeasurementParser.Parse(run);

        Assert.Null(measurement.Head);
    }

    [Fact]
    public void Parse_ReadsTheFaceBoxThatHeadFramingNeeds()
    {
        var run = new CharacterIdentityMeasurementRun(0, Json("-0.2", "0.4", "130", null, foreheadTopY: 277, chinY: 820), string.Empty);

        var measurement = CharacterIdentityMeasurementParser.Parse(run);

        Assert.NotNull(measurement.Head?.Face);
        Assert.Equal(new CharacterIdentityFaceBox(200, 277, 300, 543), measurement.Head!.Face);
    }

    [Fact]
    public void Parse_WithoutAFaceBox_LeavesTheHeadWithoutOne()
    {
        // Older tool output: the extent is usable for placing a window, but a head-framed crop has no box
        // to frame and must refuse rather than invent one.
        var run = new CharacterIdentityMeasurementRun(0, NoBoxJson(), string.Empty);

        var measurement = CharacterIdentityMeasurementParser.Parse(run);

        Assert.NotNull(measurement.Head);
        Assert.Null(measurement.Head!.Face);
    }

    private static string NoBoxJson()
        => "{\"iris_dy_pct\": -0.2, \"eye_dy_pct\": 0.4, \"interoc\": 130, \"error\": null, " +
           "\"forehead_top\": [454, 277], \"chin\": [489, 820]}";

    private static string Json(
        string? irisDy, string? eyeDy, string? interoc, string? error,
        int? foreheadTopY = null, int? chinY = null)
    {
        var iris = irisDy ?? "null";
        var eye = eyeDy ?? "null";
        var inter = interoc ?? "null";
        var errorValue = error is null ? "null" : $"\"{error}\"";
        var forehead = foreheadTopY is null ? "null" : $"[454, {foreheadTopY}]";
        var chin = chinY is null ? "null" : $"[489, {chinY}]";
        // face_box is [x, y, w, h] over all face landmarks, present only when a face was found.
        var faceBox = foreheadTopY is null || chinY is null
            ? "null"
            : $"[200, {foreheadTopY}, 300, {chinY - foreheadTopY}]";
        return $"{{\"iris_dy_pct\": {iris}, \"eye_dy_pct\": {eye}, \"interoc\": {inter}, \"error\": {errorValue}, " +
               $"\"forehead_top\": {forehead}, \"chin\": {chin}, \"face_box\": {faceBox}}}";
    }
}
