namespace DreamGenClone.Domain.RolePlay;

/// <summary>One subject's place in the frame.</summary>
public sealed record FramePosition(FrameHorizontal Horizontal, FrameDepth Depth);

/// <summary>One subject the render will place, keyed so a binding can be tied back to it.</summary>
/// <param name="SubjectKey">
/// The subject key the typed references and beat characters already use. This is what lets a role clause say which
/// image belongs to which person without naming the character (names are refuted by the vendor research).
/// </param>
/// <param name="RoleText">How the role clause refers to this subject — "the person on the LEFT".</param>
public sealed record SceneBlockingSubject(string SubjectKey, FramePosition Position, string RoleText);

/// <summary>The camera the render should frame with.</summary>
/// <param name="OmniscientShotSize">
/// The shot size an OMNISCIENT still uses (B-166 D7), snapshotted from the staging profile at compile time so the
/// value travels with the brief. Null on a brief compiled before the setting existed; the POV then keeps the
/// recorded shot size. It is never applied to a participant POV.
/// </param>
/// <param name="Height">
/// The camera's height relative to the subject — one of <see cref="CameraHeights.All"/>. Null on a brief compiled
/// before the setting existed, and then nothing is claimed about height.
/// </param>
public sealed record SceneBlockingCamera(
    string Angle,
    string ShotSize,
    string? Side,
    string? OmniscientShotSize = null,
    string? Height = null);

/// <summary>
/// How a scene is FRAMED, as opposed to what it contains (B-157).
///
/// <para>
/// The Still brief already describes the world ("Becky at the clothesline, Dean at the slider") but never the frame
/// ("Becky left, Dean right, wide side view"). Without the frame the model cannot tell whose face is which in a
/// multi-reference render, and it resolves the ambiguity the same way every time — the first face wins.
/// </para>
///
/// <para>
/// This record is SNAPSHOTTED into the compiled brief beside <c>typedReferences</c>, so a later staging-profile edit
/// cannot rewrite how a past render was framed.
/// </para>
/// </summary>
public sealed record SceneBlocking(
    string ProfileVersion,
    IReadOnlyList<SceneBlockingSubject> Subjects,
    SceneBlockingCamera Camera,
    IReadOnlyList<string> Layering,
    string Rationale);

/// <summary>
/// One subject as the brief supplies it: which person, how they participate, and where the world puts them.
/// </summary>
/// <param name="Involvement">
/// The beat's own wording — <c>active</c> or <c>observer</c>. Any other value is refused rather than treated as one
/// or the other, because guessing decides which subject is foreground.
/// </param>
public sealed record SceneBlockingSubjectInput(
    string SubjectKey,
    string Involvement,
    string? PhysicalLocation,
    string? Position,
    string? Sightline);

/// <summary>
/// Everything the resolver needs from one Still brief: the moment's composition rationale and its participants.
/// Parsing the brief JSON into this shape is the caller's job, which keeps the resolver pure and unit-testable.
/// </summary>
public sealed record SceneBlockingInput(string? Rationale, IReadOnlyList<SceneBlockingSubjectInput> Subjects);
