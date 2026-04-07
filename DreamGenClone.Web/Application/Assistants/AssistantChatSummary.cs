namespace DreamGenClone.Web.Application.Assistants;

public sealed class AssistantChatSummary
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = "New Chat";
    public DateTime ModifiedAt { get; init; }
}
