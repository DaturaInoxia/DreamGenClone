using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Parses the approved measurement tool's stdout. Extracted so the Validate step and the head-aware crop
/// read the tool's output through ONE implementation instead of two drifting copies.
/// </summary>
public static class CharacterIdentityMeasurementParser
{
    /// <summary>The tool's own token for "I found no face in this image".</summary>
    public const string NoFaceMeshToolError = "no face mesh";

    public static CharacterIdentityEyeMeasurement Parse(CharacterIdentityMeasurementRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var jsonLine = run.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.StartsWith('{'));

        if (jsonLine is null)
        {
            var detail = string.IsNullOrWhiteSpace(run.StandardError)
                ? "no stderr output"
                : run.StandardError.Trim();
            throw new InvalidOperationException(
                $"The eye-validation tool produced no result (exit code {run.ExitCode}). {detail}");
        }

        using var document = JsonDocument.Parse(jsonLine);
        var root = document.RootElement;
        var error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
            ? errorElement.GetString()
            : null;

        var measurement = new CharacterIdentityEyeMeasurement
        {
            IrisDyPercent = ReadNumber(root, "iris_dy_pct"),
            EyeDyPercent = ReadNumber(root, "eye_dy_pct"),
            InterocularPixels = ReadNumber(root, "interoc"),
            Head = ReadHeadExtent(root),
            Error = error
        };

        if (!string.IsNullOrWhiteSpace(error) && !string.Equals(error, NoFaceMeshToolError, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The eye-validation tool reported: {error}");
        }

        if (measurement.IrisDyPercent is null && string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException("The eye-validation tool returned no iris measurement.");
        }

        return measurement;
    }

    private static double? ReadNumber(JsonElement root, string property)
        => root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number
            ? element.GetDouble()
            : null;

    /// <summary>
    /// The head extent the same tool reports: hairline (landmark 10) to chin (152), plus the face box. The
    /// extent is absent when the tool found no face mesh, which is exactly the case where a head crop
    /// cannot be placed — so it stays null rather than being guessed from the eye values. The face box may
    /// be absent on its own (older tool output), in which case a head-FRAMED crop refuses rather than
    /// framing a face it cannot locate.
    /// </summary>
    private static CharacterIdentityHeadMeasurement? ReadHeadExtent(JsonElement root)
    {
        var foreheadTop = ReadPointY(root, "forehead_top");
        var chin = ReadPointY(root, "chin");
        if (foreheadTop is null || chin is null)
            return null;

        return new CharacterIdentityHeadMeasurement(foreheadTop.Value, chin.Value, ReadFaceBox(root));
    }

    private static CharacterIdentityFaceBox? ReadFaceBox(JsonElement root)
    {
        if (!root.TryGetProperty("face_box", out var element)
            || element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() < 4)
        {
            return null;
        }

        var values = new int[4];
        for (var index = 0; index < 4; index++)
        {
            if (element[index].ValueKind != JsonValueKind.Number)
                return null;

            values[index] = (int)Math.Round(element[index].GetDouble());
        }

        return values[2] > 0 && values[3] > 0
            ? new CharacterIdentityFaceBox(values[0], values[1], values[2], values[3])
            : null;
    }

    private static int? ReadPointY(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() < 2
            || element[1].ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return (int)Math.Round(element[1].GetDouble());
    }
}
