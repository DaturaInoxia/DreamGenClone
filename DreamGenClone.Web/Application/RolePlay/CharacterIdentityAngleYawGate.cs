using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The angle direction gate: does a produced image actually face the way its view says it does?
///
/// It reads ONE number — the nose offset the canonical measurement tool reports, signed in image space — and
/// never the iris or interocular metrics, which are invalid under yaw. Measured on the accepted Becky angle
/// set (2026-09-21): the nose offset correctly called 3/4 left −45.12 % and 3/4 right +27.70 %, while iris dy
/// read −13.63 % and −4.29 % on the very same images.
///
/// Full profiles are deliberately NOT asserted here: MediaPipe returns no face mesh on a true profile (profile
/// left measured "no face mesh"), so a profile's direction stays the user's explicit visual confirmation (D4).
/// A profile measurement that does appear is recorded as evidence, never used as this gate.
/// </summary>
public static class CharacterIdentityAngleYawGate
{
    /// <summary>The views whose direction a measurement can assert.</summary>
    public static bool RequiresAssertion(CharacterIdentityAngleView view)
        => view is CharacterIdentityAngleView.ThreeQuarterLeft or CharacterIdentityAngleView.ThreeQuarterRight;

    /// <summary>
    /// The sign the view's convention requires: left views −1 (nose toward the left of the image), right
    /// views +1. Image space, independent of how the model was prompted.
    /// </summary>
    public static double RequiredSign(CharacterIdentityAngleView view) => view switch
    {
        CharacterIdentityAngleView.ThreeQuarterLeft => -1,
        CharacterIdentityAngleView.ProfileLeft => -1,
        CharacterIdentityAngleView.ThreeQuarterRight => 1,
        CharacterIdentityAngleView.ProfileRight => 1,
        _ => throw new InvalidOperationException($"Unsupported angle view '{view}'.")
    };

    /// <summary>
    /// Evaluates one produced image against its view's convention. A view that needs no assertion passes
    /// unasserted; otherwise the verdict names what was measured and what the view requires.
    /// </summary>
    public static CharacterIdentityYawVerdict Evaluate(
        CharacterIdentityAngleView view,
        CharacterIdentityEyeMeasurement? measurement,
        double minAbsPercent)
    {
        if (minAbsPercent <= 0)
        {
            throw new InvalidOperationException(
                $"AngleYawMinAbsPercent must be positive, but was {minAbsPercent}.");
        }

        if (!RequiresAssertion(view))
        {
            return CharacterIdentityYawVerdict.NotAsserted(view, measurement?.NoseOffsetPercent);
        }

        if (measurement is null)
        {
            throw new InvalidOperationException(
                $"The {view} direction cannot be evaluated without a measurement of its image.");
        }

        if (!string.IsNullOrWhiteSpace(measurement.Error) || measurement.NoseOffsetPercent is not { } offset)
        {
            return CharacterIdentityYawVerdict.Blocked(
                view,
                $"The measurement tool found no face on this {view} render, so its direction cannot be asserted. "
                + "Re-render or upload a clearer view, or record a manual visual override.");
        }

        var required = RequiredSign(view);
        var side = required < 0 ? "image-left" : "image-right";
        var other = required < 0 ? "image-right" : "image-left";

        if (Math.Abs(offset) < minAbsPercent)
        {
            return CharacterIdentityYawVerdict.Blocked(
                view,
                $"The head is not turned: {view} needs the nose toward {side} by at least {minAbsPercent:F1}% of "
                + $"the face width, but the measured nose offset is {offset:F2}% (facing the camera).");
        }

        if (Math.Sign(offset) != Math.Sign(required))
        {
            return CharacterIdentityYawVerdict.Blocked(
                view,
                $"The head faces {other}: {view} needs the nose toward {side}, but the measured nose offset is "
                + $"{offset:F2}%.");
        }

        return CharacterIdentityYawVerdict.Satisfied(view, offset);
    }
}

/// <summary>
/// The gate's outcome for one view's image. <see cref="Asserted"/> is false for a view the gate does not
/// assert (a profile), which is a pass by the user's confirmation, not a measurement.
/// </summary>
public sealed record CharacterIdentityYawVerdict(
    CharacterIdentityAngleView View,
    bool Passed,
    bool Asserted,
    double? NoseOffsetPercent,
    string? BlockReason)
{
    public static CharacterIdentityYawVerdict NotAsserted(CharacterIdentityAngleView view, double? offset)
        => new(view, true, false, offset, null);

    public static CharacterIdentityYawVerdict Satisfied(CharacterIdentityAngleView view, double offset)
        => new(view, true, true, offset, null);

    public static CharacterIdentityYawVerdict Blocked(CharacterIdentityAngleView view, string reason)
        => new(view, false, true, null, reason);
}
