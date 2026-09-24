namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// What one run of the shared media edit pipeline actually does. <see cref="Edit"/> is the editor-model
/// call (prompt + references). <see cref="Crop"/> is a deterministic pixel operation: it never resolves
/// a model and never calls the image client.
///
/// Explicit and fail-fast on purpose: <see cref="Unknown"/> is the zero value, so a payload that never
/// named its operation cannot silently be treated as an edit.
/// </summary>
public enum MediaEditOperationKind
{
    Unknown = 0,

    /// <summary>Source-image edit by the configured/selected editor model.</summary>
    Edit = 1,

    /// <summary>Deterministic crop of the source image. No model is involved.</summary>
    Crop = 2,

    /// <summary>
    /// ComfyUI upscale with a configured upscale model, then a Lanczos scale down to the target edge. This
    /// one DOES reach a model (the local ComfyUI endpoint), unlike crop.
    /// </summary>
    Enhance = 3,

    /// <summary>
    /// Deterministic horizontal mirror of the source image. No model, no parameters: the identity angle
    /// remedy (a render whose head points the wrong way) is exactly this operation. It runs through
    /// <c>IImageMirrorEngine</c> so the pixel work has ONE implementation, and it is recorded with mirror
    /// provenance like every other operation.
    /// </summary>
    Mirror = 4
}

/// <summary>
/// The parameters of one crop run. Every placement value is explicit — nothing here invents a default,
/// because a silent default would move the frame without anyone asking for it.
/// </summary>
public sealed record MediaEditCropOperation(
    ImageCropMode Mode,
    ImageCropSettings Settings,
    ImageCropHeadMeasurement? Measurement,
    ImageCropRect? Rect = null)
{
    /// <summary>
    /// Rejects the combinations the executor would otherwise have to guess at. The modes are explicit
    /// alternatives: framing mixes nothing in, head-aware needs a measured head height, head-framed needs
    /// the measured face box, and manual needs the dragged window. Mixing them throws rather than quietly
    /// picking one.
    /// </summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Settings);
        Settings.Validate();

        RequireNoRectForDerivedModes();

        switch (Mode)
        {
            case ImageCropMode.Framing:
                RequireNoMeasurement("A framing crop");
                break;

            case ImageCropMode.HeadAware:
                RequireMeasurement("A head-aware crop");
                if (Measurement!.HeadHeightPx <= 0)
                    throw new InvalidOperationException(HeadHeightError());
                break;

            case ImageCropMode.HeadFramed:
                RequireMeasurement("A head-framed crop");
                if (Measurement!.Face is null)
                {
                    throw new InvalidOperationException(
                        "A head-framed crop needs the measured face box, but the measurement carried none. " +
                        "Re-measure the image with tools/eye-validation/measure_iris.py.");
                }

                if (Measurement.HeadHeightPx <= 0)
                    throw new InvalidOperationException(HeadHeightError());
                break;

            case ImageCropMode.Manual:
                if (Rect is not { } window)
                    throw new InvalidOperationException("A manual crop requires the window that was dragged.");
                if (window.Width <= 0 || window.Height <= 0)
                    throw new InvalidOperationException(
                        $"A manual crop needs a positive window size, but got {window.Width}x{window.Height}.");
                if (Measurement is not null)
                    throw new InvalidOperationException("A manual crop must not carry a head measurement.");
                break;

            default:
                throw new InvalidOperationException($"Unsupported crop mode '{Mode}'.");
        }
    }

    /// <summary>The provenance recorded with the produced image — an operation record, not a compiler revision.</summary>
    public string Describe()
        => Mode == ImageCropMode.Manual && Rect is { } window
            ? $"crop mode={Mode} window={window.X},{window.Y} {window.Width}x{window.Height}"
            : $"crop mode={Mode} aspect={Settings.TargetAspect} headroom%={Settings.HeadroomPercent} " +
              $"offset%={Settings.HorizontalOffsetPercent}";

    private void RequireNoMeasurement(string label)
    {
        if (Measurement is not null)
            throw new InvalidOperationException($"{label} must not carry a head measurement; select the head mode explicitly.");
    }

    private void RequireNoRectForDerivedModes()
    {
        if (Mode != ImageCropMode.Manual && Rect is not null)
            throw new InvalidOperationException(
                $"A {Mode} crop derives its own window and must not carry one; select the manual mode explicitly.");
    }

    private void RequireMeasurement(string label)
    {
        if (Measurement is null)
            throw new InvalidOperationException(
                $"{label} requires a measured face, but none was supplied. Measure the image with " +
                "tools/eye-validation/measure_iris.py, or select the framing mode explicitly.");
    }

    private string HeadHeightError()
        => "A head crop needs a positive head height, but the measurement gave " +
           $"{Measurement!.HeadHeightPx}px (forehead {Measurement.ForeheadTopY}, chin {Measurement.ChinY}).";
}

