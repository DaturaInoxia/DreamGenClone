using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class MultimodalMediaCompilationService : IMultimodalMediaCompilationService
{
    private readonly IMultimodalMediaCompilerRegistry _registry;
    private readonly ICompiledMediaBriefRepository _briefs;
    private readonly ISceneBeatProductionPlanRepository _plans;
    private readonly ISceneMomentSetRepository _momentSets;
    private readonly ISceneMomentEnrichmentRepository _enrichments;
    private readonly ISceneBlockingBuilder _blocking;
    private readonly TimeProvider _timeProvider;

    public MultimodalMediaCompilationService(
        IMultimodalMediaCompilerRegistry registry,
        ICompiledMediaBriefRepository briefs,
        ISceneBeatProductionPlanRepository plans,
        ISceneMomentSetRepository momentSets,
        ISceneMomentEnrichmentRepository enrichments,
        ISceneBlockingBuilder blocking,
        TimeProvider timeProvider)
    {
        _registry = registry;
        _briefs = briefs;
        _plans = plans;
        _momentSets = momentSets;
        _enrichments = enrichments;
        _blocking = blocking;
        _timeProvider = timeProvider;
    }

    public async Task<CompiledMediaBrief> CompileAndPersistAsync(
        CompileMediaBriefRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var compiler = _registry.Resolve(request.TargetProfile);

        var currentPlan = await _plans.GetCurrentAsync(
            request.BeatProductionPlan.CatalogueId,
            request.BeatProductionPlan.BeatId,
            cancellationToken);
        RequireExactCurrent(
            currentPlan,
            request.BeatProductionPlan.Id,
            request.BeatProductionPlan.Version,
            plan => plan.Id,
            plan => plan.Version,
            "Beat Production Plan");

        var currentSet = await _momentSets.GetCurrentAsync(currentPlan!.Id, cancellationToken);
        RequireExactCurrent(
            currentSet,
            request.MomentSet.Id,
            request.MomentSet.Version,
            set => set.Id,
            set => set.Version,
            "Moment Set");
        var currentMomentMatches = currentSet!.Moments
            .Where(moment => string.Equals(moment.MomentId, request.Moment.MomentId, StringComparison.Ordinal))
            .ToList();
        if (currentMomentMatches.Count != 1)
            throw new InvalidOperationException("The selected Moment is not unique in the current Moment Set.");

        var currentEnrichment = await _enrichments.GetCurrentAsync(
            currentSet.Id,
            currentMomentMatches[0].MomentId,
            cancellationToken);
        RequireExactCurrent(
            currentEnrichment,
            request.MomentEnrichment.Id,
            request.MomentEnrichment.Revision,
            enrichment => enrichment.Id,
            enrichment => enrichment.Revision,
            "Moment Enrichment");

        var canonicalRequest = request with
        {
            BeatProductionPlan = currentPlan,
            MomentSet = currentSet,
            Moment = currentMomentMatches[0],
            MomentEnrichment = currentEnrichment!
        };

        // B-157: a still's framing is derived BEFORE compilation and recorded in the brief, so the snapshot is the
        // single source for what the scene is AND how it is framed. Non-still kinds carry none.
        var compileRequest = canonicalRequest;
        if (canonicalRequest.TargetProfile.MediaKind == MediaProductionKind.StillImage)
        {
            var blocking = await _blocking.BuildAsync(
                canonicalRequest.Moment.CompositionRationale,
                canonicalRequest.MomentEnrichment.FrozenStateContractJson,
                cancellationToken);
            compileRequest = canonicalRequest with { SceneBlocking = blocking };
        }

        var brief = compiler.Compile(compileRequest, _timeProvider.GetUtcNow().UtcDateTime);
        await _briefs.CreateAsync(brief, cancellationToken);
        return brief;
    }

    private static void RequireExactCurrent<T>(
        T? current,
        string suppliedId,
        int suppliedVersion,
        Func<T, string> getId,
        Func<T, int> getVersion,
        string label) where T : class
    {
        if (current is null ||
            !string.Equals(getId(current), suppliedId, StringComparison.Ordinal) ||
            getVersion(current) != suppliedVersion)
            throw new InvalidOperationException($"The supplied {label} is not the exact current persisted record.");
    }
}