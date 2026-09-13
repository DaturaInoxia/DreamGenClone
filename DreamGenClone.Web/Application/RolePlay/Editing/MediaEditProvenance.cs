using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// What one operation row's provenance says about how the row was produced: the operation, and the checksum
/// of the exact file it was produced from.
/// </summary>
public sealed record MediaEditOperationRecord(
    MediaEditOperationKind Kind,
    string SourceImageSha256);

/// <summary>
/// The single mapping between an operation row's recorded provenance and the operation that produced it.
///
/// Every operation writes its kind (<see cref="OperationKey"/>) and its source's checksum
/// (<see cref="SourceShaKey"/>) into the derived image's <c>SourceProvenanceJson</c>, and every reader that
/// needs to know how an image was produced reads it back through here. One writer shape, one reader: an
/// operation cannot be classified two different ways in two places.
/// </summary>
public static class MediaEditProvenance
{
    /// <summary>The provenance key holding the operation's kind.</summary>
    public const string OperationKey = "operation";

    /// <summary>The provenance key holding the SHA-256 of the file the operation consumed.</summary>
    public const string SourceShaKey = "sourceImageSha256";

    public const string EditValue = "edit";

    public const string CropValue = "crop";

    public const string EnhanceValue = "enhance";

    /// <summary>The provenance value for an operation kind — what the writers record.</summary>
    public static string OperationValue(MediaEditOperationKind kind) => kind switch
    {
        MediaEditOperationKind.Edit => EditValue,
        MediaEditOperationKind.Crop => CropValue,
        MediaEditOperationKind.Enhance => EnhanceValue,
        _ => throw new InvalidOperationException(
            $"There is no provenance value for operation kind '{kind}'.")
    };

    /// <summary>
    /// Reads an operation row's provenance, failing fast rather than guessing: an absent, unreadable or
    /// unrecognised record means this image's origin is unknown, and a caller must not invent one (a guessed
    /// "edit" would silently claim the image was de-clothed).
    /// </summary>
    public static MediaEditOperationRecord RequireOperation(string? sourceProvenanceJson, string imageId)
    {
        if (string.IsNullOrWhiteSpace(sourceProvenanceJson))
        {
            throw new InvalidOperationException(
                $"Image '{imageId}' has no recorded provenance, so the operation that produced it cannot be determined.");
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(sourceProvenanceJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Image '{imageId}' has unreadable provenance, so the operation that produced it cannot be determined.", ex);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Image '{imageId}' records unreadable provenance, so the operation that produced it cannot be determined.");
        }

        var operation = RequireString(root, OperationKey, imageId);
        var sourceSha256 = RequireString(root, SourceShaKey, imageId);

        var kind = operation switch
        {
            EditValue => MediaEditOperationKind.Edit,
            CropValue => MediaEditOperationKind.Crop,
            EnhanceValue => MediaEditOperationKind.Enhance,
            _ => throw new InvalidOperationException(
                $"Image '{imageId}' records unknown operation '{operation}' in its provenance.")
        };

        return new MediaEditOperationRecord(kind, sourceSha256);
    }

    private static string RequireString(JsonElement root, string key, string imageId)
    {
        if (!root.TryGetProperty(key, out var element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidOperationException(
                $"Image '{imageId}' records no '{key}' in its provenance, so how it was produced cannot be determined.");
        }

        return element.GetString()!;
    }
}
