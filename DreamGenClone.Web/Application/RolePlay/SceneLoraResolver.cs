using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Turns the operator's per-render scene-LoRA multi-select into the descriptors the ComfyUI graph builder
/// consumes - or refuses the render.
///
/// <para>
/// Scene LoRAs are NON-IDENTITY (unlock / act / anatomy / style) and are resolved from the persisted
/// <c>SceneLora</c> catalog, filtered to the render model's family. Identity is a separate, sibling mechanism:
/// character LoRAs come from the qualified artifacts trained against the checkpoint, and a render that selects no
/// scene LoRA returns an empty list without consulting the catalog at all, so every other render is untouched.
/// </para>
///
/// <para>
/// Every refusal below is deliberate and none of them falls back to anything. A LoRA only binds to the checkpoint
/// it was trained against, so a selection naming a LoRA from another family, a LoRA the catalog does not carry, or
/// a strength nobody chose would silently produce a different image from the one requested - and would look exactly
/// like success.
/// </para>
/// </summary>
public interface ISceneLoraResolver
{
    /// <summary>
    /// The scene LoRAs this render must apply, in the operator's chosen order. Empty means NO scene LoRA - the
    /// graph builder then emits no loader node for them.
    /// </summary>
    Task<IReadOnlyList<ResolvedSceneLora>> ResolveAsync(
        ResolvedImageModel model,
        SceneImageStudioSettings? settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same resolution from a RAW selection list, for a path that carries the selection without a studio settings
    /// object (the asset/catalog render path). One owner: the settings overload delegates here, so the two entry
    /// points cannot disagree about validation.
    /// </summary>
    Task<IReadOnlyList<ResolvedSceneLora>> ResolveAsync(
        ResolvedImageModel model,
        IReadOnlyList<SceneImageLoraSelection>? selections,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same resolution for a path that has the render model's FAMILY and identifier but no
    /// <see cref="ResolvedImageModel" /> - the Qwen image-EDITOR path, whose model is a <c>RegisteredModel</c> row
    /// carrying its own family. This is the ONE owner of scene-LoRA validation: both overloads above delegate here,
    /// so the editor and the composer cannot disagree about what a valid selection is.
    /// </summary>
    Task<IReadOnlyList<ResolvedSceneLora>> ResolveAsync(
        SceneImageModelFamily family,
        string modelIdentifier,
        IReadOnlyList<SceneImageLoraSelection>? selections,
        CancellationToken cancellationToken = default);
}

public sealed class SceneLoraResolver : ISceneLoraResolver
{
    private readonly ISceneLoraRepository _catalog;
    private readonly ILogger<SceneLoraResolver> _logger;

    public SceneLoraResolver(ISceneLoraRepository catalog, ILogger<SceneLoraResolver> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public Task<IReadOnlyList<ResolvedSceneLora>> ResolveAsync(
        ResolvedImageModel model,
        SceneImageStudioSettings? settings,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(model, settings?.SceneLoras, cancellationToken);

    public async Task<IReadOnlyList<ResolvedSceneLora>> ResolveAsync(
        ResolvedImageModel model,
        IReadOnlyList<SceneImageLoraSelection>? selections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        return await ResolveAsync(model.SceneImageModelFamily, model.ModelIdentifier, selections, cancellationToken);
    }

    public async Task<IReadOnlyList<ResolvedSceneLora>> ResolveAsync(
        SceneImageModelFamily family,
        string modelIdentifier,
        IReadOnlyList<SceneImageLoraSelection>? selections,
        CancellationToken cancellationToken = default)
    {
        if (selections is null || selections.Count == 0)
        {
            // No scene LoRA is a CONFIGURED state, not a fallback: the render proceeds with the graph it would have
            // had before the scene-LoRA catalog existed.
            return [];
        }

        if (family == SceneImageModelFamily.Unknown)
        {
            throw new InvalidOperationException(
                $"Model '{modelIdentifier}' has no scene-image family set, so a scene LoRA cannot be checked "
                + "against the checkpoint it must bind to. Set the model's family in Model Manager (/model-manager), "
                + "then pick the LoRAs again.");
        }

        var resolved = new List<ResolvedSceneLora>(selections.Count);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            var fileName = selection.FileName?.Trim() ?? string.Empty;
            if (fileName.Length == 0)
            {
                throw new InvalidOperationException(
                    "A scene LoRA selection must name a catalog file. Remove the empty entry or pick a LoRA for it.");
            }

            if (!selected.Add(fileName))
            {
                throw new InvalidOperationException(
                    $"Scene LoRA '{fileName}' is selected twice. Apply each LoRA once: loading the same LoRA twice "
                    + "shifts it rather than strengthening it.");
            }

            if (selection.Strength is not { } strength || strength <= 0 || !double.IsFinite(strength))
            {
                throw new InvalidOperationException(
                    $"Scene LoRA '{fileName}' was selected without an explicit positive strength. A LoRA applied at "
                    + "a strength nobody chose is a different LoRA from the one that was proven, so the strength is "
                    + "required rather than defaulted.");
            }

            var row = await _catalog.GetByFileNameAsync(fileName, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Scene LoRA '{fileName}' is not in the scene-LoRA catalog, so the render cannot check which "
                    + "checkpoint it binds to. Add it to the SceneLoras catalog (dbq) or pick a catalog LoRA.");

            if (!row.IsEnabled)
            {
                throw new InvalidOperationException(
                    $"Scene LoRA '{fileName}' is disabled in the scene-LoRA catalog and may not be rendered. "
                    + "Re-enable the row or pick another LoRA.");
            }

            if (row.SceneImageModelFamily != family)
            {
                throw new InvalidOperationException(
                    $"Scene LoRA '{fileName}' is catalogued for family '{row.SceneImageModelFamily}', but this render "
                    + $"uses a '{family}' model. A LoRA only binds to the checkpoint family it "
                    + "was trained against, so it is refused rather than applied regardless.");
            }

            resolved.Add(new ResolvedSceneLora(
                FileName: row.FileName,
                Strength: strength,
                Purpose: string.IsNullOrWhiteSpace(row.DisplayName) ? null : row.DisplayName));
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Resolved {Count} scene LoRA(s) for model {ModelIdentifier}: {Loras}",
                resolved.Count,
                modelIdentifier,
                string.Join(", ", resolved.Select(lora => $"{lora.FileName}@{lora.Strength}")));
        }

        return resolved;
    }
}
