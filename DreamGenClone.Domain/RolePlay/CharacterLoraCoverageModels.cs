using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// The camera family a coverage cell is shot from. Deliberately coarse: it is the axis the coverage
/// matrix counts in and the axis a prompt template is keyed by. The exact facing is
/// <see cref="CoverageRecord.AngleYawDeg"/> plus the canonical reference slots, so this enum never
/// needs to grow a value per angle (the B-124 reference model carries the variety as data).
/// </summary>
public enum LoraCoverageAngleFamily
{
    Front = 1,
    ThreeQuarter = 2,
    Profile = 3,

    /// <summary>Over-shoulder / behind. No canonical face slot exists for it, which is the point.</summary>
    Behind = 4
}

/// <summary>How much of the subject fills the frame. Decides the native render aspect.</summary>
public enum LoraCoverageDistance
{
    /// <summary>Face and shoulders.</summary>
    CloseUp = 1,

    /// <summary>Waist up.</summary>
    HalfBody = 2,

    FullBody = 3
}

/// <summary>
/// Whether the body is dressed in the cell. A training set needs both states of the SAME body, and the
/// state must be explicit data — never inferred from a prompt or a filename.
/// </summary>
public enum LoraCoverageWardrobeState
{
    Clothed = 1,
    Unclothed = 2
}

/// <summary>
/// The rough body attitude a cell is shot in. It exists so a 36-cell set is not 30 standing frames; the
/// concrete pose comes from a pose frame attached in the workspace (a separate session's plumbing).
/// </summary>
public enum LoraCoveragePoseClass
{
    Standing = 1,
    Sitting = 2,
    Kneeling = 3,
    Lying = 4,
    AllFours = 5,
    HandsRaised = 6
}

/// <summary>Why a cell is in the plan: the core matrix, or the extra variation cells.</summary>
public enum LoraCoverageCellRole
{
    Core = 1,
    Variation = 2
}

/// <summary>
/// One coverage cell: one image the training set needs, described by the axes that vary, with the fixed
/// seed that makes its render reproducible. Immutable-by-convention data (it is persisted as JSON and
/// snapshotted into the dataset), so every property is set-once.
/// </summary>
public sealed class CoverageRecord
{
    /// <summary>Stable cell key. Unique within a plan; survives re-generation of the plan.</summary>
    public string Key { get; set; } = string.Empty;

    public LoraCoverageCellRole Role { get; set; }

    public LoraCoverageAngleFamily AngleFamily { get; set; }

    /// <summary>
    /// Facing relative to the camera in degrees: 0 front, negative to the left of frame, positive to the
    /// right, ±90 a full profile, 180 behind. Range-checked in <see cref="Validate"/>.
    /// </summary>
    public int AngleYawDeg { get; set; }

    /// <summary>
    /// Whether the face is in frame at all. It is explicit rather than derived from the angle, because an
    /// over-shoulder cell turns the head away but still shows it, while a view from directly behind does not —
    /// and the difference decides whether a face reference is used.
    /// </summary>
    public bool FaceVisible { get; set; }

    /// <summary>
    /// The face reference this cell's normalize step must use. Null only when <see cref="FaceVisible"/> is false:
    /// a picture with no face has no face view, and naming one is a silent identity error.
    /// </summary>
    public SceneImageReferenceFaceView? FaceCanonicalSlot { get; set; }

    /// <summary>The body reference this cell is conditioned on and normalized against.</summary>
    public SceneImageReferenceBodyView BodyCanonicalSlot { get; set; }

    /// <summary>Which state of the body this cell depicts and therefore which body reference it uses.</summary>
    public SceneImageReferenceBodyState BodyState { get; set; }

    public LoraCoverageDistance Distance { get; set; }

    public LoraCoverageWardrobeState WardrobeState { get; set; }

    public LoraCoveragePoseClass PoseClass { get; set; }

    /// <summary>Vocabulary key for the expression phrase (resolved from the plan's snapshot).</summary>
    public string ExpressionKey { get; set; } = string.Empty;

    /// <summary>Vocabulary key for the lighting phrase.</summary>
    public string LightingKey { get; set; } = string.Empty;

    /// <summary>Vocabulary key for the background phrase.</summary>
    public string BackgroundKey { get; set; } = string.Empty;

    /// <summary>Vocabulary key for the wardrobe/outfit phrase.</summary>
    public string OutfitKey { get; set; } = string.Empty;

    /// <summary>Render size for this cell, for example <c>1024x1024</c>. Taken from the curation policy.</summary>
    public string Aspect { get; set; } = string.Empty;

