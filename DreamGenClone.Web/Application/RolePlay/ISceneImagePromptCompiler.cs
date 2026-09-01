using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ISceneImagePromptCompiler
{
    SceneImageModelFamily Family { get; }
    SceneImagePromptDialect PromptDialect { get; }
    ISceneImageLLMPromptBuilder PromptBuilder { get; }
    string SfwClampSuffix { get; }
    string CanonicalNegativePrompt { get; }
    string BuildNegativePrompt(SceneImageBeat beat, string pov);
}

public interface ISceneImagePromptCompilerRegistry
{
    ISceneImagePromptCompiler Resolve(
        SceneImageModelFamily family,
        SceneImagePromptDialect promptDialect);
}