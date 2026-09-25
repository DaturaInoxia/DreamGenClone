using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// What a pose test render needs. It takes the SKELETON ITSELF rather than a library id, so the thing tested is
/// always the pose on screen — including one that has been nudged and never saved. Every other value comes from the
/// operator; none is defaulted here, because a default prompt or canvas would be a hidden claim about what was tested.
/// </summary>
public sealed record PoseTestRenderRequest(
    byte[] Skeleton,
    string PoseLabel,
    string ModelId,
    string Prompt,
    string? NegativePrompt,
    string? Size,
    long? Seed);

/// <summary>
/// The result of a test render: the image, which mechanism actually carried the pose, and a note saying what that
/// mechanism can and cannot do — so a disappointing test is readable as a pose problem rather than a mystery.
/// </summary>
public sealed record PoseTestRenderResult(byte[] Image, string Mechanism, string Note);

/// <summary>
/// One model a pose can be tested against, together with the mechanism a render would actually use and a
/// ready-to-show label for it. The label lives here rather than in the panel so the words the operator reads
/// and the mechanism the render takes come from the same resolution — a dropdown that said "reference image"
/// while the render took the ControlNet graph would be a silent lie about what was tested.
/// </summary>
/// <param name="Mechanism">The strategy string the resolver decided, e.g. <c>NativeMultiReference</c>.</param>
/// <param name="MechanismLabel">The operator-facing name of that mechanism.</param>
/// <param name="CanCarryPose">False when the model cannot carry a pose at all; <paramref name="Reason"/> says why.</param>
public sealed record PoseTestModelChoice(
    string ModelId,
    string DisplayName,
    string ProviderName,
    string Mechanism,
    string MechanismLabel,
    bool CanCarryPose,
    string Reason);