    /// <summary>
    /// One fixed seed per cell, never reused inside a plan. Seeds buy reproducible variety; they do not
    /// buy identity — identity comes from the reference conditioning and the normalize step.
    /// </summary>
    public int Seed { get; set; }

    public CharacterLoraDatasetSplit Split { get; set; }

    /// <summary>
    /// The reference rule in words, for the workspace to show beside the reference picker. Derived from
    /// the slots above — never a second source of truth, always a rendering of them.
    /// </summary>
    public string DescribeReferenceRule()
        => FaceVisible
            ? $"face {FaceCanonicalSlot} + body {BodyCanonicalSlot} ({BodyState})"
            : $"body {BodyCanonicalSlot} ({BodyState}); no face reference — the face is not visible in this view";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key))
            throw new InvalidOperationException("A coverage cell requires a key.");
        if (!Enum.IsDefined(Role))
            throw new InvalidOperationException($"Coverage cell '{Key}' has an unsupported role.");
        if (!Enum.IsDefined(AngleFamily))
            throw new InvalidOperationException($"Coverage cell '{Key}' has an unsupported angle family.");
        if (!Enum.IsDefined(Distance))
            throw new InvalidOperationException($"Coverage cell '{Key}' has an unsupported distance.");
        if (!Enum.IsDefined(WardrobeState))
            throw new InvalidOperationException($"Coverage cell '{Key}' has an unsupported wardrobe state.");
        if (!Enum.IsDefined(PoseClass))
            throw new InvalidOperationException($"Coverage cell '{Key}' has an unsupported pose class.");
        if (!Enum.IsDefined(Split))
            throw new InvalidOperationException($"Coverage cell '{Key}' has an unsupported split.");
        if (!Enum.IsDefined(BodyCanonicalSlot))
            throw new InvalidOperationException($"Coverage cell '{Key}' has an unsupported body slot.");
        if (!Enum.IsDefined(BodyState))
            throw new InvalidOperationException($"Coverage cell '{Key}' has an unsupported body state.");

        if (AngleYawDeg is < -180 or > 180)
            throw new InvalidOperationException($"Coverage cell '{Key}': angle yaw must be within -180..180 degrees.");

        // A view with no face cannot name a face reference, and a view with a face must name one — otherwise
        // the normalize step has nothing angle-matched to work from and identity drifts silently.
        if (!FaceVisible && FaceCanonicalSlot is not null)
        {
            throw new InvalidOperationException(
                $"Coverage cell '{Key}': the face is not in frame, so it cannot carry a face reference.");
        }

        if (FaceVisible && FaceCanonicalSlot is null)
        {
            throw new InvalidOperationException(
                $"Coverage cell '{Key}': the face is in frame, so it requires the face reference it will be normalized against.");
        }

        if (Seed <= 0)
            throw new InvalidOperationException($"Coverage cell '{Key}': a positive fixed seed is required.");
        if (string.IsNullOrWhiteSpace(Aspect))
            throw new InvalidOperationException($"Coverage cell '{Key}': an explicit render aspect is required.");

        RequireKey(ExpressionKey, nameof(ExpressionKey));
        RequireKey(LightingKey, nameof(LightingKey));
        RequireKey(BackgroundKey, nameof(BackgroundKey));
        RequireKey(OutfitKey, nameof(OutfitKey));

        void RequireKey(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Coverage cell '{Key}': {name} is required (a vocabulary key, never a phrase).");
        }
    }
}

/// <summary>
/// A whole coverage plan: the cells one character's training set is made of, the wording each variable
/// axis resolves to (snapshotted, so the plan is readable and reproducible after the vocabulary is
/// edited), and the provenance needed to say which pack and which policy produced it.
/// </summary>
public sealed class CoveragePlan
{
    /// <summary>Bumped when the shape of this document changes, so an old stored plan is never misread.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string CharacterProfileId { get; set; } = string.Empty;

    public string IdentityPackId { get; set; } = string.Empty;

    public int IdentityPackVersion { get; set; }

    /// <summary>The token the whole identity binds to. First token of every caption; never any other word.</summary>
    public string TriggerToken { get; set; } = string.Empty;

    public string TargetModelFamily { get; set; } = string.Empty;

    /// <summary>The seed range this plan allocated from, recorded so the allocation is auditable.</summary>
    public int SeedRangeStart { get; set; }

    public DateTime GeneratedUtc { get; set; }

    public List<CoverageRecord> Records { get; set; } = [];

