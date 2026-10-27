using System.Text;
using System.Text.Json;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Shared message assembly for the NATURAL-LANGUAGE scene-image prompt builders (SDXL-family and Qwen-Image-2.1).
/// Both produce natural-language, photorealistic image prompts from the same story-moment / canonical-brief inputs;
/// the ONLY thing that differs is the researched compiler instruction text each family feeds the pre-processor model,
/// supplied by the subclasses through <see cref="BuildSystemPrompt"/> and <see cref="BuildCanonicalSystemPrompt"/>.
///
/// <para>
/// This class exists so the SDXL and Qwen builders share one user-prompt assembly (story moment, scene context,
/// character appearance, image settings) without duplicating it, while each keeps its own dialect's system prompt.
/// It is deliberately NOT registered in DI: only the concrete family builders are, because two builders implement
/// <see cref="ISceneImageLLMPromptBuilder"/> and a single interface binding can only ever be right for one dialect.
/// </para>
/// </summary>
public abstract class NaturalLanguageSceneImagePromptBuilder : ISceneImageLLMPromptBuilder
{
    public const int InputExcerptMaxChars = 1200;

    /// <summary>
    /// The parser's safety ceiling — NOT the prompt's budget. The budget is the target checkpoint's own
    /// <c>MinChars</c>/<c>MaxChars</c> (a Qwen-Image-2.1 row asks for 3400, a Krea/BigLust row for 800), so a ceiling
    /// pinned to one family's budget would refuse a legitimate long-form caption from another.
    /// </summary>
    public const int OutputPromptMaxChars = 4000;

    /// <summary>
    /// The standing rules appended to every natural-language family's canonical system prompt.
    ///
    /// <para>
    /// They live here, not in each family's text, because they are facts about how the COMPILER is driven rather than
    /// about any checkpoint family — and because four copies of a rule is four chances for one of them to be lost in
    /// an edit. This rule states what the accompanying USER REMOVALS notice means; without it the notice is a claim
    /// the model has not been told to honour.
    /// </para>
    /// </summary>
    internal static readonly string CanonicalAuthorityRules = """

        USER REMOVALS ARE AUTHORITATIVE: when this request carries a USER REMOVALS notice, those elements were removed
        deliberately. Never render, describe, mention, infer or re-derive a removed element, and never substitute an
        equivalent taken from another element.

        NAMES ARE NOT ANCHORS (B-166 D9): a character's NAME may appear only when that person is identified either by a
        bound reference image that resolves to them, or by a full appearance description in this same prompt. Otherwise
        refer to them by their place in the frame and their traits ("the woman on the LEFT"), never by name.
        """;

    /// <summary>
    /// The standing rules for a <c>User Intent</c> draft, where the operator's own prose is the content and there is
    /// no brief to strip supplied elements out of.
    /// </summary>
    /// <remarks>
    /// A scene draft is protected from describing what an image supplies by REMOVAL: the element is taken out of the
    /// brief before the compiler reads it. An intent draft has no brief, so the same protection has to be stated. Both
    /// halves below are measured, on SceneImage 2ecd4862 (2026-10-17), from one intent and one render:
    /// <list type="bullet">
    /// <item><description>
    /// <b>Element ownership.</b> The intent said "standing at the clothes line"; the compiler elaborated that into
    /// "two posts", "a low fence" and "scuffed ground" — none of which appear in the intent, and none of which are in
    /// the bound location plate (a porch with a rotary clothesline and a flowerbed). The render followed the invented
    /// prose and the location reference was lost.
    /// </description></item>
    /// <item><description>
    /// <b>Absence is not an inference.</b> The same intent said "and she wears nothing over it", meaning no extra
    /// layer on the outfit. The compiler expanded that into "she wears nothing over them, so her shoulders, arms and
    /// midriff are bare", and the render dropped the white tank top that the bound clothing reference shows. Worse, a
    /// blanket "do not describe a supplied element" applied to clothing as well produced a NUDE render
    /// (SceneImage c0d77b66, 2026-10-18): with the outfit left unstated, nothing dressed the figure. A garment
    /// reference therefore has to be NAMED and PUT ON; only the elements an image owns outright stay silent.
    /// </description></item>
    /// </list>
    /// </remarks>
    internal static readonly string IntentAuthorityRules = """

        REFERENCES OUTRANK PROSE: where the intent is silent, vague or ambiguous about an element that a reference
        image supplies, the image decides. Never elaborate on a reference-supplied element in words — a mention of a
        place does not license inventing what it contains. Anything a reference shows is in the picture whether or not
        this intent says so, and anything a clothing reference shows is WORN.
        """;

    /// <summary>The beat-path system prompt for this builder's family (story prose in).</summary>
    protected abstract string BuildSystemPrompt();

