using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The Validate step of the Character Identity pipeline (B-121 Phase D): measure the eye level of
/// the selected front with the approved eye-validation tool, gate advancement on the configured
/// threshold, and support a recorded manual override (FR21-012..015).
/// </summary>
public interface ICharacterIdentityValidationService
{
    /// <summary>Read the persisted gate state without running the tool.</summary>
    Task<CharacterIdentityValidationResult> GetGateAsync(
        string buildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Measure the selected front image with the configured interpreter, persist the values and the
    /// raw tool output, and return the gate verdict. Fails fast naming missing configuration.
    /// </summary>
    Task<CharacterIdentityValidationResult> MeasureAsync(
        string buildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Measure ONE image. The result is stored on that image, and an image that fails the gate is rejected
    /// outright (<see cref="SceneAssetCandidateDecision.Rejected"/>), because the verdict belongs to the
    /// image rather than to the build. This is the manual re-check; it needs no build.
    /// </summary>
    Task<SceneAssetImageValidation> MeasureImageAsync(
        string imageId, CancellationToken cancellationToken = default);

    /// <summary>Record a manual visual override (reason + author). Required when no face mesh is found.</summary>
    Task<CharacterIdentityValidationResult> RecordOverrideAsync(
        string buildId, string reason, string author, CancellationToken cancellationToken = default);

    /// <summary>Complete the Validate step. Fails fast naming the block reason when the gate forbids it.</summary>
    Task<CharacterIdentityValidationResult> AdvanceAsync(
        string buildId, CancellationToken cancellationToken = default);
}
