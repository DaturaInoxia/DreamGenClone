using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The ONE reader of a source image's RECORDED references, for every store.
///
/// <para>
/// Each store keeps the record somewhere else — a scene image in its own <c>AppliedReferenceBindingsJson</c> column,
/// an asset image inside the job payload its <c>AssociationMetadataJson</c> preserved — but "what does this row say
/// this image was made from?" has one answer and is read here, so the two adapters cannot drift about what counts as
/// unreadable.
/// </para>
///
/// <para>
/// An unreadable record is reported as unreadable rather than as "none". The caller decides what to show; what it must
/// never do is present a row it could not parse as an image made from nothing.
/// </para>
/// </summary>
internal static class SourceReferenceProvenance
{
    /// <summary>The payload property an asset image's generation/edit job recorded its references under.</summary>
    private const string ReferenceApplicationsProperty = "referenceApplicationsJson";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A scene image's own applied-reference column.</summary>
    public static ImageEditSourceReferences ReadSceneImage(string? appliedReferenceBindingsJson, string imageId)
    {
        if (string.IsNullOrWhiteSpace(appliedReferenceBindingsJson))
        {
            return ImageEditSourceReferences.None;
        }

        return Deserialize(appliedReferenceBindingsJson, imageId, "AppliedReferenceBindingsJson");
    }

    /// <summary>
    /// An asset image's record, which lives inside the job payload its <c>AssociationMetadataJson</c> preserved — the
    /// same payload shape for a generation and for an edit, so the property is read by name rather than by
    /// deserializing one of the two payload types and hoping.
    /// </summary>
    public static ImageEditSourceReferences ReadAssetImage(string? associationMetadataJson, string imageId)
    {
        if (string.IsNullOrWhiteSpace(associationMetadataJson))
        {
            return ImageEditSourceReferences.None;
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(associationMetadataJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return ImageEditSourceReferences.Unreadable(
                $"Image '{imageId}' has unreadable metadata, so the references it was made with cannot be read ({ex.Message}).");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return ImageEditSourceReferences.Unreadable(
                $"Image '{imageId}' records metadata that is not an object, so the references it was made with cannot be read.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, ReferenceApplicationsProperty, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                return ImageEditSourceReferences.None;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return ImageEditSourceReferences.Unreadable(
                    $"Image '{imageId}' records its references as {property.Value.ValueKind}, not as the JSON text they are stored in.");
            }

            var json = property.Value.GetString();
            return string.IsNullOrWhiteSpace(json)
                ? ImageEditSourceReferences.None
                : Deserialize(json, imageId, ReferenceApplicationsProperty);
        }

        // No property at all: a row written before the record existed, or an upload. Both mean "made from nothing".
        return ImageEditSourceReferences.None;
    }

    private static ImageEditSourceReferences Deserialize(string json, string imageId, string sourceName)
    {
        try
        {
            var references = JsonSerializer.Deserialize<List<ReferenceApplicationSelection>>(json, JsonOptions) ?? [];
            return new ImageEditSourceReferences(references, null);
        }
        catch (JsonException ex)
        {
            return ImageEditSourceReferences.Unreadable(
                $"Image '{imageId}' records references that cannot be read from its {sourceName} ({ex.Message}).");
        }
    }
}
