using System.Text;
using System.Text.Json;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The natural-language wardrobe-item prompt compiler: long, explicit photographic prose describing ONE garment as a
/// catalogue reference. Serves every model family whose dialect is natural language (Qwen-Image-2.1, SDXL/Juggernaut,
/// FLUX, OpenAI-protocol APIs) because they all want the same shape of prompt for this job; only the render workflow
/// behind them differs.
/// </summary>
/// <remarks>
/// The rules in <see cref="BuildSystemPrompt"/> are the measured behaviour of a NATIVE-REFERENCE model, which is what
/// makes this a compiler rather than a rewriting service:
/// <list type="bullet">
/// <item><description>
/// A reference's FRAMING dominates the render (adding a body reference widened a head-and-shoulders prompt to
/// torso+hips), so framing is stated rather than left to the model.
/// </description></item>
/// <item><description>
/// A reference's BACKGROUND leaks into the output, so a plain seamless backdrop is part of the instruction.
/// </description></item>
/// <item><description>
/// Clothing APPEARANCE is copied from the reference while the clothing STATE follows the prompt, which is exactly
/// what makes one garment transferable — and why the prompt must not describe an unrelated outfit.
/// </description></item>
/// <item><description>
/// Accessories leak and a face in the reference competes with the character's own identity, so the default framing
/// keeps the wearer anonymous and out of frame above the chin.
/// </description></item>
/// <item><description>
/// The reference budget is small (six references with a pose), so ONE garment per item keeps the wardrobe's share of
/// it honest.
/// </description></item>
/// </list>
/// </remarks>
public sealed class NaturalLanguageWardrobeItemPromptCompiler : IWardrobeItemPromptCompiler
{
    /// <summary>Below this the "prompt" is a shrug, not a description of a garment.</summary>
    public const int MinimumOutputChars = 180;

    /// <summary>Above this it stops being a prompt and becomes an essay the model will read past.</summary>
    public const int MaximumOutputChars = 4000;

    /// <summary>
    /// Subjects the reference photograph does not contain, and which therefore must not be NAMED in the drafted
    /// prompt - not even to exclude them.
    /// </summary>
    /// <remarks>
    /// This is the affirmative-only rule enforced rather than hoped for. The standard for this repository is that
    /// negations belong in the negative prompt, and the model this compiler serves takes no negative at all (Qwen
    /// -Image-2.1, cfg 1, where a negative is inert), so "no mannequin" conditions the text encoder on the word
    /// "mannequin" with nothing to subtract it.
    ///
    /// Two deliberate allowances, both learned from real garment wording (2026-09-29): a gender word followed by a
    /// possessive is a CATEGORY QUALIFIER ("women's boxer shorts", "men's swim shorts") rather than a person in the
    /// frame, so it stays legal; and "body" is standard fit vocabulary ("relaxed through the body", "close to the
    /// body"), so it is not policed. "torso" and "face" carry the same hazard as "body" and have no garment use.
    /// Word-boundary matched, so a garment word that merely contains one of these ("bodice", "surface", "leggings")
    /// is left alone.
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex ForbiddenSubject = new(
        @"\b(person|people|man|woman|women|girl|boy|male|female|human|model|wearer|mannequin|dummy|face|torso|hanger|prop|props|furniture|room|watermark|logo|collage)\b(?!['\u2019]s)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public NaturalLanguageWardrobeItemPromptCompiler(
        SceneImageModelFamily family,
        SceneImagePromptDialect promptDialect)
    {
        if (family == SceneImageModelFamily.Unknown)
        {
            throw new ArgumentException("A wardrobe-item prompt compiler needs a model family.", nameof(family));
        }

        if (promptDialect != SceneImagePromptDialect.NaturalLanguage
            && promptDialect != SceneImagePromptDialect.SdxlNaturalLanguage
            && promptDialect != SceneImagePromptDialect.FluxNaturalLanguage
            && promptDialect != SceneImagePromptDialect.Krea2NaturalLanguage)
        {
            throw new ArgumentException(
                $"Dialect '{promptDialect}' is not a natural-language dialect, so this compiler cannot serve it.",
                nameof(promptDialect));
        }

        Family = family;
        PromptDialect = promptDialect;
    }

