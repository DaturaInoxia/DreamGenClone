using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Injectors;

/// <summary>
/// Injects the Active Adaptive Theme Contract block and phase guidance prose.
/// Theme-controlled — reads context.Phase for data-selection (documented, justified).
/// Fires when ActiveTheme is configured.
/// </summary>
public sealed class ThemeContractInjector : IPromptInjector
{
    public string Id => "theme-contract";
    public int Priority => 30;

    public bool ShouldFire(PromptInjectionContext context)
        => context.ActiveTheme is not null && (context.PhaseGuidanceLines.Count > 0 || context.PhaseDirectiveLines.Count > 0);

    public string BuildText(PromptInjectionContext context)
    {
        var sb = new System.Text.StringBuilder();
        var theme = context.ActiveTheme!;
        sb.AppendLine();
        sb.AppendLine($"### Active Adaptive Theme: {theme.Label} ({theme.Id})");
        sb.AppendLine();
        sb.AppendLine("Phase Guidance:");
        foreach (var line in context.PhaseGuidanceLines)
        {
            sb.AppendLine($"- {line}");
        }
        if (context.PhaseDirectiveLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Theme Directives:");
            foreach (var line in context.PhaseDirectiveLines)
            {
                sb.AppendLine($"- {line}");
            }
        }

        return sb.ToString();
    }
}
