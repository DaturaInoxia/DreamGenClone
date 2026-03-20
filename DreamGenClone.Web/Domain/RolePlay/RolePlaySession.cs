namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class RolePlaySession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Title { get; set; } = "Untitled Role-Play";

    public string? ScenarioId { get; set; }

    public RolePlaySessionStatus Status { get; set; } = RolePlaySessionStatus.NotStarted;

    public BehaviorMode BehaviorMode { get; set; } = BehaviorMode.TakeTurns;

    public string? ParentSessionId { get; set; }

    /// <summary>The POV persona name ("You" perspective). Defaults to "You".</summary>
    public string PersonaName { get; set; } = "You";

    /// <summary>Description/content of the POV persona (from template or manual).</summary>
    public string PersonaDescription { get; set; } = string.Empty;

    /// <summary>Optional link to the Persona template this was sourced from.</summary>
    public string? PersonaTemplateId { get; set; }

    public List<RolePlayInteraction> Interactions { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}