    /// <summary>
    /// The canonical-brief-path system prompt for this builder's family (immutable Still brief in).
    /// </summary>
    /// <param name="coverage">
    /// The element classes the bound images already supply. A family whose documented contract demands a full
    /// description must switch to its anchored form when images carry part of the picture, or it invents the rest
    /// (measured 2026-10-25, SceneImage 3ade4908).
    /// </param>
    protected abstract string BuildCanonicalSystemPrompt(IntentReferenceCoverage coverage);

    public (string SystemPrompt, string UserPrompt) BuildMessages(
        CompiledMediaBrief brief,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? refineInstruction)
        => BuildMessages(brief, pov, settings, resolvedPolicy, refineInstruction, null);

    public (string SystemPrompt, string UserPrompt) BuildMessages(
        CompiledMediaBrief brief,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? refineInstruction,
        IReadOnlyList<Character>? characters,
        IReadOnlyDictionary<string, string>? appearanceOverrides = null,
        IReadOnlyDictionary<string, string>? canonicalAppearance = null,
        ScenePromptOverrides? effectiveOverrides = null,
        IReadOnlyList<ReferenceApplicationSelection>? referenceBindings = null,
        SceneImagePovProjection.Projection? povProjection = null,
        IReadOnlyDictionary<string, string>? appearanceElementOverrides = null)
    {
        CompiledMediaContractValidator.ValidateBrief(brief);
        if (brief.MediaKind != MediaProductionKind.StillImage || brief.Status != MediaCompilerStatus.Complete)
            throw new InvalidOperationException("Canonical scene-image prompt generation requires a complete compiled Still brief.");
        if (string.IsNullOrWhiteSpace(pov))
            throw new InvalidOperationException("Canonical scene-image prompt generation requires the production group POV.");

        var systemPrompt = BuildCanonicalSystemPrompt(
            CoverageOf(referenceBindings, ReferenceBindingPromptRemoval.Derive(referenceBindings))) + CanonicalAuthorityRules
            + SceneImagePovProjection.PovSystemRule(povProjection)
            + (povProjection?.SceneSuppliedByReference == true ? SceneImagePovProjection.ReferenceSuppliedRoomRule : string.Empty);
        var userPrompt = BuildCanonicalUserPrompt(brief, pov, settings, resolvedPolicy, refineInstruction, characters, appearanceOverrides, canonicalAppearance, effectiveOverrides, referenceBindings, povProjection, appearanceElementOverrides);
        return (systemPrompt, userPrompt);
    }

    /// <summary>
    /// User Intent source: the operator's own instruction IS the content, compiled into this builder's dialect. The
    /// scene brief is not consulted at all, which is what makes the two sources a single decision rather than a blend.
    /// </summary>
    public (string SystemPrompt, string UserPrompt) BuildIntentMessages(
        string userIntent,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        IReadOnlyList<ReferenceApplicationSelection>? referenceBindings = null)
    {
        if (string.IsNullOrWhiteSpace(userIntent))
        {
            throw new InvalidOperationException("User Intent prompt generation requires the operator's intent text.");
        }

        if (string.IsNullOrWhiteSpace(pov))
        {
            throw new InvalidOperationException("User Intent prompt generation requires the production group POV.");
        }

        ArgumentNullException.ThrowIfNull(settings);

        var text = new StringBuilder();
        text.AppendLine("OPERATOR INTENT — AUTHORITATIVE. The operator wrote this instead of the scene brief: compile it into");
        text.AppendLine("this model's prompt dialect, honouring what it asks for. Do not substitute scene content for it, and do not");
        text.AppendLine("invent elements it does not request.");
        text.AppendLine();
        text.AppendLine(userIntent.Trim());
        text.AppendLine();
        text.AppendLine("FRAME CONTEXT (data, not scene prose):");
        // The POV role is a CAMERA, not a subject — so its name is not supplied at all. Measured 2026-10-21: naming it
        // here put the person in a frame that has none ("…as seen by Becky"), which then failed the compile. A shot
        // does not need the camera holder's name; the operator's intent and the bound images carry the content.
        text.AppendLine("- Camera: this is a participant point-of-view frame — the photograph is what one person who is");
        text.AppendLine("  present in the scene sees from where they stand.");
        text.AppendLine("- That person IS the camera. They are never named, never depicted and never described; the caption");
        text.AppendLine("  describes only what is in front of that camera, and nothing of that person's own body, hair, hands or");
        text.AppendLine("  clothing.");
        text.AppendLine($"- Style: {settings.Style}");
        text.AppendLine($"- Image size: {settings.ImageSize}");
        text.AppendLine($"- Explicitness: {SceneImageExplicitnessWording.Directive(settings.Explicitness)}");
        var referencesBound = referenceBindings is { Count: > 0 };

        // The elements the bound images take over. Computed ONCE and used twice — for the ownership notice and for
        // choosing the compile contract — so the two cannot disagree about whether this draft is reference-bound.
        // "Bound" means an image actually ARRIVES: a text-only binding supplies nothing and must not switch the
        // contract, which is the same predicate ReferenceBindingPromptRemoval.ImpliesRemoval applies.
        IReadOnlyList<ScenePromptFieldOverride> suppliedElements =
            referencesBound ? ReferenceBindingPromptRemoval.Derive(referenceBindings) : [];

        if (referencesBound)
        {
            // Number by SEND ORDER, not by binding.Ordinal. The render renumbers its references 1..N when it submits
            // them, so a plan whose stored ordinals start at 2 (a slot that was bound and then cleared) would print a
            // list that is off by one against the `<imageN>` tags the intent actually uses. Measured 2026-10-17 on
            // SceneImages 39af0b11: the list said <image2> was the unclothed body reference while the intent meant the
            // wardrobe, so the compiler read "clothing comes from <image2>" as the nude build plate and left her topless.
            text.AppendLine("- Reference images this render receives, numbered in the order they are sent:");
            var position = 0;
            foreach (var binding in referenceBindings.OrderBy(binding => binding.Ordinal))
            {
                position++;
                var label = string.IsNullOrWhiteSpace(binding.ReferenceLabel)
                    ? string.Empty
                    : $" ({binding.ReferenceLabel.Trim()})";
                text.AppendLine($"  <image{position}> — {binding.SemanticRole}{label}");
            }

            AppendReferenceSuppliedElements(text, referenceBindings, suppliedElements);
        }
        else
        {
            text.AppendLine("- Reference images: none. This render is text-only.");
        }

        return (BuildIntentSystemPrompt(CoverageOf(referenceBindings, suppliedElements))
            + CanonicalAuthorityRules
            + IntentAuthorityRules
            + IntentCameraRule, text.ToString());
    }

