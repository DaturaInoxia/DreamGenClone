namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// An operator's correction to one subject's place in the frame: the axes they changed, leaving the others as the
/// brief had them. Both are optional so "move her left" does not also restate her depth.
/// </summary>
public sealed record SceneBlockingSubjectOverride(string SubjectKey, FrameHorizontal? Horizontal, FrameDepth? Depth);

/// <summary>
/// An operator's correction to the CAMERA the frame was compiled with — the framing override (B-161).
/// </summary>
/// <remarks>
/// <para>
/// It exists because depth alone cannot make a person small. A depth word says where someone stands in the scene, but
/// how large they render is set by the shot: a "medium shot" fills the frame with the subject, so a subject marked
/// "in the background" still came out at full height standing on the lawn, nowhere near the open slider he was
/// described at (operator report, 2026-10-13). The compiled framing came from the staging profile's
/// <c>DefaultShotSize</c> — a single configured value for every Composition — and no operator control reached it.
/// </para>
///
/// <para>
/// The DEFAULT stays exactly where it was: the profile, via the brief's camera keyword map. This record is only ever
/// populated when a person chose a different framing for one render, so nothing is substituted and no second source
/// of truth appears — <see cref="SceneBlockingResolver.ApplyOverride"/> remains the one place an operator's judgement
/// outranks the compiled record.
/// </para>
/// </remarks>
/// <param name="ShotSize">
/// The framing the operator chose, in the vocabulary the staging profile declares (its camera keyword map / default
/// shot size), so the value is one the configured profile actually names.
/// </param>
/// <param name="Angle">The camera angle, when the operator also changed it; null keeps the compiled one.</param>
/// <param name="Height">
/// The camera's height relative to the subject, one of <see cref="CameraHeights.All"/>; null keeps the compiled one.
/// Choosing a height also states the aim that height implies, so the camera line can never say "low angle" above a
/// floor-level height that means the same thing twice or contradicts it.
/// </param>
public sealed record SceneBlockingCameraOverride(string ShotSize, string? Angle = null, string? Height = null);

/// <summary>
/// An operator's correction to the frame a brief was compiled with (B-157, extended by B-161).
///
/// <para>
/// This is REQUEST DATA, like every other render setting: it is persisted beside the render and applied where the
/// blocking is read, so it adjusts the frame the brief recorded rather than replacing it. It deliberately does not
/// re-derive a frame from the staging profile — the profile is what compiled the brief, and a render that consulted it
/// again would silently re-frame every re-render after an edit.
/// </para>
///
/// <para>
/// The operator overrides placement and framing because they can see the picture the prompt describes and the resolver
/// cannot. That makes this the one place a person's judgement outranks the compiled record, so the applied result says
/// so in its provenance rather than reading as the profile's own decision.
/// </para>
/// </summary>
public sealed record SceneBlockingOverride(
    IReadOnlyList<SceneBlockingSubjectOverride> Subjects,
    SceneBlockingCameraOverride? Camera = null)
{
    public bool IsEmpty => Subjects.Count == 0 && Camera is null;

    /// <summary>No correction — the brief's own frame is used unchanged.</summary>
    public static SceneBlockingOverride None { get; } = new([]);
}
