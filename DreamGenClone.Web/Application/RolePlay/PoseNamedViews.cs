namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The named angles the authoring tools offer, in one place. The projection tool, the head tool and the probe
/// export all have to mean the same thing by "3/4 right"; a copy of this list per surface would drift and the
/// probe would then be measuring an angle the operator never chose.
/// </summary>
public static class PoseNamedViews
{
    /// <summary>
    /// Yaw turns the figure about the vertical axis. At a positive yaw the subject's right side comes toward the
    /// camera, which is why +45 reads as "3/4 right" and +90 as the right-facing profile.
    /// </summary>
    public static readonly (string Label, PoseView View)[] BodyTargets =
    [
        ("Front", new PoseView()),
        ("3/4 left", new PoseView(YawDegrees: -45)),
        ("3/4 right", new PoseView(YawDegrees: 45)),
        ("Profile left", new PoseView(YawDegrees: -90)),
        ("Profile right", new PoseView(YawDegrees: 90))
    ];

    /// <summary>The named head angles. Pitch is positive looking up.</summary>
    public static readonly (string Label, PoseHeadRotation Head)[] HeadTargets =
    [
        ("Front", new PoseHeadRotation()),
        ("Profile left", new PoseHeadRotation(YawDegrees: -90)),
        ("Profile right", new PoseHeadRotation(YawDegrees: 90)),
        ("Looking up", new PoseHeadRotation(PitchDegrees: PoseHeadRotation.PitchLimitDegrees)),
        ("Looking down", new PoseHeadRotation(PitchDegrees: -PoseHeadRotation.PitchLimitDegrees))
    ];

    /// <summary>A file-name-safe form of a label, so an export cannot collide or need quoting.</summary>
    public static string Slug(string label) => PoseLibraryService.Slug(label);
}
