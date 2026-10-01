namespace DreamGenClone.Infrastructure.Configuration;

/// <summary>
/// Where the Playground reads its prompt catalogs from (B-135).
///
/// <para>
/// A catalog manifest is <b>source-controlled content authored by an agent at design time</b>; the app only reads it.
/// The root is configuration rather than a hard-coded path so a deployed app can point at wherever the catalogs were
/// shipped, and the resolver walks up from the working directory so it also works when the app is started from
/// <c>DreamGenClone.Web</c> in a repo checkout.
/// </para>
/// </summary>
public sealed class PlaygroundOptions
{
    public const string SectionName = "Playground";

    /// <summary>
    /// The folder holding the catalogs (each catalog is a folder with a <c>manifest.json</c> beside its position files).
    /// Absolute, or relative to the working directory or any parent of it. Empty means "use the default location".
    /// </summary>
    public string ManifestRoot { get; set; } = string.Empty;

    /// <summary>Fallback used when <see cref="ManifestRoot"/> is empty, relative to the repo/content root.</summary>
    public const string DefaultManifestRoot = "specs/image-generator-tests";
}
