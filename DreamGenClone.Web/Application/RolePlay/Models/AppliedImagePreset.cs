using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Models;

/// <summary>
/// One lighting / expression preset that shaped a rendered image: which axis it filled, the preset KEY the operator
/// picked, and the CLAUSE that actually reached the prompt.
///
/// <para>
/// Both halves are recorded for a reason. The clause is the fact about the image — it is the text that rendered, and
/// it is what a re-apply must REPLACE. The key is the fact about the operator's choice, and it is what the round-trip
/// reselects in the picker. Recording only the clause would leave the panel showing "no preset" for an image that was
/// visibly lit by one; recording only the key would lose what actually rendered when a preset's wording has since
/// changed (B-133 stores wording in the prompt store, so the two can diverge).
/// </para>
///
/// <para>
/// Deliberately NOT parsed back out of the prompt: a clause is prose, and guessing which part of a prompt came from a
/// preset is exactly the kind of inference this codebase refuses. The render records it because it knows it.
/// </para>
/// </summary>
public sealed record AppliedImagePreset(ImagePresetAxis Axis, string Key, string Clause)
{
    /// <summary>True when both halves are present, which is what makes the record worth showing.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Clause);
}
