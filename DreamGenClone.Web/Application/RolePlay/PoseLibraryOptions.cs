namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Where the pose library comes from and where its generated skeletons live. Both values are required:
/// a missing path fails loudly rather than falling back to a guess, because an importer that silently invents
/// a source directory would import nothing and look like it had succeeded.
/// </summary>
public sealed class PoseLibraryOptions
{
    public const string SectionName = "PoseLibrary";

    /// <summary>
    /// The folder holding one sub-folder per pose pack (<c>pose-packs</c>). Relative paths are resolved against
    /// the content root; absolute paths are used as they are. Every sub-folder becomes one library.
    /// </summary>
    public string? PacksRoot { get; set; }

    /// <summary>
    /// Folder the generated skeleton PNGs are written to, <b>relative to the app's <c>pose-library</c> folder</b>.
    /// Kept inside <c>pose-library</c> so the skeletons sit with the app's other conditioning images and are
    /// served as static files without a second file-serving path.
    /// </summary>
    public string? SkeletonFolder { get; set; }

    /// <summary>
    /// Ceiling for a downloaded pack, in megabytes. A pack is refused past it rather than filling the disk.
    /// Required: an unbounded download is not a default anyone should inherit silently.
    /// </summary>
    public int? DownloadMaxMegabytes { get; set; }
}
