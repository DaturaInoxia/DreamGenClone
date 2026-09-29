using System.Text.RegularExpressions;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Turns a picked preset into the text a model is actually sent (B-133).
///
/// <para>
/// The preset's detail is assembled DETERMINISTICALLY. It is not handed to the vision compiler as an intent, and
/// that is the whole point of the design: if a preset exists to carry the details that make an expression read
/// ("brows pulled down and drawn together with vertical creases between them"), then running that text through an
/// LLM which rewrites intents into instructions paraphrases away exactly the precision the operator picked, and it
/// costs a model call per edit. The free-text box keeps the compiler for anything not in the list; a preset is
/// already model-ready.
/// </para>
///
/// <para>
/// Like the LoRA cell composer, this class owns no wording at all - the details, the preserve clauses and the four
/// assembly templates are all rows in the prompt store. What it owns is one guarantee: an unfilled slot throws
/// naming itself rather than sending an instruction with <c>{Detail}</c> still in it.
/// </para>
/// </summary>
public static partial class ImagePresetInstructionComposer
{
    /// <summary>
    /// The instruction for a picked preset: the axis and mode choose the assembly template, which supplies the verb,
    /// and the detail and the preserve clause are filled into it.
    /// </summary>
    /// <param name="axis">Which axis the preset describes. It must agree with the preset key.</param>
    /// <param name="mode">
    /// <see cref="ImagePresetMode.Change"/> for an edit over an existing image, <see cref="ImagePresetMode.Condition"/>
    /// for a clause added to a render prompt that has not been rendered yet.
    /// </param>
    /// <param name="detail">The preset row's own wording, e.g. the lighting detail.</param>
    /// <param name="assemblyBody">The assembly template resolved from the store for this axis and mode.</param>
    /// <param name="preserveClause">
    /// The preserve clause for this axis. Required in <see cref="ImagePresetMode.Change"/> - an edit that does not
    /// state what to keep re-renders the person - and refused in <see cref="ImagePresetMode.Condition"/>, where there
    /// is nothing yet to preserve.
    /// </param>
    public static string Compose(
        ImagePresetAxis axis,
        ImagePresetMode mode,
        string detail,
        string assemblyBody,
        string? preserveClause = null)
    {
        var assembly = Require(assemblyBody, "The assembly template");
        var filled = Require(detail, "The preset's detail").Trim();

        var preserveRequired = mode == ImagePresetMode.Change;
        var preserve = (preserveClause ?? string.Empty).Trim();
        if (preserveRequired && preserve.Length == 0)
        {
            throw new InvalidOperationException(
                $"An edit preset needs the {ImagePresetKeys.PreserveKey(axis)} clause: an instruction that changes one "
                + "thing without stating what must stay identical re-renders the person, which is how an edit pass "
                + "loses the identity it was supposed to keep.");
        }

        if (!assembly.Contains($"{{{ImagePresetKeys.DetailSlot}}}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The assembly template '{ImagePresetKeys.AssemblyKey(axis, mode)}' has no "
                + $"{{{ImagePresetKeys.DetailSlot}}} slot, so the preset's own wording could never reach the model.");
        }

        var body = assembly.Replace($"{{{ImagePresetKeys.DetailSlot}}}", filled, StringComparison.Ordinal);
        body = body.Replace($"{{{ImagePresetKeys.PreserveSlot}}}", preserve, StringComparison.Ordinal);

        var leftover = SlotPattern().Match(body);
        if (leftover.Success)
        {
            throw new InvalidOperationException(
                $"The composition left the slot '{leftover.Value}' unfilled. The template and the values it is "
                + "composed with disagree, and an instruction is never sent with a slot still in it.");
        }

        // A condition assembly has no preserve slot by design; if one carries it, the clause would be dropped silently
        // here, so refuse instead of sending an instruction that quietly lost a requirement.
        if (!preserveRequired && assembly.Contains($"{{{ImagePresetKeys.PreserveSlot}}}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The assembly template '{ImagePresetKeys.AssemblyKey(axis, mode)}' declares "
                + $"{{{ImagePresetKeys.PreserveSlot}}}, but a '{mode}' composition has no clause to fill it with.");
        }

        return WhitespacePattern().Replace(body, " ").Trim();
    }

    private static string Require(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{label} is required.")
            : value;

    [GeneratedRegex(@"\{[A-Za-z][A-Za-z0-9]*\}")]
    private static partial Regex SlotPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespacePattern();
}
