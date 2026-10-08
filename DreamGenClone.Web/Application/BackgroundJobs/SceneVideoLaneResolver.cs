using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Web.Application.BackgroundJobs;

/// <summary>
/// Reads the video lane's queue bounds from the <c>RolePlaySceneVideo</c> function default.
/// </summary>
/// <remarks>
/// Every value is persisted and UI-backed (Model Manager, "Function Defaults"); nothing is defaulted here. The
/// validation is the function-default contract's own method, so the composer's save path and this resolver cannot
/// disagree about what a valid video configuration is.
///
/// <para>
/// The provider timeout comes from the resolved video model's provider, and only feeds the structured-text watchdog
/// the video lane never arms; the render's own budget is the model's configured <c>RenderTimeoutSeconds</c>, which
/// the client enforces.
/// </para>
/// </remarks>
public sealed class SceneVideoLaneResolver : ISceneVideoLaneResolver
{
    private readonly IFunctionDefaultRepository _functionDefaultRepository;
    private readonly IModelResolutionService _modelResolutionService;

    public SceneVideoLaneResolver(
        IFunctionDefaultRepository functionDefaultRepository,
        IModelResolutionService modelResolutionService)
    {
        _functionDefaultRepository = functionDefaultRepository;
        _modelResolutionService = modelResolutionService;
    }

    public async Task<DurableLaneSettings> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var functionDefault = await _functionDefaultRepository
            .GetByFunctionAsync(AppFunction.RolePlaySceneVideo, cancellationToken)
            ?? throw new ModelResolutionException(
                $"No model is assigned to '{AppFunction.RolePlaySceneVideo}', so the video lane has no configured "
                + "bounds. Assign a video model in Model Manager (/model-manager) or run the DbQuery "
                + "'h3-video-configure' command.");

        var validationError = functionDefault.ValidateSceneVideoConfiguration();
        if (validationError is not null)
        {
            throw new ModelResolutionException(
                $"The '{AppFunction.RolePlaySceneVideo}' configuration is invalid: {validationError} "
                + "Fix it in Model Manager (/model-manager).");
        }

        var model = await _modelResolutionService.ResolveVideoModelAsync(functionDefault.ModelId, cancellationToken);

        return new DurableLaneSettings(
            Lane: DurableJobLane.VideoRender,
            MaxConcurrentJobs: functionDefault.MaxConcurrentJobs!.Value,
            LeaseSeconds: functionDefault.DurableJobLeaseSeconds!.Value,
            PollIntervalMilliseconds: functionDefault.DurableJobPollIntervalMilliseconds!.Value,
            RetryDelaysSeconds: functionDefault.GetSceneVideoRetryDelaysSeconds(),
            ProviderTimeoutSeconds: model.ProviderTimeoutSeconds);
    }
}
