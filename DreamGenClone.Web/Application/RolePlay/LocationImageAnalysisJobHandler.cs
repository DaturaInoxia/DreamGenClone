using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Reads a LOCATION image with a vision model, so a frame plan can put a character on a structure that is actually in
/// the picture (B-160).
/// </summary>
/// <remarks>
/// <para>
/// Measured failure it exists for: a production render placed Dean at the left trailer's door when the moment wanted
/// him on the deck — and the deck belongs to the RIGHT-hand trailer in the location image the operator had created.
/// Nothing in the pipeline knew what was inside a location image; the plan could say "midground-left" but never "on the
/// deck".
/// </para>
///
/// <para>
/// It deliberately reuses the vision path the app already has — the same client, model resolution, input validation and
/// health check as the asset-edit description handlers — rather than introducing a second way to ask a model about a
/// picture. A durable job, like those handlers: an analysis is per IMAGE and reusable by every production group, and it
/// must never block a render.
/// </para>
///
/// <para>
/// The analysis PROPOSES. It is stored against the analysed version's checksum and later pre-fills the operator's
/// reference directions — which stay the single owner of what the prompt says, so it can never silently overrule them.
/// </para>
/// </remarks>
public sealed class LocationImageAnalysisJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISceneAssetRepository _assets;
    private readonly ISceneAssetStorageService _storage;
    private readonly IMultimodalModelResolutionService _modelResolver;
    private readonly IMultimodalCompletionClient _completionClient;
    private readonly ILocationImageAnalysisStore _analyses;
    private readonly ILogger<LocationImageAnalysisJobHandler> _logger;

    public LocationImageAnalysisJobHandler(
        ISceneAssetRepository assets,
        ISceneAssetStorageService storage,
        IMultimodalModelResolutionService modelResolver,
        IMultimodalCompletionClient completionClient,
        ILocationImageAnalysisStore analyses,
        ILogger<LocationImageAnalysisJobHandler> logger)
    {
        _assets = assets;
        _storage = storage;
        _modelResolver = modelResolver;
        _completionClient = completionClient;
        _analyses = analyses;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.LocationImageAnalysis;

    public Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<LocationImageAnalysisJobPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Location image analysis payload is missing or invalid.");

        if (string.IsNullOrWhiteSpace(payload.ImageId))
        {
            throw new InvalidOperationException("Location image analysis payload requires an image id.");
        }

        var image = await _assets.GetImageAsync(payload.ImageId, cancellationToken)
            ?? throw new InvalidOperationException($"Scene asset image '{payload.ImageId}' was not found.");

        if (image.Status != SceneAssetStatus.Complete || string.IsNullOrWhiteSpace(image.FileRelativePath))
        {
            throw new InvalidOperationException(
                $"Scene asset image '{image.Id}' is not a stored, complete image, so there is nothing to read.");
        }

        // Idempotent by design: a job that is retried, or enqueued twice for one image, must not pay for a second
        // reading of a version already read. A different checksum means a different picture, so that one DOES run — and
        // an explicit Force means the operator asked, so it runs too.
        var existing = LocationImageAnalysisJson.TryRead(image.LocationAnalysisJson);
        if (!payload.Force && existing is not null && existing.IsAbout(image.Sha256))
        {
            _logger.LogInformation(
                "Location image {ImageId} is already analysed for checksum {Sha256}; nothing to do.",
                image.Id, image.Sha256);
            return;
        }

        var resolved = await _modelResolver.ResolveAsync(AppFunction.RolePlaySceneImageEditPromptCompiler, cancellationToken);

        await using var stream = await _storage.OpenReadAsync(image.FileRelativePath, cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(stream, resolved.MaximumInputImageBytes, cancellationToken);
        SceneImageMultimodalInput.Validate(input, resolved);

        // The analysis is stored against the checksum of what was ACTUALLY read, not the row's recorded one. They
        // should agree; if they do not, recording the read bytes is the honest answer, and the mismatch is what makes
        // a later reader see the analysis as stale rather than as describing the current file.
        if (!string.Equals(input.Sha256, image.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Location image {ImageId} checksum changed since it was recorded (row {RowSha}, file {FileSha}); the "
                + "analysis will be recorded against the file that was read.",
                image.Id, image.Sha256, input.Sha256);
        }

        await _completionClient.CheckHealthAsync(resolved, cancellationToken);

        var completion = await _completionClient.GenerateAsync(
            resolved,
            new MultimodalCompletionRequest(
                LocationAnchorPromptBuilder.BuildSystemMessage(),
                LocationAnchorPromptBuilder.BuildUserMessage(),
                new MultimodalImageInput(input.MediaType, input.Bytes, input.Width, input.Height, input.Sha256),
                LocationAnchorPromptBuilder.ResponseSchemaName,
                LocationAnchorPromptBuilder.CreateResponseSchema()),
            cancellationToken);

        var analysis = LocationAnchorPromptBuilder.Parse(completion.Content, input.Sha256, DateTime.UtcNow);
        await _analyses.SaveAsync(image.Id, analysis, cancellationToken);

        _logger.LogInformation(
            "Location image analysed: ImageId={ImageId}, Anchors={Anchors}, Sha256={Sha256}",
            image.Id, analysis.Anchors.Count, analysis.AnalyzedSha256);
    }

    /// <summary>Which image to read. One field, because the analysis is a fact about the image and nothing else.</summary>
    /// <param name="Force">
    /// True when the operator asked to read it AGAIN. The handler is otherwise idempotent per checksum — which is what
    /// stops a retry or a duplicate trigger paying twice for one reading — so without this an on-demand re-read of an
    /// already-analysed image would silently do nothing.
    /// </param>
    public sealed record LocationImageAnalysisJobPayload(string ImageId, bool Force = false);
}
