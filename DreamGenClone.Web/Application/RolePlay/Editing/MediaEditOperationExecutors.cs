using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Executes one non-edit operation end to end and returns what should be persisted. The job keeps owning
/// preparation, source validation, retries, timing and failure marking, so an operation only has to know
/// how to turn the prepared source into bytes.
/// </summary>
public interface IMediaEditOperationExecutor
{
    MediaEditOperationKind Kind { get; }

    Task<MediaEditRunOutput> ExecuteAsync(
        MediaEditRunPlan plan, Stream source, CancellationToken cancellationToken = default);
}

/// <summary>Selects the executor for an operation kind. A missing executor fails fast — never a default.</summary>
public sealed class MediaEditOperationExecutorResolver
{
    private readonly IReadOnlyList<IMediaEditOperationExecutor> _executors;

    public MediaEditOperationExecutorResolver(IEnumerable<IMediaEditOperationExecutor> executors)
        => _executors = executors.ToList();

    public IMediaEditOperationExecutor Resolve(MediaEditOperationKind kind)
        => _executors.FirstOrDefault(executor => executor.Kind == kind)
            ?? throw new InvalidOperationException(
                $"No media edit operation executor is registered for operation kind '{kind}'.");
}

/// <summary>Deterministic crop. Reaches no model at all.</summary>
public sealed class CropOperationExecutor : IMediaEditOperationExecutor
{
    private readonly IImageCropEngine _cropEngine;

    public CropOperationExecutor(IImageCropEngine cropEngine) => _cropEngine = cropEngine;

    public MediaEditOperationKind Kind => MediaEditOperationKind.Crop;

    public async Task<MediaEditRunOutput> ExecuteAsync(
        MediaEditRunPlan plan, Stream source, CancellationToken cancellationToken = default)
    {
        var crop = plan.Operation.Crop
            ?? throw new InvalidOperationException("A crop run requires its crop parameters.");

        var bytes = await _cropEngine.CropAsync(crop, source, cancellationToken);
        return new MediaEditRunOutput(bytes, MediaEditOperationKind.Crop);
    }
}

/// <summary>
/// ComfyUI upscale followed by a Lanczos scale down to the configured long edge. This operation DOES reach
/// the local ComfyUI endpoint, so it resolves the configured editor endpoint and refuses a non-ComfyUI one
/// instead of guessing a URL.
/// </summary>
public sealed class EnhanceOperationExecutor : IMediaEditOperationExecutor
{
    private readonly IImageEditorModelResolver _models;
    private readonly IImageUpscaleClient _upscaler;
    private readonly IImageResizeEngine _resizer;
    private readonly ILogger<EnhanceOperationExecutor> _logger;

    public EnhanceOperationExecutor(
        IImageEditorModelResolver models,
        IImageUpscaleClient upscaler,
        IImageResizeEngine resizer,
        ILogger<EnhanceOperationExecutor> logger)
    {
        _models = models;
        _upscaler = upscaler;
        _resizer = resizer;
        _logger = logger;
    }

    public MediaEditOperationKind Kind => MediaEditOperationKind.Enhance;

    public async Task<MediaEditRunOutput> ExecuteAsync(
        MediaEditRunPlan plan, Stream source, CancellationToken cancellationToken = default)
    {
        var enhance = plan.Operation.Enhance
            ?? throw new InvalidOperationException("An enhance run requires its enhance parameters.");
        enhance.Validate();

        var endpoint = await _models.ResolveAsync(cancellationToken);
        if (endpoint.ImageProtocol != ImageProtocol.ComfyUi)
        {
            throw new InvalidOperationException(
                "Enhancing runs the ComfyUI upscale workflow, but the configured image editor model uses " +
                $"'{endpoint.ImageProtocol}'. Configure a local ComfyUI editor endpoint before enhancing.");
        }

        var upscaled = await _upscaler.UpscaleAsync(
            endpoint, enhance.UpscalerModelName, source, "enhance-source.png", cancellationToken);
        var resized = await _resizer.ScaleToLongEdgeAsync(upscaled, enhance.TargetLongEdge, cancellationToken);

        _logger.LogInformation(
            "Enhance completed: Upscaler={Upscaler}, TargetLongEdge={TargetLongEdge}, UpscaledBytes={UpscaledBytes}, FinalBytes={FinalBytes}",
            enhance.UpscalerModelName, enhance.TargetLongEdge, upscaled.Length, resized.Length);

        return new MediaEditRunOutput(resized, MediaEditOperationKind.Enhance);
    }
}
