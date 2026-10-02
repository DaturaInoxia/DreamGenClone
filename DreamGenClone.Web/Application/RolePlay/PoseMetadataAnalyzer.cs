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
    double TorsoLength,
    double HeadOffsetX,
    bool ShouldersVisible,
    bool HipsVisible,
    bool AnklesVisible,
    bool FaceVisible)
{
    /// <summary>True when a height could be measured at all, i.e. the pose has a usable spatial extent.</summary>
    public bool IsMeasurable => FigureHeight > 0 && ShouldersVisible;

    /// <summary>
    /// The shoulder line's length as a share of the torso's, which is the turn measure: a figure square to the
    /// camera shows the whole line, and one turned side-on foreshortens it to nothing. Both lengths belong to the
    /// same figure, so the ratio is independent of how big the pose is or what resolution it was stored at.
    /// </summary>
    public double ShoulderSpanOverTorso =>
        TorsoLength > 0 ? ShoulderSpanRatio * FigureHeight / TorsoLength : 0;

    /// <summary>
    /// How far the head sits off the torso's axis, SIGNED. Negative is her left: on a figure whose left side has come
    /// round toward the camera the nose ends up at smaller x (measured on the rig: -0.12 at yaw -45, +0.12 at +45).
    /// </summary>
    public double HeadOffsetOverTorso => TorsoLength > 0 ? HeadOffsetX / TorsoLength : 0;

    /// <summary>
    /// The front/back reading this measurement supports, or <see cref="PoseFacingDirection.Unknown"/> when the
    /// evidence does not decide.
    ///
    /// Two gates, both necessary. The shoulders must be VISIBLY separated (see
    /// <see cref="MinimumDecisiveOrderRatio"/>), and the torso must be UPRIGHT (see
    /// <see cref="PoseGeometryObservation.FoldedTorsoRatio"/>) — the ordering only encodes front from back when the
    /// shoulder line is roughly horizontal in the world and the camera is level. That is true of a standing figure and
    /// equally true of one on hands and knees, whose shoulder line is horizontal too; this property abstains on the
    /// quadruped anyway because its gate is an upright torso, and the quadruped reading lives in
    /// <see cref="MeasuredQuadrupedFacing"/>, which applies the same ordering without that gate.
    ///
    /// An earlier version of this note read the shipped all-fours pack's even split (the shoulders ordered one way in
    /// 6 of 12 poses and the other way in the rest) as evidence that the ordering was noise there. That inference was
    /// wrong: an even split is exactly what a pack holding both front-facing and rear-facing quadrupeds looks like. The
    /// operator, reading those poses by eye, reports some facing the camera and some away, and the ordering agrees with
    /// the two of them they named (pinned in the all-fours tests).
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

    /// <summary>
    /// The turn cuts, DERIVED FROM THE RIG'S OWN ANGLES rather than picked: exporting the rig at its five named views
    /// puts the shoulder line at 0.67 of the torso at yaw 0, 0.47 at +/-45 degrees and 0.00 at +/-90 (measured
    /// 2026-09-28). These are the midpoints between those readings, so a pose within about 22 degrees of square-on
    /// counts as square-on, a 3/4 covers roughly 22 to 68, and a profile is beyond that.
    /// </summary>
    public const double FrontalSpanCut = 0.57;

    public const double ProfileSpanCut = 0.24;

    /// <summary>
    /// How far off the torso's axis the head must sit before it can name a side. The rig measures +/-0.12 at a true
    /// 3/4 and +/-0.17 at a true profile, so this sits below both and still clear of a head that faces the camera.
    /// </summary>
    public const double MinimumDecisiveHeadOffset = 0.08;

    /// <summary>
    /// Which way the figure is turned, from the two signals a flat skeleton actually carries: how much of the shoulder
    /// line is on screen, and which way the head points. Takes the stance because the answer only means anything for a
    /// stance whose torso stands up — see <see cref="IsTurnMeasurable"/> — with all fours measured on its own terms by
    /// <see cref="MeasuredQuadrupedFacing"/>.
    ///
    /// WHY THIS EXISTS. The shoulder ORDERING above can only separate front from back, and it stays negative all the
    /// way through a 45-degree turn (measured on the rig: -0.67 at yaw 0, -0.47 at +/-45, 0.00 at +/-90). So every 3/4
    /// view in the library read as "front" and no amount of re-running could ever correct it — that is the defect this
    /// measurement answers.
    ///
    /// The two signals must AGREE. The span is measured against the torso's apparent length, so a figure whose
    /// shoulder-to-torso proportions differ from the rig's measures a smaller span for the SAME true angle, reads
    /// "turned", and is contradicted by a head that faces the camera. When they disagree the answer is Unknown:
    /// <see cref="TurnSignalsDisagree"/> records it, and nothing is guessed.
    /// </summary>
    public PoseFacingDirection MeasuredTurn(PoseStance stance)
    {
        if (!IsMeasurable) return PoseFacingDirection.Unknown;

        // All fours is asked FIRST and answered on its own terms, because a quadruped's torso is horizontal by
        // definition: the upright gate below would refuse every one of them and the category would go unmeasured.
        if (stance == PoseStance.AllFours) return MeasuredQuadrupedFacing();

        if (TorsoVerticalRatio < FoldedTorsoRatio) return PoseFacingDirection.Unknown;

        if (!IsTurnMeasurable(stance))
        {
            // One reading is still available where the turn measure does not apply, and it is the one that does not
            // depend on a stance: the shoulder ORDERING, which says front from back wherever the torso stands up. A
            // pack that declares NOTHING therefore still gets front-or-back from its keypoints, which is the behaviour
            // an undeclared pack has always had. What it does not get is the span-based turn, because that is the
            // reading a stance is required to authorize.
            return stance == PoseStance.Unknown ? MeasuredFacing : PoseFacingDirection.Unknown;
        }

        if (ShoulderSpanOverTorso >= FrontalSpanCut) return MeasuredFacing;

        // The shoulders read turned, so the head has to name the side. A head facing the camera contradicts them and
        // nothing is claimed.
        if (Math.Abs(HeadOffsetOverTorso) < MinimumDecisiveHeadOffset) return PoseFacingDirection.Unknown;

        return ShoulderSpanOverTorso < ProfileSpanCut
            ? (HeadOffsetOverTorso < 0 ? PoseFacingDirection.ProfileLeft : PoseFacingDirection.ProfileRight)
            : (HeadOffsetOverTorso < 0 ? PoseFacingDirection.ThreeQuarterLeft : PoseFacingDirection.ThreeQuarterRight);
    }

    /// <summary>
    /// A quadruped's facing, measured on its own terms. This is deliberately NOT the upright rule with its gate
    /// removed.
    ///
    /// The reason is that the upright rule reads the span as a TURN MAGNITUDE: the standing torso is vertical, so a
    /// turn about it cannot shorten it, and the shoulder line's foreshortening is the whole story. On all fours the
    /// torso is horizontal and IS shortened by the same turn — a quadruped seen from behind has its spine pointing at
    /// the camera — so the span is confounded and cannot be turned into an angle. Applying the upright cuts here is not
    /// a near miss, it is wrong: `all_fours 006` measures span 0.41 with the head at +0.45, which the upright rule
    /// calls a three-quarter view, and the pose in fact faces AWAY from the camera.
    ///
    /// So this asks the two questions a quadruped can really answer.
    ///
    ///   * Is the shoulder line EDGE-ON? Then this is a side view, and the head says which side.
    ///   * Otherwise the line is across the view, so the body is pointing at or away from the camera, and the ordering
    ///     says which — the same signal the upright rule uses, and valid for the same reason: a quadruped's shoulder
    ///     line lies in the world's horizontal plane just as a standing figure's does.
    ///
    /// What it never returns is a THREE-QUARTER. Naming one would require a turn magnitude, and that is the reading
    /// the span cannot carry here.
    /// </summary>
    public PoseFacingDirection MeasuredQuadrupedFacing()
    {
        if (ShoulderSpanOverTorso < ProfileSpanCut)
        {
            if (Math.Abs(HeadOffsetOverTorso) < MinimumDecisiveHeadOffset) return PoseFacingDirection.Unknown;
            return HeadOffsetOverTorso < 0 ? PoseFacingDirection.ProfileLeft : PoseFacingDirection.ProfileRight;
        }

        if (Math.Abs(ShoulderOrderRatio) < MinimumDecisiveOrderRatio) return PoseFacingDirection.Unknown;
        return ShoulderOrderRatio > 0 ? PoseFacingDirection.Back : PoseFacingDirection.Front;
    }

    /// <summary>
    /// True when the signals that decide a facing contradict each other, which is the one abstention worth telling the
    /// operator about. Both cases are the same disagreement in different postures: the shoulder line says the body is
    /// side-on, and the head does not say which side.
    /// </summary>
    public bool TurnSignalsDisagree(PoseStance stance)
    {
        if (!IsMeasurable) return false;
        if (Math.Abs(HeadOffsetOverTorso) >= MinimumDecisiveHeadOffset) return false;

        // A quadruped is side-on when its shoulder line is edge-on; an upright figure is side-on somewhere between the
        // square-on cut and the profile cut, which is where its span stops being able to name a side.
        return stance == PoseStance.AllFours
            ? ShoulderSpanOverTorso < ProfileSpanCut
            : IsTurnMeasurable(stance)
                && TorsoVerticalRatio >= FoldedTorsoRatio
                && ShoulderSpanOverTorso < FrontalSpanCut;
    }

    /// <summary>
    /// The stances the turn measurement applies to: those whose torso stands up in the world, which is where the rig
    /// calibration was taken.
    ///
    /// A LYING body is excluded, and the reason is geometric rather than cautious. Turning a lying figure about the
    /// vertical axis rotates it WITHIN the picture instead of foreshortening its shoulder line, so the span carries no
    /// facing there at all — the same number means a different thing. A SUSPENDED figure is excluded for the same
    /// reason: its torso is not the upright reference the cuts were measured against. Those stances keep the direction
    /// their pack declares, and a pack that declares nothing keeps "not declared" rather than a guess.
    ///
    /// ALL FOURS is not in this list either, and it is not measured by this rule: it has its own, because a
    /// quadruped's torso is horizontal by definition and this gate would refuse every pose in the category. See
    /// <see cref="MeasuredQuadrupedFacing"/>.
    /// </summary>
    public static bool IsTurnMeasurable(PoseStance stance) =>
        stance is PoseStance.Standing or PoseStance.Sitting or PoseStance.Kneeling or PoseStance.Squatting
            or PoseStance.SplitLeg or PoseStance.Dancing or PoseStance.Flexing or PoseStance.Jumping
            or PoseStance.TPose;

    /// <summary>The evidence, in words, for a review note or a report line.</summary>
    public string Evidence() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"shoulder-order {ShoulderOrderRatio:+0.000;-0.000;0.000}, span {ShoulderSpanRatio:0.000}, "
            + $"span/torso {ShoulderSpanOverTorso:0.000}, head-offset {HeadOffsetOverTorso:+0.000;-0.000;0.000}, "
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
            return new PoseGeometryObservation(0, 0, 0, 0, 0, 0, false, false, false, false);
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
        var torsoLength = 0.0;
        if (unit > 0 && hipsVisible)
        {
            var hipX = (rightHip.X + leftHip.X) / 2;
            var hipY = (rightHip.Y + leftHip.Y) / 2;
            var dx = Math.Abs(hipX - neck.X);
            var dy = Math.Abs(hipY - neck.Y);
            torsoVertical = dx + dy > 0 ? dy / (dx + dy) : 0;
            torsoLength = Math.Sqrt(dx * dx + dy * dy);
        }

        var nose = body[OpenPosePoseJson.NoseIndex];

        return new PoseGeometryObservation(
            FigureHeight: height,
            ShoulderOrderRatio: shoulderOrder,
            TorsoVerticalRatio: torsoVertical,
            ShoulderSpanRatio: shoulderSpan,
            TorsoLength: torsoLength,
            // Only a visible head can point anywhere; a hidden nose contributes no offset rather than an offset of zero
            // in the middle of the figure, which would read as "faces the camera" and be believed.
            HeadOffsetX: IsVisible(nose) ? nose.X - neck.X : 0,
            ShouldersVisible: shouldersVisible,
            HipsVisible: hipsVisible,
            AnklesVisible: anklesVisible,
            FaceVisible: IsVisible(nose)
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
        var measured = observation.MeasuredTurn(declared.Stance);

        // The MEASUREMENT outranks the pack's declaration on this one axis, and only where it is decisive. A pack
        // declares per CATEGORY, and one category holds many views — `standing` alone spans the whole range (0.08 to
        // 2.20 measured), so a single word cannot be right for every pose filed under it. Stance, camera and rating
        // keep the declaration as their authority, because nothing in a skeleton contradicts them.
        var direction = measured != PoseFacingDirection.Unknown ? measured : declared.Direction;

        var notes = new List<string>();
        if (observation.TurnSignalsDisagree(declared.Stance))
        {
            notes.Add(
                $"declared {Label(declared.Direction)} but the shoulders read turned while the head faces the "
                + $"camera ({observation.Evidence()}), so which side faces the camera was not decided");
        }

        if (IsUprightStance(declared.Stance)
            && observation.HipsVisible
            && observation.TorsoVerticalRatio > 0
            && observation.TorsoVerticalRatio < PoseGeometryObservation.FoldedTorsoRatio)
        {
            // A folded torso under an upright stance word. This is NOT a claim that the pose is something else — a
            // figure bending at the waist is still standing, and the pack's own folder is the better authority on
            // which joints carry the weight. It is the fact the prompt's stance word cannot carry ("standing" does not
            // say "bent over"), recorded where a render that comes back straight can be compared against it.
            //
            // Measured on the shipped packs (2026-09-30): 48 flagged poses in 7 categories, 28 of them in
            // openpose-nsfw/standing. Verified by recomputing four of them straight from the pack files — standing 081
            // measures torso-vertical 0.484 and standing 075 measures 0.414 — so this is data about the packs rather
            // than a threshold that needs moving.
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
