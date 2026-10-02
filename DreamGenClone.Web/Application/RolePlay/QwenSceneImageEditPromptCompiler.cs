using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Qwen Image Edit 2511 compiler: the ordered-reference edit instruction for the
/// <see cref="ImageEditorGraphKind.MergedCheckpoint"/> and <see cref="ImageEditorGraphKind.SplitUnet"/> graphs
/// (QwenImageEditPlusPipeline: immutable ordered image list, seed 0, true_cfg 4.0, blank negative, 40 steps,
/// guidance 1.0). The instruction names each ordered image's role and states the requested changes and the preserved
/// properties; it has no canvas/aspect field and must not invent one.
///
/// <para>
/// B135-008 N2: the schema, response contract and parsing live in <see cref="SceneImageEditPromptCompilerBase"/>;
/// this class supplies only the 2511 instruction text and its <see cref="SystemPromptVersion"/>.
/// </para>
/// </summary>
public sealed class QwenSceneImageEditPromptCompiler : SceneImageEditPromptCompilerBase
{
    /// <summary>Persisted on a compilation attempt so the attempt names the compiler that compiled it.</summary>
    public const string SystemPromptVersion = "qwen-edit-rules-v3";

    protected override string CompilerSystemPromptVersion => SystemPromptVersion;
}