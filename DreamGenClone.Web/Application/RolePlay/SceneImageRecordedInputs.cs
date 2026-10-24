using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The render inputs a scene image RECORDED, read back out of its own settings snapshot so a surface can re-apply
/// them — the "reload what made this image" half of the card's reload command.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reading the JSON, never deserializing the settings type.</b> <see cref="SceneImageStudioSettings"/> carries
/// defaults — a CFG, a step count, a sampler, a scheduler and an image size — so deserializing a row that recorded none
/// of them would produce values the render never used, and a surface that re-applied them would be making an invented
/// claim about the picture. Only the members the JSON actually holds come back here; everything else is empty or null.
/// </para>
///
/// <para>
/// <b>A pose without its strength stays strength-less.</b> The skeleton a render conditioned on and the strength it
/// conditioned at are two facts; when the row records only the first, the second comes back null and the caller can say
/// so instead of applying a strength nobody chose.
/// </para>
///
/// <para>
/// <b>Unreadable settings read as nothing recorded</b>, the same posture the display read-back
/// (<see cref="ImageGenerationInputDetails"/>) takes: a blob that cannot be parsed is not a description of a render, and
/// half-applying a corrupt one would be worse than applying none.
/// </para>
/// </remarks>
public sealed record SceneImageRecordedInputs(
    IReadOnlyList<SceneImageCharacterLoraSelection> CharacterLoras,
    IReadOnlyList<SceneImageLoraSelection> SceneLoras,
    string? PoseSkeletonRelativePath,
    double? PoseStrength)
{
    /// <summary>Nothing recorded — an image that named no LoRA and no pose to condition on.</summary>
    public static SceneImageRecordedInputs None { get; } = new([], [], null, null);

    /// <summary>Reads the recorded inputs out of an image's own settings snapshot.</summary>
    public static SceneImageRecordedInputs Read(string? settingsJson)
    {
        using var document = ReadObject(settingsJson);
        if (document is null)
        {
            return None;
        }

        var characterLoras = ReadList<SceneImageCharacterLoraSelection>(document, "CharacterLoras");
        var sceneLoras = ReadList<SceneImageLoraSelection>(document, "SceneLoras");
        using var pose = ReadObjectProperty(document, "PoseReference");

        return new SceneImageRecordedInputs(
            characterLoras,
            sceneLoras,
            ReadString(pose, "StoragePath"),
            ReadDouble(pose, "Strength"));
    }

    private static JsonDocument? ReadObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonDocument? ReadObjectProperty(JsonDocument document, string property)
    {
        if (!document.RootElement.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadObject(element.GetRawText());
    }

    private static string? ReadString(JsonDocument? document, string property)
    {
        if (document is null
            || !document.RootElement.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static double? ReadDouble(JsonDocument? document, string property)
    {
        if (document is null
            || !document.RootElement.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out var value))
        {
            return null;
        }

        return value;
    }

    private static IReadOnlyList<T> ReadList<T>(JsonDocument document, string property)
    {
        if (!document.RootElement.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        try
        {
            return element.Deserialize<IReadOnlyList<T>>() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
