using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class PonySceneImagePromptCompiler : ISceneImagePromptCompiler
{
    private readonly PonySceneImagePromptBuilder _builder;

    public PonySceneImagePromptCompiler(PonySceneImagePromptBuilder builder)
    {
        _builder = builder;
    }

    public SceneImageModelFamily Family => SceneImageModelFamily.Pony;
    public SceneImagePromptDialect PromptDialect => SceneImagePromptDialect.PonyV6Tags;
    public ISceneImageLLMPromptBuilder PromptBuilder => _builder;
    public string SfwClampSuffix => PonySceneImagePromptBuilder.SfwClampSuffix;
    public string CanonicalNegativePrompt => "lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry";

    public string BuildNegativePrompt(SceneImageBeat beat, string pov) =>
        _builder.BuildDeterministicBeatNegativePrompt(beat, pov);
}

public sealed class SdxlSceneImagePromptCompiler : ISceneImagePromptCompiler
{
    private readonly SdxlSceneImagePromptBuilder _builder;

    public SdxlSceneImagePromptCompiler(SdxlSceneImagePromptBuilder builder)
    {
        _builder = builder;
    }

    public SceneImageModelFamily Family => SceneImageModelFamily.Sdxl;
    public SceneImagePromptDialect PromptDialect => SceneImagePromptDialect.SdxlNaturalLanguage;
    public ISceneImageLLMPromptBuilder PromptBuilder => _builder;
    public string SfwClampSuffix => _builder.SfwClampSuffix;
    public string CanonicalNegativePrompt => SdxlSceneImagePromptBuilder.DefaultNegativePrompt;

    public string BuildNegativePrompt(SceneImageBeat beat, string pov) =>
        _builder.BuildDeterministicBeatNegativePrompt(beat, pov);
}

public sealed class SceneImagePromptCompilerRegistry : ISceneImagePromptCompilerRegistry
{
    private readonly IReadOnlyList<ISceneImagePromptCompiler> _compilers;

    public SceneImagePromptCompilerRegistry(IEnumerable<ISceneImagePromptCompiler> compilers)
    {
        _compilers = compilers.ToList();
    }

    public ISceneImagePromptCompiler Resolve(
        SceneImageModelFamily family,
        SceneImagePromptDialect promptDialect)
    {
        var matches = _compilers
            .Where(compiler => compiler.Family == family && compiler.PromptDialect == promptDialect)
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No scene-image prompt compiler is registered for family '{family}' and dialect '{promptDialect}'. Configure the model in Model Manager."),
            _ => throw new InvalidOperationException(
                $"Multiple scene-image prompt compilers are registered for family '{family}' and dialect '{promptDialect}'. Exactly one registration is required.")
        };
    }
}