    public SceneImageModelFamily Family { get; }

    public SceneImagePromptDialect PromptDialect { get; }

    public (string SystemPrompt, string UserPrompt) BuildMessages(WardrobeItemPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ItemDescription))
        {
            throw new InvalidOperationException(
                "A wardrobe item needs a description before its prompt can be compiled (for example \"a yellow sundress\").");
        }

        if (string.IsNullOrWhiteSpace(request.ImageSize))
        {
            throw new InvalidOperationException("A wardrobe item prompt needs the frame size it will be rendered at.");
        }

        return (SystemPrompt, BuildUserPrompt(request));
    }

    public string ParseOutput(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            throw new InvalidOperationException("The wardrobe-item prompt compiler returned empty output.");
        }

        var text = rawOutput.Trim();

        // A drafting model sometimes wraps prose in a JSON envelope or a fenced block even when asked not to. Unwrap
        // those two shapes, and nothing else: guessing further is how a stray brace ends up inside a render prompt.
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstBreak >= 0 && lastFence > firstBreak)
            {
                text = text[(firstBreak + 1)..lastFence].Trim();
            }
        }

        if (text.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                foreach (var property in new[] { "prompt", "Prompt", "description", "Description" })
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty(property, out var value)
                        && value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        text = value.GetString()!.Trim();
                        break;
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON after all: the text itself is the prompt, which is the common case.
            }
        }

        text = text.Trim().Trim('"').Trim();
        if (text.Length < MinimumOutputChars)
        {
            throw new InvalidOperationException(
                $"The wardrobe-item prompt compiler returned {text.Length} characters, which is too short to describe one "
                + $"garment as a reference (minimum {MinimumOutputChars}). Regenerate the prompt.");
        }

        if (text.Length > MaximumOutputChars)
        {
            throw new InvalidOperationException(
                $"The wardrobe-item prompt compiler returned {text.Length} characters, above the {MaximumOutputChars} the "
                + "render accepts. Regenerate the prompt.");
        }

        var forbidden = ForbiddenSubject.Match(text);
        if (forbidden.Success)
        {
            throw new InvalidOperationException(
                $"The compiled prompt names '{forbidden.Value}', which the reference photograph does not contain. "
                + "Excluding it does not work here: this model is given no negative prompt, so a negated noun is simply "
                + "that noun. Regenerate the prompt, or edit it so it describes only the garment and the surface it lies on.");
        }

        return text;
    }

    private static string BuildUserPrompt(WardrobeItemPromptRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Write the wardrobe reference prompt for this item: a product photograph of the garment, "
            + "described affirmatively, on its own.");
        builder.AppendLine();
        builder.Append("Item: ").AppendLine(request.ItemDescription.Trim());
        builder.Append("Frame: ").AppendLine(request.ImageSize.Trim());
        builder.AppendLine();
        builder.AppendLine("State what is in the photograph and nothing else. Return only the prompt text.");
        return builder.ToString();
    }

    /// <summary>
    /// The garment-reference rules. Kept as one constant so what the drafting model is told is reviewable in one
    /// place, and so a change to the reference contract is a change here rather than an edit scattered through prose.
    /// </summary>
    /// <remarks>
    /// Written AFFIRMATIVELY throughout, per this repository's researched standard
    /// (<c>scene-image-prompt-compiler-standards.instructions.md</c> §3.2: negations belong in the negative prompt,
    /// never as "no X" in the positive) and because the model this compiler serves carries NO negative at all — Qwen
    /// -Image-2.1 runs cfg 1, where a negative is inert. A "no mannequin" in a positive prompt with no negative to
    /// balance it is just the word "mannequin" in the conditioning text, so the instruction here is to describe the
    /// frame's contents and to delete any clause that reaches for something absent.
    /// </remarks>
    public const string SystemPrompt = """
        You write ONE image-generation prompt for a WARDROBE REFERENCE IMAGE.

        What you describe is a PRODUCT PHOTOGRAPH of a single garment on its own, laid out flat on a plain surface. It
        is not a scene and not a portrait. The image is later sent to an image model as a REFERENCE IMAGE, so how the
        garment is presented decides how it transfers.

        THE PHOTOGRAPH CONTAINS THE GARMENT AND THE SURFACE IT LIES ON.
        That is the entire inventory, and so it is the entire subject: the garment, its parts, the surface it rests
        on, the light and the camera. Everything you write describes one of those.

        WRITE AFFIRMATIVELY. Say what IS in the frame and stop there. Never phrase anything as an exclusion - not "no
        person", not "without a hanger", not "no props", not "avoid", not "not" - because the model reading this prompt
        is given no negative to balance it, and a negated noun is simply that noun. When you feel the urge to rule
        something out, delete the whole clause instead: leaving it unmentioned is what keeps it out of the picture.

        RULES
        1. ONE garment, described as it lies, using the trade's own construction words: type; sleeve or strap;
           neckline; bodice or fit; waistband; rise; inseam LENGTH (how short or long it is cut); leg opening (how wide
           or narrow the hem sits); skirt or leg shape; length; closures; fabric and weight; colour and where any print
           or pattern sits; trim and detail. Infer plausibly from the item named. Describe that garment and no other.
        2. State the arrangement: laid flat on a plain, seamless light-grey surface, arranged straight with its front
           facing the camera, sleeves, straps, collar and hem smoothed out so the cut reads, the whole garment inside
           the frame.
        3. State the treatment: even diffused lighting from above with soft, shadowless falloff; accurate fabric
           texture and colour; sharp focus; photorealistic e-commerce product photography; the garment centred and
           filling the frame.
        4. Write in full sentences, ordered: what is photographed, the surface it lies on, the garment and its
           construction, the arrangement, the light, the texture, the camera, the quality. Between 90 and 160 words.
           Prose rather than a tag list, with no numeric weights and no parenthetical emphasis.
        5. Read your draft once and delete every word that describes something absent from the photograph.
        6. Output ONLY the prompt text - no preamble, no explanation, no JSON, no field labels.
        """;
}

