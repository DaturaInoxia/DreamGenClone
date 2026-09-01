using System.Text.Json;
using DreamGenClone.Application.RolePlay;

namespace DreamGenClone.Application.Abstractions;

public sealed record StructuredTextCompletionRequest(
    string SystemMessage,
    string UserMessage,
    string ResponseSchemaName,
    JsonElement ResponseSchema);

public sealed record StructuredTextCompletionResult(
    string Content,
    string ModelIdentifier,
    string? FinishReason,
    TimeSpan Duration);

public interface IStructuredTextCompletionClient
{
    Task<StructuredTextCompletionResult> GenerateAsync(
        ResolvedSceneBeatAnalyzer analyzer,
        StructuredTextCompletionRequest request,
        CancellationToken cancellationToken = default);
}