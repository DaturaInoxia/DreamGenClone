namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Recognises a prompt compiler that answered with a REFUSAL instead of a prompt.
/// </summary>
/// <remarks>
/// <para>
/// A prompt-compile model is asked to translate an operator's intent into an image prompt. An aligned model can answer
/// with prose addressed to the operator instead ("I can't write this one. …"), and every parse path in this app
/// accepts plain text as the prompt — so the refusal was stored as <c>OutputPrompt</c> and RENDERED. Measured on
/// SceneImages a597ba82 / aef9f30c / 99fedb9d (2026-10-18): three renders of the refusal's own suggested alternative,
/// while the operator believed the prompt had been compiled.
/// </para>
/// <para>
/// Detection is deliberately conservative: a compiled prompt is a third-person description of a picture, so a response
/// that OPENS with a first-person refusal, or carries a refusal phrase early, is not a prompt. Checking only the head of
/// the response is what keeps a legitimate prompt that happens to contain the word "can't" from being misread.
/// </para>
/// </remarks>
public static class SceneImagePromptRefusal
{
    /// <summary>How much of the response is inspected for a first-person refusal.</summary>
    private const int HeaderChars = 320;

    /// <summary>How much of the response is inspected for a refusal phrase.</summary>
    private const int PhraseChars = 600;

    private static readonly string[] Openers =
    [
        "i can't", "i cannot", "i won't", "i will not", "i'm sorry", "i am sorry", "sorry",
        "i'm unable", "i am unable", "i'm not able", "i am not able", "i'd rather not",
        "i must decline", "i have to decline", "i'm not going to", "i am not going to",
        "as an ai", "that's not something i", "that is not something i", "unfortunately, i"
    ];

    private static readonly string[] Phrases =
    [
        "i can't write", "i cannot write", "i can't produce", "i cannot produce",
        "i won't produce", "i won't write", "not something i'll produce", "not something i will produce",
        "i'm unable to write", "i am unable to write", "i won't be able to", "i can't help with"
    ];

    /// <summary>Whether <paramref name="text"/> is a refusal addressed to the operator rather than a prompt.</summary>
    public static bool IsRefusal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalised = text.Trim().TrimStart('"', '\'', '“', '‘', '*', '-', ' ').ToLowerInvariant()
            .Replace('’', '\'').Replace('“', '"').Replace('”', '"');

        var header = normalised.Length > HeaderChars ? normalised[..HeaderChars] : normalised;
        foreach (var opener in Openers)
        {
            if (header.StartsWith(opener, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var body = normalised.Length > PhraseChars ? normalised[..PhraseChars] : normalised;
        foreach (var phrase in Phrases)
        {
            if (body.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Throws when the compiler answered with a refusal, so the prompt record fails with an actionable message instead
    /// of rendering the refusal text. Returns the text unchanged when it is a prompt.
    /// </summary>
    public static string EnsureIsPrompt(string? text)
    {
        if (!IsRefusal(text))
        {
            return text ?? string.Empty;
        }

        var quoted = (text ?? string.Empty).Trim();
        if (quoted.Length > 180)
        {
            quoted = quoted[..180] + "…";
        }

        throw new InvalidOperationException(
            "The prompt compiler returned a refusal instead of an image prompt, so nothing was rendered. The model "
            + "configured for this prompt function declined the operator's intent — an aligned or filtered model "
            + "cannot be used for this content. Choose a different model for the function in Model Manager "
            + "(/model-manager), or change the intent. Refusal began: \"" + quoted + "\"");
    }
}
