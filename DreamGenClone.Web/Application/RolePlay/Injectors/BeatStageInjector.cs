using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects Beat Stage Context (episodic climax beat hints).
/// Fires when BeatScope is not Single (episodic beat style is active).
/// Theme-controlled via [BeatStyle:episodic] marker (resolved by SceneDirectionResolver).
/// </summary>
public sealed class BeatStageInjector : IPromptInjector
{
    public string Id => "beat-stage";
    public int Priority => 90;

    public bool ShouldFire(PromptInjectionContext context)
        => context.SceneDirection.BeatScope == BeatScope.Extended;

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Beat Stage Context:");
        sb.AppendLine($"- Current beat scope: {context.SceneDirection.BeatScope}.");
        sb.AppendLine("- Stay present in the current moment — deepen sensory and emotional detail.");
        return sb.ToString();
    }
}
