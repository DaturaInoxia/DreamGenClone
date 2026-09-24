namespace DreamGenClone.Domain.RolePlay;

/// <summary>Which family's rules a prompt is being validated against.</summary>
public enum BodyPromptFamily
{
    Sdxl = 1,
    Pony = 2
}

/// <summary>One structural problem found in a compiled prompt.</summary>
/// <param name="Code">Stable identifier, so a test can assert the specific defect rather than a message substring.</param>
public sealed record BodyPromptFinding(string Code, string Message);

/// <summary>
/// Structural validation for a compiled body-reference prompt.
///
/// The canonical compiler standards (§0 rule 5) require validation to be STRUCTURAL — "length caps, forbidden
/// tokens, required components… never 'I ran it and it looked fine'". This is that check, and it is the reason the
/// operator's prompt below was able to reach a model while being wrong in three separate ways.
///
/// The rules are family-specific because the families genuinely differ:
/// <list type="bullet">
/// <item><b>Pony ignores "no X" in the positive</b> (validated rule 9). A negation is not a weak instruction to
/// Pony, it is an ignored token — so on the Pony path a negation is a hard defect, not a style preference. The
/// SDXL path has no such rule, so the same words are not flagged there.</item>
/// <item><b>No names, either family.</b> §2.3 rule 2: a text-to-image model cannot map a name to a person. The
/// validator cannot guess what a name looks like, so the caller supplies the tokens that must not appear (the
/// character's name, relations) — that keeps the check honest instead of heuristic.</item>
/// </list>
/// </summary>
public static class BodyPromptStructureValidator
{
    /// <summary>The documented SDXL ceiling (§2.2: ~75 tokens / ~600–800 characters).</summary>
    public const int SdxlMaxChars = 800;

    /// <summary>The validated Pony ceiling (rule: the ENTIRE prompt under 800 characters / ~40 tags).</summary>
    public const int PonyMaxChars = 800;

    /// <summary>The token codes, exposed so tests name defects rather than duplicating string literals.</summary>
    public const string EmptyPrompt = "empty-prompt";
    public const string EmptyJoin = "empty-join";
    public const string OverLength = "over-length";
    public const string NegationInPositive = "negation-in-positive";
    public const string ForbiddenToken = "forbidden-token";

    private static readonly string[] NegationMarkers =
    [
        "no ", "not ", "n't ", "without ", "never ", "none ", "free of ", "lacking "
    ];

    /// <summary>A comma, optional whitespace, another comma — an empty join however the assembler spaced it.</summary>
    private static readonly System.Text.RegularExpressions.Regex EmptyJoinPattern =
        new(@",\s*,", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static IReadOnlyList<BodyPromptFinding> Validate(
        string? prompt,
        BodyPromptFamily family,
        IReadOnlyList<string>? forbiddenTokens = null)
    {
        var findings = new List<BodyPromptFinding>();
        var text = prompt ?? string.Empty;
        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            findings.Add(new BodyPromptFinding(EmptyPrompt, "The compiled prompt is empty."));
            return findings;
        }

        // An empty join is a raw assembly defect: it means a value was concatenated with no value after it, which
        // is how "left calve,," reached the model. The whitespace-tolerant form matters — ", ," is the same defect
        // and is what a join produces when the missing value is an empty string rather than a missing element.
        if (EmptyJoinPattern.IsMatch(trimmed)
            || trimmed.StartsWith(',')
            || trimmed.EndsWith(','))
        {
            findings.Add(new BodyPromptFinding(
                EmptyJoin,
                "The prompt contains an empty join (',' with no value beside it), so a fact was concatenated with "
                + "nothing. Fix the composition rather than the punctuation."));
        }

        var cap = family == BodyPromptFamily.Pony ? PonyMaxChars : SdxlMaxChars;
        if (trimmed.Length > cap)
        {
            findings.Add(new BodyPromptFinding(
                OverLength,
                $"The prompt is {trimmed.Length} characters; the {family} ceiling is {cap}."));
        }

        if (family == BodyPromptFamily.Pony)
        {
            // Case-insensitive whole-marker scan. Pony does not act on a negation in the positive (rule 9), so the
            // token is inert: it consumes attention and expresses nothing.
            var lowered = " " + trimmed.ToLowerInvariant() + " ";
            foreach (var marker in NegationMarkers)
            {
                if (lowered.Contains(marker, StringComparison.Ordinal))
                {
                    findings.Add(new BodyPromptFinding(
                        NegationInPositive,
                        $"The Pony prompt contains a negation ('{marker.Trim()}'). Pony IGNORES \"no X\" in the "
                        + "positive (validated rule 9): express the absence by omitting the token, and put anything "
                        + "that must be suppressed in the negative prompt."));
                    break;
                }
            }
        }

        if (forbiddenTokens is not null)
        {
            foreach (var token in forbiddenTokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (ContainsWholeWord(trimmed, token.Trim()))
                {
                    findings.Add(new BodyPromptFinding(
                        ForbiddenToken,
                        $"The prompt contains the name '{token.Trim()}'. A text-to-image model cannot map a name to "
                        + "a person (§2.3 rule 2): describe the subject by physical appearance instead."));
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// Throws naming EVERY finding at once. Failing on the first defect would make prompt assembly a game of
    /// whack-a-mole; the operator should see everything wrong in one pass.
    /// </summary>
    public static void RequireValid(
        string? prompt,
        BodyPromptFamily family,
        IReadOnlyList<string>? forbiddenTokens = null)
    {
        var findings = Validate(prompt, family, forbiddenTokens);
        if (findings.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The compiled {family} body prompt is structurally invalid and will not be sent to a model:\n- "
            + string.Join("\n- ", findings.Select(finding => $"[{finding.Code}] {finding.Message}")));
    }

    /// <summary>Whole-word, case-insensitive containment, so "Becky" is caught but "beckoning" is not.</summary>
    private static bool ContainsWholeWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + word.Length;
            var afterOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
