using System.Text;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The ONE owner of the preservation clause an edit carries — what it holds at input fidelity, built from the
/// compiler's own structured <c>preserve</c> list.
///
/// <para>
/// <b>Why this is not written by the model (2026-10-15).</b> The vendor's edit guidance requires preservation to be
/// stated affirmatively, and the compiler used to ask for that sentence in the prose — where it was demonstrably
/// skipped. Measured on the store: of 22 completed edits, 20 carried a compiled prompt under 200 characters (the
/// reported case was 39) and most stated no preservation at all, while the SAME attempts recorded
/// <c>preserve: ["the woman's identity"]</c> and entries like "keep the woman's clothing unchanged" in their
/// structured output. The rule the operator needs was therefore already known to the application and simply never
/// reached the instruction.
/// </para>
///
/// <para>
/// <b>One owner per half.</b> The model writes the CHANGE — which is what a vision compiler is good at, and what the
/// vendor's "lead with the operation" rule governs. The application states what is HELD, from data the schema already
/// requires, so preservation cannot depend on the model remembering to say it. The sentence is appended where the
/// compiled prompt becomes the instruction, so the stored revision carries it and the operator reads the same text
/// the editor will.
/// </para>
///
/// <para>
/// A category is never re-described: the vendor's rule is that a concrete description of something you meant to keep
/// reads as a generation instruction and drifts, so the sentence names the category and says nothing more about it.
/// </para>
/// </summary>
public static class EditInstructionPreservation
{
    /// <summary>
    /// How many recorded categories the sentence names. A cap rather than everything, because the clause is one
    /// sentence and a twenty-entry list is a second prompt: the entries arrive in the compiler's own priority order,
    /// so the ones that fit are the ones it considered most important.
    /// </summary>
    public const int MaximumCategories = 6;

    /// <summary>
    /// The instruction with its preservation sentence appended, or the instruction unchanged when the compiler
    /// recorded nothing to hold (which a Ready result cannot do — see the compiler's own terminal-state check).
    /// </summary>
    public static string AppendPreservation(string instruction, IReadOnlyList<string>? preserve)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        var sentence = SentenceFor(preserve);
        return sentence.Length == 0
            ? instruction
            : $"{instruction.TrimEnd()} {sentence}";
    }

    /// <summary>
    /// The preservation clause alone, or an empty string when the compiler recorded no categories.
    ///
    /// <para>
    /// It is built as up to two sentence shapes, because the entries arrive in two shapes and neither can be forced
    /// into the other: a bare CATEGORY ("the woman's identity", "setting") is named inside one statement about what is
    /// held, while a full STATEMENT ("keep the woman's clothing unchanged") is already a preservation sentence and is
    /// used as it stands. Wrapping a statement in the category form would read "Hold keep the woman's clothing
    /// unchanged at input fidelity".
    /// </para>
    /// </summary>
    public static string SentenceFor(IReadOnlyList<string>? preserve)
    {
        if (preserve is not { Count: > 0 })
        {
            return string.Empty;
        }

        var categories = new List<string>();
        var statements = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in preserve)
        {
            var trimmed = entry?.Trim().TrimEnd('.', ';', ',');
            if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
            {
                continue;
            }

            if (IsStatement(trimmed))
            {
                statements.Add(Capitalize(trimmed) + ".");
            }
            else
            {
                categories.Add(trimmed);
            }

            if (statements.Count + categories.Count == MaximumCategories)
            {
                break;
            }
        }

        var sentences = new List<string>(statements);
        if (categories.Count > 0)
        {
            // The article rides the LIST, not each entry: "the setting, pose and expression" is how the categories are
            // named in one breath. It is added only when the first category does not already carry a determiner, so a
            // phrase like "the woman's identity" cannot become "the the woman's identity".
            var joined = JoinClauses(categories);
            sentences.Add($"Hold {(HasDeterminer(categories[0]) ? joined : $"the {joined}")} at input fidelity.");
        }

        if (sentences.Count == 0)
        {
            return string.Empty;
        }

        // The closing sentence is the vendor's other half of the principle: the change is pushed to an unmistakable
        // degree, and everything not named stays exactly as the source shows it.
        sentences.Add("Change nothing else.");
        return string.Join(" ", sentences);
    }

    /// <summary>
    /// Whether an entry is already a preservation STATEMENT rather than a bare category — the two words the vendor's
    /// own guidance uses ("keep X unchanged", "hold X at input fidelity"). Anything else is treated as a category,
    /// which is the safe reading: it is named, never re-described.
    /// </summary>
    private static bool IsStatement(string entry)
    {
        var end = entry.IndexOfAny([' ', '\t']);
        var firstWord = (end < 0 ? entry : entry[..end]).ToLowerInvariant();
        return firstWord is "keep" or "hold" or "preserve" or "leave" or "maintain";
    }

    private static string Capitalize(string text)
        => char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// Whether a category already begins with a determiner, so the list's own article is not doubled.
    /// </summary>
    private static bool HasDeterminer(string category)
    {
        var end = category.IndexOfAny([' ', '\t']);
        var firstWord = (end < 0 ? category : category[..end]).ToLowerInvariant();
        return firstWord is "the" or "a" or "an" or "her" or "his" or "their" or "its" or "this" or "that" or "these" or "those";
    }

    /// <summary>
    /// The clauses as one list — "a, b and c" — because a comma-separated run of five entries reads as one item and
    /// stops being a statement about each of them.
    /// </summary>
    private static string JoinClauses(IReadOnlyList<string> clauses)
    {
        if (clauses.Count == 1)
        {
            return clauses[0];
        }

        var sb = new StringBuilder();
        for (var index = 0; index < clauses.Count; index++)
        {
            if (index > 0)
            {
                sb.Append(index == clauses.Count - 1 ? " and " : ", ");
            }

            sb.Append(clauses[index]);
        }

        return sb.ToString();
    }
}
