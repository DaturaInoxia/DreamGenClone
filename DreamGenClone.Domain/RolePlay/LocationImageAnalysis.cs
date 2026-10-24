using System.Text.Json;
using System.Text.Json.Serialization;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The ROLE a feature of a location image plays — the fixed half of an anchor's identity.
/// </summary>
/// <remarks>
/// <para>
/// A fixed protocol, like <see cref="ImageStepSlotKind"/>: a closed set of roles is what lets something MATCH a
/// character's action ("watching from the deck") to a feature of the place, which free-form names alone cannot do. The
/// NAME of the anchor stays free ("his trailer's deck", "the far porch"), so one role covers every wording.
/// </para>
///
/// <para>
/// <see cref="Other"/> is the deliberate catch-all: a place contains things no vocabulary anticipated, and refusing to
/// record them would make the analysis lie by omission. An <c>Other</c> anchor carries a name and no matching
/// expectations.
/// </para>
/// </remarks>
public enum LocationAnchorRole
{
    Unknown = 0,

    /// <summary>A place a person stands, sits or walks onto — a deck, porch, step, stoop, patio.</summary>
    Deck = 1,

    /// <summary>An entrance into a structure — a door, slider, gate, hatch.</summary>
    Door = 2,

    /// <summary>A window, or any opening looked through rather than walked through.</summary>
    Window = 3,

    /// <summary>A washing line, rotary dryer, or clothesline of any kind.</summary>
    Clothesline = 4,

    /// <summary>An outbuilding or subsidiary structure.</summary>
    Shed = 5,

    /// <summary>Open ground — lawn, gravel, dirt, clearing.</summary>
    Lawn = 6,

    /// <summary>A path, track, driveway or road.</summary>
    Path = 7,

    /// <summary>A vehicle, trailer or caravan as a STRUCTURE in the frame.</summary>
    Structure = 8,

    /// <summary>A tree, hedgerow, treeline or other vegetation mass.</summary>
    Vegetation = 9,

    /// <summary>Something visible that no other role covers. Always carries a Name.</summary>
    Other = 10
}

/// <summary>
/// One feature of a location image, as the analysis found it: what it IS (role) and what it is CALLED (name).
/// </summary>
/// <param name="Role">The fixed role, which is what makes this anchor matchable.</param>
/// <param name="Name">The analysis's own words for it ("his trailer's deck", "the far shed"). Never empty.</param>
/// <param name="Horizontal">
/// Roughly where it sits left-to-right in the frame, when the analysis can say. Null is a real answer ("somewhere in
/// the middle distance"), and is NOT defaulted: a guessed side would place a character on the wrong trailer, which is
/// the defect this whole analysis exists to prevent.
/// </param>
/// <param name="Depth">Roughly how far into the frame it sits, on the same terms as <paramref name="Horizontal"/>.</param>
public sealed record LocationAnchor(
    LocationAnchorRole Role,
    string Name,
    FrameHorizontal? Horizontal = null,
    FrameDepth? Depth = null);

/// <summary>
/// What a vision model found in ONE version of a location image: prose for the prompt, and anchors for placement.
/// </summary>
/// <remarks>
/// <para>
/// Stored on the location image row, keyed by the image's <see cref="AnalyzedSha256"/>. That sha IS the staleness rule:
/// a superseded or re-edited image has a different one, so the stored analysis is visibly about a different picture
/// rather than silently describing the current one.
/// </para>
///
/// <para>
/// The analysis PROPOSES; it never decides. It reaches the prompt by pre-filling the operator's own reference
/// directions (B-159), which remain the single owner of what the prompt says, so a re-analysis can never quietly
/// revert a correction they made by hand.
/// </para>
/// </remarks>
public sealed record LocationImageAnalysis(
    string Description,
    IReadOnlyList<LocationAnchor> Anchors,
    string AnalyzedSha256,
    DateTime AnalyzedUtc)
{
    /// <summary>
    /// Whether this analysis is about the given image version — the one question a caller should ask before using it,
    /// so no caller re-implements the comparison.
    /// </summary>
    public bool IsAbout(string? sha256) =>
        !string.IsNullOrWhiteSpace(sha256)
        && string.Equals(AnalyzedSha256?.Trim(), sha256.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Value equality over the CONTENT, including the anchors.
    /// </summary>
    /// <remarks>
    /// A record's synthesized equality compares a collection member by REFERENCE, so two analyses of the same image —
    /// one just read back from storage, one just parsed — compared unequal while printing identically. Anything that
    /// compares a stored analysis to a fresh one (a staleness check, a "did it change" notice, a test) would then
    /// report a difference that does not exist. The anchors are the substance of this record, so they are compared
    /// element by element.
    /// </remarks>
    public bool Equals(LocationImageAnalysis? other) =>
        other is not null
        && string.Equals(Description, other.Description, StringComparison.Ordinal)
        && string.Equals(AnalyzedSha256, other.AnalyzedSha256, StringComparison.Ordinal)
        && AnalyzedUtc == other.AnalyzedUtc
        && Anchors.SequenceEqual(other.Anchors);

    public override int GetHashCode() =>
        HashCode.Combine(Description, AnalyzedSha256, AnalyzedUtc, Anchors.Count);
}

/// <summary>
/// Reads and writes <see cref="LocationImageAnalysis"/> as the JSON persisted on the image row.
/// </summary>
/// <remarks>
/// One shape, one place. A second reader with its own tolerances is how a stored analysis and a rendered one come to
/// disagree, which is exactly the class of defect the reference work keeps finding.
/// </remarks>
public static class LocationImageAnalysisJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write(LocationImageAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return JsonSerializer.Serialize(analysis, Options);
    }

    /// <summary>
    /// The stored analysis, or null when the cell is empty/whitespace.
    /// </summary>
    /// <exception cref="InvalidOperationException">The stored JSON is not a readable analysis.</exception>
    public static LocationImageAnalysis? TryRead(string? json) => Read(json, required: false);

    /// <summary>
    /// The stored analysis, refusing an absent one by name. For a caller that cannot proceed without it.
    /// </summary>
    public static LocationImageAnalysis Read(string? json) =>
        Read(json, required: true)!;

    private static LocationImageAnalysis? Read(string? json, bool required)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            if (required)
            {
                throw new InvalidOperationException(
                    "No location image analysis is stored, so there is nothing to read. Analyse the image first.");
            }

            return null;
        }

        LocationImageAnalysis? analysis;
        try
        {
            analysis = JsonSerializer.Deserialize<LocationImageAnalysis>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The stored location image analysis is not readable as an analysis. Re-analyse the image rather than "
                + "reading a partial one.", exception);
        }

        if (analysis is null)
        {
            throw new InvalidOperationException("The stored location image analysis deserialized to nothing.");
        }

        if (string.IsNullOrWhiteSpace(analysis.Description) && analysis.Anchors.Count == 0)
        {
            throw new InvalidOperationException(
                "The stored location image analysis carries neither a description nor an anchor, so it says nothing.");
        }

        return analysis;
    }
}