/// <summary>
/// Resolves a wardrobe-item compiler by the model's own family and dialect. Registration is data (the compiler list),
/// so adding a family is adding one entry rather than editing a switch.
/// </summary>
public sealed class WardrobeItemPromptCompilerRegistry : IWardrobeItemPromptCompilerRegistry
{
    private readonly IReadOnlyList<IWardrobeItemPromptCompiler> _compilers;

    public WardrobeItemPromptCompilerRegistry(IEnumerable<IWardrobeItemPromptCompiler> compilers)
    {
        ArgumentNullException.ThrowIfNull(compilers);
        _compilers = compilers.ToList();
        if (_compilers.Count == 0)
        {
            throw new InvalidOperationException("No wardrobe-item prompt compiler is registered.");
        }
    }

    public IReadOnlyList<IWardrobeItemPromptCompiler> Compilers => _compilers;

    public bool TryResolve(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect,
        out IWardrobeItemPromptCompiler compiler)
    {
        compiler = _compilers.FirstOrDefault(candidate =>
            candidate.Family == family && candidate.PromptDialect == dialect)!;
        return compiler is not null;
    }

    public IWardrobeItemPromptCompiler Require(
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect)
    {
        if (TryResolve(family, dialect, out var compiler))
        {
            return compiler;
        }

        var supported = string.Join(
            ", ",
            _compilers.Select(candidate => $"{candidate.Family}/{candidate.PromptDialect}").Distinct(StringComparer.Ordinal));

        throw new InvalidOperationException(
            $"No wardrobe-item prompt compiler serves family '{family}' with dialect '{dialect}'. A wardrobe item "
            + $"reference has its own framing and backdrop rules, so it is not compiled by the scene-prompt builder. "
            + $"Registered combinations: {supported}. Choose a model this tab can drive.");
    }
}
