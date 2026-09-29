using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// What a wardrobe-item prompt is compiled FROM. The operator types a short name ("a yellow sundress") and the
/// compiler expands it into the long, explicit prompt a reference image needs; the target model decides WHICH
/// compiler, so "geared for the chosen model" is structural rather than a hint inside the text.
/// </summary>
/// <param name="ItemDescription">The operator's own words, verbatim. Never rewritten by the caller.</param>
/// <param name="ImageSize">The frame the item will be rendered at, because framing is part of a reference's meaning.</param>
public sealed record WardrobeItemPromptRequest(
    string ItemDescription,
    string ImageSize);

/// <summary>
/// Drafts the prompt for a WARDROBE ITEM reference image, geared to one image-model family and dialect.
/// </summary>
/// <remarks>
/// A separate contract from <see cref="ISceneImagePromptCompiler"/> on purpose: a scene prompt describes people,
/// action, place and moment and is drafted from a compiled brief, while a wardrobe item is a CATALOGUE image of one
/// garment whose framing, backdrop and light ARE the mechanism by which the garment later transfers into other
/// renders (measured: a reference's framing dominates the output, and its background leaks). Sharing one builder would
/// mean the rules that make a good reference compete with the rules that make a good scene.
/// </remarks>
public interface IWardrobeItemPromptCompiler
{
    SceneImageModelFamily Family { get; }

    SceneImagePromptDialect PromptDialect { get; }

    /// <summary>The system and user messages for the prompt-drafting model.</summary>
    (string SystemPrompt, string UserPrompt) BuildMessages(WardrobeItemPromptRequest request);

    /// <summary>
    /// Parses the drafting model's output into the prompt to store. Tolerates a quoted or fenced body and fails fast
    /// on empty, absurdly short, or clearly-not-a-prompt output, because a stored prompt is what the render uses
    /// verbatim (the image row names its compiler, so nothing re-compiles it).
    /// </summary>
    string ParseOutput(string rawOutput);
}

/// <summary>
/// The ONE place that says which wardrobe-item compiler serves a model. Resolution is by the model's own
/// family + dialect, and a family with no compiler is refused by name rather than served by a fallback: a Pony tag
/// model and a natural-language model do not want the same garment prompt, and silently using one for the other is
/// how a wardrobe item comes back as tag soup.
/// </summary>
public interface IWardrobeItemPromptCompilerRegistry
{
    IReadOnlyList<IWardrobeItemPromptCompiler> Compilers { get; }

    /// <summary>
    /// Whether a compiler exists, for a picker that should offer only the models this tab can actually drive.
    /// </summary>
    bool TryResolve(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect,
        out IWardrobeItemPromptCompiler compiler);

    /// <summary>
    /// The compiler for a model, or a failure naming what is missing and what to do about it.
    /// </summary>
    IWardrobeItemPromptCompiler Require(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect);
}
