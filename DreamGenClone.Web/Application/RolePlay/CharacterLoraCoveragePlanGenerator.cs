using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Builds a character's coverage plan from the voice, lighting, background, outfit, expression and pose
/// vocabulary that lives in the prompt store (B-123 Phase 1).
///
/// <para>
/// Three properties are deliberate and load-bearing:
/// </para>
/// <list type="number">
/// <item><b>Deterministic.</b> The same request produces byte-identical cells. A dataset shot over weeks
/// has to be reproducible, and a plan that reshuffles itself loses which cell a picture belongs to.</item>
/// <item><b>Every number is configured.</b> The cell counts, the seed range, the aspects and every diversity
/// minimum come from the persisted curation policy. Nothing here invents a threshold.</item>
/// <item><b>Every word is data.</b> The phrases come from the vocabulary rows in the template store, and they
/// are snapshotted into the plan. A missing phrase fails fast naming its key instead of trimming a prompt
/// down to something nobody chose.</item>
/// </list>
///
/// <para>
/// It composes nothing that costs money: no render is dispatched and no batch action is exposed. The plan is
/// a checklist the operator works through cell by cell.
/// </para>
/// </summary>
public sealed class CharacterLoraCoveragePlanGenerator : ICharacterLoraCoveragePlanGenerator
{
    /// <summary>
    /// One angle of the matrix: where the camera is, which references it uses, and how many cells it
    /// contributes at each distance. The counts are the matrix from the dataset capture list — profile and
    /// angled views are weighted highest there because angled identity is the known weak point.
    /// </summary>
    private sealed record AngleAxis(
        string Key,
        LoraCoverageAngleFamily Family,
        int YawDeg,
        bool FaceVisible,
        SceneImageReferenceFaceView? FaceSlot,
        SceneImageReferenceBodyView BodySlot,
        int CloseUp,
        int HalfBody,
        int FullBody)
    {
        public int Total => CloseUp + HalfBody + FullBody;

        public bool IsHeldOut => !FaceVisible;

        /// <summary>Position in <see cref="Matrix"/>, used only to stagger which pose each axis opens on.</summary>
        public int Offset { get; init; }
    }

    private static readonly AngleAxis[] Matrix =
    [
        new("front", LoraCoverageAngleFamily.Front, 0, true,
            SceneImageReferenceFaceView.Front, SceneImageReferenceBodyView.Front, 2, 2, 2) { Offset = 0 },
        new("34l", LoraCoverageAngleFamily.ThreeQuarter, -45, true,
            SceneImageReferenceFaceView.ThreeQuarterLeft, SceneImageReferenceBodyView.ThreeQuarterLeft, 1, 2, 2) { Offset = 1 },
        new("34r", LoraCoverageAngleFamily.ThreeQuarter, 45, true,
            SceneImageReferenceFaceView.ThreeQuarterRight, SceneImageReferenceBodyView.ThreeQuarterRight, 1, 2, 2) { Offset = 2 },
        new("pl", LoraCoverageAngleFamily.Profile, -90, true,
            SceneImageReferenceFaceView.ProfileLeft, SceneImageReferenceBodyView.ProfileLeft, 2, 2, 2) { Offset = 3 },
        new("pr", LoraCoverageAngleFamily.Profile, 90, true,
            SceneImageReferenceFaceView.ProfileRight, SceneImageReferenceBodyView.ProfileRight, 2, 2, 2) { Offset = 4 },
        new("behind", LoraCoverageAngleFamily.Behind, 180, false,
            null, SceneImageReferenceBodyView.Back, 0, 1, 1) { Offset = 5 }
    ];

    /// <summary>Rotations, not choices: a cycle guarantees the diversity the policy asks for, with no repeats side by side.</summary>
    private static readonly LoraCoveragePoseClass[] PoseCycle =
    [
        LoraCoveragePoseClass.Standing,
        LoraCoveragePoseClass.Sitting,
        LoraCoveragePoseClass.Kneeling,
        LoraCoveragePoseClass.HandsRaised,
        LoraCoveragePoseClass.AllFours,
        LoraCoveragePoseClass.Lying
    ];

    private static readonly string[] CoreExpressionCycle =
    [
        LoraCellWorkflowKeys.VocabularyExpressionNeutral,
        LoraCellWorkflowKeys.VocabularyExpressionSmiling,
        LoraCellWorkflowKeys.VocabularyExpressionSerious,
        LoraCellWorkflowKeys.VocabularyExpressionSensual
    ];

