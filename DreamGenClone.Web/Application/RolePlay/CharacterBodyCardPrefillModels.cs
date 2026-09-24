using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>Where a proposed body-card value came from (B-122). The two are never mixed into one proposal.</summary>
public enum CharacterBodyCardPrefillSource
{
    /// <summary>The character template's structured <c>PhysicalAttributes</c> — deterministic, no model involved.</summary>
    CharacterAttributes = 1,

    /// <summary>A draft from the configured model reading the template's description. A proposal, never a fact.</summary>
    DescriptionDraft = 2
}

/// <summary>One proposed value for one body-card field, with its provenance so the operator can judge it.</summary>
public sealed record CharacterBodyCardFieldProposal(
    CharacterBodyCardField Field,
    string Label,
    bool RequiresDecision,
    string ProposedValue,
    CharacterBodyCardPrefillSource Source,
    string Provenance)
{
    public string DescribeSource() => Source switch
    {
        CharacterBodyCardPrefillSource.CharacterAttributes => "character template",
        CharacterBodyCardPrefillSource.DescriptionDraft => "description draft",
        _ => throw new InvalidOperationException($"Unsupported body-card prefill source '{Source}'.")
    };
}

/// <summary>A card field that stayed unanswered, and the reason — never a guessed value.</summary>
public sealed record CharacterBodyCardPrefillGap(
    CharacterBodyCardField Field,
    string Label,
    bool RequiresDecision,
    string Reason);

/// <summary>
/// A prefill result. Proposals are NEVER written by the service: applying them (and only to fields the operator has
/// left empty) belongs to the card, and saving belongs to the operator's Save button.
/// </summary>
public sealed record CharacterBodyCardPrefill(
    string CharacterTemplateId,
    string CharacterName,
    CharacterBodyCardPrefillSource Source,
    IReadOnlyList<CharacterBodyCardFieldProposal> Proposals,
    IReadOnlyList<CharacterBodyCardPrefillGap> Gaps,
    string? ModelIdentifier)
{
    public bool IsEmpty => Proposals.Count == 0;

    /// <summary>
    /// The card's axis picks proposed from the character template's structured attributes — the "pulled from the
    /// template" start point for the body card. Null for the description draft: the draft answers the gated fields
    /// the description states and never invents axis picks.
    /// </summary>
    public CharacterBodyAxes? Axes { get; init; }
}
