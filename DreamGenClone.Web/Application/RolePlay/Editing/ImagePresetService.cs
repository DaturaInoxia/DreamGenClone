using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// The store-backed preset resolver (B-133). It owns no wording: the detail, the preserve clause and the four
/// assembly templates are all rows in the prompt store, resolved through the same service every other prompt read
/// uses - including the per-character override, so a character can relight its own images in its own words.
/// </summary>
public sealed class ImagePresetService : IImagePresetService
{
    private readonly IImageWorkflowTemplateService _templates;

    public ImagePresetService(IImageWorkflowTemplateService templates)
    {
        _templates = templates;
    }

    /// <inheritdoc />
    public async Task<string> ResolveInstructionAsync(
        string presetKey,
        ImagePresetMode mode = ImagePresetMode.Change,
        string? characterId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetKey);

        var axis = ImagePresetKeys.AxisOf(presetKey);
        var detail = await ResolveAsync(presetKey, characterId, cancellationToken);
        var assembly = await ResolveAsync(ImagePresetKeys.AssemblyKey(axis, mode), characterId, cancellationToken);

        // The preserve clause is only resolved in Change mode: a condition clause has nothing yet to preserve, and
        // resolving one anyway would hide a template that asked for it.
        var preserve = mode == ImagePresetMode.Change
            ? await ResolveAsync(ImagePresetKeys.PreserveKey(axis), characterId, cancellationToken)
            : null;

        return ImagePresetInstructionComposer.Compose(axis, mode, detail, assembly, preserve);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImagePresetChoice>> ListAsync(
        ImagePresetAxis? axis = null,
        string? characterId = null,
        CancellationToken cancellationToken = default)
    {
        var keys = axis switch
        {
            ImagePresetAxis.Lighting => ImagePresetKeys.LightingKeys,
            ImagePresetAxis.Expression => ImagePresetKeys.ExpressionKeys,
            null => [.. ImagePresetKeys.LightingKeys, .. ImagePresetKeys.ExpressionKeys],
            _ => throw new InvalidOperationException($"Unsupported preset axis '{axis}'.")
        };

        var choices = new List<ImagePresetChoice>(keys.Count);
        foreach (var key in keys)
        {
            choices.Add(new ImagePresetChoice(
                key,
                ImagePresetKeys.ShortName(key),
                ImagePresetKeys.AxisOf(key),
                await ResolveAsync(key, characterId, cancellationToken)));
        }

        return choices;
    }

    /// <inheritdoc />
    public string DefaultPresetFor(string loraVocabularyKey) => ImagePresetKeys.PresetKeyFor(loraVocabularyKey);

    private async Task<string> ResolveAsync(string key, string? characterId, CancellationToken cancellationToken)
    {
        var template = await _templates.ResolveAsync(key, characterId, cancellationToken);
        if (string.IsNullOrWhiteSpace(template.Body))
        {
            throw new InvalidOperationException(
                $"The prompt store's row '{key}' is empty, so the preset cannot be assembled from it.");
        }

        return template.Body.Trim();
    }
}
