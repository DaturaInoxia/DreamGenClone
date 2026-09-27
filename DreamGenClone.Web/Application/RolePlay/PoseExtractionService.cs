using System.Security.Cryptography;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.ModelManager;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// What an extraction is asked for: the picture, and where the resulting pose belongs. The model is named rather than
/// defaulted, because the pose estimator runs on a ComfyUI host and which host that is belongs to the operator's
/// Model Manager — a default here would be a hidden choice about where an image gets sent.
/// </summary>
/// <param name="SourceImageLabel">
/// What the image was, in the operator's terms, so the stored provenance says what was measured rather than only
/// which bytes were hashed.
/// </param>
/// <param name="HeadOnly">
/// Whether the operator declared this a HEAD-ONLY pose. False is a body pose and keeps the pose's head as the COCO-18
/// five points; true keeps the estimator's 70-point face channel and lets that channel satisfy the head rule, because
/// a face-carrying pose constrains the head harder than the neck and shoulders do. Declared, never inferred from the
/// image: guessing the kind is how a body pose and a head pose end up indistinguishable in the library.
/// </param>
public sealed record PoseExtractionRequest(
    byte[] Image,
    string ModelId,
    string Name,
    string Category,
    string? Keywords,
    string LibraryId,
    string SourceImageLabel,
    bool HeadOnly);

/// <summary>
/// Reads a pose out of an image and stores it as a library pose. This is what lets the library grow from pictures the
/// app already has — a rendered candidate, an accepted identity angle, a body view — instead of only from poses
/// somebody authored by hand.
/// </summary>
public interface IPoseExtractionService
{
    Task<PosePreset> ExtractAsync(PoseExtractionRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PoseExtractionService : IPoseExtractionService
{
    private readonly IPoseKeypointExtractor _extractor;
    private readonly IModelResolutionService _models;
    private readonly IPoseLibraryService _library;
    private readonly PoseStudioOptions _studio;

    public PoseExtractionService(
        IPoseKeypointExtractor extractor,
        IModelResolutionService models,
        IPoseLibraryService library,
        IOptions<PoseStudioOptions> studioOptions)
    {
        _extractor = extractor;
        _models = models;
        _library = library;
        _studio = studioOptions.Value;
    }

    public async Task<PosePreset> ExtractAsync(
        PoseExtractionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Image.Length == 0)
        {
            throw new InvalidOperationException("There is no image to extract a pose from.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new InvalidOperationException(
                "Choose the model that will run the pose estimator. It has to be a ComfyUI model, because the "
                + "estimator is a node on that host.");
        }

        var model = await _models.ResolveImageModelByIdAsync(request.ModelId, cancellationToken);

        // ComfyUiUrl is populated only for a ComfyUI-protocol provider, so this refuses a model that could never run
        // the estimator instead of sending the image somewhere that would fail obscurely.
        if (string.IsNullOrWhiteSpace(model.ComfyUiUrl))
        {
            throw new InvalidOperationException(
                $"Image model '{request.ModelId}' is not served by ComfyUI, so it cannot run a pose estimator. "
                + "Choose a ComfyUI model.");
        }

        var label = request.Name.Trim();
        var provider = string.IsNullOrWhiteSpace(model.ProviderName) ? "ComfyUI" : model.ProviderName;

        var extraction = await _extractor.ExtractAsync(
            new PoseKeypointExtractionRequest(
                request.Image, provider, model.ComfyUiUrl, model.ProviderTimeoutSeconds, request.HeadOnly),
            cancellationToken);

        var person = OpenPosePoseJson.Parse(extraction.PersonJson, label);

        // The plan's rule, and the reason it exists: a pose whose head was not resolved still renders, so it would be
        // stored and used as if it were fine. A pose that carries a face channel is judged by that channel instead of
        // by the body's neck-and-shoulders test — the test exists to catch an UNCONSTRAINED head, and 70 face points
        // is the opposite of unconstrained.
        OpenPosePoseJson.RequireHeadKeypoints(person, label, _studio.RequireFaceMinimumVisiblePoints());

        return await _library.SaveAuthoredPoseAsync(
            new AuthoredPoseRequest(
                label,
                request.Category.Trim(),
                request.Keywords,
                new PoseView(),
                request.LibraryId,
                Head: null,
                Keypoints: person,
                Drags: null,
                Origin: "extracted",
                Extraction: new ExtractedPoseProvenance(
                    Convert.ToHexString(SHA256.HashData(request.Image)).ToLowerInvariant(),
                    request.SourceImageLabel,
                    provider,
                    model.ComfyUiUrl,
                    extraction.NodeName,
                    extraction.NodeSignature,
                    extraction.WorkflowVersion,
                    request.HeadOnly)),
            cancellationToken);
    }
}
