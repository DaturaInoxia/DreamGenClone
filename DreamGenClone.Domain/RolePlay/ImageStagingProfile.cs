namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Which render surface a staging profile governs. A Composition has a beat/moment scene to frame; the Asset Manager
/// has bindings and no scene, so it places its subject by a rule of its own rather than borrowing the composition one.
/// </summary>
public enum ImageStagingSurface
{
    Unknown = 0,

    /// <summary>A beat/moment-driven scene render (the Composition path).</summary>
    Composition = 1,

    /// <summary>A bindings-only asset render with no beat/moment scene to describe.</summary>
    AssetManager = 2
}

/// <summary>
/// Where a subject sits across the frame. <see cref="Unknown"/> is the unset value and is refused by validation: a
/// profile that does not say where a subject sits cannot place one, and inventing "center" is the fallback this
/// feature exists to remove.
/// </summary>
public enum FrameHorizontal
{
    Unknown = 0,
    Left = 1,
    Center = 2,
    Right = 3
}

/// <summary>How deep a subject sits in the frame. <see cref="Unknown"/> is the unset value and is refused.</summary>
public enum FrameDepth
{
    Unknown = 0,
    Foreground = 1,
    Midground = 2,
    Background = 3
}

/// <summary>
/// How a render frames its subjects, as CONFIGURATION (B-157).
///
/// <para>
/// The involvement-based placement rule and the camera fallback are declared here and persisted in the
/// <c>ImageStagingProfiles</c> table, edited on the <c>/profiles</c> page — never as a constant in the resolver. A
/// resolver that carried its own default would make an unconfigured host silently render "centered", which is
/// indistinguishable from a host that chose "centered" deliberately.
/// </para>
///
/// <para>
/// <see cref="PlacementLadderJson"/> declares WHICH horizontals a frame may use. The spread depends on how many
/// subjects there are — one owns the centre, a pair owns the opposite edges, three fill left/centre/right — so the
/// order the entries are listed in is an editing convenience, not a seating priority. Each subject still gets a
/// distinct position by construction, and a host whose ladder cannot supply the spread a scene needs fails fast
/// naming the missing position rather than doubling one up.
/// </para>
/// </summary>
public sealed class ImageStagingProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string DisplayName { get; set; } = string.Empty;

    public ImageStagingSurface Surface { get; set; } = ImageStagingSurface.Unknown;

    /// <summary>
    /// JSON array of <see cref="FrameHorizontal"/> names declaring the positions this profile permits. Listed in
    /// reading order for convenience; seating spreads the cast symmetrically over them, so the order is not a priority.
    /// </summary>
    public string PlacementLadderJson { get; set; } = "[]";

    /// <summary>Depth given to a subject whose beat involvement is "active".</summary>
    public FrameDepth ActiveDepth { get; set; } = FrameDepth.Unknown;

    /// <summary>Depth given to a subject whose beat involvement is "observer".</summary>
    public FrameDepth ObserverDepth { get; set; } = FrameDepth.Unknown;

    /// <summary>Camera angle used when <c>moment.compositionRationale</c> matches no keyword in the map below.</summary>
    public string DefaultCameraAngle { get; set; } = string.Empty;

    /// <summary>Shot size used when <c>moment.compositionRationale</c> matches no keyword in the map below.</summary>
    public string DefaultShotSize { get; set; } = string.Empty;

    /// <summary>
    /// The shot size an OMNISCIENT still is framed with (B-166 D7). The omniscient POV is the narrative external view
    /// of the whole moment, so it leans wide/establishing rather than inheriting the close composition default.
    ///
    /// <para>
    /// It is CONFIGURATION, not a constant: the value is persisted here and edited on <c>/profiles</c>, and it applies
    /// only when the operator has not named a framing for this render. A missing value is refused by validation
    /// rather than defaulted, because a code-level default is indistinguishable from a deliberate choice.
    /// </para>
    /// </summary>
    public string OmniscientShotSize { get; set; } = string.Empty;

    /// <summary>
    /// The camera HEIGHT the profile frames at — one of <see cref="CameraHeights.All"/> (floor, waist, eye, above,
    /// overhead), snapshotted into the brief's camera at compile time.
    ///
    /// <para>
    /// Configuration like every other framing value: persisted here, edited on the profile panel, and outranked by an
    /// operator's per-render choice. A missing value is refused by validation rather than defaulted, because a
    /// code-level default is indistinguishable from a deliberate choice.
    /// </para>
    /// </summary>
    public string DefaultCameraHeight { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of <c>{"keyword","angle","shotSize"}</c> entries. The rationale is carried into the prose verbatim;
    /// this map only lets a known framing word also fill the structured camera field, so the two never disagree.
    /// </summary>
    public string CameraKeywordMapJson { get; set; } = "[]";

    public int Version { get; set; } = 1;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One keyword the staging profile recognises in <c>compositionRationale</c>.</summary>
public sealed class ImageStagingCameraKeyword
{
    public string Keyword { get; set; } = string.Empty;

    public string? Angle { get; set; }

    public string? ShotSize { get; set; }

    /// <summary>Which side of the axis the shot is taken from, when the keyword states one.</summary>
    public string? Side { get; set; }
}
