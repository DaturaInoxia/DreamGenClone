using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// What an existing image was generated with, read back from that image's own row so it can be loaded into the
/// composer and reproduced, varied, or used as the starting point for an edit.
///
/// <para>
/// <b>Read from the row, never from the queue.</b> A durable job's payload is gone once the job completes; the
/// completed image's own metadata is the authoritative record of how it was made. Which is why the property names read
/// here are <c>SceneAssetGenerationJobHandler</c>'s: it REWRITES <c>AssociationMetadataJson</c> when the bytes arrive,
/// so a completed image carries that handler's shape rather than the enqueue payload's.
/// </para>
///
/// <para>
/// <b>Each field is read from the one place that owns it, and a missing field stays null.</b> This is a read-back, so
/// "not recorded" is a real answer (an uploaded image, or a row written before a field existed). Substituting a
/// default would make a reproduction silently differ from the original, which is the one thing this exists to prevent.
/// </para>
/// </summary>
public sealed record ImageGenerationDraft(
    string ImageId,

    /// <summary>What was typed or supplied as the description. The row's own <c>Prompt</c> column owns this.</summary>
    string? UserInput,

    /// <summary>The prompt that actually rendered, when a compiler authored one. Metadata owns this.</summary>
    string? CompiledPrompt,

    /// <summary>The registered model that rendered, which is what the composer's picker needs.</summary>
    string? ModelId,

    string? ImageSize,

    /// <summary>The negative the render used, when one was recorded.</summary>
    string? NegativePrompt,

    /// <summary>The recorded sampler seed, when one was recorded.</summary>
    long? Seed,

    /// <summary>The references that were bound, so the composer's tabs show what the render actually carried.</summary>
    IReadOnlyList<ReferenceApplicationSelection> Bindings)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ImageGenerationDraft FromImage(SceneAssetImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var association = ReadObject(image.AssociationMetadataJson);

        return new ImageGenerationDraft(
            image.Id,
            // The row's Prompt column is the description that was submitted; the handler's semanticDescription is a
            // copy of it. Reading the column means an uploaded or pre-metadata row still reports what it holds. A blank
            // column reports NULL, not the empty string the column defaults to, so "never recorded" and "empty" read
            // the same way - which is the only reading a caller can act on.
            string.IsNullOrWhiteSpace(image.Prompt) ? null : image.Prompt,
            ReadString(association, "compiledPrompt"),
            ReadString(association, "requestedModelId"),
            ReadString(association, "imageSize"),
            ReadString(association, "negativePrompt"),
            // The COLUMN owns the seed that reached the sampler (the metadata copy is a duplicate of it).
            image.Seed,
            ReadBindings(ReadString(association, "referenceApplicationsJson")));
    }

    /// <summary>True when this draft carries anything worth loading, so a caller can say so instead of doing nothing.</summary>
    public bool HasAnythingToLoad =>
        !string.IsNullOrWhiteSpace(UserInput)
        || !string.IsNullOrWhiteSpace(CompiledPrompt)
        || !string.IsNullOrWhiteSpace(ModelId)
        || Bindings.Count > 0;

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
            // Unreadable provenance is the same fact as absent provenance for a READ-BACK: there is nothing to load.
            // It is not an error the operator can act on, and refusing to open the image over it would be worse.
            return null;
        }
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

    private static IReadOnlyList<ReferenceApplicationSelection> ReadBindings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<ReferenceApplicationSelection>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // Same posture as a missing field: an unreadable binding set loads nothing, rather than half of itself.
            return [];
        }
    }
}