    private static readonly string[] LightingCycle =
    [
        LoraCellWorkflowKeys.VocabularyLightingIndoorBright,
        LoraCellWorkflowKeys.VocabularyLightingOutdoorDay,
        LoraCellWorkflowKeys.VocabularyLightingIndoorDim,
        LoraCellWorkflowKeys.VocabularyLightingOutdoorGolden,
        LoraCellWorkflowKeys.VocabularyLightingOutdoorNight,
        LoraCellWorkflowKeys.VocabularyLightingHardRim
    ];

    private static readonly string[] BackgroundCycle =
    [
        LoraCellWorkflowKeys.VocabularyBackgroundPlainWall,
        LoraCellWorkflowKeys.VocabularyBackgroundBedroom,
        LoraCellWorkflowKeys.VocabularyBackgroundLivingRoom,
        LoraCellWorkflowKeys.VocabularyBackgroundKitchen,
        LoraCellWorkflowKeys.VocabularyBackgroundOutdoors,
        LoraCellWorkflowKeys.VocabularyBackgroundStudio
    ];

    private static readonly string[] OutfitCycle =
    [
        LoraCellWorkflowKeys.VocabularyOutfitCasual,
        LoraCellWorkflowKeys.VocabularyOutfitFormal,
        LoraCellWorkflowKeys.VocabularyOutfitAthletic,
        LoraCellWorkflowKeys.VocabularyOutfitLoungewear,
        LoraCellWorkflowKeys.VocabularyOutfitSleepwear
    ];

    private readonly ICharacterLoraRepository _datasets;
    private readonly IImageWorkflowTemplateService _templates;

    public CharacterLoraCoveragePlanGenerator(
        ICharacterLoraRepository datasets,
        IImageWorkflowTemplateService templates)
    {
        _datasets = datasets;
        _templates = templates;
    }

    public async Task<CoveragePlan> GenerateAsync(
        CoveragePlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.CharacterProfileId, "The coverage plan's character");
        Require(request.IdentityPackId, "The coverage plan's identity pack");
        Require(request.TriggerToken, "The coverage plan's trigger token");
        Require(request.TargetModelFamily, "The coverage plan's target model family");
        if (request.IdentityPackVersion <= 0)
        {
            throw new InvalidOperationException("The coverage plan's identity pack version must be positive.");
        }

        // A dataset cannot be projected from a pack that has no body: the unclothed body reference is what
        // half the matrix conditions on, and a face-only pack would leave those cells rendering from text.
        if (request.IdentityPackScope != CharacterImageIdentityPackScope.BodyComplete)
        {
            throw new InvalidOperationException(
                $"A LoRA coverage plan requires a BodyComplete identity pack; pack '{request.IdentityPackId}' is "
                + $"{request.IdentityPackScope}. Promote the body set first.");
        }

        var policy = await _datasets.ResolveCurationPolicyAsync(request.CharacterProfileId, cancellationToken);
        var expectedTotal = policy.ExpectedCoreCellCount + policy.ExpectedVariationCellCount;
        if (expectedTotal > policy.SeedRangeLength)
        {
            throw new InvalidOperationException(
                $"The curation policy asks for {expectedTotal} cells but allocates only {policy.SeedRangeLength} "
                + "seeds, and a seed is used once.");
        }

        var plan = new CoveragePlan
        {
            CharacterProfileId = request.CharacterProfileId.Trim(),
            IdentityPackId = request.IdentityPackId.Trim(),
            IdentityPackVersion = request.IdentityPackVersion,
            TriggerToken = request.TriggerToken.Trim(),
            TargetModelFamily = request.TargetModelFamily.Trim(),
            SeedRangeStart = policy.SeedRangeStart,
            GeneratedUtc = DateTime.UtcNow,
            Records = BuildRecords(policy)
        };

        // Snapshot every phrase the plan depends on, so editing the store tomorrow cannot rewrite what this
        // plan was shot against. A key with no row fails here, naming itself.
        foreach (var key in LoraCellWorkflowKeys.VocabularyKeys)
        {
            var template = await _templates.ResolveAsync(key, plan.CharacterProfileId, cancellationToken);
            plan.Vocabulary[key] = template.Body;
        }

        plan.Validate();