    /// <summary>
    /// The standing rule that keeps an operator's camera instruction alive under a reference-derived contract.
    /// </summary>
    /// <remarks>
    /// The action-only contract exists so the compiler does not describe what a bound image already supplies. The CAMERA
    /// is never supplied by an image, so it must not be treated as a supplied element — but the contract does not say
    /// that, and a shot written by the operator was dropped entirely (measured 2026-10-21, prompt record 8466c595).
    /// </remarks>
    internal static readonly string IntentCameraRule = """

        THE OPERATOR'S CAMERA INSTRUCTION IS IMMUTABLE. If the intent names a shot size, an angle or a direction —
        "close up", "tight", "wide", "from below", "from above", "low angle", "looking up", "looking down" — reproduce
        that wording in the prompt EXACTLY as written, as the first element. No reference image supplies the camera, so
        no contract in this request may remove it.
        """;

    /// <summary>
    /// The element classes the bound images actually supply, derived from the SAME
    /// <see cref="ReferenceBindingPromptRemoval.Derive"/> scopes the ownership notice prints, so the contract can never
    /// claim an element an image does not carry.
    /// </summary>
    private static IntentReferenceCoverage CoverageOf(
        IReadOnlyList<ReferenceApplicationSelection>? bindings,
        IReadOnlyList<ScenePromptFieldOverride> supplied)
    {
        bool setting = false, appearance = false, clothing = false, pose = false;
        foreach (var field in supplied)
        {
            var scope = field.ElementKey;
            var separator = scope.LastIndexOf('.');
            var name = separator < 0 ? scope : scope[(separator + 1)..];

            // B-166 D10: appearance is two elements now (`appearance.face` / `appearance.build`). Both are the
            // APPEARANCE class for contract purposes; a last-segment read would see only "face"/"build".
            if (scope.EndsWith(".appearance.face", StringComparison.OrdinalIgnoreCase)
                || scope.EndsWith(".appearance.build", StringComparison.OrdinalIgnoreCase))
            {
                appearance = true;
                continue;
            }

            switch (name)
            {
                // The ROOM's own structure is what a location reference supplies; environment and lighting are not
                // removed for it any more (operator decision 2026-10-25), so they must not mark the class supplied
                // either — a class this reports as covered is a class the intent contract forbids the operator to state.
                case "location":
                    setting = true;
                    break;
                case "appearance":
                    appearance = true;
                    break;
                case "clothing":
                    clothing = true;
                    break;
                case "position" or "visibleAction":
                    pose = true;
                    break;
            }
        }

        // A PROP removes no brief element (an object is additive), so it never appears in the scope list above - but it
        // still supplies an image, and the contract has to stop the compiler writing its own version of it.
        var objects = bindings is not null && bindings.Any(binding =>
            ReferenceBindingPromptRemoval.ImpliesRemoval(binding)
            && ReferenceBindingShape.SlotKindOf(binding) == ImageStepSlotKind.Prop);

        return new IntentReferenceCoverage(setting, appearance, clothing, pose, objects);
    }

