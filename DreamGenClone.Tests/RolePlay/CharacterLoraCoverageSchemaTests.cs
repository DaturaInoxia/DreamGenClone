using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The coverage plan is the document a three-week dataset shoot is built from, so it has to be readable
/// data with real validation rather than a free-form string. These tests pin the round-trip, the refusal
/// cases, and — most importantly — that a curation policy missing a value fails loudly by name instead of
/// quietly applying a threshold nobody chose (repo Hard Rule: no fallbacks).
/// </summary>
public sealed class CharacterLoraCoverageSchemaTests
{
    // ---------------------------------------------------------------- the record

    [Fact]
    public void Record_RoundTripsThroughItsPlan()
    {
        var plan = BuildPlan();

        var json = plan.ToJson();
        var reread = CoveragePlan.FromJson(json);

        Assert.Equal(CoveragePlan.CurrentSchemaVersion, reread.SchemaVersion);
        Assert.Equal(plan.Records.Count, reread.Records.Count);
        var first = reread.Records[0];
        Assert.Equal("core.front.cu.1", first.Key);
        Assert.Equal(LoraCoverageAngleFamily.Front, first.AngleFamily);
        Assert.Equal(SceneImageReferenceFaceView.Front, first.FaceCanonicalSlot);
        Assert.Equal(SceneImageReferenceBodyView.Front, first.BodyCanonicalSlot);
        Assert.Equal(SceneImageReferenceBodyState.Clothed, first.BodyState);
        Assert.Equal(LoraCoverageDistance.CloseUp, first.Distance);
        // A close-up claims no stance, and the round trip has to preserve that as faithfully as it preserves a
        // stance: null here means "this framing shows none", not "the field was dropped on the way through".
        Assert.Null(first.PoseClass);
        // Whatever stances the plan carried, the round trip preserves exactly the same set: the rule is that a framing
        // claims a stance only where it can show one, and JSON has to carry both halves of that faithfully.
        Assert.Equal(
            plan.Records.Count(record => record.PoseClass is not null),
            reread.Records.Count(record => record.PoseClass is not null));
        Assert.Equal("1024x1024", first.Aspect);
        Assert.Equal(41000, first.Seed);
        Assert.Equal(CharacterLoraDatasetSplit.Train, first.Split);
        Assert.Equal("a front-facing portrait", reread.PhraseFor(first.ExpressionKey));
    }

    /// <summary>
    /// A plan that was PERSISTED under older rules has to still load.
    ///
    /// <para>
    /// The framing rule changed on 2026-09-27 and enforcing it on the READ path made Becky's stored v1 plan refuse to
    /// open the workspace ("a CloseUp frame cannot show the stance 'Standing'"). The guard was right; loading it
    /// strictly was wrong. A stored plan is data written under the rules of its day: it comes back with the reason it
    /// is stale so the page can say so, and the operator regenerates. This is the third time a hard guard has been
    /// applied to data that predates it, so the behaviour is pinned here rather than left to review.
    /// </para>
    /// </summary>
    [Fact]
    public void FromStoredJson_ReadsAnOlderSchemaAndSaysWhyItIsStale()
    {
        var json = BuildPlan()
            .ToJson()
            .Replace($"\"schemaVersion\":{CoveragePlan.CurrentSchemaVersion}", "\"schemaVersion\":1", StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);

        var plan = CoveragePlan.FromStoredJson(json, out var staleReason);

        Assert.NotNull(staleReason);
        Assert.Contains("schema version 1", staleReason, StringComparison.Ordinal);
        Assert.NotEmpty(plan.Records);

        // A current plan is validated strictly and carries no stale reason, so the tolerant path cannot hide a real
        // contradiction in a plan this build generated.
        var current = CoveragePlan.FromStoredJson(BuildPlan().ToJson(), out var currentReason);
        Assert.Null(currentReason);
        Assert.NotEmpty(current.Records);
    }

