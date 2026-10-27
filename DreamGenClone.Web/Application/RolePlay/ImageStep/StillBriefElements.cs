namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The ONE answer to "which payload elements does the compiled Still brief carry?" (B-167 D2).
/// </summary>
/// <remarks>
/// <para>
/// The enrichment record restates one instant several times, and the image path selects a single owner per fact
/// instead of handing all of them to the compiler. That selection has to be enforced somewhere, or a saved draft
/// (or an older composer build) that still addresses a dropped leaf would silently re-add it — the very defect the
/// selection exists to remove. This type is that enforcement: an override key that names a dropped element fails
/// fast with a diagnostic naming where the fact now lives, and a key that names nothing at all fails the way it
/// always has.
/// </para>
/// <para>
/// It is also the single definition of what IS selectable, so the inspector's clusters, the reference-removal plan
/// and the applier cannot disagree about the shape of the image payload.
/// </para>
/// </remarks>
public static class StillBriefElements
{
    /// <summary>The prefix a per-character element key carries.</summary>
    public const string CharacterPrefix = "character:";

    /// <summary>The element leaves one frozen character carries in the image payload.</summary>
    public static readonly IReadOnlyList<string> CharacterLeaves =
    [
        "physicalLocation",
        "position",
        "actionOrObservation",
        "sightline",
        "clothing",
        "appearance",
        "appearance.face",
        "appearance.build",
        // B-167 v4 world facts. Present only on a v4 moment; an operator edit creates the leaf rather than dropping
        // the instruction, because writing a fact the operator can see is the point of these rows.
        "at.anchor",
        "at.posture",
        "facing.toward",
        // P1b: how much of this body the picture CONTAINS. Stated by the frame, never inferred from a shot size.
        "framing.extent"
    ];

    /// <summary>The scene-level leaves the image payload carries.</summary>
    public static readonly IReadOnlyList<string> FrozenStateLeaves =
    [
        "location",
        "timeOfDay",
        "lighting",
        "environment",
        "mood",
        "objects",
        "contacts",
        "event.anchor",
        "event.line"
    ];

    /// <summary>The Moment-level leaves the image payload carries — identity and production data, never prose.</summary>
    public static readonly IReadOnlyList<string> MomentLeaves =
    [
        "momentId",
        "temporalAnchor",
        "participantSummary",
        "compositionRationale",
        "productionRoles"
    ];

    /// <summary>
    /// Elements that are deliberately NOT selected, and where their fact now lives. Keyed by the exact element key an
    /// older draft or a stale composer would carry.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Dropped =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["moment.frozenState"] =
                "the Moment's prose copy of the frozen instant is no longer part of the image brief; edit "
                + "'frozenState.event.line', 'frozenState.location' or the character rows instead",
            ["moment.visibleAction"] =
                "the visible action now has exactly one owner: edit 'frozenState.event.line'",
            ["frozenState.visualDescription"] =
                "the whole-event description is no longer part of the image brief; edit 'frozenState.event.line', "
                + "'frozenState.location', 'frozenState.contacts' or the character rows instead",
            ["frozenState.continuityState"] =
                "the global continuity prose is no longer part of the image brief; edit the specific location, "
                + "lighting or character row instead",
            ["continuity"] =
                "beat continuity is not part of the image brief — image composition renders one frozen instant"
        };

    /// <summary>
    /// The diagnostic for a key the image payload deliberately does not carry, or null when the key is not one of the
    /// dropped restatements (it may still be a valid leaf, which callers check separately).
    /// </summary>
    public static string? DroppedReason(string elementKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementKey);
        var key = elementKey.Trim();

        foreach (var (dropped, reason) in Dropped)
        {
            if (string.Equals(key, dropped, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(dropped + ".", StringComparison.OrdinalIgnoreCase))
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the key addresses a leaf the image payload carries. <paramref name="characterKey"/> is the character a
    /// <c>character:</c> key addresses; it is validated by the applier, which is the only place that can resolve it.
    /// </summary>
    public static bool IsSelectable(string elementKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementKey);
        var key = elementKey.Trim();

        if (key.StartsWith(CharacterPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = key[CharacterPrefix.Length..];
            var dot = rest.IndexOf('.');
            if (dot <= 0) return false;
            return CharacterLeaves.Contains(rest[(dot + 1)..], StringComparer.OrdinalIgnoreCase);
        }

        var separator = key.IndexOf('.');
        var scope = separator < 0 ? key : key[..separator];
        var remainder = separator < 0 ? string.Empty : key[(separator + 1)..];

        return scope.ToLowerInvariant() switch
        {
            // `scene.*` is the applier's alias for a frozen-state element (both have always been accepted).
            "scene" => FrozenStateLeaves.Contains(remainder, StringComparer.OrdinalIgnoreCase),
            "moment" => MomentLeaves.Contains(remainder, StringComparer.OrdinalIgnoreCase),
            "frozenstate" => FrozenStateLeaves.Contains(remainder, StringComparer.OrdinalIgnoreCase)
                || remainder.StartsWith("characters.", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>
    /// Refuses an element key the image payload cannot address, naming the offender and — when the key is a dropped
    /// restatement — where its fact lives now. Never returns a silent no-op: an override the brief cannot honor is a
    /// UI/contract drift bug, and failing here is what stops it becoming a prompt that ignores the operator.
    /// </summary>
    public static void RequireSelectable(string elementKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementKey);

        var dropped = DroppedReason(elementKey);
        if (dropped is not null)
        {
            throw new InvalidOperationException(
                $"Prompt element '{elementKey}' is not part of the compiled Still brief: {dropped}.");
        }

        if (!IsSelectable(elementKey))
        {
            throw new InvalidOperationException(
                $"Prompt element key '{elementKey}' does not name a payload element in the compiled Still brief.");
        }
    }
}