        var gaps = plan.DescribeGaps(policy);
        if (gaps.Count > 0)
        {
            // The generator's own output failing its own minima means the matrix and the policy disagree.
            // Refusing here is the difference between a misconfigured policy and a silently thin dataset.
            throw new InvalidOperationException(
                "The generated coverage plan does not meet the configured minima: " + string.Join("; ", gaps)
                + ". Adjust the curation policy or the coverage matrix; the plan is not usable as generated.");
        }

        return plan;
    }

    private static List<CoverageRecord> BuildRecords(CurationPolicy policy)
    {
        var records = new List<CoverageRecord>();
        var seed = policy.SeedRangeStart;
        var outfitCursor = 0;

        // A pose cursor per angle-and-distance series, offset per axis. Two cells shot from the same angle at
        // the same distance sit side by side in the grid, and giving them the same stance is how a training set
        // ends up full of near-duplicates; the offset keeps different axes from all opening on the same pose.
        var poseCursors = new Dictionary<string, int>(StringComparer.Ordinal);

        // ---- the core matrix: every angle, at every distance, the number of times the matrix asks for.
        foreach (var axis in Matrix)
        {
            foreach (var (distance, count) in new[]
                     {
                         (LoraCoverageDistance.CloseUp, axis.CloseUp),
                         (LoraCoverageDistance.HalfBody, axis.HalfBody),
                         (LoraCoverageDistance.FullBody, axis.FullBody)
                     })
            {
                var seriesKey = $"{axis.Key}|{DistanceKey(distance)}";
                for (var ordinal = 1; ordinal <= count; ordinal++)
                {
                    var index = records.Count;
                    // Alternate wardrobe over the whole plan rather than within a group, so the set lands at
                    // 50/50 exactly and no angle group is all one state.
                    var wardrobe = index % 2 == 0
                        ? LoraCoverageWardrobeState.Clothed
                        : LoraCoverageWardrobeState.Unclothed;

                    var record = new CoverageRecord
                    {
                        Key = $"core.{axis.Key}.{DistanceKey(distance)}.{ordinal}",
                        Role = LoraCoverageCellRole.Core,
                        AngleFamily = axis.Family,
                        AngleYawDeg = axis.YawDeg,
                        FaceVisible = axis.FaceVisible,
                        FaceCanonicalSlot = axis.FaceSlot,
                        BodyCanonicalSlot = axis.BodySlot,
                        BodyState = BodyStateFor(wardrobe),
                        Distance = distance,
                        WardrobeState = wardrobe,
                        PoseClass = NextPoseClass(poseCursors, seriesKey, axis.Offset),
                        ExpressionKey = CoreExpressionCycle[index % CoreExpressionCycle.Length],
                        LightingKey = LightingCycle[(index + 1) % LightingCycle.Length],
                        BackgroundKey = BackgroundCycle[index % BackgroundCycle.Length],
                        OutfitKey = NextOutfitKey(wardrobe, ref outfitCursor),
                        Aspect = AspectFor(policy, distance),
                        Seed = seed++,
                        // A view with no face is the strongest test of whether body identity generalized, so
                        // the behind pair is held out along with the variation cells.
                        Split = axis.IsHeldOut ? CharacterLoraDatasetSplit.Validation : CharacterLoraDatasetSplit.Train
                    };

                    records.Add(record);
                }
            }
        }

        // ---- the variation cells: the six axes the core matrix deliberately holds constant, each varied once.
        foreach (var variation in Variations)
        {
            var index = records.Count;
            var wardrobe = index % 2 == 0
                ? LoraCoverageWardrobeState.Clothed
                : LoraCoverageWardrobeState.Unclothed;

            records.Add(new CoverageRecord
            {
                Key = variation.Key,
                Role = LoraCoverageCellRole.Variation,
                AngleFamily = variation.Axis.Family,
                AngleYawDeg = variation.Axis.YawDeg,
                FaceVisible = variation.Axis.FaceVisible,
                FaceCanonicalSlot = variation.Axis.FaceSlot,
                BodyCanonicalSlot = variation.Axis.BodySlot,
                BodyState = BodyStateFor(wardrobe),
                Distance = variation.Distance,
                WardrobeState = wardrobe,
                PoseClass = PoseCycle[index % PoseCycle.Length],
                ExpressionKey = variation.ExpressionKey,
                LightingKey = variation.LightingKey,
                BackgroundKey = BackgroundCycle[index % BackgroundCycle.Length],
                OutfitKey = variation.OutfitKey ?? NextOutfitKey(wardrobe, ref outfitCursor),
                Aspect = AspectFor(policy, variation.Distance),
                Seed = seed++,
                Split = CharacterLoraDatasetSplit.Validation
            });
        }

        return records;
    }

    /// <summary>
    /// Advances one series' pose cursor. Series advance independently, so two frames of the same view never
    /// share a stance while the plan as a whole still sweeps every class.
    /// </summary>
    private static LoraCoveragePoseClass NextPoseClass(
        Dictionary<string, int> cursors, string seriesKey, int axisOffset)
    {
        cursors.TryGetValue(seriesKey, out var position);
        cursors[seriesKey] = position + 1;
        return PoseCycle[(position + axisOffset) % PoseCycle.Length];
    }

    /// <summary>
    /// The six cells that vary one axis at a time. They are the validation split, so a trained LoRA is judged
    /// on frames it was never shown rather than on the matrix it memorized.
    /// </summary>
    private static readonly VariationCell[] Variations =
    [
        new("variation.outfit.a", 0, LoraCoverageDistance.HalfBody,
            LoraCellWorkflowKeys.VocabularyOutfitFormal, LoraCellWorkflowKeys.VocabularyLightingIndoorBright,
            LoraCellWorkflowKeys.VocabularyExpressionNeutral),
        new("variation.outfit.b", 1, LoraCoverageDistance.HalfBody,
            LoraCellWorkflowKeys.VocabularyOutfitAthletic, LoraCellWorkflowKeys.VocabularyLightingOutdoorDay,
            LoraCellWorkflowKeys.VocabularyExpressionSmiling),
        new("variation.lighting.rim", 3, LoraCoverageDistance.HalfBody,
            null, LoraCellWorkflowKeys.VocabularyLightingHardRim, LoraCellWorkflowKeys.VocabularyExpressionSerious),
        new("variation.lighting.dim", 4, LoraCoverageDistance.HalfBody,
            null, LoraCellWorkflowKeys.VocabularyLightingIndoorDim, LoraCellWorkflowKeys.VocabularyExpressionNeutral),
        new("variation.expression.laughing", 0, LoraCoverageDistance.CloseUp,
            null, LoraCellWorkflowKeys.VocabularyLightingIndoorBright, LoraCellWorkflowKeys.VocabularyExpressionLaughing),
        new("variation.expression.surprised", 2, LoraCoverageDistance.CloseUp,
            null, LoraCellWorkflowKeys.VocabularyLightingOutdoorDay, LoraCellWorkflowKeys.VocabularyExpressionSurprised)
    ];

    /// <summary>A variation cell: which angle it borrows, what it varies, and what stays as the matrix has it.</summary>
    private sealed record VariationCell(
        string Key,
        int AxisIndex,
        LoraCoverageDistance Distance,
        string? OutfitKey,
        string LightingKey,
        string ExpressionKey)
    {
        public AngleAxis Axis => Matrix[AxisIndex];
    }

    private static SceneImageReferenceBodyState BodyStateFor(LoraCoverageWardrobeState wardrobe)
        => wardrobe == LoraCoverageWardrobeState.Clothed
            ? SceneImageReferenceBodyState.Clothed
            : SceneImageReferenceBodyState.Unclothed;

    private static string AspectFor(CurationPolicy policy, LoraCoverageDistance distance)
        => distance == LoraCoverageDistance.CloseUp ? policy.CloseUpAspect : policy.PortraitAspect;

    private static string DistanceKey(LoraCoverageDistance distance) => distance switch
    {
        LoraCoverageDistance.CloseUp => "cu",
        LoraCoverageDistance.HalfBody => "hb",
        LoraCoverageDistance.FullBody => "fb",
        _ => throw new InvalidOperationException($"Unsupported distance '{distance}'.")
    };

    /// <summary>
    /// Rotates the garments across clothed cells only. An unclothed cell has no garment to vary, and giving it
    /// one would caption clothing that is not there.
    /// </summary>
    private static string NextOutfitKey(LoraCoverageWardrobeState wardrobe, ref int cursor)
    {
        if (wardrobe == LoraCoverageWardrobeState.Unclothed)
        {
            return LoraCellWorkflowKeys.VocabularyOutfitUnclothed;
        }

        var key = OutfitCycle[cursor % OutfitCycle.Length];
        cursor++;
        return key;
    }

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
    }
}
