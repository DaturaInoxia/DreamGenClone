using DreamGenClone.Application.Abstractions;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Resolves the model that drafts a character's body card from its template (B-122) — the
/// <c>RolePlayCharacterBodyCardDraft</c> function, configured in Model Manager. It is a SEPARATE function from the
/// scene-beat analyzer (the body card is a synchronous, operator-triggered call), so nothing is inferred from
/// another function's configuration, and a missing assignment fails fast naming where to set it.
/// </summary>
public interface ICharacterBodyCardDraftModelResolver
{
    /// <exception cref="ModelResolutionException">
    /// When the function has no model, the model or provider is unavailable, or the configured values are invalid.
    /// </exception>
    Task<ResolvedStructuredTextFunction> ResolveAsync(CancellationToken cancellationToken = default);
}
