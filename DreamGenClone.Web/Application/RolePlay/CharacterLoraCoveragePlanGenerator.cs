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
/// <item><b>Coherent.</b> A cell's light and its setting come from one paired table, and its stance from a
/// cycle its framing can show, so no cell asks the model for two things that cannot both be true. See
/// <see cref="LightingBackgroundPairs"/> and <see cref="LoraCellWorkflowKeys.StancesFor"/>.</item>
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
        int FullBody,
        int Far)
    {
        public int Total => CloseUp + HalfBody + FullBody + Far;

        public bool IsHeldOut => !FaceVisible;

        /// <summary>Position in <see cref="Matrix"/>, used only to stagger which pose each axis opens on.</summary>
        public int Offset { get; init; }
    }

    private static readonly AngleAxis[] Matrix =
    [
        new("front", LoraCoverageAngleFamily.Front, 0, true,
            SceneImageReferenceFaceView.Front, SceneImageReferenceBodyView.Front, 2, 2, 2, 1) { Offset = 0 },
        new("34l", LoraCoverageAngleFamily.ThreeQuarter, -45, true,
            SceneImageReferenceFaceView.ThreeQuarterLeft, SceneImageReferenceBodyView.ThreeQuarterLeft, 1, 2, 2, 0) { Offset = 1 },
        new("34r", LoraCoverageAngleFamily.ThreeQuarter, 45, true,
            SceneImageReferenceFaceView.ThreeQuarterRight, SceneImageReferenceBodyView.ThreeQuarterRight, 1, 2, 2, 0) { Offset = 2 },
        new("pl", LoraCoverageAngleFamily.Profile, -90, true,
            SceneImageReferenceFaceView.ProfileLeft, SceneImageReferenceBodyView.ProfileLeft, 2, 2, 2, 1) { Offset = 3 },
        new("pr", LoraCoverageAngleFamily.Profile, 90, true,
            SceneImageReferenceFaceView.ProfileRight, SceneImageReferenceBodyView.ProfileRight, 2, 2, 2, 1) { Offset = 4 },
        new("behind", LoraCoverageAngleFamily.Behind, 180, false,
            null, SceneImageReferenceBodyView.Back, 0, 1, 1, 1) { Offset = 5 },

        // Over the shoulder, added 2026-10-04. ONE axis, not a mirrored pair, because the shot is the same picture
        // whichever shoulder the head turns over - the sign of the yaw names which shoulder, not a different framing.
        // The BODY slot is the back view and the FACE slot is a three-quarter, because that is what the camera
        // actually sees: her back to us and her face turned back into view. That combination is the whole reason the
        // cell exists - it is the only cell in the set that shows a face while the body is turned away.
        new("os", LoraCoverageAngleFamily.OverShoulder, 135, true,
            SceneImageReferenceFaceView.ThreeQuarterRight, SceneImageReferenceBodyView.Back, 1, 1, 1, 0) { Offset = 6 }
    ];

    /// <summary>
    /// The stances a framing can show, and therefore the stances the plan may claim for it, live in
    /// <see cref="LoraCellWorkflowKeys.StancesFor"/>. A close-up claims NONE: standing, sitting and kneeling cannot be
    /// told apart inside a head-and-shoulders frame, so the cell states no stance and both the prompt and the caption
    /// drop the element. An earlier pass restricted the close-up cycle to those three and fixed nothing - the problem
    /// was never which stances were listed, only that the frame shows none of them.
    /// </summary>
    private static readonly string[] CoreExpressionCycle =
    [
        LoraCellWorkflowKeys.VocabularyExpressionNeutral,
        LoraCellWorkflowKeys.VocabularyExpressionSmiling,
        LoraCellWorkflowKeys.VocabularyExpressionSerious,
        LoraCellWorkflowKeys.VocabularyExpressionSensual
    ];

    /// <summary>
    /// The light and the setting ONE cell is shot in, as pairs rather than two cycles that never met. The lighting and
    /// background used to advance independently, so 14 of the live plan's 36 cells paired an indoor phrase with an
    /// outdoor setting or the reverse ("flat daylight outdoors" against "a plain neutral wall"; "a hard rim light ...
    /// against a dark surround" against "trees and open sky"). A prompt that contradicts itself about where the
    /// subject is gets resolved by the model toward its own prior — which, with bright studio reference images, is the
    /// bright studio look the operator was seeing.
    ///
    /// What holds here, and is asserted by test, is COHERENCE and BALANCE rather than a formula: every natural-light
    /// setup sits outdoors or by a window, the studio's three setups are lit by studio light, night is lit by one
    /// practical source, and across the eighteen pairs each of the six backgrounds appears three times and each of the
    /// six lightings appears at least twice (four at most) — so a 36-cell plan still shows every axis value without
    /// ever pairing two that cannot both be true. Each pair is used twice across the plan, and no two cells of the same
    /// view sit on the same pair, because the table's neighbours differ in both axes.
    /// </summary>
    private static readonly (string LightingKey, string BackgroundKey)[] LightingBackgroundPairs =
    [
        (LoraCellWorkflowKeys.VocabularyLightingIndoorBright, LoraCellWorkflowKeys.VocabularyBackgroundPlainWall),
        (LoraCellWorkflowKeys.VocabularyLightingIndoorDim, LoraCellWorkflowKeys.VocabularyBackgroundBedroom),
        (LoraCellWorkflowKeys.VocabularyLightingOutdoorGolden, LoraCellWorkflowKeys.VocabularyBackgroundOutdoors),
        (LoraCellWorkflowKeys.VocabularyLightingIndoorDim, LoraCellWorkflowKeys.VocabularyBackgroundKitchen),
        (LoraCellWorkflowKeys.VocabularyLightingOutdoorNight, LoraCellWorkflowKeys.VocabularyBackgroundLivingRoom),
        (LoraCellWorkflowKeys.VocabularyLightingOutdoorDay, LoraCellWorkflowKeys.VocabularyBackgroundOutdoors),
        (LoraCellWorkflowKeys.VocabularyLightingIndoorBright, LoraCellWorkflowKeys.VocabularyBackgroundKitchen),
        (LoraCellWorkflowKeys.VocabularyLightingOutdoorNight, LoraCellWorkflowKeys.VocabularyBackgroundPlainWall),
        (LoraCellWorkflowKeys.VocabularyLightingIndoorDim, LoraCellWorkflowKeys.VocabularyBackgroundLivingRoom),
        (LoraCellWorkflowKeys.VocabularyLightingIndoorBright, LoraCellWorkflowKeys.VocabularyBackgroundBedroom),
        (LoraCellWorkflowKeys.VocabularyLightingHardRim, LoraCellWorkflowKeys.VocabularyBackgroundStudio),
        (LoraCellWorkflowKeys.VocabularyLightingOutdoorNight, LoraCellWorkflowKeys.VocabularyBackgroundOutdoors),
        (LoraCellWorkflowKeys.VocabularyLightingOutdoorGolden, LoraCellWorkflowKeys.VocabularyBackgroundKitchen),
        (LoraCellWorkflowKeys.VocabularyLightingOutdoorDay, LoraCellWorkflowKeys.VocabularyBackgroundPlainWall),
        (LoraCellWorkflowKeys.VocabularyLightingIndoorBright, LoraCellWorkflowKeys.VocabularyBackgroundStudio),
        (LoraCellWorkflowKeys.VocabularyLightingOutdoorGolden, LoraCellWorkflowKeys.VocabularyBackgroundBedroom),
        (LoraCellWorkflowKeys.VocabularyLightingHardRim, LoraCellWorkflowKeys.VocabularyBackgroundLivingRoom),
        (LoraCellWorkflowKeys.VocabularyLightingIndoorDim, LoraCellWorkflowKeys.VocabularyBackgroundStudio)
    ];

    /// <summary>
    /// Every setting a lighting value is coherent in, in table order. A variation cell names the axis it varies, so
    /// its light is fixed by the cell and its setting has to be re-derived from this map — otherwise a cell that
    /// exists to vary the LIGHTING could be handed the setting of a pair whose light it overrode.
    /// </summary>
    private static readonly Dictionary<string, string[]> BackgroundsForLighting = LightingBackgroundPairs
        .GroupBy(pair => pair.LightingKey, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => group.Select(pair => pair.BackgroundKey).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

    /// <summary>The six settings, in table order — the order the dealer walks them in.</summary>
    private static readonly string[] BackgroundOrder = LightingBackgroundPairs
        .Select(pair => pair.BackgroundKey)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Every light a setting can be lit by, in table order.</summary>
    private static readonly Dictionary<string, string[]> LightingsForBackground = LightingBackgroundPairs
        .GroupBy(pair => pair.BackgroundKey, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => group.Select(pair => pair.LightingKey).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

    /// <summary>
    /// The garments a full-body frame rotates through. The rotation itself lives in
    /// <see cref="LoraCellWorkflowKeys.OutfitKeyForDistance"/>, which is the one place that knows what a given frame
    /// can show - a second list here would be a second opinion about the same fact.
    /// </summary>

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

        // The matrix and the policy have to agree about how big the set is. Checking only the seed range let a matrix
        // that had outgrown its policy pass silently: the extra cells still drew seeds from a range with room in it, so
        // nothing failed, and the policy went on describing a set that no longer existed - the counts an operator
        // reads, and the diversity minima derived from them, quietly stopped being true. Compare the totals directly.
        var matrixCoreTotal = Matrix.Sum(axis => axis.Total);
        if (matrixCoreTotal != policy.ExpectedCoreCellCount)
        {
            throw new InvalidOperationException(
                $"The coverage matrix generates {matrixCoreTotal} core cells but the curation policy expects "
                + $"{policy.ExpectedCoreCellCount}. The policy's seed allocation and diversity minima describe the set "
                + $"it was written for, so the two are not interchangeable. Set the policy's core cell count to "
                + $"{matrixCoreTotal} in the LoRA dataset workspace, or restore the matrix it was written for.");
        }

        if (Variations.Length != policy.ExpectedVariationCellCount)
        {
            throw new InvalidOperationException(
                $"The coverage matrix generates {Variations.Length} variation cells but the curation policy expects "
                + $"{policy.ExpectedVariationCellCount}. Set the policy's variation cell count to {Variations.Length} "
                + "in the LoRA dataset workspace, or restore the matrix it was written for.");
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
        // How often each coherent (light, setting) pair has been used, and which settings each angle group has already
        // been given. Both exist so the dealer below can prefer an unused pair and never repeat a scene inside a group.
        var pairUsage = new Dictionary<string, int>(StringComparer.Ordinal);
        var lightUsage = new Dictionary<string, int>(StringComparer.Ordinal);
        var axesInUse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // A pose cursor per angle-and-distance series, offset per axis. Two cells shot from the same angle at
        // the same distance sit side by side in the grid, and giving them the same stance is how a training set
        // ends up full of near-duplicates; the offset keeps different axes from all opening on the same pose.
        var poseCursors = new Dictionary<string, int>(StringComparer.Ordinal);

        // One cursor per lighting value, for the variation cells that override the light and therefore have to
        // re-derive a setting that light is coherent in.
        var lightingCursors = new Dictionary<string, int>(StringComparer.Ordinal);

        // ---- the core matrix: every angle, at every distance, the number of times the matrix asks for.
        foreach (var axis in Matrix)
        {
            foreach (var (distance, count) in new[]
                     {
                         (LoraCoverageDistance.CloseUp, axis.CloseUp),
                         (LoraCoverageDistance.HalfBody, axis.HalfBody),
                         (LoraCoverageDistance.FullBody, axis.FullBody),
                         (LoraCoverageDistance.Far, axis.Far)
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

                    // The light and the setting come from ONE coherent pair, DEALT per angle group: no setting repeats
                    // inside a group, and the group's light for that setting is the one used least across the whole plan.
                    // Dealing matters because the previous code walked one flat table by cell index, so two cells of the
                    // same group could land on the same setting - the operator's "both rules are broken by the 4th cell".
                    var pair = NextLightingBackgroundPair(axesInUse, pairUsage, lightUsage, index, axis.Offset, distance);

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
                        PoseClass = NextPoseClass(poseCursors, seriesKey, axis.Offset, distance),
                        ExpressionKey = CoreExpressionCycle[index % CoreExpressionCycle.Length],
                        LightingKey = pair.LightingKey,
                        BackgroundKey = pair.BackgroundKey,
                        OutfitKey = NextOutfitKey(wardrobe, distance, ref outfitCursor),
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

            // A variation cell declares the light it varies, so it goes through the SAME dealer: its declared light
            // constrains which settings are coherent, and the dealer still refuses to reuse a setting its group has
            // already been given. Routing these cells around the dealer is exactly how variation.outfit.a ended up on
            // the same setting as core.front.hb.1.
            var variationPair = NextLightingBackgroundPair(
                axesInUse, pairUsage, lightUsage, index, variation.Axis.Offset, variation.Distance,
                variation.LightingKey);

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
                PoseClass = PoseForVariation(index, variation.Distance),
                ExpressionKey = variation.ExpressionKey,
                LightingKey = variationPair.LightingKey,
                // The declared light wins, so the setting is the coherent one the dealer dealt it.
                BackgroundKey = variationPair.BackgroundKey,
                OutfitKey = variation.OutfitKey is { } declared
                    ? LoraCellWorkflowKeys.OutfitKeyForDistance(declared, variation.Distance)
                    : NextOutfitKey(wardrobe, variation.Distance, ref outfitCursor),
                Aspect = AspectFor(policy, variation.Distance),
                Seed = seed++,
                Split = CharacterLoraDatasetSplit.Validation
            });
        }

        return records;
    }

    /// <summary>
    /// Advances one series' stance cursor. Series advance independently, so two frames of the same view never share a
    /// stance while the plan as a whole still sweeps every class. The cycle depends on the FRAMING: a framing that shows
    /// no stance returns null and the cell claims none.
    /// </summary>
    private static LoraCoveragePoseClass? NextPoseClass(
        Dictionary<string, int> cursors, string seriesKey, int axisOffset, LoraCoverageDistance distance)
    {
        return NextStance(LoraCellWorkflowKeys.StancesFor(distance), seriesKey, axisOffset, cursors);
    }

    private static LoraCoveragePoseClass? NextStance(
        IReadOnlyList<LoraCoveragePoseClass> cycle, string seriesKey, int axisOffset, Dictionary<string, int> cursors)
    {
        if (cycle.Count == 0)
        {
            // Nothing to rotate: the framing shows no stance, so the cell makes no claim about one.
            return null;
        }

        cursors.TryGetValue(seriesKey, out var position);
        cursors[seriesKey] = position + 1;
        return cycle[(position + axisOffset) % cycle.Count];
    }

    /// <summary>
    /// A variation cell's stance. These cells stand outside the same-view series, so they take the cycle by their own
    /// position in the plan - which is what makes the six of them sweep every class - but the cycle is still the one its
    /// FRAMING can show, and a framing that shows no stance claims none.
    /// </summary>
    private static LoraCoveragePoseClass? PoseForVariation(int index, LoraCoverageDistance distance)
    {
        var cycle = LoraCellWorkflowKeys.StancesFor(distance);
        return cycle.Count == 0 ? null : cycle[index % cycle.Count];
    }

    /// <summary>
    /// The next setting a lighting value is coherent in, rotating so a light used by several variation cells does not
    /// repeat one room. A lighting value the table does not carry is refused by key rather than given a setting: the
    /// table is what makes "coherent" true, and a value missing from it has no coherent setting to return.
    /// </summary>
    private static string NextBackgroundFor(string lightingKey, Dictionary<string, int> cursors)
    {
        if (!BackgroundsForLighting.TryGetValue(lightingKey, out var backgrounds) || backgrounds.Length == 0)
        {
            throw new InvalidOperationException(
                $"No coherent setting is recorded for lighting '{lightingKey}'. Add it to the paired table before it can "
                + "be planned: a lighting phrase paired with a setting it cannot occur in is a prompt that contradicts "
                + "itself.");
        }

        cursors.TryGetValue(lightingKey, out var position);
        cursors[lightingKey] = position + 1;
        return backgrounds[position % backgrounds.Length];
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
        => distance switch
        {
            LoraCoverageDistance.CloseUp => policy.CloseUpAspect,
            // Far is the one framing whose size is not a portrait shape: what makes it far is the amount of scene
            // around the subject, and that is a policy value rather than a constant in this method.
            LoraCoverageDistance.Far => policy.FarAspect,
            _ => policy.PortraitAspect
        };

    private static string DistanceKey(LoraCoverageDistance distance) => distance switch
    {
        LoraCoverageDistance.CloseUp => "cu",
        LoraCoverageDistance.HalfBody => "hb",
        LoraCoverageDistance.FullBody => "fb",
        LoraCoverageDistance.Far => "far",
        _ => throw new InvalidOperationException($"Unsupported distance '{distance}'.")
    };

    /// <summary>
    /// The next frame-honest wardrobe phrase for a cell. A cell names its outfit in the FULL-BODY vocabulary and this
    /// maps it to what the cell's own frame shows: the neckline and shoulders at close-up, the top at waist-up, the
    /// whole outfit at full body. Rotation continues only where the frame can tell the garments apart - five phrases
    /// the frame cannot distinguish would be variety in the JSON and none in the picture.
    /// </summary>
    private static string NextOutfitKey(LoraCoverageWardrobeState wardrobe, LoraCoverageDistance distance, ref int cursor)
    {
        if (wardrobe == LoraCoverageWardrobeState.Unclothed)
        {
            return LoraCellWorkflowKeys.OutfitKeyForDistance(
                LoraCellWorkflowKeys.VocabularyOutfitUnclothed, distance);
        }

        var rotation = distance switch
        {
            LoraCoverageDistance.CloseUp => null,
            LoraCoverageDistance.HalfBody => LoraCellWorkflowKeys.HalfBodyOutfitKeys,
            LoraCoverageDistance.FullBody => LoraCellWorkflowKeys.FullBodyOutfitKeys,
            // A far frame shows the whole figure, so it rotates the same garments a full-body frame does and tells
            // them apart the same way. Distance changes how much scene surrounds her, not how much outfit is visible.
            LoraCoverageDistance.Far => LoraCellWorkflowKeys.FullBodyOutfitKeys,
            _ => throw new InvalidOperationException($"Unsupported distance '{distance}'.")
        };

        if (rotation is null)
        {
            return LoraCellWorkflowKeys.OutfitKeyForDistance(LoraCellWorkflowKeys.FullBodyOutfitKeys[0], distance);
        }

        var key = rotation[cursor % rotation.Count];
        cursor++;
        return key;
    }

    /// <summary>
    /// Deals the next coherent (light, setting) pair for one cell.
    ///
    /// <para>
    /// Two rules, both from the researched diversity guidance, and both of them are the reason this is a dealer
    /// rather than a lookup: <b>(1)</b> no setting repeats inside an angle group - cells an operator shoots back to
    /// back, and the pair that a near-duplicate would come from; <b>(2)</b> a pair already used twice is only chosen
    /// when nothing else is coherent, because repeating one exactly is what makes two frames the same shot.
    /// </para>
    /// </summary>
    private static (string LightingKey, string BackgroundKey) NextLightingBackgroundPair(
        Dictionary<string, HashSet<string>> axesInUse,
        Dictionary<string, int> pairUsage,
        Dictionary<string, int> lightUsage,
        int index,
        int axisOffset,
        LoraCoverageDistance distance,
        string? declaredLightingKey = null)
    {
        var seriesKey = $"{axisOffset}|{DistanceKey(distance)}";
        if (!axesInUse.TryGetValue(seriesKey, out var used))
        {
            used = new HashSet<string>(StringComparer.Ordinal);
            axesInUse[seriesKey] = used;
        }

        for (var step = 0; step < BackgroundOrder.Length; step++)
        {
            var background = BackgroundOrder[(axisOffset + step) % BackgroundOrder.Length];
            if (used.Contains(background))
            {
                continue;
            }

            // A variation cell DECLARES its light, so that light decides which settings are available to it. A core cell
            // picks the light with the most REMAINING capacity, so no coherent pair is dealt more than twice and no
            // light runs away - capacity, not usage, is what makes that true: indoor-bright is coherent with four
            // settings and hard-rim with only two, so equal USAGE would push hard-rim's two pairs past their share.
            var light = declaredLightingKey ?? LightingsForBackground[background]
                .Where(candidate => Usage(pairUsage, candidate, background) < PairsPerCondition)
                .OrderByDescending(candidate => PairsPerCondition - Usage(pairUsage, candidate, background))
                .ThenBy(candidate => LightUsage(lightUsage, candidate))
                .ThenBy(candidate => candidate, StringComparer.Ordinal)
                .FirstOrDefault();

            if (light is null || !LightingsForBackground[background].Contains(light, StringComparer.Ordinal))
            {
                continue;
            }

            used.Add(background);
            pairUsage[$"{light}|{background}"] = Usage(pairUsage, light, background) + 1;
            lightUsage[light] = LightUsage(lightUsage, light) + 1;
            return (light, background);
        }

        throw new InvalidOperationException(
            $"No setting is left for cell {index} of {seriesKey} that has not already been used in that group. The plan "
            + $"has {BackgroundOrder.Length} settings and this group asked for more: add settings to the paired table "
            + "rather than repeating a scene.");
    }

    private static int LightUsage(Dictionary<string, int> lightUsage, string lightingKey)
        => lightUsage.TryGetValue(lightingKey, out var used) ? used : 0;

    /// <summary>
    /// How many cells may share one coherent (light, setting) pair. Two, and the number is forced rather than chosen:
    /// there are 18 coherent pairs and 36 cells, so two per pair is the only allocation that uses the whole coherent
    /// space without putting three frames in one identical condition.
    /// </summary>
    private const int PairsPerCondition = 2;

    private static int Usage(Dictionary<string, int> pairUsage, string lightingKey, string backgroundKey)
        => pairUsage.TryGetValue(lightingKey + "|" + backgroundKey, out var used) ? used : 0;

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
    }
}
