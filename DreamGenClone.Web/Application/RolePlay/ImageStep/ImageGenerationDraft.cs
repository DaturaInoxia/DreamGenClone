using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

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

    /// <summary>
    /// What the operator TYPED as the description, so the studio's "Your input" gets their words back rather than the
    /// text that rendered. Metadata owns this once the image recorded it: the row's <c>Prompt</c> holds the text that
    /// was submitted, which a compiler replaces with the model-ready prompt, and an operator's description is not the
    /// generated prompt. An image with no recorded input reports the row's <c>Prompt</c> instead, which is what its
    /// description has always been.
    /// </summary>
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
    IReadOnlyList<ReferenceApplicationSelection> Bindings,

    /// <summary>
    /// The pose library preset the render was conditioned on, when a preset was used, with the skeleton path that was
    /// sent. Null means no library preset was recorded — which is a different fact from "no pose", because a render can
    /// carry a bare STANCE instead, and that travels as a binding.
    /// </summary>
    string? PosePresetId,

    string? PoseSkeletonRelativePath,

    /// <summary>The character (identity) LoRAs that were applied, so the picker comes back showing them.</summary>
    IReadOnlyList<SceneImageCharacterLoraSelection> CharacterLoras,

    /// <summary>The non-identity scene LoRAs that were applied (unlock / act / anatomy / style), in chain order.</summary>
    IReadOnlyList<SceneImageLoraSelection> SceneLoras,

    /// <summary>
    /// The lighting / expression presets that shaped the render, each with the clause that reached the prompt.
    ///
    /// <para>
    /// Loaded with the CLAUSE seeded rather than re-applied: the prompt being restored already contains that wording,
    /// so applying it again would insert it twice. Empty means the image recorded none.
    /// </para>
    /// </summary>
    IReadOnlyList<AppliedImagePreset> AppliedPresets)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ImageGenerationDraft FromImage(SceneAssetImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var association = ReadObject(image.AssociationMetadataJson);

        // The operator's own description, or null when they wrote the prompt itself (or the row predates the field).
        var typedDescription = ReadString(association, "userInput");

        return new ImageGenerationDraft(
            image.Id,
            // Their words when the image recorded them; otherwise the row's Prompt column, which IS the description on
            // an image whose prompt was written by hand and the compiled text on one a compiler authored. The row's
            // semanticDescription metadata is a copy of that column, so the column is read directly and an uploaded or
            // pre-metadata row still reports what it holds. A blank column reports NULL, not the empty string the column
            // defaults to, so "never recorded" and "empty" read the same way - which is the only reading a caller can
            // act on.
            typedDescription ?? (string.IsNullOrWhiteSpace(image.Prompt) ? null : image.Prompt),
            ReadString(association, "compiledPrompt"),
            ReadString(association, "requestedModelId"),
            ReadString(association, "imageSize"),
            ReadString(association, "negativePrompt"),
            // The COLUMN owns the seed that reached the sampler (the metadata copy is a duplicate of it).
            image.Seed,
            ReadBindings(ReadString(association, "referenceApplicationsJson")),
            ReadString(association, "posePresetId"),
            ReadString(association, "poseSkeletonRelativePath"),
            ReadList<SceneImageCharacterLoraSelection>(association, "characterLoras"),
            ReadList<SceneImageLoraSelection>(association, "sceneLoras"),
            ReadList<AppliedImagePreset>(association, "appliedPresets"));
    }

    /// <summary>True when this draft carries anything worth loading, so a caller can say so instead of doing nothing.</summary>
    public bool HasAnythingToLoad =>
        !string.IsNullOrWhiteSpace(UserInput)
        || !string.IsNullOrWhiteSpace(CompiledPrompt)
        || !string.IsNullOrWhiteSpace(ModelId)
        || !string.IsNullOrWhiteSpace(PosePresetId)
        || CharacterLoras.Count > 0
        || SceneLoras.Count > 0
        || AppliedPresets.Count > 0
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

    /// <summary>
    /// One array property of the metadata, deserialized to the type the render STORED it as. Reading it as the same
    /// record the handler serialized is what keeps the stored shape and the loaded shape from drifting into two
    /// near-identical types — and a missing or unreadable array loads as NONE, because a partial load would reproduce a
    /// different render while looking like a success.
    /// </summary>
    private static IReadOnlyList<T> ReadList<T>(JsonDocument? document, string property)
    {
        if (document is null
            || !document.RootElement.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        try
        {
            return element.Deserialize<IReadOnlyList<T>>(JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
