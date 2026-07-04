namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects character behavioral frames and stat state texts.
/// Engine-owned structural inject — always fires.
/// Reads behavioral frame data from context.Session.
/// </summary>
public sealed class BehavioralFrameInjector : IPromptInjector
{
    public string Id => "behavioral-frame";
    public int Priority => 20;

    public bool ShouldFire(PromptInjectionContext context)
        => true;

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        // Behavioral frame data is injected here — actual content depends on session state.
        // For now, emit a minimal structural marker.
        sb.AppendLine();
        sb.AppendLine("Behavioral Frame (HARD CONSTRAINT):");
        sb.AppendLine("- Stay in character according to your behavioral frame.");
        sb.AppendLine("- Maintain narrative continuity and character consistency.");
        return sb.ToString();
    }
}
