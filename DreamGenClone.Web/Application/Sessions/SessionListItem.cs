namespace DreamGenClone.Web.Application.Sessions;

public sealed class SessionListItem
{
    public string Id { get; set; } = string.Empty;

    public string SessionType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int InteractionCount { get; set; }

    public DateTime LastUpdatedUtc { get; set; }

    // Backward-compatible alias for existing UI usage.
    public string Name
    {
        get => Title;
        set => Title = value;
    }

    // Backward-compatible alias for existing UI usage.
    public DateTime UpdatedUtc
    {
        get => LastUpdatedUtc;
        set => LastUpdatedUtc = value;
    }
}
