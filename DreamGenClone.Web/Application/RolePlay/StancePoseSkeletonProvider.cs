using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Reads a stance's committed OpenPose skeleton for ControlNet conditioning.
///
/// The bytes come from the app's own web root rather than from a path in configuration, so the conditioning input
/// travels with the repository and is identical on every machine. A missing file fails loudly: silently rendering
/// unconditioned would produce a body reference that looks like every other candidate and quietly is not the pose
/// that was asked for.
/// </summary>
public interface IStancePoseSkeletonProvider
{
    Task<byte[]> ReadAsync(BodyReferenceStance stance, CancellationToken cancellationToken = default);

    /// <summary>The stance's skeleton file name, for provenance/debugging. Does not touch the file system.</summary>
    string FileNameFor(BodyReferenceStance stance);
}

/// <inheritdoc />
public sealed class StancePoseSkeletonProvider : IStancePoseSkeletonProvider
{
    private readonly IWebHostEnvironment _environment;

    public StancePoseSkeletonProvider(IWebHostEnvironment environment) => _environment = environment;

    public string FileNameFor(BodyReferenceStance stance) => BodyStanceSkeletons.Require(stance).FileName;

    public async Task<byte[]> ReadAsync(BodyReferenceStance stance, CancellationToken cancellationToken = default)
    {
        var skeleton = BodyStanceSkeletons.Require(stance);
        return await PoseLibraryFile.ReadAsync(
            _environment,
            skeleton.FileName,
            $"the pose skeleton for stance '{stance}'",
            $"re-render it with helpers/runpod/render-single-pose.py (source: {skeleton.VerifiedOn})",
            cancellationToken);
    }
}

/// <summary>
/// Reads one skeleton file out of the app's <c>pose-library</c> folder. Shared by the stance and the angle providers
/// so "a missing skeleton" is one behaviour with one message shape, rather than two that drift apart.
///
/// A missing or empty file FAILS: silently rendering without the conditioning input would produce a candidate that
/// looks like every other one and is quietly not the pose or the angle that was asked for.
/// </summary>
internal static class PoseLibraryFile
{
    public static async Task<byte[]> ReadAsync(
        IWebHostEnvironment environment,
        string fileName,
        string what,
        string howToFix,
        CancellationToken cancellationToken)
    {
        var webRoot = environment.WebRootPath
            ?? throw new InvalidOperationException(
                "The app has no web root, so the pose-library skeletons cannot be read. Conditioning needs the "
                + "pose-library assets to be present in the content root.");

        var path = Path.Combine(webRoot, BodyStanceSkeletons.WebRootFolder, fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The skeleton file '{fileName}' for {what} is missing from {BodyStanceSkeletons.WebRootFolder}/. "
                + $"Fix: {howToFix} — conditioning cannot proceed without it.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException(
                $"The skeleton file '{path}' is empty, so it cannot condition a render. Fix: {howToFix}.");
        }

        return bytes;
    }
}
