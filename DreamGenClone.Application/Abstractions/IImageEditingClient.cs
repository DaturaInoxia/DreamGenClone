using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

/// <summary>Runs a configured source-image edit and returns the rendered image bytes.</summary>
public interface IImageEditingClient
{
    Task<byte[]> EditAsync(
        ResolvedImageEditorModel model,
        Stream sourceImage,
        string sourceFileName,
        string instruction,
        CancellationToken cancellationToken = default);

    Task<byte[]> EditWithReferencesAsync(
        ResolvedImageEditorModel model,
        Stream sourceImage,
        string sourceFileName,
        string instruction,
        IReadOnlyList<ImageEditingReference> references,
        CancellationToken cancellationToken = default);
}

public sealed record ImageEditingReference(
    int Ordinal,
    string SemanticRole,
    Stream Image,
    string FileName,
    string Checksum);