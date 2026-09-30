using System.Globalization;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// What the keypoints of ONE pose actually measure, with every number the classification below uses.
///
/// This exists as a separate value from the classification because a measurement and a decision are different
/// things: the numbers are the evidence, and the decision can be argued with. When a pose ends up flagged for
/// review, the note quotes these numbers, so the operator can check the reasoning instead of trusting a verdict.
/// </summary>
/// <param name="FigureHeight">
/// The vertical extent of the visible keypoints, in the pose's own pixels. Every ratio below is divided by it, so a
/// pose stored at 512px and one stored at 1024px classify identically.
/// </param>
/// <param name="ShoulderOrderRatio">
/// (right shoulder x − left shoulder x) ÷ figure height, in IMAGE pixels. OpenPose's "right" is the SUBJECT's right,
/// which appears on the viewer's left in a front view and on the viewer's right in a back view, so the sign of this
/// value reads front from back. Measured on the two packs whose own names state the answer: `on_stomach`
/// (face down, so a back view) is positive in 15 of 15 poses, `on_back` (face up) in 0 of 21.
/// </param>
/// <param name="TorsoVerticalRatio">
/// 0 for a torso lying flat in the frame, 1 for one standing straight up: |hip mid − neck| measured on y over the
/// same measured on x and y together. It separates a folded body from an upright one, and it CANNOT separate a
/// standing figure from a lying figure photographed from above — both measure near 1. That limitation is why the
/// camera angle is declared by the pack and never inferred from this number.
/// </param>
/// <param name="ShoulderSpanRatio">|right shoulder x − left shoulder x| ÷ figure height, unsigned.</param>
/// <param name="FaceVisible">True when the nose and both eyes clear the visibility floor, i.e. a face can be seen.</param>
public sealed record PoseGeometryObservation(
    double FigureHeight,
    double ShoulderOrderRatio,
    double TorsoVerticalRatio,
    double ShoulderSpanRatio,
    bool ShouldersVisible,
    bool HipsVisible,
    bool AnklesVisible,
    bool FaceVisible)
{
    /// <summary>True when a height could be measured at all, i.e. the pose has a usable spatial extent.</summary>
    public bool IsMeasurable => FigureHeight > 0 && ShouldersVisible;

    /// <summary>
    /// The front/back reading this measurement supports, or <see cref="PoseFacingDirection.Unknown"/> when the
    /// evidence does not decide.
    ///
    /// Two gates, both necessary. The shoulders must be VISIBLY separated (see
    /// <see cref="MinimumDecisiveOrderRatio"/>), and the torso must be UPRIGHT (see
    /// <see cref="PoseGeometryObservation.FoldedTorsoRatio"/>) — the ordering only encodes front from back when the
    /// shoulder line is roughly horizontal in the world and the camera is level. On an all-fours pose, where the torso
    /// is horizontal, the two shoulders swap sides between poses for reasons that have nothing to do with facing
    /// (measured: 6 of 12 each way), and reading the ordering there would flag half a category for review over noise.
    /// </summary>
    public PoseFacingDirection MeasuredFacing =>
        !IsMeasurable
        || Math.Abs(ShoulderOrderRatio) < MinimumDecisiveOrderRatio
        || TorsoVerticalRatio < FoldedTorsoRatio
            ? PoseFacingDirection.Unknown
            : ShoulderOrderRatio > 0
                ? PoseFacingDirection.Back
                : PoseFacingDirection.Front;

    /// <summary>
    /// How far apart the shoulders must be before their ordering means anything: 8% of the figure's height. Below
    /// that the pair reads as nearly coincident, which is what a profile view and a foreshortened quadruped both
    /// produce.
    /// </summary>
    public const double MinimumDecisiveOrderRatio = 0.08;

    /// <summary>
    /// The folded-torso threshold: a torso this far off vertical cannot belong to a standing, sitting or kneeling
    /// figure. Used only to contradict an upright declaration, never to invent a lying one.
    /// </summary>
    public const double FoldedTorsoRatio = 0.65;

    /// <summary>The evidence, in words, for a review note or a report line.</summary>
    public string Evidence() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"shoulder-order {ShoulderOrderRatio:+0.000;-0.000;0.000}, span {ShoulderSpanRatio:0.000}, "
            + $"torso-vertical {TorsoVerticalRatio:0.000}, face {(FaceVisible ? "visible" : "not visible")}, "
            + $"shoulders {(ShouldersVisible ? "visible" : "hidden")}");
}

