namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The recorded FIXED APPEARANCE of a still's depicted people — age, height, hair, eyes, skin, build — stated as DATA
/// at the end of the prompt, the way <see cref="SceneStagingStatement"/> states the geometry and
/// <see cref="SceneCameraStatement"/> states the camera.
/// </summary>
/// <remarks>
/// <para>
/// The appearance is already handed to the compile model in the DEPICTED CHARACTER APPEARANCE block. A compile model
/// cannot be trusted to carry a fact it was merely asked to carry: measured 2026-10-20 and 2026-10-27, a character with
/// no reference image had their full appearance in that block and the caption still described only their clothing. So
/// the appearance is not asked for; it is stated, after the caption, using the SAME place-based naming the staging
/// block uses, so "the clothed person on the left" names one person in both blocks.
/// </para>
/// <para>
/// Each person is included only when the frame still needs their appearance in words — a character whose face AND body
/// are both supplied by reference images is not restated, which is exactly
/// <see cref="SceneImagePovProjection.ProjectedCharacter.AppearanceFromText"/>.
/// </para>
/// </remarks>
public static class SceneAppearanceStatement
{
    /// <summary>The marker a reader uses to identify the block, and the reason it is appended last.</summary>
    public const string Marker = "APPEARANCE — AUTHORITATIVE";

    /// <summary>
    /// The appearance block for this POV, or empty when no in-frame person needs their appearance stated or no
    /// appearance was resolved.
    /// </summary>
    /// <param name="projection">The resolved POV frame, for the in-frame cast and the place-based naming.</param>
    /// <param name="appearanceByProfileKey">
    /// The resolved fixed appearance for each depicted character, keyed by frozen profile key (see
    /// <c>CanonicalCharacterAppearance.ResolveByProfileKey</c>).
    /// </param>
    public static string For(
        SceneImagePovProjection.Projection? projection,
        IReadOnlyDictionary<string, string> appearanceByProfileKey)
    {
        if (projection is null || projection.InFrame.Count == 0)
        {
            return string.Empty;
        }

        var labels = SceneStagingStatement.Labels(projection);
        var lines = new List<string>();
        foreach (var character in projection.InFrame)
        {
            if (!character.AppearanceFromText)
            {
                continue;
            }

            if (!appearanceByProfileKey.TryGetValue(character.ProfileKey, out var appearance)
                || string.IsNullOrWhiteSpace(appearance))
            {
                continue;
            }

            if (!labels.TryGetValue(character.ProfileKey, out var label))
            {
                continue;
            }

            lines.Add($"- {label.Text}: {appearance}");
        }

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        return $"{Marker} (fixed identity — the picture must show exactly this):{Environment.NewLine}"
            + string.Join(Environment.NewLine, lines);
    }
}
