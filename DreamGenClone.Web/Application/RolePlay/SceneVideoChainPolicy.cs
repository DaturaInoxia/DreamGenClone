using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Where a continuation sits in its chain, and whether the configured budget still allows it (B-156 C-13).
/// </summary>
/// <remarks>
/// The walk reads the links the records actually carry, so chain depth is derived from the lineage rather than a
/// cached counter that could drift out of step with it. Depth 1 means "the first continuation of an original clip".
/// The budget is the model qualification's configured <c>MaxContinuationChainLength</c>, resolved by the caller from
/// the model definition - nothing here substitutes a default when it is absent.
/// </remarks>
public sealed class SceneVideoChainPolicy
{
    /// <summary>A guard against walking a corrupt (rather than merely long) lineage forever.</summary>
    private const int MaxWalkableLinks = 256;

    private readonly ISceneVideoRepository _repository;
    private readonly ILogger<SceneVideoChainPolicy> _logger;

    public SceneVideoChainPolicy(ISceneVideoRepository repository, ILogger<SceneVideoChainPolicy> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// How many continuations the clip that continues <paramref name="source"/> would be: 1 for the first
    /// continuation of an original render, 2 for the next, and so on.
    /// </summary>
    public async Task<int> ResolveDepthAsync(
        SceneVideoRecord source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var depth = 1;
        var current = source;
        var seen = new HashSet<string>(StringComparer.Ordinal) { source.Id };

        while (!string.IsNullOrWhiteSpace(current.SourceVideoId))
        {
            if (!seen.Add(current.SourceVideoId!))
            {
                throw new InvalidOperationException(
                    $"The continuation chain for '{source.Id}' contains a cycle at '{current.SourceVideoId}', so its "
                    + "depth cannot be resolved. Fix the lineage rows rather than extending a malformed chain.");
            }

            var parent = await _repository.GetAsync(current.SourceVideoId!, cancellationToken);
            if (parent is null)
            {
                // A deleted source leaves broken lineage: the chain ends here, and the caller is told in the log
                // rather than the walk silently pretending the clip was an original render.
                _logger.LogWarning(
                    "Continuation lineage is broken: '{SourceId}' points at missing '{MissingId}'. Treating the "
                    + "chain as ending at the missing link.",
                    current.Id, current.SourceVideoId);
                break;
            }

            depth++;
            current = parent;

            if (depth > MaxWalkableLinks)
            {
                throw new InvalidOperationException(
                    $"The continuation chain for '{source.Id}' exceeds {MaxWalkableLinks} links, which is not a real "
                    + "chain. Refusing to walk it further.");
            }
        }

        return depth;
    }

    /// <summary>The first clip of the chain, used as the tone reference when reporting drift.</summary>
    public async Task<string> ResolveRootIdAsync(
        SceneVideoRecord source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var current = source;
        var seen = new HashSet<string>(StringComparer.Ordinal) { source.Id };

        while (!string.IsNullOrWhiteSpace(current.SourceVideoId) && seen.Add(current.SourceVideoId!))
        {
            var parent = await _repository.GetAsync(current.SourceVideoId!, cancellationToken);
            if (parent is null)
            {
                break;
            }

            current = parent;
        }

        return current.Id;
    }

    /// <summary>
    /// Refuses a continuation that would exceed <paramref name="budget"/>, naming the policy, the depth reached and
    /// the configured limit: a refusal the operator cannot act on is indistinguishable from a bug.
    /// </summary>
    public static void EnsureWithinBudget(int depth, int budget, string sourceId)
    {
        if (depth <= budget)
        {
            return;
        }

        throw new InvalidOperationException(
            $"This chain already has {depth - 1} continuation(s) and the configured limit is {budget} "
            + "(MaxContinuationChainLength). Chained clips accumulate drift of roughly 4% contrast and a third "
            + "of the treble per join, so start a fresh clip from a new frame instead of extending this chain "
            + "further.");
    }
}
