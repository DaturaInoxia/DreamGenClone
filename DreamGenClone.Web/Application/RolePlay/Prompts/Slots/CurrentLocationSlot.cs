using System.Text;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 7, Zone B — Current location: full detail for current scene, one-line for occupied,
/// others omitted. Trimmable (priority 5). FR-013.
/// </summary>
public sealed class CurrentLocationSlot : IPromptSlot
{
    private readonly ILogger<CurrentLocationSlot> _logger;

    public PromptSlotId Id => PromptSlotId.CurrentLocation;
    public PromptZone Zone => PromptZone.B;
    public int Order => 7;
    public bool IsTrimEligible => true;

    public CurrentLocationSlot(ILogger<CurrentLocationSlot> logger)
    {
        _logger = logger;
    }

    public bool ShouldWrite(PromptBuildContext context) => true;

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var session = context.Session;
        var currentScene = session.AdaptiveState?.CurrentSceneLocation;
        var scenario = context.Scenario;

        var sb = new StringBuilder();
        sb.AppendLine("Current Location:");

        if (!string.IsNullOrWhiteSpace(currentScene))
        {
            sb.AppendLine($"  Scene: {currentScene.Trim()}");
        }
        else if (!string.IsNullOrWhiteSpace(context.Scenario.DefaultStartingLocationName))
        {
            sb.AppendLine($"  Scene: {context.Scenario.DefaultStartingLocationName}");
        }
        else
        {
            sb.AppendLine("  Scene: Unknown");
        }

        // Occupied locations: one-line summaries for other known locations.
        var locationNames = scenario.LocationNames;
        if (locationNames.Count > 0)
        {
            var otherLocations = locationNames
                .Where(l => !string.IsNullOrWhiteSpace(l) &&
                            !string.Equals(l.Trim(), currentScene?.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (otherLocations.Count > 0)
            {
                sb.AppendLine("  Other available locations:");
                foreach (var loc in otherLocations)
                {
                    sb.AppendLine($"    - {loc.Trim()}");
                }
            }
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        // Keep the header + current scene only, drop other locations.
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var remaining = maxChars;

        // Always keep header.
        if (lines.Length > 0)
        {
            var headerLine = lines[0].TrimEnd('\r');
            sb.AppendLine(headerLine);
            remaining -= headerLine.Length + Environment.NewLine.Length;
        }

        // Keep current scene line if it fits.
        if (lines.Length > 1 && remaining > 0)
        {
            var sceneLine = lines[1].TrimEnd('\r');
            if (sceneLine.Length <= remaining)
            {
                sb.Append(sceneLine);
            }
            else if (remaining > 20)
            {
                sb.Append(sceneLine[..remaining]);
            }
        }

        var result = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(result) ? text[..Math.Min(maxChars, text.Length)] : result;
    }
}
