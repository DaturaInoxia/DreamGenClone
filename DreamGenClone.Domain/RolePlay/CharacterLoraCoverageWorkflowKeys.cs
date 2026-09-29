namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Every prompt-template key the LoRA dataset pipeline resolves, seeded into the ONE template store under
/// the <c>lora.</c> namespace (B-123 Phase 1). Nothing here is a prompt body — the bodies and every phrase
/// live in the store as data, exactly as the face and body pipelines do it.
/// </summary>
public static class LoraCellWorkflowKeys
{
    // ---- Render templates: one per angle family × framing, twelve rows. -------------------------------
    public const string RenderFrontClose = "lora.cell.render.front.close";
    public const string RenderFrontHalf = "lora.cell.render.front.half";
    public const string RenderFrontFull = "lora.cell.render.front.full";
    public const string RenderThreeQuarterClose = "lora.cell.render.threequarter.close";
    public const string RenderThreeQuarterHalf = "lora.cell.render.threequarter.half";
    public const string RenderThreeQuarterFull = "lora.cell.render.threequarter.full";
    public const string RenderProfileClose = "lora.cell.render.profile.close";
    public const string RenderProfileHalf = "lora.cell.render.profile.half";
    public const string RenderProfileFull = "lora.cell.render.profile.full";
    public const string RenderBehindClose = "lora.cell.render.behind.close";
    public const string RenderBehindHalf = "lora.cell.render.behind.half";
    public const string RenderBehindFull = "lora.cell.render.behind.full";

    // ---- The cell's other single-purpose prompts. -----------------------------------------------------
    //
    // There is deliberately no negative-prompt key. The families this pipeline renders carry no negative:
    // SDXL / Juggernaut / BigLust resolve to an EMPTY negative by model-author research, Pony takes only a short
    // guard set authored by its own compiler, and FLUX has no negative field at all. A per-cell negative would be
    // a control that contradicts three of those documents at once.
    public const string Caption = "lora.cell.caption";
    public const string EditTweak = "lora.cell.edit.tweak";
    public const string References = "lora.cell.references";

    // ---- Vocabulary: the wording of every variable axis, one row per value. ---------------------------
    public const string VocabularyWardrobeClothed = "lora.vocabulary.wardrobe.clothed";
    public const string VocabularyWardrobeUnclothed = "lora.vocabulary.wardrobe.unclothed";

    public const string VocabularyPoseStanding = "lora.vocabulary.pose.standing";
    public const string VocabularyPoseSitting = "lora.vocabulary.pose.sitting";
    public const string VocabularyPoseKneeling = "lora.vocabulary.pose.kneeling";
    public const string VocabularyPoseLying = "lora.vocabulary.pose.lying";
    public const string VocabularyPoseAllFours = "lora.vocabulary.pose.allfours";
    public const string VocabularyPoseHandsRaised = "lora.vocabulary.pose.handsraised";

    public const string VocabularyExpressionNeutral = "lora.vocabulary.expression.neutral";
    public const string VocabularyExpressionSmiling = "lora.vocabulary.expression.smiling";
    public const string VocabularyExpressionLaughing = "lora.vocabulary.expression.laughing";
    public const string VocabularyExpressionSurprised = "lora.vocabulary.expression.surprised";
    public const string VocabularyExpressionSerious = "lora.vocabulary.expression.serious";
    public const string VocabularyExpressionSensual = "lora.vocabulary.expression.sensual";

    public const string VocabularyLightingIndoorBright = "lora.vocabulary.lighting.indoor-bright";
    public const string VocabularyLightingIndoorDim = "lora.vocabulary.lighting.indoor-dim";
    public const string VocabularyLightingOutdoorDay = "lora.vocabulary.lighting.outdoor-day";
    public const string VocabularyLightingOutdoorGolden = "lora.vocabulary.lighting.outdoor-golden";
    public const string VocabularyLightingOutdoorNight = "lora.vocabulary.lighting.outdoor-night";
    public const string VocabularyLightingHardRim = "lora.vocabulary.lighting.hard-rim";

