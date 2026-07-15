using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Detects the current scene location from recent interaction narrative via an LLM call.
/// Used as a fire-and-forget background job in the adaptive pipeline; never blocks
/// foreground turn generation.
///
/// No-fallback contract: <see cref="DetectAsync"/> returns <c>Success = false</c>
/// (without mutating prior state) when no model is configured for
/// <see cref="AppFunction.RolePlayLocationDetection"/>, the LLM call fails, the
/// response times out, or the JSON output fails to parse.
/// </summary>
public interface ILocationDetectionService
{
    /// <summary>
    /// Detects the current scene location. Resolves the model via
    /// <see cref="IModelResolutionService.ResolveAsync"/>, builds the LLM
    /// prompt, calls the completion client, and parses the JSON response.
    /// </summary>
    Task<Models.LocationDetectionResult> DetectAsync(
        Models.LocationDetectionRequest request,
        CancellationToken cancellationToken = default);
}
