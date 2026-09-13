using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// Measures the head of the subject an edit workspace is open on, so the head-aware crop can be offered
/// wherever a real measurement can be taken — not only where a host already measured the image earlier.
/// </summary>
public interface IMediaEditHeadMeasurementService
{
    /// <summary>
    /// Measures the subject's current source image. Returns null when the tool found no face mesh (there is
    /// no head to place a crop against), and fails fast on a missing interpreter or a missing file.
    /// </summary>
    Task<CharacterIdentityHeadMeasurement?> MeasureAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default);
}

public sealed class MediaEditHeadMeasurementService : IMediaEditHeadMeasurementService
{
    private readonly ImageEditWorkspaceServiceResolver _workspaces;
    private readonly ICharacterIdentityMeasurementService _measurements;
    private readonly PersistenceOptions _persistence;

    public MediaEditHeadMeasurementService(
        ImageEditWorkspaceServiceResolver workspaces,
        ICharacterIdentityMeasurementService measurements,
        IOptions<PersistenceOptions> persistence)
    {
        _workspaces = workspaces;
        _measurements = measurements;
        _persistence = persistence.Value;
    }

    public async Task<CharacterIdentityHeadMeasurement?> MeasureAsync(
        ImageEditSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var workspace = _workspaces.Resolve(subject.Kind);
        var source = await workspace.GetSourceAsync(subject, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The selected image '{subject.ImageId}' is unavailable or is not complete.");
        if (string.IsNullOrWhiteSpace(source.FileRelativePath))
            throw new InvalidOperationException($"The selected image '{subject.ImageId}' has no stored file to measure.");

        var imagePath = Path.GetFullPath(Path.Combine(_persistence.SceneImageRoot, source.FileRelativePath));
        if (!File.Exists(imagePath))
            throw new InvalidOperationException($"The image file was not found at '{imagePath}'.");

        var result = await _measurements.MeasureFileAsync(imagePath, cancellationToken);
        return result.Measurement.Head;
    }
}
