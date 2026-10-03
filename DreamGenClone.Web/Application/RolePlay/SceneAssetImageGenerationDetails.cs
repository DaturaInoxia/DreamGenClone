using System.Text.Json;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Everything one generated asset image was made with, read back from the image's OWN row.
///
/// <para>
/// <b>Read from the row, never from the queue.</b> A durable job's payload is gone once the job completes; the
/// completed image's <c>AssociationMetadataJson</c> (rewritten by <c>SceneAssetGenerationJobHandler</c>) is the
/// authoritative record. A field the image did not record is null, and null is the honest answer — an uploaded image,
/// or a row written before the field existed — never a defaulted value. Substituting a default would make the
/// review surface lie about what rendered.
/// </para>
///
/// <para>
/// <b>Ids, not names.</b> The image records which POSE, which CHARACTER, which BODY and which LoRAs it used, by id;
/// the ids are what the render actually read, so they are what is persisted. Resolving them to human names is a VIEW
/// concern (a name can change, a row can be superseded), done by the surface that shows this record, never rewritten
/// back onto the image.
/// </para>
/// </summary>
public sealed record SceneAssetImageGenerationDetails(
    string ImageId,

    /// <summary>The description that was submitted (the row's own Prompt column, a copy of the payload's semanticDescription).</summary>
    string? SemanticDescription,

    /// <summary>The prompt that actually reached the sampler, when a compiler authored one.</summary>
    string? CompiledPrompt,

    string? CompilerId,

    /// <summary>The registered model id that rendered.</summary>
    string? ModelId,

    string? ImageSize,

    string? NegativePrompt,

    /// <summary>The seed the sampler actually received (the image column owns this).</summary>
    long? Seed,

    /// <summary>The bound references, exactly as the render carried them.</summary>
    IReadOnlyList<ReferenceApplicationSelection> References,

    string? PosePresetId,

    string? PoseSkeletonRelativePath,

    string? PoseStance,

    double? PoseStrength,

    string? IdentityPackId,

    string? IdentityFaceAssetId,

    string? BodyReferencePackId,

    string? BodyReferenceAssetId,

    string? BodyAngleView,

    string? BodyAngleSourceImageId,

    /// <summary>The character LoRAs applied, each as its artifact id and strength.</summary>
    IReadOnlyList<SceneImageCharacterLoraSelection> CharacterLoras,

    /// <summary>
    /// The NON-IDENTITY scene LoRAs applied (unlock / act / anatomy / style), in the order they were chained, each as
    /// its ComfyUI file name, strength and catalog display name. Empty when the image recorded none — a
    /// distinguishable fact from a row written before this field existed, which reads back empty for the same reason
    /// every other unrecorded field does: there is nothing to show.
    /// </summary>
    IReadOnlyList<ResolvedSceneLora> SceneLoras,

    /// <summary>The lighting / expression presets that shaped this render, with the clause each contributed.</summary>
    IReadOnlyList<AppliedImagePreset> AppliedPresets)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SceneAssetImageGenerationDetails FromImage(SceneAssetImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var metadata = ReadObject(image.AssociationMetadataJson);

        return new SceneAssetImageGenerationDetails(
            image.Id,
            string.IsNullOrWhiteSpace(image.Prompt) ? null : image.Prompt,
            ReadString(metadata, "compiledPrompt"),
            ReadString(metadata, "compilerId"),
            ReadString(metadata, "requestedModelId"),
            ReadString(metadata, "imageSize"),
            // The handler records the negative it actually resolved in the metadata; the image column is only set at
            // enqueue, so it is null for a precompiled render even when a profile negative was used. The empty string
            // is preserved because "the author chose an empty negative" and "no negative was authored" are different.
            ReadNegative(metadata),
            image.Seed,
            ReadBindings(ReadString(metadata, "referenceApplicationsJson")),
            ReadString(metadata, "posePresetId"),
            ReadString(metadata, "poseSkeletonRelativePath"),
            ReadString(metadata, "poseStance"),
            ReadDouble(metadata, "poseStrength"),
            ReadString(metadata, "identityPackId"),
            ReadString(metadata, "identityFaceAssetId"),
            ReadString(metadata, "bodyReferencePackId"),
            ReadString(metadata, "bodyReferenceAssetId"),
            ReadString(metadata, "bodyAngleView"),
            ReadString(metadata, "bodyAngleSourceImageId"),
            ReadLoras(metadata),
            ReadSceneLoras(metadata),
            ReadAppliedPresets(metadata));
    }

    /// <summary>True when this image recorded anything beyond its own bytes — i.e. when there is something to show.</summary>
    public bool HasAnythingToShow =>
        !string.IsNullOrWhiteSpace(SemanticDescription)
        || !string.IsNullOrWhiteSpace(CompiledPrompt)
        || !string.IsNullOrWhiteSpace(ModelId)
        || References.Count > 0
        || !string.IsNullOrWhiteSpace(PosePresetId)
        || !string.IsNullOrWhiteSpace(IdentityPackId);

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
            // Unreadable provenance is the same fact as absent provenance for a READ-BACK: there is nothing to show.
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

    private static double? ReadDouble(JsonDocument? document, string property)
    {
        if (document is null
            || !document.RootElement.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return element.GetDouble();
    }

    private static string? ReadNegative(JsonDocument? document)
    {
        if (document is null
            || !document.RootElement.TryGetProperty("negativePrompt", out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        // Preserved as-is: the empty string is a real answer, distinct from an absent field.
        return element.GetString();
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
            return [];
        }
    }

    private static IReadOnlyList<SceneImageCharacterLoraSelection> ReadLoras(JsonDocument? document)
    {
        if (document is null
            || !document.RootElement.TryGetProperty("characterLoras", out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        try
        {
            return element.Deserialize<IReadOnlyList<SceneImageCharacterLoraSelection>>(JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The scene LoRAs, read from the SAME property name the handler writes. Kept as the resolved record so the shape
    /// stored and the shape read back cannot drift into two near-identical types.
    /// </summary>
    private static IReadOnlyList<ResolvedSceneLora> ReadSceneLoras(JsonDocument? document)
    {
        if (document is null
            || !document.RootElement.TryGetProperty("sceneLoras", out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        try
        {
            return element.Deserialize<IReadOnlyList<ResolvedSceneLora>>(JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<AppliedImagePreset> ReadAppliedPresets(JsonDocument? document)
    {
        if (document is null
            || !document.RootElement.TryGetProperty("appliedPresets", out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        try
        {
            return element.Deserialize<IReadOnlyList<AppliedImagePreset>>(JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
