using System.Text.Json;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Application.Abstractions;

public sealed record MultimodalImageInput(
    string MediaType,
    ReadOnlyMemory<byte> Bytes,
    int Width,
    int Height,
    string Sha256);

public sealed record MultimodalCompletionRequest(
    string SystemMessage,
    string UserMessage,
    MultimodalImageInput Image,
    string ResponseSchemaName,
    JsonElement ResponseSchema);

public sealed record MultimodalCompletionResult(
    string Content,
    string ModelIdentifier,
    TimeSpan Duration);

public interface IMultimodalCompletionClient
{
    Task<MultimodalCompletionResult> GenerateAsync(
        ResolvedMultimodalModel model,
        MultimodalCompletionRequest request,
        CancellationToken cancellationToken = default);

    Task CheckHealthAsync(
        ResolvedMultimodalModel model,
        CancellationToken cancellationToken = default);
}