    /// <summary>
    /// The system prompt a User Intent draft is compiled under.
    /// </summary>
    /// <param name="coverage">
    /// The element classes the bound images actually supply. This is the one condition that changes the compile
    /// contract: a bound image and a prompt that describes what the image supplies are in direct conflict, and an
    /// element nothing supplies stays the operator's to state (which is what makes a clothing change, or a removed
    /// garment, expressible at all).
    /// </param>
    /// <remarks>
    /// The default keeps every family that has no reference-mode dialect byte-identical to before. Qwen-Image-2.1
    /// overrides it, because its long-form contract ("a full, concrete account of the finished picture", 300-1600
    /// characters) forces the compiler to write the room, the outfit and the objects no matter what else it is told —
    /// measured on SceneImage f74058d3, 2026-10-18.
    /// </remarks>
    protected virtual string BuildIntentSystemPrompt(IntentReferenceCoverage coverage) => BuildCanonicalSystemPrompt(coverage);

    /// <summary>
    /// States which elements the reference images OWN, so the compiler does not write them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the intent path's counterpart to the scene path's element removal: the scene path strips a supplied
    /// element out of the brief so the compiler never sees it, and there is no brief here — only the operator's prose.
    /// Saying which elements arrive as images is the only thing that stops the compiler describing them itself, which
    /// is what let an invented "two posts, a low fence, scuffed ground" override the bound location plate.
    /// </para>
    /// <para>
    /// The element set comes from <see cref="ReferenceBindingPromptRemoval.Derive"/> — the SAME function
    /// <see cref="ReferenceBindingPromptRemoval.Merge"/> uses on the scene path — and the names come from
    /// <see cref="StepPromptElementPlan.Describe"/>, so this notice cannot disagree with what the scene path removes or
    /// with what the operator is shown as left out. A binding that takes nothing over (text-only, or an image the slot
    /// plan refuses) contributes nothing here, exactly as it removes nothing there.
    /// </para>
    /// </remarks>
    private static void AppendReferenceSuppliedElements(
        StringBuilder text,
        IReadOnlyList<ReferenceApplicationSelection> bindings,
        IReadOnlyList<ScenePromptFieldOverride> supplied)
    {
        if (supplied.Count == 0)
        {
            return;
        }

        var tags = ReferenceRoleClauses.TagsFor(bindings);
        var silent = new List<string>();
        var worn = new List<string>();
        var placed = new List<string>();

        foreach (var binding in ReferenceBindingShape.InSendOrder(bindings))
        {
            if (!ReferenceBindingPromptRemoval.ImpliesRemoval(binding))
            {
                continue;
            }

            var slotKind = ReferenceBindingShape.SlotKindOf(binding)!.Value;
            var named = tags.TryGetValue(binding, out var tag)
                ? tag
                : $"the {binding.SemanticRole} reference";

            // A prop replaces no brief element, so it has no scope to classify — but it must still be NAMED, or the
            // render treats an object reference as another picture to ignore.
            if (slotKind == ImageStepSlotKind.Prop)
            {
                placed.Add($"- {named} is the OBJECT in this frame: it appears as that image shows it — its shape,");
                placed.Add("  material and colour — placed where the composition puts it. Do not substitute a different");
                placed.Add("  object, do not restyle it, and do not leave it out.");
                continue;
            }

            var scopes = StepPromptElementPlan.ScopesFor(slotKind, binding.ActorKey);
            if (scopes.Count == 0)
            {
                continue;
            }

            foreach (var group in scopes
                .GroupBy(StepPromptElementPlan.OwnershipOf)
                .OrderBy(group => group.Key))
            {
                // Deduplicated: the room's scope list reaches the same element from several blocks (the moment and
                // both continuity sides), and naming "the setting" four times teaches the compiler nothing.
                var names = string.Join("; ", group
                    .Select(StepPromptElementPlan.Describe)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                var line = $"- {named} supplies {names}.";
                if (group.Key == StepPromptElementPlan.PromptElementOwnership.SuppliedWorn)
                {
                    worn.Add(line);
                }
                else
                {
                    silent.Add(line);
                }
            }
        }

        text.AppendLine();
        text.AppendLine("REFERENCE-SUPPLIED ELEMENTS — AUTHORITATIVE:");
        text.AppendLine("These elements arrive as IMAGES in this request, and an image is exact where prose beside it is not.");

        if (silent.Count > 0)
        {
            text.AppendLine("Do NOT describe these in words and do not re-derive them from another description:");
            foreach (var line in silent)
            {
                text.AppendLine(line);
            }
        }

        if (worn.Count > 0)
        {
            text.AppendLine("These are WORN. Say what she has on, naming each garment the image actually shows, and dress");
            text.AppendLine("the figure in it: a clothing reference is a photograph of garments, not of a body, so saying");
            text.AppendLine("nothing about it leaves her with nothing on:");
            foreach (var line in worn)
            {
                text.AppendLine(line);
            }

            text.AppendLine("OMIT CONTRADICTIONS: leave out of the prompt every statement about what she is NOT wearing or");
            text.AppendLine("what is NOT added on top — \"she wears nothing over it\", \"her shoulders are bare\", \"nothing");
            text.AppendLine("over them\" and any remark like them. Do not copy such a sentence from the intent: the clothing");
            text.AppendLine("reference already shows the truth, and carrying the sentence over undresses her.");
        }

        if (placed.Count > 0)
        {
            text.AppendLine("These are OBJECTS that must appear in the picture. Name each one in the prompt and give it a");
            text.AppendLine("place in the frame; an object nobody mentions is an object nobody draws:");
            foreach (var line in placed)
            {
                text.AppendLine(line);
            }
        }
    }

    public (string SystemPrompt, string UserPrompt) BuildMessages(
        RolePlaySession session,
        RolePlayInteraction interaction,
        AdaptiveScenarioState scenarioState,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? excerptOverride,
        string? refineInstruction,
        IReadOnlyList<Character>? characters = null)
    {
        return (
            BuildSystemPrompt(),
            BuildUserPrompt(session, interaction, scenarioState, settings, resolvedPolicy, scenarioState.CurrentPhase, excerptOverride, refineInstruction, characters, null, null));
    }

    /// <summary>
    /// Full-turn variant: builds the prompt from the whole turn so the Narrative (omniscient)
    /// interaction contributes setting/environment detail. The selected interaction remains the
    /// primary subject; the turn's other interactions are appended as context.
    /// </summary>
    public (string SystemPrompt, string UserPrompt) BuildMessages(
        RolePlaySession session,
        FullTurnContext fullTurn,
        AdaptiveScenarioState scenarioState,
        SceneImageStudioSettings settings,
        ImageContentPolicy resolvedPolicy,
        string? excerptOverride,
        string? refineInstruction,
        IReadOnlyList<Character>? characters = null,
        SceneImageBeat? selectedBeat = null,
        string? pov = null)
    {
        var selected = fullTurn.SelectedInteraction;
        var baseUser = BuildUserPrompt(session, selected, scenarioState, settings, resolvedPolicy, scenarioState.CurrentPhase, excerptOverride, refineInstruction, characters, selectedBeat, pov);

        var turnContext = BuildFullTurnContextBlock(fullTurn, selected);
        if (!string.IsNullOrWhiteSpace(turnContext))
        {
            baseUser += "\n" + turnContext;
        }

        if (selectedBeat is not null)
        {
            var renderBrief = SceneImageRenderBriefBuilder.Build(
                selectedBeat,
                pov ?? throw new InvalidOperationException("A POV is required with a selected beat."),
                settings,
                resolvedPolicy);
            baseUser += "\n" + renderBrief;
        }

        return (BuildSystemPrompt(), baseUser);
    }

    public SceneImagePreprocessorResult ParseOutput(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            throw new InvalidOperationException("Scene image pre-processor returned empty output.");
        }

        var trimmed = rawOutput.Trim();
        string prompt;
        string excerpt = string.Empty;

        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("prompt", out var promptElement)
                    && promptElement.ValueKind == JsonValueKind.String)
                {
                    prompt = promptElement.GetString()?.Trim() ?? string.Empty;
                    if (doc.RootElement.TryGetProperty("excerpt", out var excerptElement)
                        && excerptElement.ValueKind == JsonValueKind.String)
                    {
                        excerpt = excerptElement.GetString()?.Trim() ?? string.Empty;
                    }
                }
                else
                {
                    prompt = trimmed;
                }
            }
            catch (JsonException)
            {
                prompt = trimmed;
            }
        }
        else
        {
            prompt = trimmed;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException("Scene image pre-processor returned a prompt that is empty after parsing.");
        }

        prompt = SceneImagePromptRefusal.EnsureIsPrompt(prompt);

        // B-167: a caption is ONE paragraph. A model that hard-wraps its answer, or answers one sentence per line, is
        // producing the right content in a shape the image model does not care about — newlines carry no meaning in an
        // image prompt — so the shape is normalised here rather than refused. A genuine LIST (bullets, numbering) or a
        // per-person element recap is still detected and refused by ValidateCaptionShape, because those are content
        // defects, not wrapping.
        prompt = CollapseToSingleParagraph(prompt);

        if (prompt.Length > OutputPromptMaxChars)
        {
            throw new InvalidOperationException(
                $"Scene image pre-processor returned an overlong prompt ({prompt.Length} chars); cap is {OutputPromptMaxChars}.");
        }

        return new SceneImagePreprocessorResult(prompt, excerpt);
    }

    /// <summary>
    /// Collapses a produced caption to a single paragraph: every newline becomes a space and whitespace runs are
    /// squeezed. Lossless — an image prompt's meaning does not include its line breaks — and it is what makes a
    /// hard-wrapped answer acceptable instead of a refused render.
    /// </summary>
    internal static string CollapseToSingleParagraph(string prompt)
        => System.Text.RegularExpressions.Regex.Replace(
            prompt.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' '),
            @"\s{2,}",
            " ").Trim();

    private static string BuildCanonicalUserPrompt(
        CompiledMediaBrief brief,
        string pov,
        SceneImageStudioSettings settings,
        ImageContentPolicy policy,
        string? refineInstruction,
        IReadOnlyList<Character>? characters,
        IReadOnlyDictionary<string, string>? appearanceOverrides,
        IReadOnlyDictionary<string, string>? canonicalAppearance = null,
        ScenePromptOverrides? effectiveOverrides = null,
        IReadOnlyList<ReferenceApplicationSelection>? referenceBindings = null,
        SceneImagePovProjection.Projection? povProjection = null,
        IReadOnlyDictionary<string, string>? appearanceElementOverrides = null)
    {
        var sb = new StringBuilder();
        // The brief is the SINGLE semantic source. The provider-request snapshot restates it and used to hand back
        // every element an override had removed, which is why it is not sent (debug 052); the DB record keeps it for
        // provenance. Only the brief reaches the pre-processor.
        //
        // B-161: the operator's frame override is applied BEFORE the brief is handed over, so the frame the model reads
        // is the frame the render will use. Without this the model wrote the compiled profile's framing into the prose
        // ("The camera sits at eye level in a medium shot") while the render appended the operator's — two cameras
        // stated in one prompt, which is how a subject marked "in the background" still filled the frame. A brief that
        // carries no frame has none to correct, so it stays exactly as compiled.
        var blocking = SceneBlockingJson.TryRead(brief.SemanticInputSnapshotJson);
        // B-166 D7: an Omniscient still leans wide/establishing, from the value the staging profile snapshotted into the
        // brief. A participant POV is egocentric and gets no external framing, and an explicit operator framing wins.
        var omniscientLean = string.Equals(pov.Trim(), SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase)
            ? blocking?.Camera.OmniscientShotSize
            : null;
        var omniscientAngle = string.Equals(pov.Trim(), SceneImagePovFramer.Omniscient, StringComparison.OrdinalIgnoreCase)
            ? settings.OmniscientAngle
            : null;
        var effectiveBlocking = blocking is null
            ? null
            : SceneBlockingResolver.ApplyForPrompt(blocking, settings.SceneBlockingOverride, omniscientLean, omniscientAngle);
        // POV is a FRAMING dimension (CR-006 P5): the brief stays POV-independent and this projection states the frame
        // this POV actually sees. The caller resolves it from the COMPILED brief (this `brief` may already have had
        // elements removed by overrides/bindings); a direct caller with no compiled brief gets it resolved here.
        var projection = povProjection ?? SceneImagePovProjection.Resolve(
            brief, pov, effectiveBlocking, operatorCamera: settings.SceneBlockingOverride?.Camera,
            cameraHolderPartsMode: settings.CameraHolderParts, cameraHolderPartsText: settings.CameraHolderPartsText);
        sb.AppendLine("CANONICAL STILL BRIEF (the selected facts this request must realize):");
        // A reference-supplied room also drops the frame rationale: it is prose ABOUT the room ("barred slat light and
        // the workbench giving a gritty still composition") and the compiler echoes it as room description. The frame
        // itself — camera and placements — is untouched.
        var promptBlocking = effectiveBlocking is not null && projection.SceneSuppliedByReference
            ? effectiveBlocking with { Rationale = string.Empty }
            : effectiveBlocking;
        var briefJson = projection.IsOmniscient
            ? SceneBlockingJson.Replace(brief.SemanticInputSnapshotJson, promptBlocking)
            : SceneBlockingJson.WithoutBlocking(brief.SemanticInputSnapshotJson);
        if (projection.SceneSuppliedByReference)
        {
            briefJson = SceneImagePovProjection.DropCharacterLocations(briefJson);
        }
        // B-166 POV fix: a participant POV is the viewpoint's EYES. The viewpoint must not appear in the data the
        // compiler reads, or it writes an external two-person scene no matter what the projection says. A member the
        // operator rewrote in the Prompt Input inspector survives, with the camera holder's name scrubbed out of it —
        // an operator instruction is not the enrichment's prose.
        if (!projection.IsOmniscient)
        {
            briefJson = SceneImagePovProjection.DropViewpoint(
                briefJson, projection.UnseenViewpointProfileKey, OperatorEditedMembers(effectiveOverrides));
        }
        // B-167 Part B: the compiler is handed LABELLED FACTS, not the raw snapshot. A fact sheet states each fact once
        // and marks an operator-edited value as intent to translate, which is what stops it being copied back.
        sb.AppendLine(StillBriefFactSheet.Render(
            briefJson,
            effectiveOverrides,
            projection.UnseenViewpointProfileKey,
            ParticipantNames.FromProjection(projection)));
        var appearanceBlock = BuildCanonicalCharacterAppearanceBlock(brief, pov, characters, appearanceOverrides, canonicalAppearance, appearanceElementOverrides);
        if (!string.IsNullOrWhiteSpace(appearanceBlock))
        {
            sb.AppendLine(appearanceBlock);
            sb.AppendLine();
        }
        // A participant POV is a CAMERA, so its name is never put in front of the compiler. It used to read
        // "PRODUCTION POV: Becky", and the model copied that name straight into the caption ("as seen by Becky"),
        // failing the contract twice in a row (record f5a2a9df, 2026-10-22). The SHOT block below already states the
        // frame; a name adds nothing a render can use. The locally resolved projection decides this, not the optional
        // parameter: a caller that lets this method resolve the frame gets the same name-free line.
        sb.AppendLine(projection is { IsOmniscient: false }
            ? "PRODUCTION POV: a participant standing in the scene — that person IS the camera and is never in the picture."
            : $"PRODUCTION POV: {pov}");
        sb.AppendLine(projection.ToPromptText());
        sb.AppendLine($"IMAGE SETTINGS: style={settings.Style}; size={settings.ImageSize}; aspect={settings.AspectRatio}; policy={policy}");
        sb.AppendLine($"EXPLICITNESS: {SceneImageExplicitnessWording.Directive(settings.Explicitness)}");
        if (!string.IsNullOrWhiteSpace(refineInstruction))
            sb.AppendLine($"REFINE INSTRUCTION: {refineInstruction.Trim()}");

        // Prompt adaptation is TWO halves and both have to reach the pre-processor. The removal notice stops it
        // re-deriving an element an override deliberately took out (the brief's neighbours often restate the same
        // instant). The role clause says what each reference IMAGE is, which is the only thing that binds a
        // description to one image rather than to another.
        var removalNotice = ScenePromptRemovalNotice.Build(effectiveOverrides ?? settings.PromptOverrides, characters);
        if (!string.IsNullOrWhiteSpace(removalNotice))
        {
            sb.AppendLine();
            sb.AppendLine(removalNotice);
        }

        // B-157 T5: the SAME blocking the snapshot already carries, so the prose the pre-processor writes names the
        // same positions the appended block names. One vocabulary on both halves of the prompt. B-161: the operator's
        // frame override is applied here, so the positions AND the camera the prose names are the ones the render uses.
        // A participant POV has no external frame: the projection above owns its camera, so positional reference naming
        // is withheld rather than asserting a placement the first-person projection contradicts.
        var roleClause = ReferenceRoleClauses.ForPreprocessor(
            referenceBindings, projection.IsOmniscient ? effectiveBlocking : null);
        if (!string.IsNullOrWhiteSpace(roleClause))
        {
            sb.AppendLine();
            sb.AppendLine(roleClause);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds the AUTHORITATIVE FIXED IDENTITY appearance block for the canonical composition path.
    /// Delegates to <see cref="CanonicalCharacterAppearance.BuildBlock"/> (shared with the Pony
    /// builder) so per-character physical appearance reaches the pre-processor. The POV character
    /// is excluded for a named observer POV (rule 1: never in frame); an Omniscient POV includes
    /// every frozen character. Characters with no appearance data are omitted entirely.
    /// </summary>
    private static string BuildCanonicalCharacterAppearanceBlock(
        CompiledMediaBrief brief,
        string pov,
        IReadOnlyList<Character>? characters,
        IReadOnlyDictionary<string, string>? appearanceOverrides,
        IReadOnlyDictionary<string, string>? canonicalAppearance = null,
        IReadOnlyDictionary<string, string>? appearanceElementOverrides = null)
        => CanonicalCharacterAppearance.BuildBlock(brief, pov, characters, appearanceOverrides, canonicalAppearance, appearanceElementOverrides);

    /// <summary>
    /// The element keys the OPERATOR rewrote in the Prompt Input inspector (never a removal, and never a
    /// binding-derived suppression): these are the members <see cref="SceneImagePovProjection.DropViewpoint"/> must
    /// keep even when they name the camera holder.
    /// </summary>
    private static IReadOnlySet<string> OperatorEditedMembers(ScenePromptOverrides? overrides)
        => overrides is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : overrides.Fields
                .Where(field => !field.Removed && !string.IsNullOrWhiteSpace(field.ElementKey))
                .Select(field => field.ElementKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string BuildUserPrompt(
         RolePlaySession session,
         RolePlayInteraction interaction,
         AdaptiveScenarioState scenarioState,
         SceneImageStudioSettings settings,
         ImageContentPolicy resolvedPolicy,
         NarrativePhase phase,
         string? excerptOverride,
         string? refineInstruction,
         IReadOnlyList<Character>? characters,
         SceneImageBeat? selectedBeat,
         string? pov)
    {
        var sb = new StringBuilder();

        var moment = string.IsNullOrWhiteSpace(excerptOverride) ? interaction.Content : excerptOverride;
        if (moment.Length > InputExcerptMaxChars)
        {
            moment = moment[..InputExcerptMaxChars];
        }

        sb.AppendLine("STORY MOMENT:");
        if (string.IsNullOrWhiteSpace(moment) || moment.Trim().Length < 2)
        {
            sb.AppendLine("(The story moment text is empty. Depict the current scene using the context below.)");
            var setting = scenarioState.CurrentSceneLocation ?? "the current scene";
            var timeOfDay = scenarioState.CurrentTimeOfDay.ToString();
            sb.AppendLine($"- Setting: {setting}, {timeOfDay}");
            if (!string.IsNullOrWhiteSpace(interaction.ActorName))
            {
                sb.AppendLine($"- Character: {interaction.ActorName}");
            }
        }
        else
        {
            sb.AppendLine(moment);
        }
        sb.AppendLine();

        sb.AppendLine("SCENE CONTEXT:");
        sb.AppendLine($"- Actor: {interaction.ActorName}");
        sb.AppendLine($"- Setting: {scenarioState.CurrentSceneLocation ?? "unknown"}");
        sb.AppendLine($"- Time of day: {scenarioState.CurrentTimeOfDay}");
        sb.AppendLine($"- Narrative phase: {phase}");
        sb.AppendLine($"- Resolved intensity: {session.LastResolvedIntensityLabel ?? "unknown"}");

        var participants = SceneImageParticipantResolver.ResolveParticipants(session, interaction, scenarioState, characters);
        var presentNames = participants
            .Where(p => p.Presence != SceneImageParticipantResolver.Presence.Observer)
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        if (presentNames.Count > 0)
        {
            sb.AppendLine($"- Characters present: {string.Join(", ", presentNames)}");
        }
        if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
        {
            sb.AppendLine($"- Persona: {Truncate(session.PersonaDescription, 200)}");
        }

        if (interaction.WasInSexScene == true)
        {
            sb.AppendLine("- In-encounter: yes (explicit story beat)");
        }
        sb.AppendLine();

        var appearanceBlock = selectedBeat is null
            ? PonySceneImagePromptBuilder.BuildCharacterAppearanceBlock(session, interaction, scenarioState, characters)
            : PonySceneImagePromptBuilder.BuildBeatCharacterAppearanceBlock(session, selectedBeat, pov, characters);
        if (!string.IsNullOrWhiteSpace(appearanceBlock))
        {
            sb.AppendLine(appearanceBlock);
            sb.AppendLine();
        }

        if (selectedBeat is null)
        {
            var clothingBlock = PonySceneImagePromptBuilder.BuildCharacterClothingBlock(session, interaction, scenarioState, characters);
            if (!string.IsNullOrWhiteSpace(clothingBlock))
            {
                sb.AppendLine(clothingBlock);
                sb.AppendLine();
            }
        }

        sb.AppendLine("IMAGE SETTINGS:");
        sb.AppendLine($"- Style: {settings.Style}");
        sb.AppendLine($"- Size/Aspect: {settings.ImageSize}{(string.IsNullOrWhiteSpace(settings.AspectRatio) ? "" : $" / {settings.AspectRatio}")}");
        sb.AppendLine($"- Explicitness: {SceneImageExplicitnessWording.Directive(settings.Explicitness)}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(refineInstruction))
        {
            sb.AppendLine($"REFINE INSTRUCTION (apply to the existing prompt direction): {refineInstruction.Trim()}");
            sb.AppendLine("Adjust the prompt accordingly; keep the same subject matter unless instructed otherwise.");
        }

        return sb.ToString();
    }

    private static string BuildFullTurnContextBlock(FullTurnContext fullTurn, RolePlayInteraction selected)
    {
        var siblings = fullTurn.Interactions
            .Where(x => !string.Equals(x.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.CreatedAt)
            .Take(6)
            .ToList();

        if (siblings.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("FULL TURN CONTEXT (the other interactions from this same turn — use for setting, environment, and who is present):");
        foreach (var interaction in siblings)
        {
            var actor = string.IsNullOrWhiteSpace(interaction.ActorName) ? "Unknown" : interaction.ActorName;
            var content = Truncate(interaction.Content, 400);
            sb.AppendLine($"- [{actor}]: {content}");
        }
        return sb.ToString();
    }

    private static string Truncate(string value, int maxChars)
    {
        return value.Length <= maxChars ? value : value[..maxChars];
    }
}
