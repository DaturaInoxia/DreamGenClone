using System.Text.Json;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Everything one generated image was made with, read back from the image's OWN row.
///
/// <para>
/// <b>Read from the row, never from the queue.</b> A durable job's payload is gone once the job completes; the
/// completed image's own provenance — an asset image's <c>AssociationMetadataJson</c>, a rendered image's
/// <c>PromptSnapshot</c> plus <c>SettingsJson</c> — is the authoritative record. A field the image did not record is
/// null, and null is the honest answer — an uploaded image, or a row written before the field existed — never a
/// defaulted value. Substituting a default would make the review surface lie about what rendered.
/// </para>
///
/// <para>
/// <b>One shape for both stores.</b> The asset library and the app's own rendered images record their inputs in
/// different places, in different shapes, but they record the SAME facts, and an operator judges the two with the same
/// eyes. Each store gets its own factory (<see cref="FromAssetImage"/>, <see cref="FromSceneImage"/>) and the view is
/// written once against this record — which is what keeps "what made this image" from being answered twice, differently.
/// </para>
///
/// <para>
/// <b>Ids, not names.</b> The image records which POSE, which CHARACTER, which BODY and which LoRAs it used, by id;
/// the ids are what the render actually read, so they are what is persisted. Resolving them to human names is a VIEW
/// concern (a name can change, a row can be superseded), done by the surface that shows this record, never rewritten
/// back onto the image.
/// </para>
/// </summary>
public sealed record ImageGenerationInputDetails(
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

    /// <summary>
    /// A details record for a caller that has none — a null parameter, or a pose read out of a single reference. Every
    /// field is unrecorded, which is what <see cref="HasAnythingToShow"/> then reports.
    /// </summary>
    public static ImageGenerationInputDetails None { get; } = new(
        string.Empty, null, null, null, null, null, null, null, [], null, null, null, null,
        null, null, null, null, null, null, [], [], []);

    /// <summary>
    /// An ASSET-LIBRARY image's inputs, read from the row the asset generation handler rewrote when the bytes arrived.
    /// </summary>
    public static ImageGenerationInputDetails FromAssetImage(SceneAssetImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var metadata = ReadObject(image.AssociationMetadataJson);

        return new ImageGenerationInputDetails(
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
            ReadCharacterLoras(metadata, "characterLoras"),
            ReadSceneLoras(metadata),
            ReadAppliedPresets(metadata));
    }

    /// <summary>
    /// A RENDERED image's inputs, read from the row the scene render wrote: the prompt that reached the model, the
    /// negative it was enqueued with, the model it was pinned to, and the studio's own settings snapshot.
    ///
    /// <para>
    /// The settings JSON is read by PROPERTY, never deserialized into <c>SceneImageStudioSettings</c>. That class
    /// carries defaults for the sampler, the scheduler, CFG and steps — so deserializing a row written before one of
    /// them existed would report a value the render never used. A field the row does not hold stays null, which is the
    /// same rule every other field here follows. The property names are the settings class's own (the studio serializes
    /// it with the default options, so only the two annotated members differ) — reading <c>"PoseReference"</c> here is
    /// reading exactly what the studio wrote.
    /// </para>
    /// </summary>
    /// <param name="image">The rendered image row, with its own settings and binding snapshots.</param>
    public static ImageGenerationInputDetails FromSceneImage(SceneImageRecord image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var settings = ReadObject(image.SettingsJson);
        using var pose = ReadObjectProperty(settings, "PoseReference");

        return new ImageGenerationInputDetails(
            image.Id,
            // A rendered row carries no separately-typed description: the text it holds IS the prompt that reached the
            // model. Reporting it twice, once as a description, would invent a distinction the row does not make.
            null,
            string.IsNullOrWhiteSpace(image.PromptSnapshot) ? null : image.PromptSnapshot,
            // The camera/model provenance of an EDIT is its own compilation record; a composed render's prompt author is
            // the compiled brief, which is a lineage the surface shows elsewhere rather than a compiler id here.
            null,
            // The IDENTIFIER is the model that actually RAN; RequestedModelId is the registration the operator pinned,
            // and on a rendered row that is the registration's id — a GUID. Preferring the request therefore printed
            // "8b2e4d16-3a5f-4c7e-9d10-5f6a7c8b9d20" where the model's name belonged, on every row of production
            // group 0f98cb6f (27 Compositions + 14 edits). The request is still read, for the row that never resolved
            // a model to run — a failed render records no identifier, and its pin is the only provenance it has.
            string.IsNullOrWhiteSpace(image.ModelIdentifier) ? image.RequestedModelId : image.ModelIdentifier,
            string.IsNullOrWhiteSpace(image.ImageSize) ? ReadString(settings, "ImageSize") : image.ImageSize,
            string.IsNullOrWhiteSpace(image.NegativePromptSnapshot) ? null : image.NegativePromptSnapshot,
            ReadLong(settings, "Seed"),
            ReadBindings(image.AppliedReferenceBindingsJson),
            // A rendered image's pose travels in its settings as a stored ControlNet skeleton, with no library preset
            // behind it: the preset id is what an asset render records, and inventing one here would name a preset the
            // render never read.
            null,
            ReadString(pose, "StoragePath"),
            null,
            ReadDouble(pose, "Strength"),
            image.IdentityPackId,
            null,
            null,
            null,
            null,
            null,
            ReadCharacterLoras(settings, "CharacterLoras"),
            ReadSelectedSceneLoras(settings),
            // Lighting and expression presets are recorded on an ASSET render's metadata as the preset keys and the
            // clauses they contributed. A rendered image carries the substitutions instead, which is a different record
            // of the same idea, so nothing is claimed here rather than reconstructed from it.
            []);
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

    /// <summary>
    /// One OBJECT property of a provenance blob, re-parsed so its own properties can be read the same way. A property
    /// that is absent, or is not an object at all, reads as nothing to show.
    /// </summary>
    private static JsonDocument? ReadObjectProperty(JsonDocument? document, string property)
    {
        if (document is null
            || !document.RootElement.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadObject(element.GetRawText());
    }

    private static double? ReadDouble(JsonDocument? document, string property)    {
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

    private static long? ReadLong(JsonDocument? document, string property)
    {
        if (document is null
            || !document.RootElement.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var value))
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// One array property of a provenance blob, read as the type the recording handler wrote it as.
    ///
    /// <para>
    /// An unreadable or missing array reads as NONE, which is the same posture every other read-back here takes: a
    /// partial list would describe a render that was never made, and it would look exactly like a successful read.
    /// </para>
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

    /// <summary>
    /// The character LoRAs a render applied. The two stores name the property differently and both names are passed in
    /// rather than guessed: an asset render's completed metadata is written camelCase by its handler, while the studio
    /// serializes its settings with the default serializer, so there the key IS the property name. Same shape either
    /// way, which is why one view renders both.
    /// </summary>
    private static IReadOnlyList<SceneImageCharacterLoraSelection> ReadCharacterLoras(
        JsonDocument? document, string property)
        => ReadList<SceneImageCharacterLoraSelection>(document, property);

    /// <summary>
    /// The scene LoRAs a render applied, resolved to the shape the view renders: the catalog FILE NAME and the strength.
    ///
    /// <para>
    /// The studio stores its selections as <c>{ fileName, strength }</c>. A selection missing either is not something
    /// the render could have applied — an unstated strength is refused at resolution time rather than guessed — so a
    /// list holding one is reported as NONE rather than partly, exactly as every other unreadable blob here reads.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ResolvedSceneLora> ReadSelectedSceneLoras(JsonDocument? settings)
    {
        var selections = ReadList<SceneImageLoraSelection>(settings, "SceneLoras");
        if (selections.Any(selection =>
            string.IsNullOrWhiteSpace(selection.FileName) || selection.Strength is null))
        {
            return [];
        }

        // No purpose label: that comes from the catalog's own row, which is not what the image recorded. The file name
        // is, and it is what ComfyUI loaded.
        return selections
            .Select(selection => new ResolvedSceneLora(selection.FileName, selection.Strength!.Value))
            .ToList();
    }

    /// <summary>
    /// The scene LoRAs, read from the SAME property name the handler writes. Kept as the resolved record so the shape
    /// stored and the shape read back cannot drift into two near-identical types.
    /// </summary>
    private static IReadOnlyList<ResolvedSceneLora> ReadSceneLoras(JsonDocument? document)
        => ReadList<ResolvedSceneLora>(document, "sceneLoras");

    private static IReadOnlyList<AppliedImagePreset> ReadAppliedPresets(JsonDocument? document)
        => ReadList<AppliedImagePreset>(document, "appliedPresets");
}
