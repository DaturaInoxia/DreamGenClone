using Microsoft.Extensions.Logging;
using System.Text;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Orchestrates behavioral prompt injections through a priority-sorted loop over registered
/// <see cref="IPromptInjector"/> implementations. Replaces the ~1100-line procedural pipeline
/// in <c>BuildPromptAsync</c> with an auditable, extensible, and testable coordinator.
/// </summary>
public sealed class SceneDirectionCoordinator
{
    private readonly List<IPromptInjector> _injectors;
    private readonly ILogger<SceneDirectionCoordinator> _logger;

    public SceneDirectionCoordinator(
        IEnumerable<IPromptInjector> injectors,
        ILogger<SceneDirectionCoordinator> logger)
    {
        _injectors = injectors.OrderBy(i => i.Priority).ToList();
        _logger = logger;
    }

    /// <summary>
    /// Builds the behavioral inject text for a prompt. Iterates registered injectors in priority
    /// order, calling <see cref="IPromptInjector.ShouldFire"/> and appending
    /// <see cref="IPromptInjector.BuildText"/> for each that fires.
    /// Exceptions from injectors propagate per the fail-fast contract (FR-015).
    /// </summary>
    public string BuildPrompt(PromptInjectionContext context)
    {
        var sb = new StringBuilder();
        var firingSequence = new List<string>();

        foreach (var injector in _injectors)
        {
            if (injector.ShouldFire(context))
            {
                var text = injector.BuildText(context);
                sb.Append(text);
                firingSequence.Add($"{injector.Id}(p{injector.Priority})");
            }
        }

        _logger.LogInformation(
            "Coordinator built prompt: SessionId={SessionId} Phase={Phase} " +
            "PositionInTurn={PositionInTurn} Intent={Intent} Actor={ActorName} " +
            "ActiveThemeId={ActiveThemeId} Pacing={Pacing} TimeShift={TimeShift} " +
            "Deepening={Deepening} " +
            "FiringSequence={FiringSequence}",
            context.Session.Id,
            context.Phase,
            context.PositionInTurn,
            context.Intent,
            context.ActorName,
            context.ActiveTheme?.Id ?? "(none)",
            context.SceneDirection.Pacing,
            context.SceneDirection.TimeShift,
            context.SceneDirection.Deepening,
            string.Join(" -> ", firingSequence));

        return sb.ToString();
    }
}