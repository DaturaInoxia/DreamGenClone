using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Maps an editor's <see cref="ImageEditorGraphKind"/> to the edit-prompt compiler whose instruction text matches that
/// graph's capability envelope. An editor with no graph kind configured, or a graph kind with no compiler, fails fast —
/// never silently picks a compiler and never compiles a graph in a dialect its editor cannot act on.
/// </summary>
public interface ISceneImageEditPromptCompilerResolver
{
    /// <summary>
    /// Resolves the compiler for an editor graph. <paramref name="graphKind"/> is null when the editor has no graph
    /// configured, which is refused by name rather than defaulted.
    /// </summary>
    ISceneImageEditPromptCompiler Resolve(ImageEditorGraphKind? graphKind);

    /// <summary>
    /// Resolves the compiler for a resolved editor model, normalising a serverless editor's null graph kind to the
    /// merged-checkpoint graph it always runs.
    /// </summary>
    ISceneImageEditPromptCompiler Resolve(ResolvedImageEditorModel editorModel);

    /// <summary>
    /// Resolves the compiler from the <c>SystemPromptVersion</c> persisted on a compilation attempt, so the job
    /// handler re-selects exactly the compiler that compiled the attempt. A version with no compiler is a
    /// deployment/versioning error, refused by name rather than defaulted.
    /// </summary>
    ISceneImageEditPromptCompiler ResolveByVersion(string systemPromptVersion);
}

public sealed class SceneImageEditPromptCompilerResolver : ISceneImageEditPromptCompilerResolver
{
    private readonly QwenSceneImageEditPromptCompiler _qwen2511;
    private readonly QwenImage21EditPromptCompiler _qwen21;

    public SceneImageEditPromptCompilerResolver(
        QwenSceneImageEditPromptCompiler qwen2511,
        QwenImage21EditPromptCompiler qwen21)
    {
        _qwen2511 = qwen2511;
        _qwen21 = qwen21;
    }


    public ISceneImageEditPromptCompiler Resolve(ResolvedImageEditorModel editorModel)
    {
        ArgumentNullException.ThrowIfNull(editorModel);

        // A serverless editor always runs the merged-checkpoint (2511) graph; its null graph kind is implicit-merged,
        // not "unset". A local ComfyUI editor with no graph kind was already refused by the model resolver.
        var graphKind = editorModel.GraphKind
            ?? (editorModel.ImageProtocol == ImageProtocol.ComfyUiServerless
                ? ImageEditorGraphKind.MergedCheckpoint
                : (ImageEditorGraphKind?)null);

        return Resolve(graphKind);
    }

    public ISceneImageEditPromptCompiler ResolveByVersion(string systemPromptVersion)
    {
        if (string.Equals(systemPromptVersion, QwenSceneImageEditPromptCompiler.SystemPromptVersion, StringComparison.Ordinal))
            return _qwen2511;
        if (string.Equals(systemPromptVersion, QwenImage21EditPromptCompiler.SystemPromptVersion, StringComparison.Ordinal))
            return _qwen21;

        throw new InvalidOperationException(
            $"No edit-prompt compiler is registered for system-prompt version '{systemPromptVersion}'. The version "
            + "persisted on an attempt names a compiler that no longer exists, which is a deployment/versioning error, "
            + "not a cue to pick another compiler.");
    }
    public ISceneImageEditPromptCompiler Resolve(ImageEditorGraphKind? graphKind) => graphKind switch
    {
        ImageEditorGraphKind.MergedCheckpoint => _qwen2511,
        ImageEditorGraphKind.SplitUnet => _qwen2511,
        ImageEditorGraphKind.QwenImage21Native => _qwen21,
        null => throw new InvalidOperationException(
            "No image editor graph kind is configured, so no edit-prompt compiler can be selected. Set 'Editor Graph' "
            + $"in Model Manager (/model-manager): '{ImageEditorGraphKinds.MergedCheckpoint}' for a merged 2511 "
            + $"checkpoint, or '{ImageEditorGraphKinds.QwenImage21Native}' for Qwen-Image-2.1."),
        _ => throw new InvalidOperationException(
            $"No edit-prompt compiler exists for editor graph kind '{graphKind}'. A graph with no compiler is a "
            + "configuration error, not a cue to pick another compiler.")
    };
}
