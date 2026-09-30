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
    Mirror = 4,

    /// <summary>
    /// Confines the instruction to a REGION of the source (CASE-21): everything outside it is pinned to the source
    /// instead of being regenerated. The mask is built from the operator's rectangle at run time, so the geometry lives
    /// in persisted settings rather than only inside a browser canvas - which is also what lets a proof reproduce a
    /// region an operator drew.
    /// </summary>
    MaskedRegion = 5
}

/// <summary>
/// The parameters of one masked-region edit. The rectangle is PERCENT of the frame, the same units the crop mode and
/// the region proof use, so a region drawn in the UI, a region stored on a run, and a region measured in a case file
/// are the same numbers.
/// </summary>
/// <param name="GrowMaskBy">Pixels to grow the mask by at encode time. The host's own node accepts 0-64, and a seam
/// shows at a bare rectangle edge, so this is the operator's value rather than one invented here.</param>
/// <param name="FeatherPixels">Pixels of softening at the region edge; 0 emits no feather node at all.</param>
public sealed record MediaEditRegionOperation(
    double LeftPercent,
    double TopPercent,
    double WidthPercent,
    double HeightPercent,
    int GrowMaskBy,
    int FeatherPixels)
{
    /// <summary>
    /// Rejects geometry the graph cannot honour, naming why. Nothing is CLAMPED on the way: a rectangle that runs off
    /// the frame, or a grow value the host's node refuses, is a mistake to report rather than a value to quietly fix -
    /// a silently clamped region edits a different area than the one the operator drew, and the result looks fine.
    /// </summary>
    public void Validate()
    {
        if (LeftPercent < 0 || TopPercent < 0)
            throw new InvalidOperationException(
                $"A region needs non-negative edges, but got left {LeftPercent}% and top {TopPercent}%.");
        if (WidthPercent <= 0 || HeightPercent <= 0)
            throw new InvalidOperationException(
                $"A region needs a positive size, but got {WidthPercent}% x {HeightPercent}%. Draw a region, or edit the whole frame.");
        if (LeftPercent + WidthPercent > 100 || TopPercent + HeightPercent > 100)
            throw new InvalidOperationException(
                $"A region must lie inside the frame, but left {LeftPercent}%+{WidthPercent}% and top {TopPercent}%+{HeightPercent}% run past its edge.");
        if (GrowMaskBy is < 0 or > 64)
            throw new InvalidOperationException(
                $"A region 'grow' value must be between 0 and 64 pixels (the host node's own bound), but was {GrowMaskBy}.");
        if (FeatherPixels < 0)
            throw new InvalidOperationException($"A region feather must not be negative, but was {FeatherPixels} pixels.");
    }

    /// <summary>The provenance recorded with the produced image - an operation record, not a compiler revision.</summary>
    public string Describe()
        => $"region rect={LeftPercent},{TopPercent} {WidthPercent}x{HeightPercent}% grow={GrowMaskBy} feather={FeatherPixels}";
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
