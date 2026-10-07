namespace DreamGenClone.Web.Application.TextEntry;

/// <summary>
/// The two halves a text box needs to be safe to type in on Blazor Server.
///
/// <para>
/// A box bound as <c>value="@field" @oninput="…field = …"</c> hands the text to the server on every keystroke, and
/// every server render writes the field's value back into the DOM. When the round trip is slower than the operator's
/// typing - which any unrelated render makes likely, and a periodic poll makes routine - the render writes an OLDER
/// value: the box is truncated and the caret is thrown to the end, so the characters typed meanwhile land after it
/// and the text arrives interleaved. This state keeps the box's DRAWN text on what the operator committed (blur or
/// Enter) while the host's live value still moves on every keystroke, so no render can write a stale value into a box
/// someone is typing in.
/// </para>
///
/// <para>
/// A private field inside a component cannot be tested at all, so the rule lives here: <see cref="Report"/> on a
/// keystroke, <see cref="Commit"/> on blur or Enter, and <see cref="AdoptHostText"/> when the host has something the
/// operator did not type (a seed, a draft load, a compiled prompt, a clear).
/// </para>
/// </summary>
public sealed class CommittedTextState
{
    /// <summary>What the box DRAWS. Moves on an adoption or a commit - deliberately never on a keystroke.</summary>
    public string Rendered { get; private set; } = string.Empty;

    /// <summary>
    /// What this box last reported to the host. Kept so the host's verbatim echo of a keystroke is not mistaken for a
    /// new value - adopting that echo would re-draw the box on every render, which is the failure this class exists
    /// to prevent.
    /// </summary>
    public string Reported { get; private set; } = string.Empty;

    /// <summary>
    /// Adopts the host's text when it is something the operator has not typed. Returns true when the drawn text moved,
    /// which is what makes a later render write it.
    /// </summary>
    public bool AdoptHostText(string? hostText)
    {
        var text = hostText ?? string.Empty;
        if (string.Equals(text, Reported, StringComparison.Ordinal))
        {
            return false;
        }

        Reported = text;
        Rendered = text;
        return true;
    }

    /// <summary>A keystroke: the host learns the text, the drawn text deliberately does not move.</summary>
    public void Report(string? text) => Reported = text ?? string.Empty;

    /// <summary>Blur or Enter: the operator's text becomes the drawn text too, so the two agree again.</summary>
    public void Commit(string? text)
    {
        Reported = text ?? string.Empty;
        Rendered = Reported;
    }
}
