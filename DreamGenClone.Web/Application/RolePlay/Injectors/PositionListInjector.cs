namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects available positions for Approaching/Climax phases.
/// Fires when session has an active scenario bound.
/// The actual position list is injected by the data-assembly pipeline (stays inline).
/// Engine-owned.
/// </summary>
public sealed class PositionListInjector : IPromptInjector
{
    public string Id => "position-list";
    public int Priority => 80;

    public bool ShouldFire(PromptInjectionContext context)
    {
        // Positions are only relevant during Approaching/Climax phases
        var phase = context.Phase;
        return string.Equals(phase, "Approaching", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase, "Climax", System.StringComparison.OrdinalIgnoreCase);
    }

    public string BuildText(PromptInjectionContext context)
    {
        return ""; // Placeholder — actual position list injected inline by data pipeline
    }
}
