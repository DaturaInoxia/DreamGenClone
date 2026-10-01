using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Applies and reverts the prompt CLAUSE a lighting or expression preset contributes, deterministically.
///
/// <para>
/// <b>Why replace, and why by tracked text.</b> Picking a second lighting condition is a statement that the first is
/// not wanted, so a second clause appended beside it is a contradiction the app created rather than one the operator
/// asked for. Replacing it requires knowing which words are the clause, and prose cannot be parsed for that - so the
/// caller passes back the EXACT text it inserted last time. Nothing is ever removed that was not first recorded
/// verbatim, which is what keeps a hand-written prompt safe from a panel guessing at its wording.
/// </para>
///
/// <para>
/// <b>The one refusal.</b> When the previously inserted clause is no longer in the prompt, the operator has edited it,
/// so there is nothing to replace. That is refused rather than appended to, because appending is the conflict this
/// exists to prevent - and it is refused by NAME so the operator knows which axis to fix.
/// </para>
/// </summary>
public static class ImagePresetClauseEditor
{
    /// <summary>
    /// The prompt with <paramref name="clause"/> as its <paramref name="axis"/> condition: the previous clause for that
    /// axis is removed first when there is one, so the result carries exactly one condition for the axis.
    /// </summary>
    public static string Apply(string prompt, string? previousClause, string clause, ImagePresetAxis axis)
    {
        if (string.IsNullOrWhiteSpace(clause))
        {
            throw new InvalidOperationException(
                $"No {axis} clause was supplied, so applying it would change nothing while looking like it had.");
        }

        var current = prompt ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(previousClause))
        {
            if (!current.Contains(previousClause, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The {axis} wording this preset would replace is no longer in the prompt, so it cannot be "
                    + $"replaced. Choose the empty option to clear it, or rewrite the {axis} text by hand, then pick "
                    + "the preset again.");
            }

            current = Remove(current, previousClause);
        }

        return string.IsNullOrWhiteSpace(current) ? clause : $"{current.TrimEnd()} {clause}";
    }

    /// <summary>
    /// The prompt without the clause. The leading space goes with it, so removing a trailing clause leaves neither a
    /// double space nor a lingering one.
    /// </summary>
    public static string Remove(string prompt, string clause)
    {
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(clause))
        {
            return prompt ?? string.Empty;
        }

        var withoutClause = prompt.Replace($" {clause}", string.Empty, StringComparison.Ordinal);
        return withoutClause.Replace(clause, string.Empty, StringComparison.Ordinal).TrimEnd();
    }
}
