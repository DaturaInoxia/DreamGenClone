using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Proposes values for a character's body card (B-122) so the operator does not retype what the character template
/// already says. Two sources, deliberately separate:
///
/// <list type="number">
/// <item><see cref="FromCharacterTemplateAsync"/> — deterministic, built from the template's structured
/// <c>PhysicalAttributes</c>. Nothing is inferred and no model is called.</item>
/// <item><see cref="DraftFromDescriptionAsync"/> — the configured draft model reads the template's description and
/// proposes the fields it can actually see in the text. Absent stays absent: the model may not answer "none".</item>
/// </list>
///
/// Neither writes to the card, and neither overwrites a field the operator has already answered — applying a
/// proposal is <see cref="CharacterBodyCard.TryApplyPrefill"/>, and saving stays the operator's explicit action.
/// </summary>
public interface ICharacterBodyCardPrefillService
{
    Task<CharacterBodyCardPrefill> FromCharacterTemplateAsync(
        string characterTemplateId, CancellationToken cancellationToken = default);

    Task<CharacterBodyCardPrefill> DraftFromDescriptionAsync(
        string characterTemplateId, CancellationToken cancellationToken = default);
}
