namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Picks which drafted prompt the wardrobe form should show in its Prompt box.
/// </summary>
/// <remarks>
/// This is a pure decision on purpose, and it is separated from the panel because the panel got it wrong twice for the
/// same reason: it decided from memory of the request IT made rather than from the rows that exist. A prompt is written
/// by a background job, so by the time the box is filled the job may have finished in another tab, or before a reload -
/// and a box that only fills for a draft this session asked for shows nothing after a refresh, which reads exactly like
/// "the button does nothing" (reported 2026-09-29).
///
/// The rule is therefore: the ROWS decide. Show the newest draft that actually carries a compiled prompt - the one
/// belonging to the item the form is working on when there is one - and never a row that is still being written.
/// </remarks>
public static class WardrobePromptDraftSelection
{
    /// <summary>
    /// The draft whose prompt belongs in the box, or null when no draft has been compiled yet.
    /// </summary>
    /// <param name="items">Every wardrobe item with its rows.</param>
    /// <param name="itemId">The item the form is working on, when it has one.</param>
    public static WardrobeItemImage? SelectPromptToShow(IEnumerable<WardrobeItem> items, string? itemId)
    {
        var compiled = items
            .SelectMany(item => item.Images)
            .Where(entry => entry.IsPromptDraft && !string.IsNullOrWhiteSpace(entry.Image.PromptCompilerId))
            .ToList();

        if (compiled.Count == 0)
        {
            return null;
        }

        return compiled
                .Where(entry => !string.IsNullOrWhiteSpace(itemId)
                    && string.Equals(entry.Image.AssetId, itemId, StringComparison.Ordinal))
                .OrderByDescending(entry => entry.Image.CreatedUtc)
                .FirstOrDefault()
            ?? compiled.OrderByDescending(entry => entry.Image.CreatedUtc).First();
    }
}
