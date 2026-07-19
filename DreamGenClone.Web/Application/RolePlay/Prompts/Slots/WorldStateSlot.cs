using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Prompts.Slots;

/// <summary>
/// Slot 4a, Zone A — Conditional world state (FR-009, B-062).
/// Fires only when <see cref="PromptBuildContext.WorldState"/> is non-null;
/// silently omitted otherwise. Never trimmed.
/// Format per GAP-5: Day N, time phase, weather, world rhythm, temporal pressure.
/// </summary>
public sealed class WorldStateSlot : IPromptSlot
{
    public PromptSlotId Id => PromptSlotId.WorldState;
    public PromptZone Zone => PromptZone.A;
    public int Order => 4;
    public bool IsTrimEligible => false;

    public bool ShouldWrite(PromptBuildContext context)
    {
        return context.WorldState is not null;
    }

    public Task<string> WriteAsync(PromptBuildContext context, CancellationToken ct)
    {
        var ws = context.WorldState!;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("World State:");

        // Day line
        var dayRange = ws.TotalDays.HasValue
            ? $"Day {ws.DayNumber} of {ws.TotalDays}"
            : $"Day {ws.DayNumber}";

        var dayOfWeek = !string.IsNullOrWhiteSpace(ws.DayOfWeek) ? $" — {ws.DayOfWeek}" : "";
        var timePhase = !string.IsNullOrWhiteSpace(ws.TimePhase) ? $". {ws.TimePhase}" : "";
        var specificTime = !string.IsNullOrWhiteSpace(ws.SpecificTime) ? $" ({ws.SpecificTime})" : "";
        sb.AppendLine($"- {dayRange}{dayOfWeek}.{timePhase}{specificTime}.");

        // Weather line
        if (!string.IsNullOrWhiteSpace(ws.WeatherCondition))
        {
            var temp = ws.TemperatureCelsius.HasValue ? $", {ws.TemperatureCelsius:F0}°C" : "";
            var humidity = !string.IsNullOrWhiteSpace(ws.HumidityDescription) ? $". {ws.HumidityDescription}" : "";
            sb.AppendLine($"- Weather: {ws.WeatherCondition}{temp}.{humidity}");
        }

        // World rhythm
        if (!string.IsNullOrWhiteSpace(ws.WorldRhythm))
        {
            sb.AppendLine($"- World rhythm: {ws.WorldRhythm}.");
        }

        // Temporal pressure
        if (!string.IsNullOrWhiteSpace(ws.TemporalPressure))
        {
            sb.AppendLine($"- Temporal pressure: {ws.TemporalPressure}.");
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public string Trim(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        // Never trimmed, but implement contractually.
        return text[..Math.Max(1, maxChars)];
    }
}
