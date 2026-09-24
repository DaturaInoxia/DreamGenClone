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
///
/// The builder is the NATURAL-LANGUAGE one by concrete type, NOT <c>ISceneImageLLMPromptBuilder</c>:
/// that interface is implemented by both builders, so DI can only bind it to one of them (it is bound to the
/// Pony tag builder), and injecting it here silently compiled API models with Pony's tag system prompt. See
/// the same note on <see cref="QwenImage21SceneImagePromptCompiler"/>.
/// </summary>
public sealed class ApiSceneImagePromptCompiler : ISceneImagePromptCompiler
{
    private readonly SdxlSceneImagePromptBuilder _builder;

    public ApiSceneImagePromptCompiler(SdxlSceneImagePromptBuilder builder)
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

/// <summary>
/// Natural-language compiler for local Qwen-Image-2.1 scene images (ComfyUI unified
/// text-to-image + reference-conditioned generation). 2.1 is a single 7B DiT behind one
/// <c>TextEncodeQwenImage21</c> node that returns positive, negative AND the latent, and that takes
/// up to sixteen reference images through its <c>images</c> autogrow input. The official path runs
/// cfg 1 with euler/simple, where the negative prompt is inert, so this compiler carries NO
/// canonical negative - the same posture as API and FLUX models.
///
/// The positive prompt is a natural-language photography brief, which is why the builder is the
/// NATURAL-LANGUAGE builder by concrete type and not <c>ISceneImageLLMPromptBuilder</c>. Two builders
/// implement that interface (Pony tags and natural language), so DI can bind it to only one of them, and it is
/// bound to the Pony tag builder - injecting the interface here silently compiled 2.1 prompts with the Pony tag
/// system prompt. Reported 2026-09-24: with 2.1 selected the Studio's "Generate Prompt" returned
/// <c>score_9, score_8_up, ... rating_explicit, 1girl, ...</c> into the natural-language ("SDXL") draft, because
/// the record's style came from the requested style while the text came from the Pony builder. The render
/// workflow is unaffected either way; only the dialect of the drafted text was wrong.
/// </summary>
public sealed class QwenImage21SceneImagePromptCompiler : ISceneImagePromptCompiler
{
    private readonly SdxlSceneImagePromptBuilder _builder;

    public QwenImage21SceneImagePromptCompiler(SdxlSceneImagePromptBuilder builder)
    {
        _builder = builder;
    }

    public SceneImageModelFamily Family => SceneImageModelFamily.QwenImage21;
    public SceneImagePromptDialect PromptDialect => SceneImagePromptDialect.NaturalLanguage;
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
            (SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage) => new(
                "scene-asset-qwen-image-21-natural-language",
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