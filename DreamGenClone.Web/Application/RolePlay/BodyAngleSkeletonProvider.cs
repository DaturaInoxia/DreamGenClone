using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Reads a canonical angle's committed OpenPose skeleton for a body-view render.
///
/// Why this is not <see cref="IStancePoseSkeletonProvider"/>: a stance answers "which pose", an angle answers "which
/// view", and they are keyed by different things. Both read the same <c>pose-library</c> folder through the same
/// helper, so a missing file fails the same way in both.
/// </summary>
public interface IBodyAngleSkeletonProvider
{
    Task<byte[]> ReadAsync(SceneImageReferenceBodyView view, CancellationToken cancellationToken = default);

    /// <summary>The view's skeleton file name, for provenance/debugging. Does not touch the file system.</summary>
    string FileNameFor(SceneImageReferenceBodyView view);
}

/// <inheritdoc />
public sealed class BodyAngleSkeletonProvider : IBodyAngleSkeletonProvider
{
    private readonly IWebHostEnvironment _environment;

    public BodyAngleSkeletonProvider(IWebHostEnvironment environment) => _environment = environment;

    public string FileNameFor(SceneImageReferenceBodyView view) => BodyAngleSkeletons.Require(view).FileName;

    public async Task<byte[]> ReadAsync(SceneImageReferenceBodyView view, CancellationToken cancellationToken = default)
    {
        var skeleton = BodyAngleSkeletons.Require(view);
        return await PoseLibraryFile.ReadAsync(
            _environment,
            skeleton.FileName,
            $"the angle skeleton for '{view}'",
            $"re-annotate it with specs/image-generator-tests/qwen-21-native-reference/prompts/dwpose-annotate.json "
            + $"(source: {skeleton.VerifiedOn})",
            cancellationToken);
    }
}
