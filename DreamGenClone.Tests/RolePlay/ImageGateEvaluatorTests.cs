using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Evaluation;
using DreamGenClone.Web.Application.RolePlay.Evaluation.Gates;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 P3 wiring: the evaluator turns a rendered image plus an optional candidate pose into per-gate verdicts.
/// Pinned behaviours: sanitisation always runs, pose runs only with a candidate, and a gate failure records a verdict
/// instead of throwing — "no gate ever blocks" means the evaluator never propagates an exception out of a render.
/// </summary>
public sealed class ImageGateEvaluatorTests
{
    [Fact]
    public async Task NoCandidatePose_MeasuresSanitisationOnly()
    {
        var evaluator = new ImageGateEvaluator(new ThrowingExtractor());

        var results = await evaluator.EvaluateAsync(PngBytes(), null, Model("http://comfy"));

        var gate = Assert.Single(results);
        Assert.Equal("sanitisation", gate.Name);
    }

    [Fact]
    public async Task CandidatePose_AddsPoseAgreement_AndPassesWhenTheReadbackMatches()
    {
        var body = TBody();
        var personJson = OpenPosePoseJson.Serialize(new PosePerson { Body = body });
        var evaluator = new ImageGateEvaluator(
            new StubExtractor(request => new PoseKeypointExtractionResult(personJson, "dwpose", "sig", "v1")));

        var results = await evaluator.EvaluateAsync(PngBytes(), body, Model("http://comfy"));

        var pose = results.Single(result => result.Name == "pose-agreement");
        Assert.True(pose.Pass);
        Assert.Contains("0.00%", pose.Detail, StringComparison.Ordinal);
        Assert.Contains("sanitisation", results.Select(result => result.Name));
    }

    [Fact]
    public async Task NoComfyHost_PoseGateRecordsAFailure_AndDoesNotThrow()
    {
        var evaluator = new ImageGateEvaluator(new ThrowingExtractor());

        var results = await evaluator.EvaluateAsync(PngBytes(), TBody(), Model(null));

        var pose = results.Single(result => result.Name == "pose-agreement");
        Assert.False(pose.Pass);
        Assert.Contains("no ComfyUI host", pose.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractorThrows_PoseGateRecordsAFailure_AndDoesNotThrow()
    {
        var evaluator = new ImageGateEvaluator(
            new StubExtractor(_ => throw new InvalidOperationException("estimator down")));

        var results = await evaluator.EvaluateAsync(PngBytes(), TBody(), Model("http://comfy"));

        var pose = results.Single(result => result.Name == "pose-agreement");
        Assert.False(pose.Pass);
        Assert.Contains("gate error: estimator down", pose.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableImage_SanitisationRecordsAFailure_AndDoesNotThrow()
    {
        var evaluator = new ImageGateEvaluator(new ThrowingExtractor());

        var results = await evaluator.EvaluateAsync([1, 2, 3], null, Model("http://comfy"));

        var sanitisation = Assert.Single(results);
        Assert.False(sanitisation.Pass);
        Assert.Contains("gate error", sanitisation.Detail, StringComparison.Ordinal);
    }

    private static ResolvedImageModel Model(string? comfyUrl) => new(
        ProviderBaseUrl: "http://localhost:8188",
        ImageGenerationPath: "/v1/images/generations",
        ProviderTimeoutSeconds: 30,
        ApiKeyEncrypted: null,
        ModelIdentifier: "test-checkpoint",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "ComfyUI",
        IsSessionOverride: false,
        SceneImageModelFamily: SceneImageModelFamily.Sdxl,
        PromptDialect: SceneImagePromptDialect.SdxlNaturalLanguage,
        ImageProtocol: ImageProtocol.ComfyUi,
        ComfyUiUrl: comfyUrl);

    private static byte[] PngBytes()
    {
        var image = new Image<Rgb24>(8, 8);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                image[x, y] = new Rgb24(180, 140, 100);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static IReadOnlyList<PoseKeypoint> TBody() =>
    [
        new(0.5, 0.0, 1.0),    // nose
        new(0.5, 0.1, 1.0),    // neck
        new(0.3, 0.1, 1.0),    // r_shoulder
        new(0.15, 0.3, 1.0),   // r_elbow
        new(0.1, 0.5, 1.0),    // r_wrist
        new(0.7, 0.1, 1.0),    // l_shoulder
        new(0.85, 0.3, 1.0),   // l_elbow
        new(0.9, 0.5, 1.0),    // l_wrist
        new(0.4, 0.5, 1.0),    // r_hip
        new(0.4, 0.75, 1.0),   // r_knee
        new(0.4, 1.0, 1.0),    // r_ankle
        new(0.6, 0.5, 1.0),    // l_hip
        new(0.6, 0.75, 1.0),   // l_knee
        new(0.6, 1.0, 1.0),    // l_ankle
        new(0.48, 0.02, 1.0),  // r_eye
        new(0.52, 0.02, 1.0),  // l_eye
        new(0.44, 0.03, 1.0),  // r_ear
        new(0.56, 0.03, 1.0),  // l_ear
    ];

    private sealed class ThrowingExtractor : IPoseKeypointExtractor
    {
        public Task<PoseKeypointExtractionResult> ExtractAsync(
            PoseKeypointExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("should not be called");
    }

    private sealed class StubExtractor : IPoseKeypointExtractor
    {
        private readonly Func<PoseKeypointExtractionRequest, PoseKeypointExtractionResult> _result;

        public StubExtractor(Func<PoseKeypointExtractionRequest, PoseKeypointExtractionResult> result) => _result = result;

        public Task<PoseKeypointExtractionResult> ExtractAsync(
            PoseKeypointExtractionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result(request));
    }
}
