namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// A named collection of pose presets. Libraries are the unit the user creates and searches over: the
/// seeded OpenPose pack is one library, and a library the user makes is a peer of it rather than a
/// second-class category value on a preset.
///
/// Kept deliberately separate from <see cref="PosePreset.Category"/>, which answers a different question
/// ("what kind of pose is this" — standing, kneeling) rather than "which collection does it belong to".
/// </summary>
public sealed class PoseLibrary
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// True for a library the seeder maintains. Its presets may be re-imported idempotently, while a
    /// user library is never touched by the importer.
    /// </summary>
    public bool IsSystem { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Stable ids for libraries the application itself owns. Declared as constants so the seeder and any
/// reader agree on one value instead of two string literals that can drift apart.
/// </summary>
public static class PoseLibraryIds
{
    /// <summary>The bundled pack (<c>pose-packs/openpose-nsfw</c>), imported as the first library.</summary>
    public const string BundledPackFolder = "openpose-nsfw";

    /// <summary>
    /// Poses the user authored in the studio (mannequin projection, and later joint editing / DWPose extraction).
    /// The id is the slug of its own display name, so the library can be created through the ordinary
    /// "create library" path and still be found by this constant.
    /// </summary>
    public const string Authored = "authored-poses";

    /// <summary>The display name behind <see cref="Authored"/>.</summary>
    public const string AuthoredName = "Authored poses";
}
