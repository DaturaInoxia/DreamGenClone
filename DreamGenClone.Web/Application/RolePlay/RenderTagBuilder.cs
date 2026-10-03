using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Everything known about a render at the moment its tags are built.
///
/// <para>
/// Split into two kinds of fact on purpose, because they have different owners. <see cref="DeclaredTags"/> are the
/// names only the CALLER knows — the character's display name, the suite cell that was rendered, the wardrobe and
/// location the operator picked, the sex position they typed. The rest are facts the RENDER knows for itself: the
/// pose preset it loaded, the scene LoRAs it resolved, the presets it applied, the model it used.
/// </para>
///
/// <para>
/// Nothing here is inferred from the prompt text or the pixels. A tag is a claim that a search will act on, so a
/// guessed one — "this looks like missionary" — is worse than a missing one.
/// </para>
/// </summary>
public sealed record RenderTagRequest
{
    /// <summary>The caller's own tags, already in catalog shape (<c>prefix:value</c>).</summary>
    public IReadOnlyList<string>? DeclaredTags { get; init; }

    /// <summary>The asset the image belongs to, from which an owner tag is derived by its TYPE.</summary>
    public string? AssetName { get; init; }

    public SceneAssetType? AssetType { get; init; }

    /// <summary>The registered model that rendered.</summary>
    public string? ModelId { get; init; }

    /// <summary>The pose preset that conditioned the render, when a library preset was used.</summary>
    public PosePreset? Pose { get; init; }

    /// <summary>The library the preset came from, when the caller resolved it.</summary>
    public string? PoseLibraryName { get; init; }

    /// <summary>
    /// The stance the payload carried when NO library preset did (a saved skeleton pose). Parsed from the enum name
    /// the payload stores, so a render conditioned on a bare stance is still findable by "kneeling".
    /// </summary>
    public string? PoseStance { get; init; }

    /// <summary>The scene LoRAs that were chained into this render.</summary>
    public IReadOnlyList<ResolvedSceneLora>? SceneLoras { get; init; }

    /// <summary>The lighting / expression presets this render applied.</summary>
    public IReadOnlyList<AppliedImagePreset>? AppliedPresets { get; init; }
}

/// <summary>
/// Builds the tag list one rendered image is stored with, from the facts the render has.
///
/// <para>
/// Its whole job is to be the ONLY place that decides which facts become tags, so two render paths cannot tag the
/// same image differently. It composes nothing itself: every word comes from <see cref="ImageTagCatalog"/>, which
/// owns the vocabulary and the normalization, and every fact comes from the request.
/// </para>
///
/// <para>
/// <b>What it does NOT tag, and why.</b> A character LoRA is carried as an ARTIFACT ID (<c>SceneImageCharacterLoraSelection</c>),
/// and an id is not a word anybody searches for; resolving it to the artifact's name needs a repository this builder
/// deliberately does not take, so a host that knows the LoRA's name declares it instead. Reference bindings are ids
/// for the same reason: the review panel resolves them to names for display, which is where a name belongs.
/// </para>
/// </summary>
public static class RenderTagBuilder
{
    /// <summary>
    /// The tag list for one image: declared tags first (the caller's stated facts), then what the render derived, with
    /// duplicates removed in that order so the stored list is reproducible rather than incidental.
    /// </summary>
    public static IReadOnlyList<string> Build(RenderTagRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tags = new List<string?>();

        // 1. What the caller declared. Kept first so an operator's own words are not reordered behind derived ones.
        if (request.DeclaredTags is { Count: > 0 } declared)
        {
            tags.AddRange(declared);
        }

        // 2. The owner of the image, by the asset's type. A CharacterFace/CharacterBody pack asset belongs to a
        //    character, so it is tagged as one - the two are the same person and searching for them separately would
        //    split a character's pictures in half.
        if (!string.IsNullOrWhiteSpace(request.AssetName) && request.AssetType is { } type)
        {
            tags.Add(type switch
            {
                SceneAssetType.Character or SceneAssetType.CharacterFace or SceneAssetType.CharacterBody
                    => ImageTagCatalog.Tag(ImageTagCatalog.Character, request.AssetName),
                SceneAssetType.Location => ImageTagCatalog.Tag(ImageTagCatalog.Location, request.AssetName),
                SceneAssetType.Wardrobe => ImageTagCatalog.Tag(ImageTagCatalog.Wardrobe, request.AssetName),
                _ => null
            });
        }

        // 3. The pose. The preset carries the four metadata axes as ENUMS (the honest source); a payload stance string
        //    is only consulted when there is no preset, because it is the weaker record of the same fact.
        if (request.Pose is { } pose)
        {
            tags.AddRange(ImageTagCatalog.FromPosePreset(pose, request.PoseLibraryName));
        }
        else if (!string.IsNullOrWhiteSpace(request.PoseStance))
        {
            tags.Add(ImageTagCatalog.StanceFromName(request.PoseStance));
        }

        // 4. The presets that shaped the render, by the WORD in their key ("image.preset.expression.laughing" ->
        //    "expression:laughing"), which is the word the operator picked in the picker.
        foreach (var preset in request.AppliedPresets ?? [])
        {
            var word = LastSegment(preset.Key);
            tags.Add(preset.Axis switch
            {
                ImagePresetAxis.Expression => ImageTagCatalog.Tag(ImageTagCatalog.Expression, word),
                ImagePresetAxis.Lighting => ImageTagCatalog.Tag(ImageTagCatalog.Lighting, word),
                _ => null
            });
        }

        // 5. The scene LoRAs, by the catalog's own purpose ("Krea2 NSFW unlock", "Cowgirl act LoKr"). The purpose is
        //    what makes "which act LoRA was in this render" a query instead of a file-name grep; it falls back to the
        //    file's stem only when the catalog recorded no purpose, because the file name is at least a real word.
        foreach (var lora in request.SceneLoras ?? [])
        {
            tags.Add(ImageTagCatalog.Tag(
                ImageTagCatalog.SceneLora,
                string.IsNullOrWhiteSpace(lora.Purpose) ? Stem(lora.FileName) : lora.Purpose));
        }

        tags.Add(ImageTagCatalog.Tag(ImageTagCatalog.Model, request.ModelId));

        return ImageTagCatalog.NormalizeAll(tags);
    }

    private static string? LastSegment(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot >= 0 && lastDot < trimmed.Length - 1 ? trimmed[(lastDot + 1)..] : trimmed;
    }

    private static string Stem(string? fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        // The version suffix of a catalogued LoRA ("…_v43exp") is not a word an operator searches for.
        return name;
    }
}
