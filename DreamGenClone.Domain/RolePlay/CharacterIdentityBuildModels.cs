namespace DreamGenClone.Domain.RolePlay;

/// <summary>The fixed, ordered steps of a character identity build (B-121 FR21-002).</summary>
public enum CharacterIdentityBuildStep
{
    Front = 1,
    Validate = 2,
    GarmentRemoval = 3,
    Crop = 4,
    Enhance = 5,
    Angles = 6,
    Promote = 7
}

public enum CharacterIdentityBuildStatus
{
    InProgress = 1,
    Complete = 2,
    Failed = 3
}

public enum CharacterIdentityBuildStepStatus
{
    NotStarted = 1,
    Running = 2,
    Complete = 3,
    Failed = 4,
    Skipped = 5
}

public enum CharacterIdentityAngleView
{
    ThreeQuarterLeft = 1,
    ThreeQuarterRight = 2,
    ProfileLeft = 3,
    ProfileRight = 4
}

public enum CharacterIdentityAngleStatus
{
    NotStarted = 1,
    Pending = 2,
    Complete = 3,
    Accepted = 4,
    Failed = 5,
    Blocked = 6
}

public sealed class CharacterIdentityAngleRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BuildId { get; set; } = string.Empty;
    public CharacterIdentityAngleView View { get; set; }
    public CharacterIdentityAngleStatus Status { get; set; } = CharacterIdentityAngleStatus.NotStarted;
    public string? InputArtifactId { get; set; }
    public string? OutputArtifactId { get; set; }
    public string? AcceptedAttemptId { get; set; }
    public string? ResolvedPromptText { get; set; }
    public string? ResolvedModelId { get; set; }
    public string? FailureReason { get; set; }
    public bool MirrorDerived { get; set; }
    public bool ManualOverrideApplied { get; set; }
    public string? ManualOverrideReason { get; set; }
    public string? ManualOverrideAuthor { get; set; }
    public DateTime? ManualOverrideUtc { get; set; }
    public bool ManualConfirmationRequired { get; set; }
    public bool ManualConfirmed { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One immutable render attempt for an angle. The view record points at the accepted attempt.</summary>
public sealed class CharacterIdentityAngleAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AngleId { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public string InputArtifactId { get; set; } = string.Empty;
    public string OutputArtifactId { get; set; } = string.Empty;
    public string? PromptText { get; set; }
    public string? ResolvedModelId { get; set; }
    public CharacterIdentityAngleStatus Status { get; set; } = CharacterIdentityAngleStatus.Pending;
    public bool MirrorDerived { get; set; }
    public bool ManualOverrideApplied { get; set; }
    public string? ManualOverrideReason { get; set; }
    public string? ManualOverrideAuthor { get; set; }
    public DateTime? ManualOverrideUtc { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>The outcome of the Validate step's eye-level gate.</summary>
public enum CharacterIdentityValidationVerdict
{
    /// <summary>No measurement has been recorded yet.</summary>
    NotRun = 0,

    /// <summary>Measured and within the configured threshold.</summary>
    Pass = 1,

    /// <summary>Measured and outside the configured threshold.</summary>
    Fail = 2,

    /// <summary>The tool returned no face mesh (expected for a full profile).</summary>
    NoFaceMesh = 3
}

/// <summary>The fixed pipeline order.</summary>
public static class CharacterIdentityBuildSteps
{
    public static readonly CharacterIdentityBuildStep[] Ordered =
    [
        CharacterIdentityBuildStep.Front,
        CharacterIdentityBuildStep.Validate,
        CharacterIdentityBuildStep.GarmentRemoval,
        CharacterIdentityBuildStep.Crop,
        CharacterIdentityBuildStep.Enhance,
        CharacterIdentityBuildStep.Angles,
        CharacterIdentityBuildStep.Promote
    ];
}

/// <summary>
/// A persisted, resumable character identity build: the target character, the producing batch, the
/// current step, and overall status. Per-step state lives in <see cref="CharacterIdentityBuildStepRecord"/>.
/// </summary>
public sealed class CharacterIdentityBuild
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string CharacterProfileId { get; set; } = string.Empty;

    public string? BatchId { get; set; }

    /// <summary>The asset-library container that holds the front attempts (B-121 Phase C).</summary>
    public string? FrontContainerAssetId { get; set; }

    /// <summary>
    /// The image the user approved in Panel B as the canonical front (the de-clothed / cropped /
    /// enhanced result). It is deliberately separate from the Front step's output, which records the
    /// generated candidate that was chosen: the pipeline advances past Front long before an edit result
    /// exists, and the later steps work from this image.
    /// </summary>
    public string? CanonicalFrontAssetId { get; set; }

    public string? ProducedIdentityPackId { get; set; }

    public CharacterIdentityBuildStep CurrentStep { get; set; } = CharacterIdentityBuildStep.Front;

    public CharacterIdentityBuildStatus Status { get; set; } = CharacterIdentityBuildStatus.InProgress;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One step of a build: its status, input/output artifacts, the resolved prompt and model actually
/// used, and manual-override / mirror-derivation markers. No step overwrites its input.
/// </summary>
public sealed class CharacterIdentityBuildStepRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string BuildId { get; set; } = string.Empty;

    public CharacterIdentityBuildStep Step { get; set; }

    public CharacterIdentityBuildStepStatus Status { get; set; } = CharacterIdentityBuildStepStatus.NotStarted;

    public string? InputArtifactId { get; set; }

    public string? OutputArtifactId { get; set; }

    public string? ResolvedPromptText { get; set; }

    public string? ResolvedModelId { get; set; }

    public string? FailureReason { get; set; }

    public bool MirrorDerived { get; set; }

    public bool ManualOverrideApplied { get; set; }

    public string? ManualOverrideReason { get; set; }

    /// <summary>Author recorded with a manual override (required when one is applied).</summary>
    public string? ManualOverrideAuthor { get; set; }

    /// <summary>When the manual override was recorded.</summary>
    public DateTime? ManualOverrideUtc { get; set; }

    /// <summary>Validate-step evidence: the measured values serialized as JSON.</summary>
    public string? MeasurementJson { get; set; }

    /// <summary>Raw stdout of the validation tool, persisted verbatim as evidence.</summary>
    public string? RawToolOutput { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>The eye-level measurement the validation tool reported for one image.</summary>
public sealed class CharacterIdentityEyeMeasurement
{
    /// <summary>Signed iris-level offset as a percentage of interocular distance.</summary>
    public double? IrisDyPercent { get; set; }

    /// <summary>Signed eye-corner-midpoint offset as a percentage of interocular distance.</summary>
    public double? EyeDyPercent { get; set; }

    public double? InterocularPixels { get; set; }

    /// <summary>
    /// The head extent the same tool run reported. Used by the head-aware crop, which needs a measured
    /// head rather than the surrounding slack. Null when the tool found no face mesh.
    /// </summary>
    public CharacterIdentityHeadMeasurement? Head { get; set; }

    /// <summary>Tool-reported error, e.g. "no face mesh"; null when a measurement was produced.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Pixel box of the detected face (min/max over the tool's face landmarks). It covers the face and not
/// the hair, which is why head framing adds its own margin.
/// </summary>
public sealed record CharacterIdentityFaceBox(int X, int Y, int Width, int Height);

/// <summary>
/// Hairline-to-chin head extent in pixels, from the approved MediaPipe tool (landmark 10 hairline,
/// 152 chin), plus the face box when the tool reported one. FaceMesh stops at the hairline, so the extent
/// is slightly under a true skull height.
/// </summary>
public sealed record CharacterIdentityHeadMeasurement(
    int ForeheadTopY, int ChinY, CharacterIdentityFaceBox? Face = null)
{
    /// <summary>Hairline to chin.</summary>
    public int HeadHeightPx => ChinY - ForeheadTopY;
}

/// <summary>
/// The Validate step's gate state: the persisted measurement, its verdict against the configured
/// threshold, the manual override (if recorded), and whether the pipeline may advance.
/// </summary>
public sealed class CharacterIdentityValidationResult
{
    public string BuildId { get; set; } = string.Empty;

    public string? FrontArtifactId { get; set; }

    public CharacterIdentityValidationVerdict Verdict { get; set; } = CharacterIdentityValidationVerdict.NotRun;

    public CharacterIdentityEyeMeasurement? Measurement { get; set; }

    /// <summary>The configured gate: <c>abs(IrisDyPercent) &lt;= ThresholdPercent</c>.</summary>
    public double ThresholdPercent { get; set; }

    public bool ManualOverrideApplied { get; set; }

    public string? ManualOverrideReason { get; set; }

    public string? ManualOverrideAuthor { get; set; }

    public DateTime? ManualOverrideUtc { get; set; }

    /// <summary>True when the Validate step may be completed and the pipeline may advance.</summary>
    public bool CanAdvance { get; set; }

    /// <summary>Why advancement is blocked; null when it is allowed.</summary>
    public string? BlockReason { get; set; }
}
