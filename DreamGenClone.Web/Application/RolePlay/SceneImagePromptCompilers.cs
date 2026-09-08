using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
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
    public string CanonicalNegativePrompt => SdxlSceneImagePromptBuilder.DefaultNegativePrompt;

    public string BuildNegativePrompt(SceneImageBeat beat, string pov) =>
        _builder.BuildDeterministicBeatNegativePrompt(beat, pov);
}

/// <summary>
/// Plain-request compiler for API-protocol image models (OpenAI-compatible images endpoint, e.g.
/// TogetherAI GPT-Image-2 / Seedream / Imagen). These are natural-language image generators with no
/// checkpoint-prompt dialect, so the compiler uses the LLM natural-language prompt builder and no
/// deterministic negative prompt.
/// </summary>
public sealed class ApiSceneImagePromptCompiler : ISceneImagePromptCompiler
{
    private readonly ISceneImageLLMPromptBuilder _builder;

    public ApiSceneImagePromptCompiler(ISceneImageLLMPromptBuilder builder)
    {
        _builder = builder;
    }

    public SceneImageModelFamily Family => SceneImageModelFamily.Api;
    public SceneImagePromptDialect PromptDialect => SceneImagePromptDialect.NaturalLanguage;
    public ISceneImageLLMPromptBuilder PromptBuilder => _builder;
    public string CanonicalNegativePrompt => string.Empty;

    public string BuildNegativePrompt(SceneImageBeat beat, string pov) => string.Empty;
}

/// <summary>
/// Natural-language compiler for local FLUX.1-dev scene images (ComfyUI split-UNET workflow).
/// FLUX shares SDXL's natural-language photography-brief dialect (no tag vocabulary, no heavy
/// negative), so the same natural-language builder produces its positive prompts; FLUX differs only
/// in the render workflow (UNETLoader + FluxGuidance, cfg 1.0) and carries NO negative (BFL: most
/// FLUX models do not support negatives — describe the desired state positively).
/// NOTE (follow-up): the shared natural-language system prompt is SDXL-branded; a FLUX-grounded
/// system prompt is a documented follow-up, not required to route FLUX. See
/// sdxl-juggernaut-prompting.instructions.md and the B-112 plan.
/// </summary>
public sealed class FluxSceneImagePromptCompiler : ISceneImagePromptCompiler
{
    private readonly SdxlSceneImagePromptBuilder _builder;

    public FluxSceneImagePromptCompiler(SdxlSceneImagePromptBuilder builder)
    {
        _builder = builder;
    }

    public SceneImageModelFamily Family => SceneImageModelFamily.Flux;
    public SceneImagePromptDialect PromptDialect => SceneImagePromptDialect.FluxNaturalLanguage;
    public ISceneImageLLMPromptBuilder PromptBuilder => _builder;
    public string CanonicalNegativePrompt => string.Empty;

    public string BuildNegativePrompt(SceneImageBeat beat, string pov) => string.Empty;
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

public sealed record SceneAssetPromptCompilation(
    string CompilerId,
    string CompilerVersion,
    string Prompt);

public static class SceneAssetPromptCompiler
{
    public static SceneAssetPromptCompilation Compile(
        string description,
        SceneAssetType assetType,
        ResolvedImageModel model)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("An asset description is required for prompt compilation.");

        var semanticDescription = description.Trim();
        return (model.SceneImageModelFamily, model.PromptDialect) switch
        {
            (SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags) => new(
                "scene-asset-pony-v6",
                "1",
                CompilePony(semanticDescription, assetType)),
            (SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage) => new(
                "scene-asset-sdxl-natural-language",
                "1",
                semanticDescription),
            (SceneImageModelFamily.Api, SceneImagePromptDialect.NaturalLanguage) => new(
                "scene-asset-api-natural-language",
                "1",
                semanticDescription),
            (SceneImageModelFamily.Flux, SceneImagePromptDialect.FluxNaturalLanguage) => new(
                "scene-asset-flux-natural-language",
                "1",
                semanticDescription),
            _ => throw new InvalidOperationException(
                $"No asset prompt compiler matches family '{model.SceneImageModelFamily}' and dialect '{model.PromptDialect}'.")
        };
    }

    private static string CompilePony(
        string description,
        SceneAssetType assetType)
    {
        var subjectCount = assetType is SceneAssetType.CharacterFace or SceneAssetType.CharacterBody
            ? "1person"
            : null;
        var semanticTags = description
            .Replace(";", ",", StringComparison.Ordinal)
            .Trim()
            .TrimEnd('.');
        var terms = new List<string>
        {
            "score_9", "score_8_up", "score_7_up", "score_6_up", "score_5_up", "score_4_up", "rating_explicit"
        };
        if (subjectCount is not null) terms.Add(subjectCount);
        terms.Add(semanticTags);
        var prompt = string.Join(", ", terms);
        if (prompt.Length > 800)
            throw new InvalidOperationException("Compiled Pony asset prompt exceeds the qualified 800-character limit.");
        return prompt;
    }
}