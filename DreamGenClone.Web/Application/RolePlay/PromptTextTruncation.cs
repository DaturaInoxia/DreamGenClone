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
        // Truncation disabled — full prompt stored for diagnostic accuracy.
        // Previous implementation trimmed interaction history block between
        // markers that no longer exist in the current prompt format.
        return fullPrompt ?? string.Empty;
    }
}