    /// <summary>
    /// Vocabulary snapshot: key → phrase, exactly as the phrases read when the plan was generated. The
    /// render prompt and the caption are composed from this, so editing the store later never silently
    /// rewrites a plan that has already been shot.
    /// </summary>
    public Dictionary<string, string> Vocabulary { get; set; } = new(StringComparer.Ordinal);

    public string PhraseFor(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("A vocabulary key is required to resolve a phrase.");
        }

        if (!Vocabulary.TryGetValue(key, out var phrase) || string.IsNullOrWhiteSpace(phrase))
        {
            throw new InvalidOperationException(
                $"The coverage plan has no vocabulary phrase for '{key}'. The plan is incomplete and must be regenerated.");
        }

        return phrase;
    }

    public CoverageRecord? FindRecord(string key)
        => Records.FirstOrDefault(record => string.Equals(record.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Reports every way this plan fails the configured diversity/coverage minima, in the operator's
    /// words. An empty list means the plan is complete. This is the readiness header's whole content,
    /// and freeze refuses while anything is listed.
    /// </summary>
    public IReadOnlyList<string> DescribeGaps(CurationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var gaps = new List<string>();

        var coreCount = Records.Count(record => record.Role == LoraCoverageCellRole.Core);
        if (coreCount != policy.ExpectedCoreCellCount)
        {
            gaps.Add($"core cells {coreCount}/{policy.ExpectedCoreCellCount}");
        }

        var variationCount = Records.Count(record => record.Role == LoraCoverageCellRole.Variation);
        if (variationCount != policy.ExpectedVariationCellCount)
        {
            gaps.Add($"variation cells {variationCount}/{policy.ExpectedVariationCellCount}");
        }

        foreach (var family in Enum.GetValues<LoraCoverageAngleFamily>())
        {
            if (!Records.Any(record => record.AngleFamily == family))
            {
                gaps.Add($"no {family} cells");
            }
        }

        CountBelow("outfits", record => record.OutfitKey, policy.MinimumDistinctOutfits);
        CountBelow("backgrounds", record => record.BackgroundKey, policy.MinimumDistinctBackgrounds);
        CountBelow("lighting setups", record => record.LightingKey, policy.MinimumDistinctLighting);
        CountBelow("expressions", record => record.ExpressionKey, policy.MinimumDistinctExpressions);
        CountBelow("pose classes", record => record.PoseClass.ToString(), policy.MinimumPoseClasses);

        var train = Records.Count(record => record.Split == CharacterLoraDatasetSplit.Train);
        if (train < policy.MinimumTrainMembers)
        {
            gaps.Add($"train split {train}/{policy.MinimumTrainMembers}");
        }

        var validation = Records.Count(record => record.Split == CharacterLoraDatasetSplit.Validation);
        if (validation < policy.MinimumValidationMembers)
        {
            gaps.Add($"validation split {validation}/{policy.MinimumValidationMembers}");
        }

        var unclothed = Records.Count(record => record.WardrobeState == LoraCoverageWardrobeState.Unclothed);
        if (Records.Count > 0)
        {
            var unclothedPercent = (int)Math.Round(unclothed * 100.0 / Records.Count);
            var drift = Math.Abs(unclothedPercent - 50);
            if (drift > policy.WardrobeBalanceTolerancePercent)
            {
                gaps.Add($"wardrobe balance {unclothedPercent}% unclothed (want 50% ±{policy.WardrobeBalanceTolerancePercent}%)");
            }
        }

        if (!Records.Any(record => record.FaceVisible))
        {
            gaps.Add("no cell shows the face");
        }

        return gaps;

        void CountBelow(string what, Func<CoverageRecord, string> select, int minimum)
        {
            var distinct = Records.Select(select).Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).Count();
            if (distinct < minimum)
            {
                gaps.Add($"{what} {distinct}/{minimum}");
            }
        }
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported coverage plan schema version {SchemaVersion}; this build reads version {CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(CharacterProfileId))
            throw new InvalidOperationException("A coverage plan requires the character it belongs to.");
        if (string.IsNullOrWhiteSpace(IdentityPackId))
            throw new InvalidOperationException("A coverage plan requires the identity pack id it was projected from.");
        if (IdentityPackVersion <= 0)
            throw new InvalidOperationException("A coverage plan requires the identity pack version it was projected from.");
        if (string.IsNullOrWhiteSpace(TriggerToken))
            throw new InvalidOperationException("A coverage plan requires a trigger token.");
        if (SeedRangeStart <= 0)
            throw new InvalidOperationException("A coverage plan requires the seed range it allocated from.");
        if (Records.Count == 0)
            throw new InvalidOperationException("A coverage plan requires at least one coverage cell.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seeds = new HashSet<int>();
        foreach (var record in Records)
        {
            record.Validate();
            if (!seen.Add(record.Key))
            {
                throw new InvalidOperationException($"Duplicate coverage cell key '{record.Key}'.");
            }

            if (!seeds.Add(record.Seed))
            {
                throw new InvalidOperationException(
                    $"Seed {record.Seed} is used by more than one cell; a seed is allocated once per cell and never reused.");
            }
        }

        if (!Vocabulary.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            throw new InvalidOperationException("A coverage plan requires a resolved vocabulary snapshot.");
        }
    }

    public string ToJson() { Validate(); return JsonSerializer.Serialize(this, JsonOptions); }

    public static CoveragePlan FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The coverage plan is empty.");
        }

        var plan = JsonSerializer.Deserialize<CoveragePlan>(json, JsonOptions)
            ?? throw new InvalidOperationException("The coverage plan is invalid.");
        plan.Validate();
        return plan;
    }
}

