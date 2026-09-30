using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public interface ISceneImagePromptCompiler
{
    SceneImageModelFamily Family { get; }
    SceneImagePromptDialect PromptDialect { get; }
    ISceneImageLLMPromptBuilder PromptBuilder { get; }

    // B-135 D10: there is deliberately NO negative-prompt member here. Negatives were purged across this app in
    // 2026-09-08 as VALUES, but this member survived and let them come back. The negative is now declared on the
    // checkpoint's ImageCompilerProfile and read from there by the render path, so a compiler cannot introduce one.
}

public interface ISceneImagePromptCompilerRegistry
{
    ISceneImagePromptCompiler Resolve(
        SceneImageModelFamily family,
        SceneImagePromptDialect promptDialect);

    /// <summary>
    /// Resolves the compiler for a CHECKPOINT PROFILE (B-135 B135-003). The family and dialect are read FROM the
    /// profile rather than used as the lookup key, so two SDXL checkpoints can carry different compilers and an
    /// unknown pairing is reported against the checkpoint the operator chose, not against a family.
    /// </summary>
    ISceneImagePromptCompiler Resolve(ImageCompilerProfile profile);
}