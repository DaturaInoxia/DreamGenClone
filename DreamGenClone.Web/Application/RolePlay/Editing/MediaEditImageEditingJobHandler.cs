using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The ONE image-editing job: it owns editor-model resolution, the editor call, timing, logging and
/// failure marking for every subject kind. What differs per kind — provenance validation, reference
/// assembly, and how the result is stored — lives behind <see cref="IMediaEditSubjectWriter"/>.
/// </summary>
public sealed class MediaEditImageEditingJobHandler : IDurableBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MediaEditSubjectWriterResolver _writers;
    private readonly MediaEditOperationExecutorResolver _operations;
    private readonly IImageEditorModelResolver _modelResolver;
    private readonly IImageEditingClient _imageEditingClient;
    private readonly IImageRegionMaskEngine _regionMaskEngine;
    private readonly ILogger<MediaEditImageEditingJobHandler> _logger;

    public MediaEditImageEditingJobHandler(
        MediaEditSubjectWriterResolver writers,
        MediaEditOperationExecutorResolver operations,
        IImageEditorModelResolver modelResolver,
        IImageEditingClient imageEditingClient,
        IImageRegionMaskEngine regionMaskEngine,
        ILogger<MediaEditImageEditingJobHandler> logger)
    {
        _writers = writers;
        _operations = operations;
        _modelResolver = modelResolver;
        _imageEditingClient = imageEditingClient;
        _regionMaskEngine = regionMaskEngine;
        _logger = logger;
    }

    public string JobType => BackgroundJobTypes.MediaEditImageEditing;

    public Task HandleAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
        => HandleAsync(job.PayloadJson, cancellationToken);

    private async Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<MediaEditImageEditingJobPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Media edit image job payload is missing or invalid.");
        if (payload.SubjectKind == MediaEditSubjectKind.Unknown)
            throw new InvalidOperationException("Media edit image job payload requires an explicit subject kind.");
        if (string.IsNullOrWhiteSpace(payload.ImageId))
            throw new InvalidOperationException("Media edit image job payload requires an image id.");

        var operation = ReadOperation(payload);
        var writer = _writers.Resolve(payload.SubjectKind);
        var context = new MediaEditRunContext(
            payload.ImageId, operation, payload.EditorModelId, payload.ReferenceApplicationsJson, payload.ScopeId);

        // Preparation is inside the failure scope on purpose: a provenance or checksum refusal must
        // still mark the queued image failed, exactly as the editor handlers it replaces did.
        MediaEditRunPlan? plan = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            plan = await writer.PrepareAsync(context, cancellationToken);

            // Nothing to do: the image already finished (or was cancelled) before this delivery.
            if (plan is null)
            {
                _logger.LogInformation(
                    "Media edit image skipped: the queued image is already complete or cancelled. Subject={SubjectKind}, ImageId={ImageId}, Scope={Scope}",
                    payload.SubjectKind, payload.ImageId, payload.ScopeId);
                return;
            }

            // The row is claimed here, before any work is paid for, because the completion of a claimed
            // subject only matches a row this run owns. A subject whose store has no claim transition
            // reports success, and a deterministic operation is deliberately never claimed.
            if (!await writer.ClaimAsync(context, cancellationToken))
            {
                _logger.LogInformation(
                    "Media edit image skipped: the queued image could not be claimed and is already terminal. Subject={SubjectKind}, ImageId={ImageId}, Scope={Scope}",
                    payload.SubjectKind, payload.ImageId, payload.ScopeId);
                return;
            }

            // ---- Operations (crop, enhance) share this job, lane, retry budget and failure marking. Each is
            // executed by its own executor, so the job never learns what an operation does and an operation
            // never relearns how to be queued, retried, timed or failed.
            if (plan.Operation.Kind != MediaEditOperationKind.Edit)
            {
                // Parameter validation happens inside the failure scope: an unusable operation is a failure
                // of the queued row, so the row is marked failed rather than left looking pending.
                plan.Operation.Validate();
                RequireOperationOnlyPlan(plan);

                var executor = _operations.Resolve(plan.Operation.Kind);
                await using var operationSource = await OpenSourceAsync(plan, cancellationToken);
                var operationOutput = await executor.ExecuteAsync(plan, operationSource, cancellationToken);
                stopwatch.Stop();

                await writer.CompleteAsync(plan, operationOutput, cancellationToken);

                _logger.LogInformation(
                    "Media edit operation completed: Subject={SubjectKind}, ImageId={ImageId}, SourceImageId={SourceImageId}, Operation={Operation}, DurationMs={DurationMs}, Scope={Scope}",
                    payload.SubjectKind, plan.ImageId, plan.SourceImageId, plan.Operation.Describe(),
                    stopwatch.ElapsedMilliseconds, plan.LogScope);
                return;
            }

            var parts = RequireEditParts(plan);
            var resolved = string.IsNullOrWhiteSpace(parts.Editor.ExplicitModelId)
                ? await _modelResolver.ResolveAsync(cancellationToken)
                : await _modelResolver.ResolveByIdAsync(parts.Editor.ExplicitModelId, cancellationToken);

            // The Finish stage may request adult content; its plan says so and the resolved editor
            // model decides whether that is allowed. Refuse rather than silently downgrade.
            if (parts.Editor.RequiresAdultContentPolicy
                && resolved.ContentPolicy is ImageContentPolicy.SfwFiltered or ImageContentPolicy.Unknown)
            {
                throw new InvalidOperationException(
                    "Adult-content Finish edits are unavailable because the resolved editor model does not allow them.");
            }

            await using var source = await OpenSourceAsync(plan, cancellationToken);
            var bytes = await ExecuteAsync(plan, parts.Prompt, parts.References, resolved, source, cancellationToken);
            stopwatch.Stop();

            await writer.CompleteAsync(plan, new MediaEditRunOutput(
                bytes, MediaEditOperationKind.Edit,
                resolved.ModelIdentifier, resolved.ProviderName, resolved.ContentPolicy), cancellationToken);

            _logger.LogInformation(
                "Media edit image completed: Subject={SubjectKind}, ImageId={ImageId}, SourceImageId={SourceImageId}, Model={Model}, DurationMs={DurationMs}, References={References}, Scope={Scope}",
                payload.SubjectKind, plan.ImageId, plan.SourceImageId, resolved.ModelIdentifier,
                stopwatch.ElapsedMilliseconds, parts.References.Count, plan.LogScope);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await writer.FailAsync(context, ex.Message, cancellationToken);
            _logger.LogWarning(ex,
                "Media edit image failed: Subject={SubjectKind}, ImageId={ImageId}, DurationMs={DurationMs}, Scope={Scope}",
                payload.SubjectKind, plan?.ImageId ?? payload.ImageId, stopwatch.ElapsedMilliseconds, plan?.LogScope);
            throw;
        }
    }

    /// <summary>
    /// The subject writer owns the store, so it supplies the source opener through the plan and this
    /// handler never learns which storage service is behind it. The bytes are re-read and checksummed
    /// here so a source that changed after enqueue cannot be edited.
    /// </summary>
    private static async Task<Stream> OpenSourceAsync(MediaEditRunPlan plan, CancellationToken cancellationToken)
    {
        await using var reader = await plan.SourceOpenAsync(cancellationToken);
        var input = await SceneImageMultimodalInput.ReadAsync(reader, int.MaxValue, cancellationToken);
        if (!string.Equals(input.Sha256, plan.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The source image checksum changed after edit execution was queued.");

        return new MemoryStream(input.Bytes);
    }

    /// <summary>
    /// Reads the operation the job was queued for. It is never inferred from what the payload happens
    /// to contain: an unnamed operation, a crop without parameters, or an edit carrying crop parameters
    /// all fail fast.
    /// </summary>
    private static MediaEditOperation ReadOperation(MediaEditImageEditingJobPayload payload)
    {
        switch (payload.OperationKind)
        {
            // Every operation that carries its own parameters is read the same way: the payload must name
            // the kind and carry parameters of that same kind. What the parameters mean stays the
            // operation's business.
            case MediaEditOperationKind.Crop:
            case MediaEditOperationKind.Enhance:
            // A region edit carries the same envelope as they do: the kind, plus parameters of that same kind. Without
            // this arm a region payload falls to the default and is refused as an unnamed operation kind.
            case MediaEditOperationKind.MaskedRegion:
            {
                var kind = payload.OperationKind;
                if (string.IsNullOrWhiteSpace(payload.OperationJson))
                    throw new InvalidOperationException($"A {kind} media edit run requires its {kind} parameters.");

                var operation = JsonSerializer.Deserialize<MediaEditOperation>(payload.OperationJson, JsonOptions)
                    ?? throw new InvalidOperationException($"The {kind} media edit parameters are invalid.");

                // Envelope check only: the parameters themselves are validated inside the run's failure
                // scope, so an unusable operation marks its image failed instead of leaving it pending.
                if (operation.Kind != kind)
                    throw new InvalidOperationException(
                        $"A {kind} media edit run requires {kind} parameters, but the payload carried '{operation.Kind}'.");
                return operation;
            }

            case MediaEditOperationKind.Edit:
                if (!string.IsNullOrWhiteSpace(payload.OperationJson))
                    throw new InvalidOperationException("An edit media edit run must not carry operation parameters.");
                return MediaEditOperation.ForEdit;

            default:
                throw new InvalidOperationException(
                    "Media edit image job payload requires an explicit operation kind, but got " +
                    $"'{payload.OperationKind}'.");
        }
    }

    /// <summary>An edit needs the prompt, references and editor resolution its writer prepared.</summary>
    private static (string Prompt, IReadOnlyList<MediaEditReference> References, MediaEditEditorResolution Editor)
        RequireEditParts(MediaEditRunPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Prompt))
            throw new InvalidOperationException("An edit run requires the compiled prompt its subject writer prepared.");

        return (
            plan.Prompt,
            plan.References
                ?? throw new InvalidOperationException("An edit run requires its reference list (empty when there are none)."),
            plan.Editor
                ?? throw new InvalidOperationException("An edit run requires the editor resolution its subject writer prepared."));
    }

    /// <summary>A crop is not a render, so it must carry none of the edit-only members.</summary>
    private static void RequireOperationOnlyPlan(MediaEditRunPlan plan)
    {
        if (plan.Editor is not null || plan.References is not null || !string.IsNullOrWhiteSpace(plan.Prompt))
        {
            throw new InvalidOperationException(
                "A crop run must not carry an editor resolution, a prompt or references.");
        }
    }

    private async Task<byte[]> ExecuteAsync(
        MediaEditRunPlan plan,
        string prompt,
        IReadOnlyList<MediaEditReference> planReferences,
        ResolvedImageEditorModel resolved,
        Stream source,
        CancellationToken cancellationToken)
    {
        var sourceFileName = $"{plan.SourceImageId}.png";

        // A region edit is an EDIT that also carries a mask (CASE-21): the instruction is untouched, and the mask is what
        // pins everything outside the rectangle. It is built at the SOURCE's own pixel size here, so the operation's
        // percentages mean the same thing on any frame.
        using var mask = await BuildRegionMaskAsync(plan, source, resolved, cancellationToken);

        if (planReferences.Count == 0)
            return await _imageEditingClient.EditAsync(resolved, source, sourceFileName, prompt, cancellationToken, mask);

        var streams = new List<Stream>(planReferences.Count);
        try
        {
            var references = new List<ImageEditingReference>(planReferences.Count);
            foreach (var reference in planReferences)
            {
                var stream = await reference.OpenAsync(cancellationToken);
                streams.Add(stream);
                references.Add(new ImageEditingReference(
                    reference.Ordinal, reference.Description, stream, reference.FileName, reference.Sha256));
            }

            return await _imageEditingClient.EditWithReferencesAsync(
                resolved, source, sourceFileName, prompt, references, cancellationToken, mask);
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    /// <summary>
    /// The mask for a region run, or null for a whole-frame edit. The region is refused HERE - before a render is paid
    /// for - when the selected model's graph cannot confine an edit: the client refuses too, but an operator should not
    /// learn that from a failed run.
    ///
    /// The source has to be MEASURED to build a mask at its own pixel size, so a region run requires a seekable source
    /// and the stream is rewound: the editor client reads it again from the start.
    /// </summary>
    private async Task<ImageEditingMask?> BuildRegionMaskAsync(
        MediaEditRunPlan plan,
        Stream source,
        ResolvedImageEditorModel resolved,
        CancellationToken cancellationToken)
    {
        if (plan.Operation.Region is not { } region)
            return null;

        region.Validate();

        if (resolved.GraphKind != ImageEditorGraphKind.QwenImage21Native)
        {
            throw new InvalidOperationException(
                $"A region edit needs a model whose edit graph can confine one, but '{resolved.ModelIdentifier}' uses "
                + $"'{resolved.GraphKind}'. Set 'Editor Graph' to Qwen-Image-2.1 for this model in Model Manager "
                + "(/model-manager), or edit the whole frame.");
        }

        if (!source.CanSeek)
        {
            throw new InvalidOperationException(
                "A region edit must measure the source to build its mask at the frame's own size, which needs a seekable "
                + "source image. Open the image as a file-backed stream, or edit the whole frame.");
        }

        var info = await Image.IdentifyAsync(source, cancellationToken);
        source.Position = 0;

        var bytes = _regionMaskEngine.Build(region, info.Width, info.Height);
        var checksum = Convert.ToHexString(SHA256.HashData(bytes));

        return new ImageEditingMask(
            new MemoryStream(bytes),
            $"{plan.SourceImageId}-region.png",
            checksum,
            region.GrowMaskBy,
            region.FeatherPixels);
    }
}
