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

        // ── Current scene: full name + full description ──
        var currentLocationName = !string.IsNullOrWhiteSpace(currentScene)
            ? currentScene.Trim()
            : context.Scenario.DefaultStartingLocationName;

        if (!string.IsNullOrWhiteSpace(currentLocationName))
        {
            sb.AppendLine($"  Scene: {currentLocationName}");

            // Find the full description for the current location from the scenario locations list.
            var currentLocationData = scenario.Locations
                .FirstOrDefault(l => string.Equals(l.Name, currentLocationName, StringComparison.OrdinalIgnoreCase));
            if (currentLocationData is not null && !string.IsNullOrWhiteSpace(currentLocationData.Description))
            {
                sb.AppendLine($"  {currentLocationData.Description!.Trim()}");
            }
        }
        else
        {
            sb.AppendLine("  Scene: Unknown");
        }

        // ── Other locations: full name + full description ──
        var otherLocations = scenario.Locations
            .Where(l => !string.IsNullOrWhiteSpace(l.Name) &&
                        !string.Equals(l.Name.Trim(), currentLocationName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (otherLocations.Count > 0)
        {
            sb.AppendLine("  Other locations in this world:");
            foreach (var loc in otherLocations)
            {
                sb.AppendLine($"    - {loc.Name!.Trim()}");
                if (!string.IsNullOrWhiteSpace(loc.Description))
                {
                    sb.AppendLine($"      {loc.Description!.Trim()}");
                }
            }
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        // Keep the header + current scene + description. Drop other locations first.
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

        // Keep current scene + description lines until we run out of budget.
        for (var i = 1; i < lines.Length && remaining > 0; i++)
        {
            var line = lines[i].TrimEnd('\r');
            // Stop at the "Other locations" section — drop it first under trim pressure.
            if (line.StartsWith("  Other locations", StringComparison.OrdinalIgnoreCase))
                break;

            if (line.Length + Environment.NewLine.Length <= remaining)
            {
                sb.AppendLine(line);
                remaining -= line.Length + Environment.NewLine.Length;
            }
            else if (remaining > 20)
            {
                sb.Append(line[..remaining]);
                remaining = 0;
            }
        }

        var result = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(result) ? text[..Math.Min(maxChars, text.Length)] : result;
    }
}