/// <summary>
/// The parameters of one enhance run. Both values are persisted configuration carried onto the run, so the
/// record says exactly which upscale model and target edge produced the image.
/// </summary>
public sealed record MediaEditEnhanceOperation(string UpscalerModelName, int TargetLongEdge)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(UpscalerModelName))
        {
            throw new InvalidOperationException(
                "Missing required configuration 'UpscalerModelName': the ComfyUI upscale model to use must be "
                + "configured before an image can be enhanced.");
        }

        if (TargetLongEdge <= 0)
        {
            throw new InvalidOperationException(
                $"EnhanceTargetLongEdge must be positive, but was {TargetLongEdge}.");
        }
    }

    public string Describe() => $"enhance upscaler={UpscalerModelName} targetLongEdge={TargetLongEdge}";
}

/// <summary>
/// The operation half of a run: which one, plus its parameters. The edit operation carries no payload
/// because a compiled prompt revision is its payload and that is validated and passed separately.
/// </summary>
public sealed record MediaEditOperation(
    MediaEditOperationKind Kind,
    MediaEditCropOperation? Crop,
    MediaEditEnhanceOperation? Enhance = null)
{
    /// <summary>The editor-model operation.</summary>
    public static MediaEditOperation ForEdit { get; } = new(MediaEditOperationKind.Edit, null);

    /// <summary>The deterministic crop operation.</summary>
    public static MediaEditOperation ForCrop(MediaEditCropOperation crop)
        => new(MediaEditOperationKind.Crop, crop ?? throw new ArgumentNullException(nameof(crop)));

    /// <summary>The upscale-enhance operation.</summary>
    public static MediaEditOperation ForEnhance(MediaEditEnhanceOperation enhance)
        => new(MediaEditOperationKind.Enhance, null, enhance ?? throw new ArgumentNullException(nameof(enhance)));

    /// <summary>The deterministic horizontal-mirror operation (no parameters).</summary>
    public static MediaEditOperation ForMirror { get; } = new(MediaEditOperationKind.Mirror, null);

    /// <summary>Fails fast when the operation is unnamed or when its parameters do not match its kind.</summary>
    public void Validate()
    {
        if (Kind != MediaEditOperationKind.Crop && Crop is not null)
            throw new InvalidOperationException($"A {Kind} operation must not carry crop parameters.");
        if (Kind != MediaEditOperationKind.Enhance && Enhance is not null)
            throw new InvalidOperationException($"A {Kind} operation must not carry enhance parameters.");

        switch (Kind)
        {
            case MediaEditOperationKind.Edit:
                break;

            case MediaEditOperationKind.Crop:
                if (Crop is null)
                    throw new InvalidOperationException("A crop operation requires its crop parameters.");
                Crop.Validate();
                break;

            case MediaEditOperationKind.Enhance:
                if (Enhance is null)
                    throw new InvalidOperationException("An enhance operation requires its enhance parameters.");
                Enhance.Validate();
                break;

            default:
                throw new InvalidOperationException(
                    $"A media edit run requires an explicit operation kind, but got '{Kind}'.");
        }
    }

    public string Describe() => Kind switch
    {
        MediaEditOperationKind.Crop => Crop!.Describe(),
        MediaEditOperationKind.Enhance => Enhance!.Describe(),
        _ => "edit"
    };
}
