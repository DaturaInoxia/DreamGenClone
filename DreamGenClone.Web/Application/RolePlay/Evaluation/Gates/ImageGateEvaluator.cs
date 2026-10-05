using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.ModelManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Web.Application.RolePlay.Evaluation.Gates;

/// <summary>
/// Runs the native gates over a freshly rendered image (B-135 P3). Sanitisation always runs; pose agreement runs only
/// when a candidate pose body is supplied (a Playground pose-library render). Every gate is a measurement — the
/// evaluator records a verdict for each and NEVER throws or blocks the render that produced the image.
/// </summary>
public interface IImageGateEvaluator
{
    /// <summary>
    /// Computes the gate verdicts for one rendered image. <paramref name="candidateBody"/> is the pose the render was
    /// asked for (null for a render with no pose, which skips the pose gate). <paramref name="model"/> supplies the
    /// ComfyUI host the pose estimator runs on.
    /// </summary>
    Task<IReadOnlyList<ImageGateResult>> EvaluateAsync(
        byte[] renderedImage,
        IReadOnlyList<PoseKeypoint>? candidateBody,
        ResolvedImageModel model,
        CancellationToken cancellationToken = default);
}

public sealed class ImageGateEvaluator : IImageGateEvaluator
{
    /// <summary>
    /// The pose agreement bar, as the canonical tool states it: all five proven routes pass a 6 % mean joint-error
    /// bar. Declared here by the caller rather than inside the gate, so the gate itself never decides the bar.
    /// </summary>
    public const double PoseMeanErrorBarPercent = 6.0;

    private readonly IPoseKeypointExtractor _extractor;

    public ImageGateEvaluator(IPoseKeypointExtractor extractor)
    {
        _extractor = extractor;
    }

    public async Task<IReadOnlyList<ImageGateResult>> EvaluateAsync(
        byte[] renderedImage,
        IReadOnlyList<PoseKeypoint>? candidateBody,
        ResolvedImageModel model,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ImageGateResult>
        {
            EvaluateSanitisation(renderedImage)
        };

        if (candidateBody is not null)
        {
            results.Add(await EvaluatePoseAsync(renderedImage, candidateBody, model, cancellationToken));
        }

        return results;
    }

    private static ImageGateResult EvaluateSanitisation(byte[] renderedImage)
    {
        try
        {
            using var image = Image.Load<Rgb24>(renderedImage);
            var result = SanitisationGate.Measure(image);
            return new ImageGateResult(
                "sanitisation",
                !result.SuspectedSanitised,
                $"skin fraction {result.SkinFraction:P2} ({result.SkinPixels}/{result.TotalPixels} px)");
        }
        catch (Exception exception)
        {
            // A gate that cannot read the image records its own failure; it never fails the render.
            return new ImageGateResult("sanitisation", false, $"gate error: {exception.Message}");
        }
    }

    private async Task<ImageGateResult> EvaluatePoseAsync(
        byte[] renderedImage,
        IReadOnlyList<PoseKeypoint> candidateBody,
        ResolvedImageModel model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.ComfyUiUrl))
        {
            return new ImageGateResult(
                "pose-agreement",
                false,
                "no ComfyUI host for the pose estimator, so the render could not be read back");
        }

        try
        {
            var extraction = await _extractor.ExtractAsync(
                new PoseKeypointExtractionRequest(
                    renderedImage,
                    model.ProviderName,
                    model.ComfyUiUrl,
                    model.ProviderTimeoutSeconds,
                    KeepFace: false),
                cancellationToken);

            var readback = OpenPosePoseJson.Parse(extraction.PersonJson, "render readback");
            var result = PoseAgreementGate.Measure(candidateBody, readback.Body, PoseMeanErrorBarPercent);

            return new ImageGateResult(
                "pose-agreement",
                result.Pass,
                $"mean joint error {result.MeanErrorPctOfHeight:F2}% of height "
                + $"(bar {PoseMeanErrorBarPercent:F0}%, {result.JointsCompared} joints)");
        }
        catch (Exception exception)
        {
            return new ImageGateResult("pose-agreement", false, $"gate error: {exception.Message}");
        }
    }
}
