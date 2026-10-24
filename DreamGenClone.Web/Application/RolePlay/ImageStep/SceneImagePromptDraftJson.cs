using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The operator's uncommitted prompt-panel state: which source they picked, the intent they typed, and the per-element
/// edits they made in the Prompt Input inspector.
/// </summary>
/// <remarks>
/// Deliberately NOT a <see cref="SceneImagePromptRecord"/>: a record is a compile, and it only exists once Generate
/// Prompt has been pressed. This is the panel itself, so text the operator has not compiled yet has somewhere to live.
/// The inspector edits belong here for the same reason: they were kept only in the page's memory, so a reload silently
/// threw them away and the next Generate sent no overrides at all (reported 2026-10-22, "I think I lost the changes").
/// </remarks>
public sealed record SceneImagePromptDraft(
    SceneImagePromptSource Source,
    string UserIntent,
    ScenePromptOverrides? Overrides = null,
    SceneImageExplicitness Explicitness = SceneImageExplicitness.Explicit,
    CameraHolderPartsMode CameraHolderParts = CameraHolderPartsMode.None,
    string? CameraHolderPartsText = null);

/// <summary>
/// Reads and writes <see cref="SceneImagePromptDraft"/> on its production group.
/// </summary>
/// <remarks>
/// One place for the format, because two callers need it — the composer that writes it and the composer that reads it
/// back — and a second copy of the shape is a second thing to keep in step. Unreadable stored JSON FAILS FAST naming
/// the group rather than silently reverting the panel to defaults: a draft that quietly disappears is the exact
/// complaint this exists to answer.
/// </remarks>
public static class SceneImagePromptDraftJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The JSON to store, or null when the draft says nothing — a Scene-sourced panel with no intent text, no
    /// inspector edits, the default explicitness and no camera-holder parts IS the default panel, so storing it would
    /// only make every group look edited.
    /// </summary>
    public static string? Serialize(
        SceneImagePromptSource source,
        string? userIntent,
        ScenePromptOverrides? overrides = null,
        SceneImageExplicitness explicitness = SceneImageExplicitness.Explicit,
        CameraHolderPartsMode cameraHolderParts = CameraHolderPartsMode.None,
        string? cameraHolderPartsText = null)
    {
        var intent = userIntent?.Trim() ?? string.Empty;
        if (source == SceneImagePromptSource.Scene
            && intent.Length == 0
            && overrides is null
            && explicitness == SceneImageExplicitness.Explicit
            && cameraHolderParts == CameraHolderPartsMode.None)
        {
            return null;
        }

        return JsonSerializer.Serialize(
            new SceneImagePromptDraft(source, intent, overrides, explicitness, cameraHolderParts, cameraHolderPartsText),
            Options);
    }

    /// <summary>The stored draft, or null when the group carries none.</summary>
    public static SceneImagePromptDraft? Read(string? json, string productionGroupId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        SceneImagePromptDraft? draft;
        try
        {
            draft = JsonSerializer.Deserialize<SceneImagePromptDraft>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The prompt draft recorded on production group '{productionGroupId}' is unreadable: {exception.Message}",
                exception);
        }

        if (draft is null)
        {
            throw new InvalidOperationException(
                $"The prompt draft recorded on production group '{productionGroupId}' is empty.");
        }

        if (!Enum.IsDefined(draft.Source))
        {
            throw new InvalidOperationException(
                $"The prompt draft recorded on production group '{productionGroupId}' names prompt source " +
                $"'{draft.Source}', which is not a known source.");
        }

        if (!Enum.IsDefined(draft.Explicitness))
        {
            throw new InvalidOperationException(
                $"The prompt draft recorded on production group '{productionGroupId}' names explicitness " +
                $"'{draft.Explicitness}', which is not a known explicitness.");
        }

        if (!Enum.IsDefined(draft.CameraHolderParts))
        {
            throw new InvalidOperationException(
                $"The prompt draft recorded on production group '{productionGroupId}' names camera-holder parts mode " +
                $"'{draft.CameraHolderParts}', which is not a known mode.");
        }

        return draft with { UserIntent = draft.UserIntent ?? string.Empty };
    }
}
