using System.Text.Json;
using System.Text.Json.Serialization;

namespace DreamGenClone.Web.Application.RolePlay.Evaluation;

/// <summary>
/// The single read/write contract for a run cell's layer verdicts and request snapshot (B-135 B135-015).
///
/// <para>
/// The writer is the run executor and the reader is the Playground, so the options live here rather than as a private
/// field on either side: two private copies is how a stored blob comes to be written in one shape and read in another.
/// </para>
///
/// <para>
/// <b>Enum NAMES, not numbers.</b> A stored verdict reading <c>"outcome":1</c> is unreadable in the database and in the
/// UI, and it silently changes meaning if anyone reorders <see cref="ImagePromptCheckOutcome"/>. The name is stable and
/// legible, so a run recorded today still says what it meant after the code moves on.
/// </para>
/// </summary>
public static class ImageLayerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// Reads the checks a layer recorded. An empty blob means the layer has not run, which is a legitimate state and
    /// reads as an empty list. <b>Malformed JSON throws</b> rather than returning an empty list: unreadable evidence must
    /// never be indistinguishable from "no checks recorded", because the second reads as a clean run.
    /// </summary>
    public static IReadOnlyList<ImagePromptCheck> ReadChecks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ImagePromptCheck>>(json, Options) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"A recorded layer verdict is not readable: {exception.Message}. The run's evidence is corrupt, and "
                + "showing no checks would read as a clean run.", exception);
        }
    }
}