/// <summary>
/// Derives a pose's <see cref="PoseMetadata"/> from its own keypoints PLUS the declaration its pack makes.
///
/// The division of labour is the whole design, and it is not arbitrary:
///
///   * The DECLARATION wins. A pack's category folder says what the pose is ("on_stomach", "NSFW_Kneeling"), which is
///     information the keypoints do not contain — a lying body photographed from above and a standing body
///     photographed from the front produce near-identical 2D skeletons (measured: torso-vertical 0.86 for the lying
///     category against 0.92 for the standing one). Nothing in a flat keypoint set can break that tie.
///   * The MEASUREMENT contradicts. The keypoints CAN read front from back, because the label ordering of the
///     shoulders flips between the two, and they can see a folded torso. A disagreement is recorded as a review flag
///     instead of being averaged away.
///   * The measurement FILLS IN only when the pack declares nothing, and only for the axes it can actually decide.
public static class PoseMetadataAnalyzer
{
    /// <summary>Measures one pose. Never throws for a degenerate pose; the observation says what was missing.</summary>
    public static PoseGeometryObservation Measure(PosePerson person)
    {
        ArgumentNullException.ThrowIfNull(person);

        var body = person.Body;
        if (body.Count != PosePerson.BodyJointCount)
        {
            throw new InvalidOperationException(
                $"A pose must carry {PosePerson.BodyJointCount} body keypoints to be measured, but this one has "
                + $"{body.Count}.");
        }

        var visible = body
            .Where(point => point.Confidence > OpenPosePoseJson.VisibilityFloor)
            .ToArray();

        if (visible.Length == 0)
        {
            return new PoseGeometryObservation(0, 0, 0, 0, false, false, false, false);
        }

        var height = visible.Max(point => point.Y) - visible.Min(point => point.Y);

        var rightShoulder = body[OpenPosePoseJson.RightShoulderIndex];
        var leftShoulder = body[OpenPosePoseJson.LeftShoulderIndex];
        var neck = body[OpenPosePoseJson.NeckIndex];
        var rightHip = body[RightHipIndex];
        var leftHip = body[LeftHipIndex];

        var shouldersVisible = IsVisible(rightShoulder) && IsVisible(leftShoulder);
        var hipsVisible = IsVisible(rightHip) && IsVisible(leftHip) && IsVisible(neck);
        var anklesVisible = IsVisible(body[RightAnkleIndex]) && IsVisible(body[LeftAnkleIndex]);

        // Every ratio is against the figure's own height, so the same pose stored at any resolution classifies the
        // same way. A pose with no vertical extent at all (every joint on one line) yields zeroes, and the thresholds
        // then read it as "not decisive" rather than dividing by zero.
        var unit = height > 0 ? height : 0;
        var shoulderOrder = unit > 0 && shouldersVisible ? (rightShoulder.X - leftShoulder.X) / unit : 0;
        var shoulderSpan = unit > 0 && shouldersVisible ? Math.Abs(rightShoulder.X - leftShoulder.X) / unit : 0;

        var torsoVertical = 0.0;
        if (unit > 0 && hipsVisible)
        {
            var hipX = (rightHip.X + leftHip.X) / 2;
            var hipY = (rightHip.Y + leftHip.Y) / 2;
            var dx = Math.Abs(hipX - neck.X);
            var dy = Math.Abs(hipY - neck.Y);
            torsoVertical = dx + dy > 0 ? dy / (dx + dy) : 0;
        }

        return new PoseGeometryObservation(
            FigureHeight: height,
            ShoulderOrderRatio: shoulderOrder,
            TorsoVerticalRatio: torsoVertical,
            ShoulderSpanRatio: shoulderSpan,
            ShouldersVisible: shouldersVisible,
            HipsVisible: hipsVisible,
            AnklesVisible: anklesVisible,
            FaceVisible: IsVisible(body[OpenPosePoseJson.NoseIndex])
                && IsVisible(body[RightEyeIndex])
                && IsVisible(body[LeftEyeIndex]));
    }

    /// <summary>
    /// The pose's metadata: the declaration, corrected only where it is absent, plus the disagreement flag.
    /// The prompt is composed from the result by <see cref="PoseMetadataPrompt.Compose"/>.
    /// </summary>
    public static PoseMetadata Classify(PosePerson person, PoseMetadataDeclaration declared)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(declared);

        var observation = Measure(person);
        var measured = observation.MeasuredFacing;

        // The declaration is the answer; the measurement only stands in when there is no declaration, and only for
        // the axis it can actually decide (front from back).
        var direction = declared.Direction != PoseFacingDirection.Unknown ? declared.Direction : measured;

        var notes = new List<string>();
        if (declared.Direction is PoseFacingDirection.Front or PoseFacingDirection.Back
            && measured != PoseFacingDirection.Unknown
            && measured != declared.Direction)
        {
            notes.Add(
                $"declared {Label(declared.Direction)} but the shoulders read {Label(measured)} "
                + $"({observation.Evidence()})");
        }

        if (IsUprightStance(declared.Stance)
            && observation.HipsVisible
            && observation.TorsoVerticalRatio > 0
            && observation.TorsoVerticalRatio < PoseGeometryObservation.FoldedTorsoRatio)
        {
            notes.Add(
                $"declared {Label(declared.Stance)} but the torso is folded "
                + $"({observation.Evidence()})");
        }

