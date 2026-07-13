namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Pure function for truncating the prior interactions block within a prompt string.
/// Used to reduce storage size while preserving diagnostic value.
/// </summary>
public static class PromptTextTruncation
{
    public const int DefaultEdgeSize = 100;

    /// <summary>
    /// Trims the interaction history block between the start marker
    /// ("Session Memory below = summarized past events...") and the end marker
    /// ("Session Memory:"), keeping first N + last N characters of the middle.
    /// </summary>
    public static string TrimInteractionHistoryBlock(string fullPrompt, int edgeSize = DefaultEdgeSize)
    {
        if (string.IsNullOrEmpty(fullPrompt) || edgeSize <= 0)
            return fullPrompt;

        // Start marker: "Session Memory below = summarized past events for long-term context:"
        const string startMarker = "Session Memory below = summarized past events for long-term context:";
        var startIdx = fullPrompt.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIdx < 0)
            return fullPrompt;

        // Content starts after the start marker line
        var contentStart = fullPrompt.IndexOf('\n', startIdx);
        if (contentStart < 0)
            return fullPrompt;
        contentStart++; // skip the newline

        // End marker: the next "Session Memory:" section header
        const string endMarker = "\nSession Memory:";
        var endIdx = fullPrompt.IndexOf(endMarker, contentStart, StringComparison.Ordinal);
        if (endIdx < 0)
            return fullPrompt;

        // Extract the middle content (interaction history)
        var middle = fullPrompt.Substring(contentStart, endIdx - contentStart);

        if (middle.Length <= 2 * edgeSize)
            return fullPrompt;

        var firstPart = middle.Substring(0, edgeSize);
        var lastPart = middle.Substring(middle.Length - edgeSize);
        var replacement = firstPart + "\nREMOVED FOR BREVITY\n" + lastPart;

        return fullPrompt.Substring(0, contentStart) + replacement + fullPrompt.Substring(endIdx);
    }
}
