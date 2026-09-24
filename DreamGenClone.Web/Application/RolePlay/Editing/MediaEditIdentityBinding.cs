using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// One approved identity-pack face bound to a detected person of an edited image: the reference the editor
/// model receives, plus the provenance saying which character, pack version and face asset it came from.
///
/// It is the persisted shape of an identity run on a store that has no identity column of its own — the
/// bindings live in the queued image's <c>SourceProvenanceJson</c> under
/// <see cref="MediaEditIdentityProvenance.ReferencesKey"/>. The writer and the reader below are one
/// contract, so a queued row cannot be classified two different ways in two places.
/// </summary>
public sealed record MediaEditIdentityBinding(
    int Ordinal,
    string CharacterId,
    string CharacterName,
    string IdentityPackId,
    int IdentityPackVersion,
    string CanonicalFaceAssetId,
    string FileRelativePath,
    string Sha256,
    string TargetKey = "",
    string VisibleLocator = "");

/// <summary>
/// The provenance contract of an identity run: the key its bindings are stored under, and the one reader
/// that decides whether a queued image is an identity run and returns the references it must send.
/// </summary>
public static class MediaEditIdentityProvenance
{
    /// <summary>The provenance key holding the bound identity references, ordinally ordered.</summary>
    public const string ReferencesKey = "identityReferences";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads the identity bindings a queued image carries, or null when the row is not an identity run.
    /// </summary>
    public static IReadOnlyList<MediaEditIdentityBinding>? TryRead(string? provenanceJson)
    {
        if (string.IsNullOrWhiteSpace(provenanceJson))
            return null;

        using var provenance = JsonDocument.Parse(provenanceJson);
        if (!provenance.RootElement.TryGetProperty(ReferencesKey, out var element))
            return null;

        var bindings = element.Deserialize<IReadOnlyList<MediaEditIdentityBinding>>(JsonOptions)
            ?? throw new InvalidOperationException("Identity reference bindings are invalid.");
        Validate(bindings);
        return bindings;
    }

    /// <summary>Fails fast on a binding set that cannot drive a run, rather than running with it.</summary>
    public static void Validate(IReadOnlyList<MediaEditIdentityBinding> bindings)
    {
        if (bindings.Count == 0)
            throw new InvalidOperationException("An identity run requires at least one bound person.");

        var ordinals = bindings.Select(binding => binding.Ordinal).ToList();
        if (!ordinals.SequenceEqual(ordinals.OrderBy(ordinal => ordinal)))
            throw new InvalidOperationException("Identity reference bindings must be ordinally ordered.");

        if (bindings.Any(binding => string.IsNullOrWhiteSpace(binding.CharacterId)
            || string.IsNullOrWhiteSpace(binding.CharacterName)
            || string.IsNullOrWhiteSpace(binding.IdentityPackId)
            || string.IsNullOrWhiteSpace(binding.CanonicalFaceAssetId)
            || string.IsNullOrWhiteSpace(binding.FileRelativePath)
            || string.IsNullOrWhiteSpace(binding.Sha256)
            || string.IsNullOrWhiteSpace(binding.VisibleLocator)))
        {
            throw new InvalidOperationException(
                "Every identity reference binding requires its character, pack, face asset, file path, checksum and visible locator.");
        }
    }
}
