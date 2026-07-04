namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects the Intensity Writing Contract: "This governs WRITING STYLE and EXPLICITNESS LEVEL only."
/// Intensity controls HOW content is written, never WHAT can happen narratively.
/// Engine-owned structural inject — always fires.
/// </summary>
public sealed class IntensityContractInjector : IPromptInjector
{
    public string Id => "intensity-contract";
    public int Priority => 50;

    public bool ShouldFire(PromptInjectionContext context)
        => true;

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Intensity Writing Contract:");
        sb.AppendLine("- This governs WRITING STYLE and EXPLICITNESS LEVEL only.");
        sb.AppendLine("- It does NOT override Phase Guidance.");
        sb.AppendLine("- Phase Guidance specifies WHAT beats must occur; intensity specifies HOW they are written.");
        return sb.ToString();
    }
}
