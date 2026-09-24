using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Drives the character identity build state machine (B-121): the target kind's step order, explicit skip,
/// resume from the first incomplete step, per-step re-run, and fail-fast on out-of-order requests,
/// missing input artifacts and missing output artifacts.
/// </summary>
public interface ICharacterIdentityBuildService
{
    /// <summary>
    /// Starts a build for one target kind. The kind selects the step plan, so this is the only thing a new
    /// pipeline shape changes: the machinery below walks whatever steps the plan names.
    /// </summary>
    Task<CharacterIdentityBuild> CreateBuildAsync(
        string characterProfileId,
        string? batchId,
        CharacterIdentityTargetKind targetKind,
        CancellationToken cancellationToken = default);

    Task<CharacterIdentityBuild?> GetBuildAsync(string buildId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityBuild>> ListBuildsAsync(
        string characterProfileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterIdentityBuildStepRecord>> ListStepsAsync(
        string buildId, CancellationToken cancellationToken = default);

    Task<CharacterIdentityBuild> CompleteStepAsync(
        string buildId,
        CharacterIdentityBuildStep step,
        string? inputArtifactId,
        string outputArtifactId,
        string? resolvedPromptText = null,
        string? resolvedModelId = null,
        bool mirrorDerived = false,
        CancellationToken cancellationToken = default);

    Task<CharacterIdentityBuild> FailStepAsync(
        string buildId, CharacterIdentityBuildStep step, string failureReason, CancellationToken cancellationToken = default);

    Task<CharacterIdentityBuild> SkipStepAsync(
        string buildId, CharacterIdentityBuildStep step, CancellationToken cancellationToken = default);

    Task<CharacterIdentityBuild> ReRunStepAsync(
        string buildId, CharacterIdentityBuildStep step, CancellationToken cancellationToken = default);

    Task<CharacterIdentityBuild> SetFrontContainerAsync(
        string buildId, string frontContainerAssetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the image the user approved in Panel B as the build's canonical front — the de-clothed /
    /// cropped / enhanced result the later steps work from. This is a build-level choice, not a step
    /// transition: the Front step is long since complete by the time an edit result exists, so nothing
    /// here moves the pipeline or re-opens an earlier step.
    /// </summary>
    Task<CharacterIdentityBuild> SetCanonicalFrontAsync(
        string buildId, string imageId, CancellationToken cancellationToken = default);
}