    public const string VocabularyBackgroundPlainWall = "lora.vocabulary.background.plain-wall";
    public const string VocabularyBackgroundBedroom = "lora.vocabulary.background.bedroom";
    public const string VocabularyBackgroundLivingRoom = "lora.vocabulary.background.living-room";
    public const string VocabularyBackgroundKitchen = "lora.vocabulary.background.kitchen";
    public const string VocabularyBackgroundOutdoors = "lora.vocabulary.background.outdoors";
    public const string VocabularyBackgroundStudio = "lora.vocabulary.background.studio";

    public const string VocabularyOutfitCasual = "lora.vocabulary.outfit.casual";
    public const string VocabularyOutfitFormal = "lora.vocabulary.outfit.formal";
    public const string VocabularyOutfitAthletic = "lora.vocabulary.outfit.athletic";
    public const string VocabularyOutfitLoungewear = "lora.vocabulary.outfit.loungewear";
    public const string VocabularyOutfitSleepwear = "lora.vocabulary.outfit.sleepwear";
    public const string VocabularyOutfitUnclothed = "lora.vocabulary.outfit.unclothed";

    // ---- Frame-scoped wardrobe. -------------------------------------------------------------------------
    //
    // A cell may only claim what its framing can show. The full-body phrases name garments BELOW the waist
    // ("a plain t-shirt and jeans"), which a "waist up" or a "head and both shoulders" frame cannot show at
    // all: measured on the live plan, all ten close-up cells claimed a full outfit - five of them claiming the
    // subject was completely unclothed - over a frame that shows neither. These are the SAME states phrased for
    // the frame that carries them, so the wardrobe axis survives at every distance instead of being dropped, and
    // the caption tag (clothed / nude) stays one stable concept word across the whole set.
    public const string VocabularyWardrobeClothedClose = "lora.vocabulary.wardrobe.clothed.close";
    public const string VocabularyWardrobeUnclothedClose = "lora.vocabulary.wardrobe.unclothed.close";
    public const string VocabularyOutfitCasualHalf = "lora.vocabulary.outfit.casual.half";
    public const string VocabularyOutfitFormalHalf = "lora.vocabulary.outfit.formal.half";
    public const string VocabularyOutfitAthleticHalf = "lora.vocabulary.outfit.athletic.half";
    public const string VocabularyOutfitLoungewearHalf = "lora.vocabulary.outfit.loungewear.half";
    public const string VocabularyOutfitSleepwearHalf = "lora.vocabulary.outfit.sleepwear.half";
    public const string VocabularyOutfitUnclothedHalf = "lora.vocabulary.outfit.unclothed.half";

    public const string VocabularyDistanceClose = "lora.vocabulary.distance.close";
    public const string VocabularyDistanceHalf = "lora.vocabulary.distance.half";
    public const string VocabularyDistanceFull = "lora.vocabulary.distance.full";

    public const string VocabularyAngleFront = "lora.vocabulary.angle.front";
    public const string VocabularyAngleThreeQuarter = "lora.vocabulary.angle.threequarter";
    public const string VocabularyAngleProfile = "lora.vocabulary.angle.profile";
    public const string VocabularyAngleBehind = "lora.vocabulary.angle.behind";

    // Which way the subject faces relative to the camera. Separate from the angle family because a
    // three-quarter and a profile can both face the same way, and the family sentence supplies the amount
    // of turn while this supplies the direction.
    public const string VocabularyFacingCamera = "lora.vocabulary.facing.camera";
    public const string VocabularyFacingLeft = "lora.vocabulary.facing.left";
    public const string VocabularyFacingRight = "lora.vocabulary.facing.right";
    public const string VocabularyFacingAway = "lora.vocabulary.facing.away";

    public const string VocabularySplitTrain = "lora.vocabulary.split.train";
    public const string VocabularySplitValidation = "lora.vocabulary.split.validation";

