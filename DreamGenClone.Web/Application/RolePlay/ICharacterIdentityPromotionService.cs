using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ICharacterIdentityPromotionService
{
    Task<CharacterIdentityPackPromotionResult> GetReadinessAsync(string buildId, CancellationToken cancellationToken = default);

    Task<CharacterIdentityPackPromotionResult> PromoteAsync(string buildId, CancellationToken cancellationToken = default);
}

public sealed record CharacterIdentityPackPromotionView(
    CharacterIdentityAngleView? View,
    string Label,
    string ArtifactId,
    bool Ready);

public sealed record CharacterIdentityPackPromotionResult(
    bool Ready,
    string? PackId,
    IReadOnlyList<CharacterIdentityPackPromotionView> Views,
    IReadOnlyList<string> BlockingReasons);