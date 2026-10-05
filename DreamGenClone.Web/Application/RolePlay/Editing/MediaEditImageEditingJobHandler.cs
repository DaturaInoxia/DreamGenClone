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
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

            // ---- Operations (crop, enhance, mirror) share this job, lane, retry budget and failure marking. Each is
            // executed by its own executor, so the job never learns what an operation does and an operation
            // never relearns how to be queued, retried, timed or failed.
            // A MaskedRegion (and an Outpaint) are NOT operations: they are EDITs that also carry a mask, so they run the
            // editor path below (the mask is built and passed there) and never resolve a deterministic-operation executor.
            if (plan.Operation.Kind is not MediaEditOperationKind.Edit
                and not MediaEditOperationKind.MaskedRegion
                and not MediaEditOperationKind.Outpaint)
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
            // The references are NAMED in the instruction, numbered from the image AFTER the source: the 2.1 edit
            // graph puts the source in slot 1, so the first reference is <image2>. Composed here, from the references
            // actually being sent, so a reference revalidation dropped cannot leave a tag pointing at nothing.
            var instruction = ReferenceRoleClauses.AppendToEditInstruction(
                parts.Prompt,
                [.. parts.References.Select(reference => (reference.SlotKind, reference.Description))]);
            var bytes = await ExecuteAsync(plan, instruction, parts.References, resolved, source, cancellationToken);
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
            // A region edit (and an outpaint) carry the same envelope as they do: the kind, plus parameters of that same
            // kind. Without these arms their payload falls to the default and is refused as an unnamed operation kind.
            case MediaEditOperationKind.MaskedRegion:
            case MediaEditOperationKind.Outpaint:
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

        // A region edit (and an outpaint) are EDITs that also carry a mask (CASE-21 / CASE-24): the instruction is
        // untouched, and the mask is what pins everything outside the rectangle (or outside the newly exposed strip).
        // An outpaint also names its geometry in the instruction, so the model generates the new strip and not a new frame.
        if (plan.Operation.Outpaint is { } outpaint)
            prompt = $"{prompt} Extend the image to the {outpaint.Direction.ToString().ToLowerInvariant()}; generate only the newly revealed strip and keep the existing area exactly as it is at input fidelity.";

        using var mask = await BuildMaskAsync(plan, source, resolved, cancellationToken);

        // A confined run needs the source's own pixels again AFTER the render: the mask the host confines with is
        // rounded to 0/1, so the area it confines the edit to is a rectangle whose edge survives into the render as a
        // one-pixel step. They are read here, before the editor call, because the upload transport disposes the stream
        // it sends (and because the blend must happen against the frame the render started from, not whatever the
        // source file holds by the time the render finishes).
        using var confinement = await ReadConfinementAsync(plan, source, cancellationToken);

        byte[] rendered;
        if (planReferences.Count == 0)
        {
            rendered = await _imageEditingClient.EditAsync(
                resolved, source, sourceFileName, prompt, cancellationToken, mask);
        }
        else
        {
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

                rendered = await _imageEditingClient.EditWithReferencesAsync(
                    resolved, source, sourceFileName, prompt, references, cancellationToken, mask);
            }
            finally
            {
                foreach (var stream in streams)
                    await stream.DisposeAsync();
            }
        }

        // A whole-frame edit has nothing to blend back over: the render IS the result.
        return confinement is null
            ? rendered
            : CompositeConfinedRender(confinement, rendered);
    }

    /// <summary>
    /// The mask for a confined run (region or outpaint), or null for a whole-frame edit. The geometry is refused HERE -
    /// before a render is paid for - when the selected model's graph cannot confine an edit: the client refuses too, but
    /// an operator should not learn that from a failed run.
    ///
    /// The source has to be MEASURED to build a mask at its own pixel size, so a confined run requires a seekable source
    /// and the stream is rewound: the editor client reads it again from the start.
    /// </summary>
    private async Task<ImageEditingMask?> BuildMaskAsync(
        MediaEditRunPlan plan,
        Stream source,
        ResolvedImageEditorModel resolved,
        CancellationToken cancellationToken)
    {
        var region = plan.Operation.Region;
        var outpaint = plan.Operation.Outpaint;
        if (region is null && outpaint is null)
            return null;

        region?.Validate();
        outpaint?.Validate();

        if (resolved.GraphKind != ImageEditorGraphKind.QwenImage21Native)
        {
            throw new InvalidOperationException(
                $"A confined edit needs a model whose edit graph can confine one, but '{resolved.ModelIdentifier}' uses "
                + $"'{resolved.GraphKind}'. Set 'Editor Graph' to Qwen-Image-2.1 for this model in Model Manager "
                + "(/model-manager), or edit the whole frame.");
        }

        if (!source.CanSeek)
        {
            throw new InvalidOperationException(
                "A confined edit must measure the source to build its mask at the frame's own size, which needs a seekable "
                + "source image. Open the image as a file-backed stream, or edit the whole frame.");
        }

        var info = await Image.IdentifyAsync(source, cancellationToken);
        source.Position = 0;

        if (region is not null)
        {
            var bytes = _regionMaskEngine.Build(region, info.Width, info.Height);
            var checksum = Convert.ToHexString(SHA256.HashData(bytes));
            return new ImageEditingMask(
                new MemoryStream(bytes),
                $"{plan.SourceImageId}-region.png",
                checksum,
                region.GrowMaskBy,
                region.FeatherPixels);
        }

        // Outpaint (CASE-24): the strip is the complement of the source on the padded canvas. It is expressed as a
        // region in PERCENT of the PADDED canvas and painted by the same engine, so grow + feather behave identically
        // to a region. The pad amounts travel on the mask so the client can pad the source to match.
        var (paddedWidth, paddedHeight, padLeft, padTop, padRight, padBottom) = OutpaintPads(outpaint!, info.Width, info.Height);
        var strip = OutpaintStrip(outpaint!, info.Width, info.Height, paddedWidth, paddedHeight);
        var stripBytes = _regionMaskEngine.Build(strip, paddedWidth, paddedHeight);
        var stripChecksum = Convert.ToHexString(SHA256.HashData(stripBytes));
        return new ImageEditingMask(
            new MemoryStream(stripBytes),
            $"{plan.SourceImageId}-outpaint.png",
            stripChecksum,
            strip.GrowMaskBy,
            strip.FeatherPixels,
            padLeft, padTop, padRight, padBottom);
    }

    /// <summary>
    /// How far the host may crop a frame before it encodes it: it encodes whole VAE blocks, so a canvas that is not a
    /// multiple of the block size comes back a few pixels smaller. A render that is smaller than this is a size this
    /// cannot place back on its own frame, and is refused rather than shifted.
    /// </summary>
    private const int MaxEncodeCropPixels = 8;

    /// <summary>
    /// The frame a confined render is blended back over, with the rectangle that confinement means ON that frame, or
    /// null for a whole-frame edit (which has nothing to blend back over - the render IS the result).
    ///
    /// A region blends over the source itself. An outpaint blends over the source on the padded canvas the host pads
    /// to, painted with the same mid-grey the host's own pad node fills with: that area is only ever read where the
    /// blend is fully opaque, but keeping the two canvases the same image is what makes the alignment below arithmetic
    /// rather than trust.
    /// </summary>
    private static async Task<ConfinedFrame?> ReadConfinementAsync(
        MediaEditRunPlan plan, Stream source, CancellationToken cancellationToken)
    {
        var region = plan.Operation.Region;
        var outpaint = plan.Operation.Outpaint;
        if (region is null && outpaint is null)
            return null;

        // The source is copied into memory rather than decoded straight off the stream: the editor reads that same
        // stream next, and it has to find it at its start - uploading an exhausted stream sends an empty body, and the
        // host answers an empty body with a render of nothing.
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        source.Position = 0;

        var baseImage = Image.Load<Rgb24>(buffer.ToArray());
        if (region is not null)
            return new ConfinedFrame(baseImage, region);

        var sourceWidth = baseImage.Width;
        var sourceHeight = baseImage.Height;
        var (paddedWidth, paddedHeight, padLeft, padTop, _, _) =
            OutpaintPads(outpaint!, sourceWidth, sourceHeight);

        var padded = new Image<Rgb24>(paddedWidth, paddedHeight, new Rgb24(128, 128, 128));
        padded.Mutate(context => context.DrawImage(baseImage, new Point(padLeft, padTop), 1f));
        baseImage.Dispose();

        return new ConfinedFrame(
            padded, OutpaintStrip(outpaint!, sourceWidth, sourceHeight, paddedWidth, paddedHeight));
    }

    /// <summary>
    /// Blends the render back over the frame it started from, through the region's feather ramp: the render's pixels
    /// where the operator may change the picture, the untouched source where they may not, and a fade between the two.
    /// This is what removes the visible rectangle - the host's hard mask edge lies where the ramp has already reached
    /// zero, and everything outside the region is handed back byte for byte as the source had it.
    /// </summary>
    private byte[] CompositeConfinedRender(ConfinedFrame frame, byte[] renderedBytes)
    {
        using var rendered = Image.Load<Rgb24>(renderedBytes);
        var offsetX = AlignRender(frame.Base.Width, rendered.Width, "width");
        var offsetY = AlignRender(frame.Base.Height, rendered.Height, "height");

        using var alpha = Image.Load<L8>(
            _regionMaskEngine.BuildCompositeAlpha(frame.Confinement, frame.Base.Width, frame.Base.Height));

        using var output = new Image<Rgb24>(rendered.Width, rendered.Height);
        rendered.ProcessPixelRows(output, (renderAccessor, outputAccessor) =>
        {
            for (var y = 0; y < rendered.Height; y++)
            {
                var renderRow = renderAccessor.GetRowSpan(y);
                var outputRow = outputAccessor.GetRowSpan(y);
                for (var x = 0; x < rendered.Width; x++)
                {
                    var weight = alpha[x + offsetX, y + offsetY].PackedValue;
                    var source = frame.Base[x + offsetX, y + offsetY];
                    var edit = renderRow[x];
                    outputRow[x] = new Rgb24(
                        Blend(source.R, edit.R, weight),
                        Blend(source.G, edit.G, weight),
                        Blend(source.B, edit.B, weight));
                }
            }
        });

        using var buffer = new MemoryStream();
        output.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Where a render sits on the frame it was rendered from. The host crops a frame it cannot encode whole, taking
    /// half the remainder from the leading edge of each axis, so the render arrives centred and this is the offset that
    /// puts it back. A size that is neither the frame nor that crop fails here, by name: a misplaced render is
    /// invisible in the code and obvious in the picture.
    /// </summary>
    private static int AlignRender(int frameExtent, int renderExtent, string axis)
    {
        var crop = frameExtent - renderExtent;
        if (crop < 0 || crop > MaxEncodeCropPixels)
        {
            throw new InvalidOperationException(
                $"A confined render came back {renderExtent}px along the {axis} of a {frameExtent}px frame, which is not "
                + "the centred crop the host's encode node makes. Refused rather than placed wrongly.");
        }

        return crop / 2;
    }

    /// <summary>The render's share of one channel, with the source keeping the rest.</summary>
    private static byte Blend(byte source, byte edit, byte weight)
        => (byte)((source * (255 - weight) + edit * weight + 127) / 255);

    /// <summary>The frame a confined render is blended back over, and the rectangle that confinement means on it.</summary>
    private sealed record ConfinedFrame(Image<Rgb24> Base, MediaEditRegionOperation Confinement) : IDisposable
    {
        public void Dispose() => Base.Dispose();
    }

    /// <summary>The padded canvas size and per-edge pixel pads an outpaint adds to the source.</summary>
    private static (int Width, int Height, int Left, int Top, int Right, int Bottom) OutpaintPads(
        MediaEditOutpaintOperation outpaint, int width, int height)
    {
        var pad = (int)Math.Round(
            (outpaint.Direction is MediaEditOutpaintDirection.Top or MediaEditOutpaintDirection.Bottom ? height : width)
            * outpaint.Percent / 100.0);

        var (left, top, right, bottom) = outpaint.Direction switch
        {
            MediaEditOutpaintDirection.Left => (pad, 0, 0, 0),
            MediaEditOutpaintDirection.Right => (0, 0, pad, 0),
            MediaEditOutpaintDirection.Top => (0, pad, 0, 0),
            MediaEditOutpaintDirection.Bottom => (0, 0, 0, pad),
            _ => throw new InvalidOperationException($"Unsupported outpaint direction '{outpaint.Direction}'.")
        };

        return (width + left + right, height + top + bottom, left, top, right, bottom);
    }

    /// <summary>The newly exposed strip, as a region in percent of the PADDED canvas.</summary>
    private static MediaEditRegionOperation OutpaintStrip(
        MediaEditOutpaintOperation outpaint, int width, int height, int paddedWidth, int paddedHeight)
    {
        double left = 0, top = 0, stripWidth = 100, stripHeight = 100;
        switch (outpaint.Direction)
        {
            case MediaEditOutpaintDirection.Left:
                left = 0; top = 0; stripWidth = (double)(paddedWidth - width) / paddedWidth * 100.0; stripHeight = 100; break;
            case MediaEditOutpaintDirection.Right:
                left = (double)width / paddedWidth * 100.0; top = 0; stripWidth = 100.0 - left; stripHeight = 100; break;
            case MediaEditOutpaintDirection.Top:
                left = 0; top = 0; stripWidth = 100; stripHeight = (double)(paddedHeight - height) / paddedHeight * 100.0; break;
            case MediaEditOutpaintDirection.Bottom:
                left = 0; top = (double)height / paddedHeight * 100.0; stripWidth = 100; stripHeight = 100.0 - top; break;
        }

        return new MediaEditRegionOperation(left, top, stripWidth, stripHeight, outpaint.GrowMaskBy, outpaint.FeatherPixels);
    }
}
