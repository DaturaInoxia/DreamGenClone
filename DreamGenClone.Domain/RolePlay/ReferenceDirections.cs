namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Which way a referenced STRUCTURE or PERSON faces in the frame, as the OPERATOR states it.
/// </summary>
/// <remarks>
/// Deliberately NOT <see cref="FrameHorizontal"/>: that is where something SITS. A trailer on the left of the frame can
/// face right (toward its neighbour), and the two facts are independent, so one enum cannot carry both.
///
/// <para>
/// <see cref="Unknown"/> is the unset value and means "the operator said nothing", which is the state every binding
/// starts in. It is never emitted: an unset facing adds no sentence.
/// </para>
/// </remarks>
public enum ReferenceFacing
{
    Unknown = 0,

    /// <summary>Faces the left of the frame.</summary>
    Left = 1,

    /// <summary>Faces the right of the frame.</summary>
    Right = 2,

    /// <summary>Faces the camera.</summary>
    TowardViewer = 3,

    /// <summary>Faces away from the camera.</summary>
    AwayFromViewer = 4
}

/// <summary>
/// Who authored a reference direction statement — the operator, or an analysis of the image.
/// </summary>
/// <remarks>
/// This exists so an automatic reading can never silently revert a correction a person made. The rule it serves is
/// one-way: an ANALYSIS-sourced statement may be replaced by a newer analysis, and an OPERATOR-sourced one may not be
/// replaced by anything. Without it, re-reading an image after an edit would quietly overwrite what the operator wrote.
/// </remarks>
public enum ReferenceDirectionSource
{
    /// <summary>The operator wrote it, or edited what an analysis proposed. Nothing may overwrite it.</summary>
    Operator = 1,

    /// <summary>Proposed by an image analysis and not since touched. A newer analysis may replace it.</summary>
    Analysis = 2
}

/// <summary>
/// What the OPERATOR says a reference image is doing in this frame — the manual half of the reference reading.
/// </summary>
/// <remarks>
/// The automatic path infers a reference's ROLE from its slot and its name. These are the things it cannot infer,
/// because nothing in the data says them: which side of the frame a structure sits on, which way it faces, and whatever
/// else the operator needs the model told about it.
///
/// <para>
/// <b>Anchored to the REFERENCE, not to a numbered tag.</b> The operator says "this trailer is on the left"; the clause
/// assigns that statement its <c>&lt;imageN&gt;</c> at RENDER, from the order the images are actually sent. That is what
/// makes the statement survive renumbering: adding a pack face moves every location reference one slot later
/// (<c>ReferenceBindingShape.InSendOrder</c> sends pack-supplied images first), and a statement written against
/// <c>&lt;image1&gt;</c> would silently start describing a different image. This one cannot.
/// </para>
///
/// <para>
/// It is persisted ON THE BINDING, so it travels with the reference through the job payload and through every re-plan
/// the composer performs — the same channel <c>ReferenceLabel</c> and the placement override already use.
/// </para>
/// </remarks>
/// <param name="Side">Where in the frame the operator says this reference's subject sits.</param>
/// <param name="Facing">Which way the operator says it faces.</param>
/// <param name="Note">
/// The operator's own words about what this image is doing here, verbatim. Free text rather than another enum because
/// the cases that need it are the ones no vocabulary anticipated ("the far trailer, partly hidden by the trees").
/// </param>
/// <param name="Source">
/// Who authored it — see <see cref="ReferenceDirectionSource"/>. Defaults to the OPERATOR, which is the safe reading of
/// a statement whose author is not recorded: nothing automatic may overwrite something a person may have written.
/// </param>
public sealed record ReferenceDirections(
    FrameHorizontal? Side,
    ReferenceFacing? Facing,
    string? Note,
    ReferenceDirectionSource Source = ReferenceDirectionSource.Operator)
{
    /// <summary>Nothing said. A binding in this state emits no direction sentence at all.</summary>
    public static readonly ReferenceDirections Empty = new(null, null, null);

    /// <summary>
    /// Whether the operator has actually said anything. The clause asks THIS rather than testing three fields, so a
    /// future direction axis cannot be added without every emitter seeing it.
    /// </summary>
    public bool IsEmpty =>
        Side is null
        && (Facing is null || Facing == ReferenceFacing.Unknown)
        && string.IsNullOrWhiteSpace(Note);

    /// <summary>
    /// Whether a fresh image analysis may REPLACE this statement.
    /// </summary>
    /// <remarks>
    /// True only when there is nothing here yet, or what is here was itself proposed by an analysis and never touched.
    /// This is the one rule that keeps "the model tries first, the operator can overwrite" true in both directions:
    /// the model may speak first, and having spoken it may correct itself — but it may never talk over the operator.
    /// </remarks>
    public bool MayBeReplacedByAnalysis => IsEmpty || Source == ReferenceDirectionSource.Analysis;
}