    /// <summary>Every render key, in matrix order.</summary>
    public static readonly IReadOnlyList<string> RenderKeys =
    [
        RenderFrontClose, RenderFrontHalf, RenderFrontFull,
        RenderThreeQuarterClose, RenderThreeQuarterHalf, RenderThreeQuarterFull,
        RenderProfileClose, RenderProfileHalf, RenderProfileFull,
        RenderBehindClose, RenderBehindHalf, RenderBehindFull
    ];

    /// <summary>The garments a FULL-BODY frame shows, in rotation order. Its index order is the waist-up order.</summary>
    public static readonly IReadOnlyList<string> FullBodyOutfitKeys =
    [
        VocabularyOutfitCasual, VocabularyOutfitFormal, VocabularyOutfitAthletic,
        VocabularyOutfitLoungewear, VocabularyOutfitSleepwear
    ];

    /// <summary>
    /// The same garments as a "waist up" frame shows them: the top only, because the rest is not in frame.
    /// Parallel to <see cref="FullBodyOutfitKeys" /> by index, so the rotation advances over one garment list.
    /// </summary>
    public static readonly IReadOnlyList<string> HalfBodyOutfitKeys =
    [
        VocabularyOutfitCasualHalf, VocabularyOutfitFormalHalf, VocabularyOutfitAthleticHalf,
        VocabularyOutfitLoungewearHalf, VocabularyOutfitSleepwearHalf
    ];

    /// <summary>Every vocabulary key the generator resolves. A missing one fails fast naming it.</summary>
    public static readonly IReadOnlyList<string> VocabularyKeys =
    [
        VocabularyWardrobeClothed, VocabularyWardrobeUnclothed,
        VocabularyWardrobeClothedClose, VocabularyWardrobeUnclothedClose,
        VocabularyPoseStanding, VocabularyPoseSitting, VocabularyPoseKneeling,
        VocabularyPoseLying, VocabularyPoseAllFours, VocabularyPoseHandsRaised,
        VocabularyExpressionNeutral, VocabularyExpressionSmiling, VocabularyExpressionLaughing,
        VocabularyExpressionSurprised, VocabularyExpressionSerious, VocabularyExpressionSensual,
        VocabularyLightingIndoorBright, VocabularyLightingIndoorDim, VocabularyLightingOutdoorDay,
        VocabularyLightingOutdoorGolden, VocabularyLightingOutdoorNight, VocabularyLightingHardRim,
        VocabularyBackgroundPlainWall, VocabularyBackgroundBedroom, VocabularyBackgroundLivingRoom,
        VocabularyBackgroundKitchen, VocabularyBackgroundOutdoors, VocabularyBackgroundStudio,
        VocabularyOutfitCasual, VocabularyOutfitFormal, VocabularyOutfitAthletic,
        VocabularyOutfitLoungewear, VocabularyOutfitSleepwear, VocabularyOutfitUnclothed,
        VocabularyOutfitCasualHalf, VocabularyOutfitFormalHalf, VocabularyOutfitAthleticHalf,
        VocabularyOutfitLoungewearHalf, VocabularyOutfitSleepwearHalf, VocabularyOutfitUnclothedHalf,
        VocabularyDistanceClose, VocabularyDistanceHalf, VocabularyDistanceFull,
        VocabularyAngleFront, VocabularyAngleThreeQuarter, VocabularyAngleProfile, VocabularyAngleBehind,
        VocabularyFacingCamera, VocabularyFacingLeft, VocabularyFacingRight, VocabularyFacingAway,
        VocabularySplitTrain, VocabularySplitValidation
    ];