public interface IPoseTestRenderService
{
    /// <summary>
    /// The models a pose can be tested against, from the one model-resolution service, each carrying the pose
    /// mechanism it would use.
    ///
    /// Ordered by capability rather than by name: models that carry a pose come first, and among those the
    /// reference-image route comes before the ControlNet graph, because that is the route the pose work is built
    /// on. Alphabetical order was the previous behaviour and it put an SDXL/ControlNet model first and the
    /// reference-image model LAST, so the default selection tested the one mechanism already known to re-pose
    /// awkward stances (reported 2026-09-25: "the pose-library test pose does not work at all").
    /// </summary>
    Task<IReadOnlyList<PoseTestModelChoice>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders one throwaway image of a library pose and returns it. Nothing is persisted: no session, no image
    /// record, no gallery entry.
    ///
    /// Why this is not the ordinary render path: that path is session-scoped (<c>SceneRenderRequest</c> requires
    /// SessionId, InteractionId and PromptRecordId), and a pose test has no session. Inventing those ids to reuse it
    /// would fabricate the very values the render path keys on and file a diagnostic into a session's real image
    /// store. So this calls the SAME two things the render path calls — the one capability resolver and the same
    /// client for the mechanism it names — and skips only the persistence.
    /// </summary>
    Task<PoseTestRenderResult> RenderAsync(
        PoseTestRenderRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PoseTestRenderService : IPoseTestRenderService
{
    private readonly IModelResolutionService _models;
    private readonly IReferenceStrategyResolver _referenceStrategies;
    private readonly IPoseImageModelResolver _poseModels;
    private readonly IPoseConditionedImageClient _poseClient;
    private readonly ILogger<PoseTestRenderService> _logger;

    /// <summary>
    /// Optional for the same reason it is optional on the render handler: only the reference route needs it, and a
    /// test that cannot actually send the skeleton must say so instead of quietly rendering without it.
    /// </summary>
    private readonly IReferenceConditionedImageClient? _referenceClient;

    public PoseTestRenderService(
        IModelResolutionService models,
        IReferenceStrategyResolver referenceStrategies,
        IPoseImageModelResolver poseModels,
        IPoseConditionedImageClient poseClient,
        ILogger<PoseTestRenderService> logger,
        IReferenceConditionedImageClient? referenceClient = null)
    {
        _models = models;
        _referenceStrategies = referenceStrategies;
        _poseModels = poseModels;
        _poseClient = poseClient;
        _logger = logger;
        _referenceClient = referenceClient;
    }

    public async Task<IReadOnlyList<PoseTestModelChoice>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var models = await _models.ListSceneImageModelsAsync(identityCapableOnly: false, cancellationToken);

        var choices = new List<PoseTestModelChoice>(models.Count);
        foreach (var model in models)
        {
            // The SAME decision the render takes, through the ONE resolver, so the label cannot promise a mechanism
            // the render would refuse. An unavailable model is LISTED with its reason rather than dropped: "your
            // model is missing from the list" is not an explanation, and the reason is the actionable part.
            var resolution = await _referenceStrategies.ResolvePoseAsync(model.ModelId, cancellationToken);
            choices.Add(new PoseTestModelChoice(
                model.ModelId,
                model.DisplayName,
                model.ProviderName,
                resolution.Strategy,
                MechanismLabelFor(resolution.Strategy),
                resolution.IsAvailable,
                resolution.Reason));
        }

        return choices
            .OrderBy(choice => choice.CanCarryPose ? 0 : 1)
            .ThenBy(choice => IsReferenceRoute(choice.Mechanism) ? 0 : 1)
            .ThenBy(choice => choice.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// The reference-image route, named once. It is the route this work is about: the skeleton travels in the
    /// model's own reference slots and no ControlNet weights are involved.
    /// </summary>
    private static bool IsReferenceRoute(string mechanism) =>
        string.Equals(mechanism, ReferenceStrategyResolver.IdentityNativeMultiReference, StringComparison.OrdinalIgnoreCase);

    private static string MechanismLabelFor(string mechanism) =>
        mechanism switch
        {
            _ when IsReferenceRoute(mechanism) => "pose as a reference image",
            _ when string.Equals(mechanism, ReferenceStrategyResolver.PoseControlNet, StringComparison.OrdinalIgnoreCase)
                => "OpenPose ControlNet graph",
            _ => mechanism
        };

    public async Task<PoseTestRenderResult> RenderAsync(
        PoseTestRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new InvalidOperationException(
                "A pose test render needs an image model. Pick one, so the test uses the mechanism that model "
                + "actually renders with.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new InvalidOperationException(
                "A pose test render needs a prompt. The skeleton carries the body; the prompt carries everything "
                + "else, including the direction the figure faces on a reference-conditioned model.");
        }

        if (request.Skeleton.Length == 0)
        {
            throw new InvalidOperationException(
                $"The skeleton for '{request.PoseLabel}' is empty, so there is no pose to test. A render without it "
                + "would look like a pass while testing nothing.");
        }

        var skeleton = request.Skeleton;

        // ONE capability decision, from the resolver that owns it. A second decision made here could disagree with
        // what the render path would do, and then the test would be measuring something the app never runs.
        var strategy = await _referenceStrategies.ResolvePoseAsync(request.ModelId, cancellationToken);
        if (!strategy.IsAvailable)
        {
            throw new InvalidOperationException(
                $"The selected model cannot carry a pose, so a test render would not test one: {strategy.Reason}");
        }

        // ById, not ResolveImageModelAsync: that one takes a SESSION override and otherwise resolves the configured
        // default model, so passing a model id there silently tested the default instead of the operator's choice
        // (hit 2026-09-25 — the default resolved to a serverless provider and the test refused with
        // "ComfyUiServerless cannot run reference-conditioned generation" while the selected model was a local
        // ComfyUI model that can).
        var resolved = await _models.ResolveImageModelByIdAsync(request.ModelId, cancellationToken);
        _logger.LogInformation(
            "Pose test render: model={Model} protocol={Protocol} mechanism={Mechanism} pose={PoseLabel}",
            resolved.ModelIdentifier, resolved.ImageProtocol, strategy.Strategy, request.PoseLabel);

        if (string.Equals(
            strategy.Strategy, ReferenceStrategyResolver.IdentityNativeMultiReference, StringComparison.OrdinalIgnoreCase))
        {
            var client = _referenceClient ?? throw new InvalidOperationException(
                "This model carries a pose as a reference image, but the configured provider protocol has no "
                + "reference-conditioned client, so the skeleton could not be sent and the test would render an "
                + "unposed image that looks like a pass.");

            var bytes = await client.GenerateWithReferencesAsync(
                resolved,
                new ReferenceConditionedImageRequest
                {
                    PositivePrompt = request.Prompt,
                    NegativePrompt = request.NegativePrompt ?? string.Empty,
                    Size = request.Size,
                    Seed = request.Seed,
                    References =
                    [
                        new ReferenceConditionedImageInput
                        {
                            SemanticRole = "pose reference (OpenPose skeleton)",
                            FileName = "pose-skeleton.png",
                            Content = skeleton
                        }
                    ],
                    CorrelationId = $"pose-test:{request.PoseLabel}"
                },
                cancellationToken);

            return new PoseTestRenderResult(
                bytes,
                MechanismLabelFor(strategy.Strategy),
                "The skeleton travelled as a reference image. This carries the body's geometry but NOT which way the "
                + "figure faces (measured 2026-09-24: an identical front-facing skeleton came back turned away), so "
                + "state the view in the prompt — \"front view, facing the camera\" — or expect the figure to face "
                + "either way.");
        }

        if (string.Equals(strategy.Strategy, ReferenceStrategyResolver.PoseControlNet, StringComparison.OrdinalIgnoreCase))
        {
            var poseModel = await _poseModels.ResolveAsync(request.ModelId, cancellationToken);

            var bytes = await _poseClient.GenerateAsync(
                poseModel,
                new PoseConditionedImageRequest
                {
                    PositivePrompt = request.Prompt,
                    NegativePrompt = request.NegativePrompt ?? string.Empty,
                    Size = request.Size,
                    Seed = request.Seed,
                    PoseImageBytes = skeleton,
                    // The model's CONFIGURED strength, never one invented here: the qualification that admitted this
                    // model is what measured a strength that holds.
                    Strength = poseModel.DefaultStrength,
                    CorrelationId = $"pose-test:{request.PoseLabel}"
                },
                cancellationToken);

            return new PoseTestRenderResult(
                bytes,
                MechanismLabelFor(strategy.Strategy),
                $"The skeleton conditioned the sampler through '{poseModel.ControlNetAdapterRef}' at strength "
                + $"{poseModel.DefaultStrength:0.00}, which is the model's configured strength. This route honoured "
                + "the facing in testing, so it does not need the view stated in the prompt.");
        }

        throw new InvalidOperationException(
            $"The selected model reports pose mechanism '{strategy.Strategy}', which this test does not know how to "
            + "render. Teach it here and in the render path together — a test that runs through a mechanism the "
            + "render does not use is worse than no test.");
    }
}
