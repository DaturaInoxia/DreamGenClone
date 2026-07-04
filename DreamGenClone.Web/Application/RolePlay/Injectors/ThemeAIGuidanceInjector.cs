using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects AI Guidance Notes from the active theme, including Hard Constraint sections.
/// Theme-controlled — fires when AiGuidanceNotes are present.
/// </summary>
public sealed class ThemeAIGuidanceInjector : IPromptInjector
{
    public string Id => "theme-ai-guidance";
    public int Priority => 40;

    public bool ShouldFire(PromptInjectionContext context)
        => context.AiGuidanceNotes.Count > 0;

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("### AI Guidance Notes (Theme)");
        foreach (var note in context.AiGuidanceNotes)
        {
            sb.AppendLine($"[{note.Section}] {note.Text}");
        }
        return sb.ToString();
    }
}
