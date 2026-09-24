using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;

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
    TimeSpan Duration,
    StructuredTextCompletionDiagnostics? Diagnostics = null);

public sealed record StructuredTextCompletionDiagnostics(
    long HeadersWaitMs,
    long ResponseBodyReadMs,
    int ResponseBytes,
    long JsonDeserializationMs,
    string? UsageJson,
    string? ReasoningContent);

public interface IStructuredTextCompletionClient
{
    Task<StructuredTextCompletionResult> GenerateAsync(
        ResolvedSceneBeatAnalyzer analyzer,
        StructuredTextCompletionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Synchronous structured-text completion (B-122: the character body card's draft). It is a SEPARATE capability from
/// <see cref="IStructuredTextCompletionClient"/>, whose envelope is the scene-beat analyzer's — queue-shaped, with a
/// lease, poll interval, retry schedule and catalogue bounds that a synchronous, operator-triggered call neither
/// has nor should invent.
/// </summary>
public interface ISynchronousStructuredTextCompletionClient
{
    Task<StructuredTextCompletionResult> GenerateAsync(
        ResolvedStructuredTextFunction function,
        StructuredTextCompletionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The resolved model + structured-output envelope for a synchronous structured-text function: which function it is,
/// which model and provider serve it, and whether it returns a strict JSON schema or a JSON object.
/// </summary>
public sealed record ResolvedStructuredTextFunction(
    AppFunction Function,
    string ModelId,
    string ProviderId,
    ResolvedModel Model,
    StructuredOutputMode StructuredOutputMode);