    /// <summary>A plan's vocabulary is a snapshot, so the caption it produces cannot change under it later.</summary>
    [Fact]
    public void Plan_MissingVocabularyPhrase_FailsFastNamingTheKey()
    {
        var plan = BuildPlan();

        var missing = Assert.Throws<InvalidOperationException>(() => plan.PhraseFor("lora.vocabulary.expression.absent"));

        Assert.Contains("lora.vocabulary.expression.absent", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_DuplicateSeed_IsRefused()
    {
        var plan = BuildPlan();
        plan.Records[1].Seed = plan.Records[0].Seed;

        var error = Assert.Throws<InvalidOperationException>(() => plan.Validate());

        Assert.Contains("never reused", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_DuplicateCellKey_IsRefused()
    {
        var plan = BuildPlan();
        plan.Records[1].Key = plan.Records[0].Key;

        var error = Assert.Throws<InvalidOperationException>(() => plan.Validate());

        Assert.Contains("Duplicate coverage cell key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_EmptyTriggerToken_IsRefused()
    {
        var plan = BuildPlan();
        plan.TriggerToken = "   ";

        var error = Assert.Throws<InvalidOperationException>(() => plan.Validate());

        Assert.Contains("trigger token", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_UnknownSchemaVersion_IsRefused()
    {
        var plan = BuildPlan();
        plan.SchemaVersion = 99;

        var error = Assert.Throws<InvalidOperationException>(() => plan.Validate());

        Assert.Contains("schema version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_EmptyJson_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => CoveragePlan.FromJson(""));
    }

    // ---------------------------------------------------------------- the record's own invariants

    [Fact]
    public void Record_FaceInFrameWithoutAReference_IsRefused()
    {
        var record = BuildRecord();
        record.FaceVisible = true;
        record.FaceCanonicalSlot = null;

        var error = Assert.Throws<InvalidOperationException>(() => record.Validate());

        Assert.Contains("requires the face reference", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_NoFaceWithAFaceReference_IsRefused()
    {
        var record = BuildRecord();
        record.FaceVisible = false;
        record.FaceCanonicalSlot = SceneImageReferenceFaceView.Front;

        var error = Assert.Throws<InvalidOperationException>(() => record.Validate());

        Assert.Contains("cannot carry a face reference", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_NonPositiveSeed_IsRefused()
    {
        var record = BuildRecord();
        record.Seed = 0;

        var error = Assert.Throws<InvalidOperationException>(() => record.Validate());

        Assert.Contains("positive fixed seed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_PhraseWhereAVocabularyKeyBelongsTo_IsRefused()
    {
        var record = BuildRecord();
        record.BackgroundKey = string.Empty;

        var error = Assert.Throws<InvalidOperationException>(() => record.Validate());

        Assert.Contains("BackgroundKey", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_YawOutsideTheCompass_IsRefused()
    {
        var record = BuildRecord();
        record.AngleYawDeg = 200;

        var error = Assert.Throws<InvalidOperationException>(() => record.Validate());

        Assert.Contains("yaw", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The reference rule is a rendering of the slots, so it can never disagree with them.</summary>
    [Fact]
    public void Record_ReferenceRule_NamesTheSlotsItActuallyUses()
    {
        var withFace = BuildRecord();
        Assert.Equal("face Front + body Front (Clothed)", withFace.DescribeReferenceRule());

        var withoutFace = BuildRecord();
        withoutFace.FaceVisible = false;
        withoutFace.FaceCanonicalSlot = null;
        withoutFace.AngleFamily = LoraCoverageAngleFamily.Behind;
        withoutFace.BodyCanonicalSlot = SceneImageReferenceBodyView.Back;
        withoutFace.AngleYawDeg = 180;
        withoutFace.Validate();
        Assert.Contains("no face reference", withoutFace.DescribeReferenceRule(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- gaps

    [Fact]
    public void DescribeGaps_OnACompletePlan_IsEmpty()
    {
        // One core cell plus three variation cells, all four angle families, both wardrobe states,
        // four pose classes, four expressions, two outfits — nothing left for the plan to be short of.
        var plan = BuildPlan(recordCount: 4, distinctEverything: true);
        var policy = BuildPolicy(cores: 1, variations: 3);

        Assert.Empty(plan.DescribeGaps(policy));
    }

    [Fact]
    public void DescribeGaps_ReportsMissingAnglesAndThinVariety()
    {
        var plan = BuildPlan(recordCount: 2);
        var policy = BuildPolicy(cores: 3, variations: 1);
        policy.MinimumTrainMembers = 2;

        var gaps = plan.DescribeGaps(policy);

        Assert.Contains(gaps, gap => gap.Contains("core cells 1/3", StringComparison.Ordinal));
        Assert.Contains(gaps, gap => gap.Contains("no ThreeQuarter cells", StringComparison.Ordinal));
        Assert.Contains(gaps, gap => gap.Contains("no Profile cells", StringComparison.Ordinal));
        Assert.Contains(gaps, gap => gap.Contains("no Behind cells", StringComparison.Ordinal));
        Assert.Contains(gaps, gap => gap.Contains("train split 1/2", StringComparison.Ordinal));
    }

    [Fact]
    public void DescribeGaps_ReportsWardrobeImbalance()
    {
        // Every cell clothed: 0% unclothed against a 10% tolerance. Too much clothed is as much a defect as
        // too much nude — it is what makes a trained LoRA refuse to undress.
        var plan = BuildPlan(recordCount: 4);
        var policy = BuildPolicy(cores: 3, variations: 1);

        var gaps = plan.DescribeGaps(policy);

        Assert.Contains(gaps, gap => gap.Contains("wardrobe balance 0%", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the policy

    /// <summary>
    /// The whole point of <c>required</c> members: a policy row written before a threshold existed must
    /// refuse to load by name rather than run the gates with a threshold somebody guessed.
    /// </summary>
    [Fact]
    public void Policy_MissingAMember_FailsToDeserializeNamingIt()
    {
        var complete = BuildPolicy(cores: 3, variations: 1).ToJson();
        var incomplete = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(complete)!;
        incomplete.Remove("nearDuplicateMaxSimilarity");
        var json = JsonSerializer.Serialize(incomplete);

        var error = Assert.Throws<JsonException>(() => CurationPolicy.FromJson(json));

        Assert.Contains("NearDuplicateMaxSimilarity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The strict reader still refuses a missing far aspect: it is required of every policy this build WRITES, and
    /// only the stored reader may tolerate its absence.
    /// </summary>
    [Fact]
    public void Policy_MissingFarAspect_IsStillRefusedByTheStrictReader()
    {
        var complete = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(BuildPolicy(cores: 3, variations: 1).ToJson())!;
        complete.Remove("farAspect");

        var error = Assert.Throws<JsonException>(() => CurationPolicy.FromJson(JsonSerializer.Serialize(complete)));

        Assert.Contains("FarAspect", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A policy that was SAVED before the FAR rung existed still loads - that is what lets an existing dataset open -
    /// but it comes back stale, naming the absent member, and the far aspect stays EMPTY: no substitute size is
    /// invented for a framing nobody chose a size for.
    /// </summary>
    [Fact]
    public void Policy_StoredWithoutFarAspect_LoadsAsStaleWithNoSubstitute()
    {
        var complete = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(BuildPolicy(cores: 30, variations: 6).ToJson())!;
        complete.Remove("farAspect");

        var policy = CurationPolicy.FromStoredJson(JsonSerializer.Serialize(complete), out var staleReason);

        Assert.NotNull(staleReason);
        Assert.Contains("farAspect", staleReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, policy.FarAspect);
        Assert.Equal(30, policy.ExpectedCoreCellCount);
        // Still incomplete: saving it back is refused, so nothing persists a policy with an unstated size.
        Assert.Throws<InvalidOperationException>(() => policy.Validate());
        Assert.Throws<InvalidOperationException>(() => policy.ToJson());
    }

    /// <summary>A stored policy that carries the far aspect is current - the tolerance must not mark it stale.</summary>
    [Fact]
    public void Policy_StoredWithFarAspect_IsNotStale()
    {
        var policy = CurationPolicy.FromStoredJson(BuildPolicy(cores: 30, variations: 6).ToJson(), out var staleReason);

        Assert.Null(staleReason);
        Assert.Equal("1536x1024", policy.FarAspect);
    }

    /// <summary>
    /// Only the far aspect may be absent from a stored policy. Any other missing member is still refused by name:
    /// no policy that was ever saved could have omitted a threshold that existed when it was written.
    /// </summary>
    [Fact]
    public void Policy_StoredMissingAnyOtherMember_IsRefusedByName()
    {
        var complete = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(BuildPolicy(cores: 3, variations: 1).ToJson())!;
        complete.Remove("nearDuplicateMaxSimilarity");

        var error = Assert.Throws<JsonException>(() =>
            CurationPolicy.FromStoredJson(JsonSerializer.Serialize(complete), out _));

        Assert.Contains("NearDuplicateMaxSimilarity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Policy_NonPositiveCoreCount_IsRefused(int cores)
    {
        var policy = BuildPolicy(cores: cores, variations: 1);

        Assert.Throws<InvalidOperationException>(() => policy.Validate());
    }

    [Fact]
    public void Policy_SeedRangeShorterThanThePlan_IsRefused()
    {
        var policy = BuildPolicy(cores: 30, variations: 6);
        policy.SeedRangeLength = 10;

        var error = Assert.Throws<InvalidOperationException>(() => policy.Validate());

        Assert.Contains("one seed each", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_SimilarityAboveOne_IsRefused()
    {
        var policy = BuildPolicy(cores: 3, variations: 1);
        policy.NearDuplicateMaxSimilarity = 1.5;

        Assert.Throws<InvalidOperationException>(() => policy.Validate());
    }

    [Fact]
    public void Policy_RoundTrips()
    {
        var policy = BuildPolicy(cores: 30, variations: 6);

        var reread = CurationPolicy.FromJson(policy.ToJson());

        Assert.Equal(policy.ExpectedCoreCellCount, reread.ExpectedCoreCellCount);
        Assert.Equal(policy.SeedRangeStart, reread.SeedRangeStart);
        Assert.Equal(policy.NearDuplicateMaxSimilarity, reread.NearDuplicateMaxSimilarity);
        Assert.Equal("1024x1024", reread.CloseUpAspect);
        Assert.Equal("832x1216", reread.PortraitAspect);
        // Far is its own shape and has to survive the round trip as its own value - if it were dropped, every
        // far cell would silently render at whatever the fallback shape happened to be.
        Assert.Equal("1536x1024", reread.FarAspect);
    }

    // ---------------------------------------------------------------- findings

    [Fact]
    public void Findings_OneBlockingItemIsBlocking_HoweverGoodTheRestAre()
    {
        var findings = CurationFindings.For(
            new CurationFinding("identity", CurationFindingSeverity.Info, "identity matches", 0.97),
            new CurationFinding("direction", CurationFindingSeverity.Info, "faces the right way", 0.9),
            new CurationFinding("near-duplicate", CurationFindingSeverity.Blocking, "same image as cell 7", 0.99));

        Assert.True(findings.IsBlocking);
    }

    [Fact]
    public void Findings_NoMetricAtAll_IsNotTreatedAsAPass()
    {
        var findings = CurationFindings.NotScorable("pose-adherence", "too few joints were detected to measure the pose");

        Assert.False(findings.IsBlocking);
        Assert.True(findings.IsUnmeasurable);
        Assert.False(findings.IsPassing);
    }

    [Fact]
    public void Findings_MeasuredAndClean_IsAPass()
    {
        var findings = CurationFindings.For(
            new CurationFinding("identity", CurationFindingSeverity.Info, "identity matches", 0.97),
            new CurationFinding("sharpness", CurationFindingSeverity.Info, "sharp enough", 410));

        Assert.True(findings.IsPassing);
        Assert.False(findings.IsBlocking);
        Assert.False(findings.IsUnmeasurable);
    }

    /// <summary>No findings at all is not a pass — nothing was measured, so nothing was cleared.</summary>
    [Fact]
    public void Findings_NothingRecorded_IsNotAPass()
    {
        Assert.False(new CurationFindings().IsPassing);
    }

    [Fact]
    public void Findings_RoundTripThroughJson()
    {
        var findings = CurationFindings.For(
            new CurationFinding("near-duplicate", CurationFindingSeverity.Blocking, "same image as cell 7", 0.99));

        var reread = CurationFindings.FromJson(findings.ToJson());

        var item = Assert.Single(reread.Items);
        Assert.Equal("near-duplicate", item.Code);
        Assert.Equal(CurationFindingSeverity.Blocking, item.Severity);
        Assert.Equal(0.99, item.Metric);
        Assert.True(reread.IsBlocking);
    }

    [Fact]
    public void Findings_EmptyJson_IsAnEmptySetRatherThanAnError()
    {
        Assert.Empty(CurationFindings.FromJson("").Items);
    }

    // ---------------------------------------------------------------- fixtures

    private static CoveragePlan BuildPlan(int recordCount = 2, bool distinctEverything = false)
    {
        var plan = new CoveragePlan
        {
            CharacterProfileId = "character-1",
            IdentityPackId = "pack-1",
            IdentityPackVersion = 9,
            TriggerToken = "ohwx-becky",
            TargetModelFamily = "biglust",
            SeedRangeStart = 41000,
            GeneratedUtc = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc),
            Vocabulary = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lora.vocabulary.expression.neutral"] = "a front-facing portrait",
                ["lora.vocabulary.expression.smiling"] = "smiling",
                ["lora.vocabulary.lighting.indoor-dim"] = "dim indoor light",
                ["lora.vocabulary.background.plain-wall"] = "a plain wall",
                ["lora.vocabulary.outfit.casual"] = "casual clothes",
                ["lora.vocabulary.outfit.unclothed"] = "no clothing"
            }
        };

        for (var index = 0; index < recordCount; index++)
        {
            var record = BuildRecord();
            record.Key = index == 0 ? "core.front.cu.1" : $"cell.{index}";
            record.Seed = 41000 + index;
            record.Role = index == 0 ? LoraCoverageCellRole.Core : LoraCoverageCellRole.Variation;
            record.Split = index == 0 ? CharacterLoraDatasetSplit.Train : CharacterLoraDatasetSplit.Validation;

            if (distinctEverything)
            {
                // Four core-ish cells that between them cover every angle family, both wardrobe states,
                // four backgrounds, four lighting setups, four expressions and four outfits.
                record.AngleFamily = index switch
                {
                    0 => LoraCoverageAngleFamily.Front,
                    1 => LoraCoverageAngleFamily.ThreeQuarter,
                    2 => LoraCoverageAngleFamily.Profile,
                    _ => LoraCoverageAngleFamily.Behind
                };
                record.AngleYawDeg = record.AngleFamily == LoraCoverageAngleFamily.Behind ? 180 : 0;
                record.FaceVisible = true;
                record.FaceCanonicalSlot = SceneImageReferenceFaceView.ProfileLeft;
                record.BodyCanonicalSlot = record.AngleFamily == LoraCoverageAngleFamily.Behind
                    ? SceneImageReferenceBodyView.Back
                    : SceneImageReferenceBodyView.Front;
                record.WardrobeState = index % 2 == 0
                    ? LoraCoverageWardrobeState.Clothed
                    : LoraCoverageWardrobeState.Unclothed;
                record.ExpressionKey = index % 2 == 0
                    ? "lora.vocabulary.expression.neutral"
                    : "lora.vocabulary.expression.smiling";
                record.BackgroundKey = "lora.vocabulary.background.plain-wall";
                record.OutfitKey = index % 2 == 0 ? "lora.vocabulary.outfit.casual" : "lora.vocabulary.outfit.unclothed";
                // Body framings only: these cells exist to cover the AXES, and a stance can only be claimed where the
                // framing shows one - which is the rule the plan now enforces on itself.
                record.Distance = index % 2 == 0 ? LoraCoverageDistance.HalfBody : LoraCoverageDistance.FullBody;
                var stances = LoraCellWorkflowKeys.StancesFor(record.Distance);
                record.PoseClass = stances[index % stances.Count];
                record.Aspect = "1024x1024";
            }

            plan.Records.Add(record);
        }

        plan.Validate();
        return plan;
    }

    private static CoverageRecord BuildRecord() => new()
    {
        Key = "core.front.cu.1",
        Role = LoraCoverageCellRole.Core,
        AngleFamily = LoraCoverageAngleFamily.Front,
        AngleYawDeg = 0,
        FaceVisible = true,
        FaceCanonicalSlot = SceneImageReferenceFaceView.Front,
        BodyCanonicalSlot = SceneImageReferenceBodyView.Front,
        BodyState = SceneImageReferenceBodyState.Clothed,
        Distance = LoraCoverageDistance.CloseUp,
        WardrobeState = LoraCoverageWardrobeState.Clothed,
        // A close-up claims no stance: its frame cannot tell one apart, so the plan refuses to let it name one.
        PoseClass = null,
        ExpressionKey = "lora.vocabulary.expression.neutral",
        LightingKey = "lora.vocabulary.lighting.indoor-dim",
        BackgroundKey = "lora.vocabulary.background.plain-wall",
        OutfitKey = "lora.vocabulary.outfit.casual",
        Aspect = "1024x1024",
        Seed = 41000,
        Split = CharacterLoraDatasetSplit.Train
    };

    private static CurationPolicy BuildPolicy(int cores, int variations) => new()
    {
        ExpectedCoreCellCount = cores,
        ExpectedVariationCellCount = variations,
        SeedRangeStart = 41000,
        SeedRangeLength = 100,
        CloseUpAspect = "1024x1024",
        PortraitAspect = "832x1216",
        FarAspect = "1536x1024",
        MinimumDistinctOutfits = 1,
        MinimumDistinctBackgrounds = 1,
        MinimumDistinctLighting = 1,
        MinimumDistinctExpressions = 1,
        MinimumPoseClasses = 1,
        WardrobeBalanceTolerancePercent = 10,
        MinimumTrainMembers = 1,
        MinimumValidationMembers = 1,
        NearDuplicateMaxSimilarity = 0.96,
        BodyInvariantDriftTolerancePercent = 12,
        PoseAdherenceMaxJointErrorPercent = 8
    };
}
