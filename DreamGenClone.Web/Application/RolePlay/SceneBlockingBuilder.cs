using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Builds the frame-space <see cref="SceneBlocking"/> for a still render (B-157).
///
/// <para>
/// It exists as its own step, ahead of compilation, because the compiler is synchronous and pure while resolving a
/// staging profile is I/O. The caller awaits this, then hands the result to the compiler as part of the request, so
/// the brief records the framing that was actually used instead of resolving one later.
/// </para>
/// </summary>
public interface ISceneBlockingBuilder
{
    /// <summary>
    /// Derives the blocking for one moment. Throws when the brief cannot be read or the cast cannot be placed — a
    /// still is refused rather than framed by an invented default.
    /// </summary>
    Task<SceneBlocking> BuildAsync(
        string? compositionRationale,
        string frozenStateContractJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Derives the framing for a render that has NO beat/moment scene — the Asset Manager — from its own staging
    /// profile.
    ///
    /// <para>
    /// Null when nothing is bound per character (a location-only asset, say): there is then no subject to place, and
    /// claiming a frame for nobody would put a position in the prompt that describes no person. The Asset Manager's
    /// profile is the SAME resolver path as a Composition's, so the placement ladder and camera rules have one owner
    /// and one editor.
    /// </para>
    /// </summary>
    Task<SceneBlocking?> BuildAssetAsync(
        IReadOnlyList<string?> subjectKeysInSendOrder,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class SceneBlockingBuilder : ISceneBlockingBuilder
{
    private readonly IImageStagingProfileResolver _profiles;

    public SceneBlockingBuilder(IImageStagingProfileResolver profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    public async Task<SceneBlocking> BuildAsync(
        string? compositionRationale,
        string frozenStateContractJson,
        CancellationToken cancellationToken = default)
    {
        // One reader for the frozen-state contract (the shared one the moment/location bind service uses), so the
        // participant shape has exactly one definition.
        var frozen = SceneMomentPlaceSeedBuilder.TryReadFrozenState(frozenStateContractJson)
            ?? throw new InvalidOperationException(
                "The moment enrichment carries no readable frozen-state contract, so the scene's participants cannot "
                + "be placed in the frame. Enrich the moment before compiling a still brief; no placement is invented.");

        var characters = frozen.Characters ?? [];
        if (characters.Count == 0)
        {
            throw new InvalidOperationException(
                "The frozen-state contract names no participants, so there is no subject to place in the frame. A "
                + "still brief requires at least one participant.");
        }

        var subjects = characters
            .Select(character => new SceneBlockingSubjectInput(
                character.ProfileKey,
                character.Involvement,
                character.PhysicalLocation,
                character.Position,
                character.Sightline))
            .ToList();

        var profile = await _profiles.ResolveAsync(ImageStagingSurface.Composition, cancellationToken);
        return SceneBlockingResolver.Resolve(new SceneBlockingInput(compositionRationale, subjects), profile);
    }

    public async Task<SceneBlocking?> BuildAssetAsync(
        IReadOnlyList<string?> subjectKeysInSendOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjectKeysInSendOrder);

        // Send order decides which subject takes the head of the ladder, so the same bound face always lands in the
        // same place for a given set of bindings.
        var keys = subjectKeysInSendOrder
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0)
        {
            // Nothing is bound per character, so there is no subject to place. Returning no frame is the honest
            // answer: a blocking here would state a position for a person the prompt never mentions.
            return null;
        }

        // An asset has no beat, so it has no involvement to read: every image the operator bound to a character IS a
        // subject of that picture. That is what this surface means by "subject", not a tunable — the placement ladder,
        // the depths and the camera all still come from the Asset Manager's staging profile on /profiles.
        var subjects = keys
            .Select(key => new SceneBlockingSubjectInput(
                key,
                SceneBlockingResolver.ActiveInvolvement,
                PhysicalLocation: null,
                Position: null,
                Sightline: null))
            .ToList();

        var profile = await _profiles.ResolveAsync(ImageStagingSurface.AssetManager, cancellationToken);
        return SceneBlockingResolver.Resolve(new SceneBlockingInput(Rationale: null, subjects), profile);
    }
}
