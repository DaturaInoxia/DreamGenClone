using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// What a pack DECLARES about its own poses, read from its <c>pack.json</c>.
///
/// This is the authoritative layer. A pack's folder layout says what its poses are ("on_stomach", "NSFW_Kneeling")
/// and the keypoints do not contain that information — a lying body photographed from above and a standing body
/// photographed from the front produce near-identical 2D skeletons — so the declaration is what the app stores, and
/// the keypoint measurement's only job is to disagree out loud when it can.
///
///   * <c>rating</c> — "nsfw" or "sfw", the pack's default for every pose in it.
///   * <c>categories</c> — one entry per category FOLDER, the default for every pose in that folder.
///   * <c>poses</c> — per-FILE entries keyed by the path relative to the pack root, for a pose that differs from its
///     category.
///
/// A category with no entry is NOT an error: the pack imports and its poses say "not declared". The library page
/// names the categories that need a declaration, which is a better outcome than refusing a pack whose poses are
/// otherwise perfectly usable.
/// </summary>
public sealed record PosePackDeclarations(
    PoseContentRating Rating,
    IReadOnlyDictionary<string, PoseMetadataDeclaration> Categories,
    IReadOnlyDictionary<string, PoseMetadataDeclaration> Poses)
{
    /// <summary>A pack that declares nothing. Every pose it holds is imported as "not declared".</summary>
    public static readonly PosePackDeclarations None = new(
        PoseContentRating.Unrated,
        new Dictionary<string, PoseMetadataDeclaration>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, PoseMetadataDeclaration>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The declaration for one pose: the most specific entry that matches, then the pack's rating.
    ///
    /// A <c>categories</c> key may be either a category NAME (<c>standing</c>) or a FOLDER PATH inside the pack
    /// (<c>NSFW_lying</c>, or deeper). The path wins over the name because it is more specific, and the name stays
    /// accepted because it is what most packs need. That matters on a real pack: <c>openpose-nsfw</c> holds folders
    /// <c>NSFW_lying</c> AND <c>lying</c>, both of which the importer files under the single category <c>lying</c>, so
    /// only a path key can tell those 38 poses from the other 35.
    ///
    /// Specificity runs file -> deepest folder -> ... -> category -> pack rating, so a pack-wide rating is the weakest
    /// statement available and a single file is the strongest.
    /// </summary>
    public PoseMetadataDeclaration For(string category, string relativePath)
    {
        // The merge STARTS at the pack-wide rating because that is the WEAKEST statement the pack makes: a category,
        // a folder or a single file that states a rating replaces it. Applying it last instead would make it the
        // strongest and silently discard a per-file override — which is what happened before 2026-09-30: a file
        // declaring "nsfw" inside an "sfw" pack resolved back to Sfw, so the one escape hatch that makes a mixed
        // category expressible did nothing.
        var declaration = new PoseMetadataDeclaration(
            PoseStance.Unknown, PoseFacingDirection.Unknown, PoseCameraAngle.Unknown, Rating);

        // Each step overrides the last, so a more specific key always wins: the category NAME, then each folder on the
        // path from shallowest to deepest, then the file itself.
        if (Categories.TryGetValue(category, out var categoryDefault))
        {
            declaration = declaration.OverriddenBy(categoryDefault);
        }

        var path = relativePath.Replace('\\', '/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var depth = 1; depth < segments.Length; depth++)
        {
            if (Categories.TryGetValue(string.Join('/', segments.Take(depth)), out var folderDefault))
            {
                declaration = declaration.OverriddenBy(folderDefault);
            }
        }

        if (Poses.TryGetValue(path, out var poseOverride))
        {
            declaration = declaration.OverriddenBy(poseOverride);
        }

        return declaration;
    }

    /// <summary>True when this pack declares nothing at all, i.e. every pose in it will be "not declared".</summary>
    public bool IsEmpty => Rating == PoseContentRating.Unrated && Categories.Count == 0 && Poses.Count == 0;

    /// <summary>
    /// Reads the declaration block. An unrecognised value FAILS with the pack, the property and the offending text:
    /// "stnading" silently becoming "not declared" would look like a pack that had not declared anything, and the
    /// operator would go looking in the wrong place.
    /// </summary>
    public static PosePackDeclarations Parse(JsonObject manifest, string packFolder)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var rating = ParseRating(Text(manifest, "rating"), packFolder, "rating") ?? PoseContentRating.Unrated;
        var categories = ParseMap(manifest, "categories", packFolder);
        var poses = ParseMap(manifest, "poses", packFolder);

        return new PosePackDeclarations(rating, categories, poses);
    }

    private static IReadOnlyDictionary<string, PoseMetadataDeclaration> ParseMap(
        JsonObject manifest, string property, string packFolder)
    {
        var result = new Dictionary<string, PoseMetadataDeclaration>(StringComparer.OrdinalIgnoreCase);

        if (manifest[property] is not JsonObject map)
        {
            if (manifest[property] is null)
            {
                return result;
            }

            throw new InvalidOperationException(
                $"Pose pack '{packFolder}/pack.json' has a '{property}' that is not an object. Each entry is a "
                + $"key plus a metadata object, for example \"{property}\": {{ \"standing\": {{ \"stance\": "
                + "\"standing\" }} }}.");
        }

        foreach (var (key, node) in map)
        {
            if (node is not JsonObject entry)
            {
                throw new InvalidOperationException(
                    $"Pose pack '{packFolder}/pack.json' declares '{property}.{key}' but its value is not an object.");
            }

            result[key.Replace('\\', '/')] = ParseEntry(entry, packFolder, $"{property}.{key}");
        }

        return result;
    }

    private static PoseMetadataDeclaration ParseEntry(JsonObject entry, string packFolder, string where)
    {
        return new PoseMetadataDeclaration(
            Stance: ParseStance(Text(entry, "stance"), packFolder, where) ?? PoseStance.Unknown,
            Direction: ParseDirection(Text(entry, "direction"), packFolder, where) ?? PoseFacingDirection.Unknown,
            Camera: ParseCamera(Text(entry, "camera"), packFolder, where) ?? PoseCameraAngle.Unknown,
            Rating: ParseRating(Text(entry, "rating"), packFolder, where) ?? PoseContentRating.Unrated);
    }

    /// <summary>
    /// The raw, trimmed text of a property, or null when it is absent or blank. The RAW text is kept, not a normalized
    /// copy, so an error can quote what the pack actually says — reporting "explicitsh" for a typo of "explicit-ish"
    /// would send the operator looking for a word that is not in their file.
    /// </summary>
    private static string? Text(JsonObject node, string property) =>
        node[property] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    /// <summary>
    /// Case, spaces and hyphens are all ignored, so "Three Quarter Left", "three-quarter-left" and
    /// "threequarterleft" are one value. Nothing else is guessed: an unrecognised word is an error, not a default.
    /// </summary>
    private static string? Normalized(string? value) =>
        value is null ? null : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static PoseStance? ParseStance(string? value, string packFolder, string where) => Normalized(value) switch
    {
        null => null,
        "standing" => PoseStance.Standing,
        "sitting" => PoseStance.Sitting,
        "kneeling" => PoseStance.Kneeling,
        "lying" => PoseStance.Lying,
        "allfours" => PoseStance.AllFours,
        "squatting" => PoseStance.Squatting,
        "suspended" => PoseStance.Suspended,
        "splitleg" or "splitlegs" => PoseStance.SplitLeg,
        "jumping" => PoseStance.Jumping,
        "dancing" => PoseStance.Dancing,
        "flexing" => PoseStance.Flexing,
        "tpose" => PoseStance.TPose,
        "unknown" => PoseStance.Unknown,
        _ => throw Unrecognised(packFolder, where, "stance", value!)
    };

    private static PoseFacingDirection? ParseDirection(string? value, string packFolder, string where) => Normalized(value) switch
    {
        null => null,
        "front" => PoseFacingDirection.Front,
        "threequarterleft" or "34left" => PoseFacingDirection.ThreeQuarterLeft,
        "threequarterright" or "34right" => PoseFacingDirection.ThreeQuarterRight,
        "profileleft" => PoseFacingDirection.ProfileLeft,
        "profileright" => PoseFacingDirection.ProfileRight,
        "back" => PoseFacingDirection.Back,
        "unknown" => PoseFacingDirection.Unknown,
        _ => throw Unrecognised(packFolder, where, "direction", value!)
    };

    private static PoseCameraAngle? ParseCamera(string? value, string packFolder, string where) => Normalized(value) switch
    {
        null => null,
        "eyelevel" => PoseCameraAngle.EyeLevel,
        "fromabove" => PoseCameraAngle.FromAbove,
        "frombelow" => PoseCameraAngle.FromBelow,
        "unknown" => PoseCameraAngle.Unknown,
        _ => throw Unrecognised(packFolder, where, "camera", value!)
    };

    private static PoseContentRating? ParseRating(string? value, string packFolder, string where) => Normalized(value) switch
    {
        null => null,
        "sfw" => PoseContentRating.Sfw,
        "nsfw" => PoseContentRating.Nsfw,
        "unrated" => PoseContentRating.Unrated,
        _ => throw Unrecognised(packFolder, where, "rating", value!)
    };

    private static InvalidOperationException Unrecognised(
        string packFolder, string where, string property, string value) =>
        new($"Pose pack '{packFolder}/pack.json' declares '{where}.{property}' as '{value}', which is not a value "
            + "this app knows. Fix the pack.json, or remove the property so the pose is imported as not declared "
            + "rather than with a value nothing understands.");
}
