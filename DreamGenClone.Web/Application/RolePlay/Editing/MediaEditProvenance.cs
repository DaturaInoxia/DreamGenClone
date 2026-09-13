using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The single mapping between an operation row's recorded provenance and the operation that produced it.
///
/// Every operation writes its kind into the derived image's <c>SourceProvenanceJson</c> under
/// <see cref="OperationKey"/>, and every reader (the character front pipeline's step derivation, the
/// editor's lineage labels) reads it back through here. Keeping one writer constant and one reader means an
/// operation cannot be classified two different ways in two places.
/// </summary>
public static class MediaEditProvenance
{
    /// <summary>The provenance key holding the operation's kind.</summary>
    public const string OperationKey = "operation";

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
    /// Reads the operation kind back out of an operation row's provenance, failing fast rather than
    /// guessing: an absent, unreadable or unrecognised record means this image's origin is unknown, and a
    /// caller must not invent one (a guessed "edit" would silently claim the image was de-clothed).
    /// </summary>
    public static MediaEditOperationKind RequireOperationKind(string? sourceProvenanceJson, string imageId)
    {
        if (string.IsNullOrWhiteSpace(sourceProvenanceJson))
        {
            throw new InvalidOperationException(
                $"Image '{imageId}' has no recorded provenance, so the operation that produced it cannot be determined.");
        }

        string? operation;
        try
        {
            using var document = JsonDocument.Parse(sourceProvenanceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(OperationKey, out var element)
                || element.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"Image '{imageId}' records no '{OperationKey}' in its provenance, so the operation that produced it cannot be determined.");
            }

            operation = element.GetString();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Image '{imageId}' has unreadable provenance, so the operation that produced it cannot be determined.", ex);
        }

        return operation switch
        {
            EditValue => MediaEditOperationKind.Edit,
            CropValue => MediaEditOperationKind.Crop,
            EnhanceValue => MediaEditOperationKind.Enhance,
            _ => throw new InvalidOperationException(
                $"Image '{imageId}' records unknown operation '{operation}' in its provenance.")
        };
    }
}
