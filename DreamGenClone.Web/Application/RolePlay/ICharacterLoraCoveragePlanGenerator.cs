using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>What a coverage plan needs to know before it can be projected.</summary>
/// <param name="CharacterProfileId">The character template the identity belongs to.</param>
/// <param name="IdentityPackId">The approved pack the references are read from.</param>
/// <param name="IdentityPackVersion">That pack's version, recorded so the plan is traceable to exact assets.</param>
/// <param name="IdentityPackScope">
/// The pack's declared scope. Only a body-complete pack can back a LoRA dataset: a face-only pack has no
/// unclothed body reference, so half the matrix would render with nothing to condition on.
/// </param>
/// <param name="TriggerToken">The token the whole identity binds to.</param>
/// <param name="TargetModelFamily">The model family the dataset is intended for.</param>
public sealed record CoveragePlanRequest(
    string CharacterProfileId,
    string IdentityPackId,
    int IdentityPackVersion,
    CharacterImageIdentityPackScope IdentityPackScope,
    string TriggerToken,
    string TargetModelFamily);

/// <summary>
/// Projects a character's approved identity pack onto the coverage matrix: the list of images the training
/// set needs, one cell each, with a fixed seed and the axes that vary.
/// <para>
/// It plans. It renders nothing and dispatches nothing — the operator shoots the cells one at a time in the
/// workspace, and a "generate them all" action must never exist.
/// </para>
/// </summary>
public interface ICharacterLoraCoveragePlanGenerator
{
    Task<CoveragePlan> GenerateAsync(CoveragePlanRequest request, CancellationToken cancellationToken = default);
}