    /// <summary>All keys, so a test can prove each one resolves and none is a stray literal.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        .. RenderKeys, Caption, EditTweak, References, .. VocabularyKeys
    ];

    /// <summary>Key → the wardrobe state it phrases.</summary>
    public static string WardrobeKey(LoraCoverageWardrobeState state) => state switch
    {
        LoraCoverageWardrobeState.Clothed => VocabularyWardrobeClothed,
        LoraCoverageWardrobeState.Unclothed => VocabularyWardrobeUnclothed,
        _ => throw new InvalidOperationException($"Unsupported wardrobe state '{state}'.")
    };

    /// <summary>Key → the pose class it phrases.</summary>
    public static string PoseKey(LoraCoveragePoseClass poseClass) => poseClass switch
    {
        LoraCoveragePoseClass.Standing => VocabularyPoseStanding,
        LoraCoveragePoseClass.Sitting => VocabularyPoseSitting,
        LoraCoveragePoseClass.Kneeling => VocabularyPoseKneeling,
        LoraCoveragePoseClass.Lying => VocabularyPoseLying,
        LoraCoveragePoseClass.AllFours => VocabularyPoseAllFours,
        LoraCoveragePoseClass.HandsRaised => VocabularyPoseHandsRaised,
        _ => throw new InvalidOperationException($"Unsupported pose class '{poseClass}'.")
    };

    /// <summary>Key → the distance it phrases.</summary>
    public static string DistanceKey(LoraCoverageDistance distance) => distance switch
    {
        LoraCoverageDistance.CloseUp => VocabularyDistanceClose,
        LoraCoverageDistance.HalfBody => VocabularyDistanceHalf,
        LoraCoverageDistance.FullBody => VocabularyDistanceFull,
        _ => throw new InvalidOperationException($"Unsupported distance '{distance}'.")
    };

    /// <summary>The waist-up phrasing of each full-body garment, by the full-body key.</summary>
    private static readonly IReadOnlyDictionary<string, string> HalfBodyPhraseFor =
        FullBodyOutfitKeys.Zip(HalfBodyOutfitKeys, (full, half) => (full, half))
            .ToDictionary(pair => pair.full, pair => pair.half, StringComparer.Ordinal);

    /// <summary>
    /// Whether a framing can show a stance AT ALL.
    ///
    /// <para>
    /// A head-and-shoulders close-up cannot. Standing, sitting and kneeling are indistinguishable inside it - an
    /// earlier pass restricted the close-up cycle to exactly those three and it fixed nothing, because the problem
    /// was never which stances were listed but that the frame shows none of them. A stance the picture cannot show
    /// is a claim the render contradicts and the caption teaches anyway.
    /// </para>
    /// </summary>
    public static bool FramingShowsStance(LoraCoverageDistance distance) => distance switch
    {
        LoraCoverageDistance.CloseUp => false,
        LoraCoverageDistance.HalfBody => true,
        LoraCoverageDistance.FullBody => true,
        _ => throw new InvalidOperationException($"Unsupported distance '{distance}'.")
    };

    /// <summary>
    /// The stances a framing can actually TELL APART, in rotation order. Empty means the frame shows no stance, so
    /// the cell claims none. Waist up, standing against sitting is readable from the thigh line and the seat; kneeling
    /// is not, because the knees are below the frame.
    /// </summary>
    public static IReadOnlyList<LoraCoveragePoseClass> StancesFor(LoraCoverageDistance distance) => distance switch
    {
        LoraCoverageDistance.CloseUp => [],
        LoraCoverageDistance.HalfBody => [LoraCoveragePoseClass.Standing, LoraCoveragePoseClass.Sitting],
        LoraCoverageDistance.FullBody =>
        [
            LoraCoveragePoseClass.Standing, LoraCoveragePoseClass.Sitting, LoraCoveragePoseClass.Kneeling,
            LoraCoveragePoseClass.Lying, LoraCoveragePoseClass.AllFours, LoraCoveragePoseClass.HandsRaised
        ],
        _ => throw new InvalidOperationException($"Unsupported distance '{distance}'.")
    };

    /// <summary>
    /// A cell declares its outfit in the FULL-BODY vocabulary, because that is where the garment is named, and this
    /// maps it to what the cell's own framing shows. Every distance therefore gets a phrase true of its own frame,
    /// and a key with no frame-honest phrasing is refused by name rather than narrowed to something adjacent.
    /// </summary>
    public static string OutfitKeyForDistance(string declaredOutfitKey, LoraCoverageDistance distance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredOutfitKey);

        var unclothed = string.Equals(declaredOutfitKey, VocabularyOutfitUnclothed, StringComparison.Ordinal);
        if (!unclothed && !FullBodyOutfitKeys.Contains(declaredOutfitKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{declaredOutfitKey}' is neither a full-body outfit nor a wardrobe phrase, so no frame-honest "
                + "phrasing can be derived for it. A cell names its outfit in the full-body vocabulary and this maps "
                + "it to what the cell's own frame shows.");
        }

        return distance switch
        {
            LoraCoverageDistance.CloseUp => unclothed
                ? VocabularyWardrobeUnclothedClose
                : VocabularyWardrobeClothedClose,
            LoraCoverageDistance.HalfBody => unclothed
                ? VocabularyOutfitUnclothedHalf
                : HalfBodyPhraseFor[declaredOutfitKey],
            LoraCoverageDistance.FullBody => declaredOutfitKey,
            _ => throw new InvalidOperationException($"Unsupported distance '{distance}'.")
        };
    }

    /// <summary>Key → the angle family it phrases.</summary>
    public static string AngleKey(LoraCoverageAngleFamily family) => family switch
    {
        LoraCoverageAngleFamily.Front => VocabularyAngleFront,
        LoraCoverageAngleFamily.ThreeQuarter => VocabularyAngleThreeQuarter,
        LoraCoverageAngleFamily.Profile => VocabularyAngleProfile,
        LoraCoverageAngleFamily.Behind => VocabularyAngleBehind,
        _ => throw new InvalidOperationException($"Unsupported angle family '{family}'.")
    };

    /// <summary>The namespace every vocabulary row lives under.</summary>
    public const string VocabularyPrefix = "lora.vocabulary.";

    /// <summary>
    /// The short value of a vocabulary key — <c>lora.vocabulary.background.plain-wall</c> becomes
    /// <c>plain-wall</c>. For display only: the key stays the identity, and nothing resolves a phrase through this.
    /// </summary>
    public static string ShortName(string? vocabularyKey)
    {
        if (string.IsNullOrWhiteSpace(vocabularyKey))
        {
            return string.Empty;
        }

        return vocabularyKey.StartsWith(VocabularyPrefix, StringComparison.Ordinal)
            ? vocabularyKey[VocabularyPrefix.Length..]
            : vocabularyKey;
    }

    /// <summary>
    /// Key → the direction phrase for a cell. One function so the phrase and the yaw can never disagree:
    /// a cell that shows no face is a view from behind, and any other yaw is described by its sign.
    /// </summary>
    public static string FacingKey(bool faceVisible, int angleYawDeg)
    {
        if (!faceVisible)
        {
            return VocabularyFacingAway;
        }

        return angleYawDeg switch
        {
            0 => VocabularyFacingCamera,
            > 0 => VocabularyFacingRight,
            _ => VocabularyFacingLeft
        };
    }

    /// <summary>Key → the split it phrases.</summary>
    public static string SplitKey(CharacterLoraDatasetSplit split) => split switch
    {
        CharacterLoraDatasetSplit.Train => VocabularySplitTrain,
        CharacterLoraDatasetSplit.Validation => VocabularySplitValidation,
        _ => throw new InvalidOperationException($"Unsupported split '{split}'.")
    };

    /// <summary>
    /// The render template key for a cell. Framing comes from the distance, so the two axes can never
    /// disagree about which template a cell uses.
    /// </summary>
    public static string RenderKey(LoraCoverageAngleFamily family, LoraCoverageDistance distance)
    {
        var framing = distance switch
        {
            LoraCoverageDistance.CloseUp => "close",
            LoraCoverageDistance.HalfBody => "half",
            LoraCoverageDistance.FullBody => "full",
            _ => throw new InvalidOperationException($"Unsupported distance '{distance}'.")
        };

        var angle = family switch
        {
            LoraCoverageAngleFamily.Front => "front",
            LoraCoverageAngleFamily.ThreeQuarter => "threequarter",
            LoraCoverageAngleFamily.Profile => "profile",
            LoraCoverageAngleFamily.Behind => "behind",
            _ => throw new InvalidOperationException($"Unsupported angle family '{family}'.")
        };

        return $"lora.cell.render.{angle}.{framing}";
    }
}