        var metadata = new PoseMetadata(
            Stance: declared.Stance,
            Direction: direction,
            Camera: declared.Camera,
            Rating: declared.Rating,
            Prompt: string.Empty,
            NeedsReview: notes.Count > 0,
            ReviewNote: string.Join("; ", notes));

        return metadata with { Prompt = PoseMetadataPrompt.Compose(metadata) };
    }

    /// <summary>The stances that are upright by definition, so a folded torso contradicts them.</summary>
    private static bool IsUprightStance(PoseStance stance) =>
        stance is PoseStance.Standing or PoseStance.Sitting or PoseStance.Kneeling or PoseStance.Squatting;

    private static string Label(PoseStance stance) => PoseMetadataLabels.Stance(stance);

    private static string Label(PoseFacingDirection direction) => PoseMetadataLabels.Direction(direction);

    private static bool IsVisible(PoseKeypoint point) => point.Confidence > OpenPosePoseJson.VisibilityFloor;

    // COCO-18 joints the measurement reads directly. The wrist and shoulder indices live on OpenPosePoseJson because
    // other code already shares them; these are the ones only this measurement needs.
    private const int RightHipIndex = 8;
    private const int LeftHipIndex = 11;
    private const int RightAnkleIndex = 10;
    private const int LeftAnkleIndex = 13;
    private const int RightEyeIndex = 14;
    private const int LeftEyeIndex = 15;
}

/// <summary>
/// What a pack declares about one category of its poses. Held as its own type, separate from the pack manifest, so a
/// per-pose override and a per-category default are the same shape and neither can be silently incomplete.
/// </summary>
public sealed record PoseMetadataDeclaration(
    PoseStance Stance,
    PoseFacingDirection Direction,
    PoseCameraAngle Camera,
    PoseContentRating Rating)
{
    /// <summary>
    /// The declaration for a category nothing declares. Every field says "not declared" rather than carrying a
    /// value someone would later mistake for an answer.
    /// </summary>
    public static readonly PoseMetadataDeclaration None = new(
        PoseStance.Unknown, PoseFacingDirection.Unknown, PoseCameraAngle.Unknown, PoseContentRating.Unrated);

    /// <summary>True when nothing at all is declared — the state a pack added without a declaration block is in.</summary>
    public bool IsEmpty =>
        Stance == PoseStance.Unknown
        && Direction == PoseFacingDirection.Unknown
        && Camera == PoseCameraAngle.Unknown
        && Rating == PoseContentRating.Unrated;

    /// <summary>
    /// This declaration with the fields <paramref name="overrides"/> actually states replaced, so a per-pose entry
    /// only has to name what it disagrees with instead of repeating its category's whole declaration.
    /// </summary>
    public PoseMetadataDeclaration OverriddenBy(PoseMetadataDeclaration? overrides)
    {
        if (overrides is null) return this;

        return new PoseMetadataDeclaration(
            overrides.Stance != PoseStance.Unknown ? overrides.Stance : Stance,
            overrides.Direction != PoseFacingDirection.Unknown ? overrides.Direction : Direction,
            overrides.Camera != PoseCameraAngle.Unknown ? overrides.Camera : Camera,
            overrides.Rating != PoseContentRating.Unrated ? overrides.Rating : Rating);
    }
}

/// <summary>Operator-facing names for the metadata enums, in one place so a card and a report cannot disagree.</summary>
public static class PoseMetadataLabels
{
    public static string Stance(PoseStance stance) => stance switch
    {
        PoseStance.Standing => "standing",
        PoseStance.Sitting => "sitting",
        PoseStance.Kneeling => "kneeling",
        PoseStance.Lying => "lying",
        PoseStance.AllFours => "all fours",
        PoseStance.Squatting => "squatting",
        PoseStance.Suspended => "suspended",
        PoseStance.SplitLeg => "split legs",
        PoseStance.Jumping => "jumping",
        PoseStance.Dancing => "dancing",
        PoseStance.Flexing => "flexing",
        PoseStance.TPose => "T-pose",
        _ => "not declared"
    };

    public static string Direction(PoseFacingDirection direction) => direction switch
    {
        PoseFacingDirection.Front => "front",
        PoseFacingDirection.ThreeQuarterLeft => "3/4 left",
        PoseFacingDirection.ThreeQuarterRight => "3/4 right",
        PoseFacingDirection.ProfileLeft => "profile left",
        PoseFacingDirection.ProfileRight => "profile right",
        PoseFacingDirection.Back => "back",
        _ => "not declared"
    };

    public static string Camera(PoseCameraAngle camera) => camera switch
    {
        PoseCameraAngle.EyeLevel => "eye level",
        PoseCameraAngle.FromAbove => "from above",
        PoseCameraAngle.FromBelow => "from below",
        _ => "not declared"
    };

    public static string Rating(PoseContentRating rating) => rating switch
    {
        PoseContentRating.Sfw => "SFW",
        PoseContentRating.Nsfw => "NSFW",
        _ => "not declared"
    };
}
