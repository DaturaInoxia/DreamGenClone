using System.Text.Json;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public static class SceneImageProductionSettingsContract
{
    public static string Serialize(SceneImageStudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(settings.ImageSize))
            throw new InvalidOperationException("Phase 2 production image size is required.");

        var dimensions = settings.ImageSize.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (dimensions.Length != 2
            || !int.TryParse(dimensions[0], out var width) || width < 1
            || !int.TryParse(dimensions[1], out var height) || height < 1)
        {
            throw new InvalidOperationException($"Phase 2 production image size '{settings.ImageSize}' must be formatted as width x height.");
        }

        if (!settings.Steps.HasValue || settings.Steps.Value is < 30 or > 40)
            throw new InvalidOperationException("Phase 2 SDXL production setting 'steps' must be an integer from 30 through 40.");
        if (!settings.Cfg.HasValue || double.IsNaN(settings.Cfg.Value) || double.IsInfinity(settings.Cfg.Value)
            || settings.Cfg.Value is < 3.5 or > 5)
        {
            throw new InvalidOperationException("Phase 2 BigLust production setting 'guidance' must be a finite number from 3.5 through 5.");
        }
        if (string.IsNullOrWhiteSpace(settings.SamplerName))
            throw new InvalidOperationException("Phase 2 production setting 'sampler' is required.");
        if (string.IsNullOrWhiteSpace(settings.Scheduler))
            throw new InvalidOperationException("Phase 2 production setting 'scheduler' is required.");

        return JsonSerializer.Serialize(new
        {
            width,
            height,
            steps = settings.Steps.Value,
            guidance = settings.Cfg.Value,
            sampler = settings.SamplerName.Trim(),
            scheduler = settings.Scheduler.Trim(),
            negativePrompt = settings.NegativePrompt ?? string.Empty,
            seed = settings.Seed ?? Random.Shared.NextInt64(0, int.MaxValue)
        });
    }
}
