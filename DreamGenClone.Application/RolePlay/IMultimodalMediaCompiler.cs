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
    ApprovedMediaDerivative? ApprovedSpeechDerivative,
    // B-157: how the scene is FRAMED, derived by the caller from the moment plus the staging profile before
    // compilation. Null for a non-still media kind. For a still it is built by ISceneBlockingBuilder BEFORE
    // compilation, because a still's brief must be the single snapshot of what the scene is and how it is framed —
    // deriving it after the fact would let a later staging-profile edit rewrite a past render's framing. The
    // compiler itself stays synchronous and pure: it records the blocking it is handed and resolves no configuration.
    SceneBlocking? SceneBlocking = null);

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