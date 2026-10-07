namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The azimuth (horizontal orbit) values of the fal Qwen-Image-Edit-2511 Multiple-Angles LoRA. These are the
/// LoRA's fixed vocabulary — its prompt contract — not app behaviour, so the strings are the exact tokens the
/// LoRA was trained on.
/// </summary>
public enum MultiAngleAzimuth
{
    Front = 0,
    FrontRight = 45,
    RightSide = 90,
    BackRight = 135,
    Back = 180,
    BackLeft = 225,
    LeftSide = 270,
    FrontLeft = 315
}

/// <summary>
/// The elevation (vertical camera angle) values of the multi-angle LoRA. Fixed vocabulary, exact trained tokens.
/// </summary>
public enum MultiAngleElevation
{
    LowAngle = 0,
    EyeLevel = 1,
    Elevated = 2,
    HighAngle = 3
}

/// <summary>
/// The distance (how far the camera sits from the subject) values of the multi-angle LoRA. Fixed vocabulary,
/// exact trained tokens.
/// </summary>
public enum MultiAngleDistance
{
    CloseUp = 0,
    Medium = 1,
    Wide = 2
}

/// <summary>The multi-angle LoRA's prompt contract: the exact tokens per pick, plus the trigger token.</summary>
public static class MultiAngleCameraTokens
{
    /// <summary>The LoRA's trigger token that opens every multi-angle instruction.</summary>
    public const string Trigger = "<sks>";

    public static string Azimuth(MultiAngleAzimuth azimuth) => azimuth switch
    {
        MultiAngleAzimuth.Front => "front view",
        MultiAngleAzimuth.FrontRight => "front-right quarter view",
        MultiAngleAzimuth.RightSide => "right side view",
        MultiAngleAzimuth.BackRight => "back-right quarter view",
        MultiAngleAzimuth.Back => "back view",
        MultiAngleAzimuth.BackLeft => "back-left quarter view",
        MultiAngleAzimuth.LeftSide => "left side view",
        MultiAngleAzimuth.FrontLeft => "front-left quarter view",
        _ => throw new ArgumentOutOfRangeException(nameof(azimuth), azimuth, "Unknown multi-angle azimuth.")
    };

    public static string Elevation(MultiAngleElevation elevation) => elevation switch
    {
        MultiAngleElevation.LowAngle => "low-angle shot",
        MultiAngleElevation.EyeLevel => "eye-level shot",
        MultiAngleElevation.Elevated => "elevated shot",
        MultiAngleElevation.HighAngle => "high-angle shot",
        _ => throw new ArgumentOutOfRangeException(nameof(elevation), elevation, "Unknown multi-angle elevation.")
    };

    public static string Distance(MultiAngleDistance distance) => distance switch
    {
        MultiAngleDistance.CloseUp => "close-up",
        MultiAngleDistance.Medium => "medium shot",
        MultiAngleDistance.Wide => "wide shot",
        _ => throw new ArgumentOutOfRangeException(nameof(distance), distance, "Unknown multi-angle distance.")
    };

    /// <summary>The full instruction for a picked pose: <c>&lt;sks&gt; azimuth elevation distance</c>.</summary>
    public static string Instruction(MultiAngleAzimuth azimuth, MultiAngleElevation elevation, MultiAngleDistance distance)
        => $"{Trigger} {Azimuth(azimuth)} {Elevation(elevation)} {Distance(distance)}";
}
