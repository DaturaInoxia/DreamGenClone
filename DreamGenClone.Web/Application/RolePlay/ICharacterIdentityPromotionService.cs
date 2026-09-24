using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ICharacterIdentityPromotionService
{
    Task<CharacterIdentityPackPromotionResult> GetReadinessAsync(string buildId, CancellationToken cancellationToken = default);

    Task<CharacterIdentityPackPromotionResult> PromoteAsync(string buildId, CancellationToken cancellationToken = default);
}

/// <summary>One canonical face slot of the pack a promotion targets.</summary>
public sealed record CharacterIdentityPackPromotionView(
    CharacterIdentityAngleView? View,
    string Label,
    string ArtifactId,
    bool Ready);

/// <summary>
/// One canonical body slot of the body build being promoted: the state and view it fills (never an extended
/// view — a BodyComplete pack is defined by the 5×2 canonical matrix) and the image it would contribute.
/// </summary>
public sealed record CharacterIdentityPackPromotionBodyView(
    SceneImageReferenceBodyState State,
    SceneImageReferenceBodyView View,
    string Label,
    string ArtifactId,
    bool Ready);

/// <summary>
/// Promotion readiness for one build. A face build fills <see cref="Views"/> from its own accepted artifacts and
/// contributes no body slots; a body build fills <see cref="BodyViews"/> from its accepted body views and
/// <see cref="Views"/> from the FACE HALF of the draft pack it promotes into — a body build holds no face
/// artifacts, and a <c>BodyComplete</c> pack is a strict superset of the five face slots.
/// </summary>
public sealed record CharacterIdentityPackPromotionResult(
    bool Ready,
    string? PackId,
    CharacterImageIdentityPackScope Scope,
    IReadOnlyList<CharacterIdentityPackPromotionView> Views,
    IReadOnlyList<CharacterIdentityPackPromotionBodyView> BodyViews,
    IReadOnlyList<string> BlockingReasons);