/// <summary>
/// Every threshold the coverage plan and the gates are held to. Every member is <c>required</c>: there is
/// no code default for any of them, so a policy row that is missing a value fails to deserialize by name
/// instead of quietly applying a number nobody chose. The seeded global row is migration data, never a
/// runtime fallback.
/// </summary>
public sealed class CurationPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>How many core matrix cells a complete plan has.</summary>
    public required int ExpectedCoreCellCount { get; set; }

    /// <summary>How many variation cells a complete plan has.</summary>
    public required int ExpectedVariationCellCount { get; set; }

    /// <summary>First seed the generator may allocate.</summary>
    public required int SeedRangeStart { get; set; }

    /// <summary>How many seeds the generator may allocate (one per cell, never reused).</summary>
    public required int SeedRangeLength { get; set; }

    /// <summary>Render size for close-up cells.</summary>
    public required string CloseUpAspect { get; set; }

    /// <summary>Render size for half-body and full-body cells.</summary>
    public required string PortraitAspect { get; set; }

    public required int MinimumDistinctOutfits { get; set; }

    public required int MinimumDistinctBackgrounds { get; set; }

    public required int MinimumDistinctLighting { get; set; }

    public required int MinimumDistinctExpressions { get; set; }

    public required int MinimumPoseClasses { get; set; }

    /// <summary>How far the unclothed share may sit from 50% before the plan is called unbalanced.</summary>
    public required int WardrobeBalanceTolerancePercent { get; set; }

    public required int MinimumTrainMembers { get; set; }

    public required int MinimumValidationMembers { get; set; }

    /// <summary>
    /// Two accepted images whose measured similarity reaches this are treated as the same image. This is
    /// the "identical backgrounds" overfit symptom, caught at the door instead of after a training run.
    /// </summary>
    public required double NearDuplicateMaxSimilarity { get; set; }

    /// <summary>How far a member's measured property may drift from the accepted baseline before it is flagged.</summary>
    public required double BodyInvariantDriftTolerancePercent { get; set; }

    /// <summary>The pose-adherence error a pose cell may not exceed.</summary>
    public required double PoseAdherenceMaxJointErrorPercent { get; set; }

    public void Validate()
    {
        Require(ExpectedCoreCellCount > 0, nameof(ExpectedCoreCellCount), "must be positive");
        Require(ExpectedVariationCellCount > 0, nameof(ExpectedVariationCellCount), "must be positive");
        Require(SeedRangeStart > 0, nameof(SeedRangeStart), "must be positive");
        Require(SeedRangeLength > 0, nameof(SeedRangeLength), "must be positive");
        Require(SeedRangeLength >= ExpectedCoreCellCount + ExpectedVariationCellCount, nameof(SeedRangeLength),
            "must cover every cell, one seed each");
        Require(!string.IsNullOrWhiteSpace(CloseUpAspect), nameof(CloseUpAspect), "is required");
        Require(!string.IsNullOrWhiteSpace(PortraitAspect), nameof(PortraitAspect), "is required");
        Require(MinimumDistinctOutfits > 0, nameof(MinimumDistinctOutfits), "must be positive");
        Require(MinimumDistinctBackgrounds > 0, nameof(MinimumDistinctBackgrounds), "must be positive");
        Require(MinimumDistinctLighting > 0, nameof(MinimumDistinctLighting), "must be positive");
        Require(MinimumDistinctExpressions > 0, nameof(MinimumDistinctExpressions), "must be positive");
        Require(MinimumPoseClasses > 0, nameof(MinimumPoseClasses), "must be positive");
        Require(WardrobeBalanceTolerancePercent is >= 0 and <= 50, nameof(WardrobeBalanceTolerancePercent), "must be within 0..50");
        Require(MinimumTrainMembers > 0, nameof(MinimumTrainMembers), "must be positive");
        Require(MinimumValidationMembers > 0, nameof(MinimumValidationMembers), "must be positive");
        Require(NearDuplicateMaxSimilarity is > 0 and <= 1, nameof(NearDuplicateMaxSimilarity), "must be within (0,1]");
        Require(BodyInvariantDriftTolerancePercent is > 0 and <= 100, nameof(BodyInvariantDriftTolerancePercent), "must be within (0,100]");
        Require(PoseAdherenceMaxJointErrorPercent is > 0 and <= 100, nameof(PoseAdherenceMaxJointErrorPercent), "must be within (0,100]");

        void Require(bool condition, string name, string requirement)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Curation policy value '{name}' {requirement}.");
            }
        }
    }

    public string ToJson() { Validate(); return JsonSerializer.Serialize(this, JsonOptions); }

    public static CurationPolicy FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The curation policy is empty.");
        }

        // A missing member throws JsonException naming it: that is the fail-fast for an incomplete policy.
        var policy = JsonSerializer.Deserialize<CurationPolicy>(json, JsonOptions)
            ?? throw new InvalidOperationException("The curation policy is invalid.");
        policy.Validate();
        return policy;
    }
}

