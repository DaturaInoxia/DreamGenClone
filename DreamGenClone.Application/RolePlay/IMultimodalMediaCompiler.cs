using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record MediaCompilerDescriptor(
    MediaProductionKind MediaKind,
    string FamilyKey,
    string CompilerKey,
    string CompilerVersion,
    IReadOnlySet<MediaCompilerCapability> Capabilities);

public sealed record CompileMediaBriefRequest(
    SceneBeatProductionPlan BeatProductionPlan,
    SceneMomentSet MomentSet,
    SceneMoment Moment,
    SceneMomentEnrichment MomentEnrichment,
    MediaCompilerTargetProfile TargetProfile,
    string? VideoCoveragePlanId,
    IReadOnlyList<string> DialogueCueIds,
    IReadOnlyList<string> SoundCueIds,
    IReadOnlyList<string> MusicSectionKeys,
    ApprovedMediaDerivative? ApprovedVisualDerivative,
    ApprovedMediaDerivative? ApprovedSpeechDerivative);

public interface IMultimodalMediaCompiler
{
    MediaCompilerDescriptor Descriptor { get; }

    CompiledMediaBrief Compile(CompileMediaBriefRequest request, DateTime createdUtc);
}

public interface IMultimodalMediaCompilerRegistry
{
    IMultimodalMediaCompiler Resolve(MediaCompilerTargetProfile targetProfile);
}

public interface IMultimodalMediaCompilationService
{
    Task<CompiledMediaBrief> CompileAndPersistAsync(
        CompileMediaBriefRequest request,
        CancellationToken cancellationToken = default);
}