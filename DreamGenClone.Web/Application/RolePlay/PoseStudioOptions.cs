namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The authoring rig's camera and stepping. Every value is required: a projection with an invented focal length
/// or an inherited 5° step would make the geometry depend on a default nobody chose, and the geometry is the
/// thing this feature exists to get right.
/// </summary>
public sealed class PoseStudioOptions
{
    public const string SectionName = "PoseStudio";

    /// <summary>Focal length in pixels at the projection's plane. Sets how strong the perspective is.</summary>
    public double? FocalLengthPx { get; set; }

    /// <summary>Camera distance from the figure along +Z. Must clear the figure's own depth, or the projection inverts.</summary>
    public double? CameraDistance { get; set; }

    /// <summary>Square canvas edge in pixels the projection is laid out on.</summary>
    public int? Canvas { get; set; }

    /// <summary>Degrees moved by one press of a rotate arrow.</summary>
    public double? RotationStepDegrees { get; set; }

    public double RequireFocalLengthPx() => RequirePositive(FocalLengthPx, nameof(FocalLengthPx));

    public double RequireCameraDistance() => RequirePositive(CameraDistance, nameof(CameraDistance));

    public double RequireRotationStepDegrees() => RequirePositive(RotationStepDegrees, nameof(RotationStepDegrees));

    public int RequireCanvas()
    {
        if (Canvas is not int canvas || canvas <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(Canvas)}' is required and must be positive; the projection "
                + "has no canvas to lay a figure out on without it.");
        }

        return canvas;
    }

    private static double RequirePositive(double? value, string property)
    {
        if (value is not double number || number <= 0 || double.IsNaN(number) || double.IsInfinity(number))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{property}' is required and must be a positive number; the pose "
                + "geometry cannot be produced from a guess.");
        }

        return number;
    }
}