/// <summary>
/// Row identity for the curation policy store. A character row overrides the global row; there is no third
/// source and no in-code default, so a missing global row is a hard error naming the row rather than a
/// silently applied threshold (repo Hard Rule: no fallbacks).
/// </summary>
public static class CharacterLoraCurationPolicyKeys
{
    public const string GlobalId = "global";

    public static string ComputeId(string? characterProfileId)
        => string.IsNullOrWhiteSpace(characterProfileId)
            ? GlobalId
            : $"character:{characterProfileId.Trim()}";
}

public enum CurationFindingSeverity
{
    /// <summary>Measured, and it passed. Worth showing, changes nothing.</summary>
    Info = 1,

    /// <summary>Measured, degraded, but does not block accept.</summary>
    Warning = 2,

    /// <summary>
    /// Could not be measured at all. Its own severity on purpose: "we could not measure this" is neither a
    /// pass nor a failure, and collapsing it into either one is how an unmeasurable image gets waved through.
    /// </summary>
    NotScorable = 3,

    /// <summary>Blocks accept. A manual override must carry a reason and an author.</summary>
    Blocking = 4
}

/// <summary>One measured verdict about one image. Code is stable so tests and the UI can key on it.</summary>
public sealed record CurationFinding(
    string Code,
    CurationFindingSeverity Severity,
    string Message,
    double? Metric = null);

/// <summary>
/// The measured verdicts for one attempt or one member. Persisted alongside the member, so "why was this
/// accepted" is answerable later. One favourable metric is never a pass: <see cref="IsBlocking"/> is
/// what the accept action obeys, and a single <see cref="CurationFindingSeverity.Blocking"/> finding
/// refuses it no matter how good the others look.
/// </summary>
public sealed class CurationFindings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public List<CurationFinding> Items { get; set; } = [];

    public bool IsBlocking => Items.Any(item => item.Severity == CurationFindingSeverity.Blocking);

    /// <summary>True when at least one thing could not be measured. Never silently treated as acceptable.</summary>
    public bool IsUnmeasurable => Items.Any(item => item.Severity == CurationFindingSeverity.NotScorable);

    /// <summary>
    /// A pass means measured and not refused: nothing blocking, and nothing left unmeasurable. This is the
    /// only definition offered, so "no findings" can never be read as "good".
    /// </summary>
    public bool IsPassing => Items.Count > 0 && !IsBlocking && !IsUnmeasurable;

    public static CurationFindings For(params CurationFinding[] findings) => new() { Items = [.. findings] };

    public static CurationFindings NotScorable(string code, string reason)
        => For(new CurationFinding(code, CurationFindingSeverity.NotScorable, reason));

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CurationFindings FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CurationFindings();
        }

        return JsonSerializer.Deserialize<CurationFindings>(json, JsonOptions) ?? new CurationFindings();
    }
}
