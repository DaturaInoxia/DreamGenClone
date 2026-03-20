namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class RolePlayInteraction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public InteractionType InteractionType { get; set; }

    public string ActorName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
