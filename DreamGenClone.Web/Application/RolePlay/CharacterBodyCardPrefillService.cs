using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The two prefill sources for a body card (B-122), implemented against the character TEMPLATE — the identity owner
/// since B-127 — so the studio's resolved key is all the input that is needed.
///
/// The deterministic path composes each field from named attributes and records exactly which ones it used; the
/// draft path asks the configured model for the fields the description actually states and leaves the rest absent.
/// </summary>
public sealed class CharacterBodyCardPrefillService : ICharacterBodyCardPrefillService
{
    private const string DraftSchemaName = "character_body_card_draft";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITemplateService _templates;
    private readonly ICharacterBodyCardDraftModelResolver _modelResolver;
    private readonly ISynchronousStructuredTextCompletionClient _completionClient;

    public CharacterBodyCardPrefillService(
        ITemplateService templates,
        ICharacterBodyCardDraftModelResolver modelResolver,
        ISynchronousStructuredTextCompletionClient completionClient)
    {
        _templates = templates;
        _modelResolver = modelResolver;
        _completionClient = completionClient;
    }

    public async Task<CharacterBodyCardPrefill> FromCharacterTemplateAsync(
        string characterTemplateId, CancellationToken cancellationToken = default)
    {
        var template = await RequireTemplateAsync(characterTemplateId, cancellationToken);
        var attributes = template.PhysicalAttributes;

        var proposals = new List<CharacterBodyCardFieldProposal>();
        var gaps = new List<CharacterBodyCardPrefillGap>();

        AddPart(proposals, gaps, template, CharacterBodyCardField.BodyShape, attributes, [
            ("BodyBuild", attributes?.BodyBuild, null),
            ("Silhouette", attributes?.Silhouette, null),
            ("Adiposity", attributes?.Adiposity, null),
            ("FatDistribution", attributes?.FatDistribution, null),
            ("MuscleMass", attributes?.MuscleMass, null),
            ("MuscleDefinition", attributes?.MuscleDefinition, null),
            ("BustSize", attributes?.BustSize, "bust"),
            ("WaistSize", attributes?.WaistSize, "waist"),
            ("HipSize", attributes?.HipSize, "hips"),
            ("ButtSize", attributes?.ButtSize, "rear")
        ], ", ");

        AddPart(proposals, gaps, template, CharacterBodyCardField.HeightBuild, attributes, [
            ("Height", attributes?.Height, null)
        ], ", ");

        AddPart(proposals, gaps, template, CharacterBodyCardField.Skin, attributes, [
            ("SkinTone", attributes?.SkinTone, null),
            ("SkinTexture", attributes?.SkinTexture, null)
        ], ", ");

        AddPart(proposals, gaps, template, CharacterBodyCardField.BodyHair, attributes, [
            ("BodyHair", attributes?.BodyHair, null)
        ], ", ");

        // The card wants design AND placement; the template attribute documents the design. Saying so in the
        // provenance is the honest option — guessing a placement from a design would train a wrong invariant.
        AddPart(proposals, gaps, template, CharacterBodyCardField.Tattoos, attributes, [
            ("Tattoos", attributes?.Tattoos, null)
        ], ", ", "the template records the design; the card also needs the exact placement");

        AddPart(proposals, gaps, template, CharacterBodyCardField.ScarsMarks, attributes, [
            ("DistinguishingMarks", attributes?.DistinguishingMarks, null)
        ], ", ");

        AddPart(proposals, gaps, template, CharacterBodyCardField.PubicHair, attributes, [
            ("PubicHair", attributes?.PubicHair, null)
        ], ", ");

        return new CharacterBodyCardPrefill(
            template.Id.ToString(),
            DisplayName(template),
            CharacterBodyCardPrefillSource.CharacterAttributes,
            proposals,
            gaps,
            ModelIdentifier: null)
        {
            Axes = BuildAxes(attributes)
        };
    }

    /// <summary>
    /// The card's axis picks, taken from the template's structured attributes. Only the axes the template actually
    /// states are carried across — an unset attribute stays unset, so the card is never handed an invented value.
    /// Null when the template states none, so the editor does not offer an empty axes proposal.
    /// </summary>
    private static CharacterBodyAxes? BuildAxes(PhysicalAttributes? attributes)
    {
        if (attributes is null)
        {
            return null;
        }

        var axes = new CharacterBodyAxes
        {
            BodyBuild = attributes.BodyBuild,
            Silhouette = attributes.Silhouette,
            Adiposity = attributes.Adiposity,
            FatDistribution = attributes.FatDistribution,
            MuscleMass = attributes.MuscleMass,
            MuscleDefinition = attributes.MuscleDefinition,
            BustSize = attributes.BustSize,
            WaistSize = attributes.WaistSize,
            HipSize = attributes.HipSize,
            ButtSize = attributes.ButtSize
        };

        return axes.IsEmpty ? null : axes;
    }

    public async Task<CharacterBodyCardPrefill> DraftFromDescriptionAsync(
        string characterTemplateId, CancellationToken cancellationToken = default)
    {
        var template = await RequireTemplateAsync(characterTemplateId, cancellationToken);
        if (string.IsNullOrWhiteSpace(template.Content))
        {
            throw new InvalidOperationException(
                $"The character template '{DisplayName(template)}' has no description, so there is nothing to draft "
                + "the body card from. Write the character's Content first.");
        }

        var function = await _modelResolver.ResolveAsync(cancellationToken);
        var result = await _completionClient.GenerateAsync(
            function,
            new StructuredTextCompletionRequest(
                BuildDraftSystemMessage(),
                BuildDraftUserMessage(template),
                DraftSchemaName,
                CreateDraftSchema()),
            cancellationToken);

        return ParseDraft(template, result);
    }

    /// <summary>
    /// The instructions are the contract: report only what the description states, answer <c>null</c> for anything
    /// it does not, and never write "none" as a way of filling a field in.
    /// </summary>
    private static string BuildDraftSystemMessage() => string.Join(
        "\n",
        "You extract a character's invariant body description from their character description.",
        "Return one JSON object with exactly these keys: bodyShape, heightBuild, skin, bodyHair, tattoos, scarsMarks, pubicHair.",
        "Rules:",
        "- Use ONLY what the character description states or clearly implies about the body. Never invent detail.",
        "- If the description does not state a key, return null for it. Do not guess and do not fill a field in.",
        "- Never answer \"none\", \"unknown\", \"not specified\" or \"n/a\". Those are not facts; null is the correct answer for silence.",
        "- \"none\" is only correct when the description explicitly says the character has no tattoos / no marks / no body hair.",
        "- tattoos must include the exact placement when the description gives it; keep the description's own wording.",
        "- Write each value as a short, literal descriptor suitable for training an image model, not as prose.");

    private static string BuildDraftUserMessage(TemplateDefinition template)
    {
        var attributes = template.PhysicalAttributes;
        var structured = new List<string>();
        AddStructured(structured, "Gender", template.Gender);
        AddStructured(structured, "Age", attributes?.Age);
        AddStructured(structured, "Height", attributes?.Height);
        AddStructured(structured, "Ethnicity", attributes?.Ethnicity);
        AddStructured(structured, "Skin tone", attributes?.SkinTone);
        AddStructured(structured, "Skin texture", attributes?.SkinTexture);
        AddStructured(structured, "Body build", attributes?.BodyBuild);
        AddStructured(structured, "Silhouette", attributes?.Silhouette);
        AddStructured(structured, "Adiposity", attributes?.Adiposity);
        AddStructured(structured, "Fat distribution", attributes?.FatDistribution);
        AddStructured(structured, "Muscle mass", attributes?.MuscleMass);
        AddStructured(structured, "Muscle definition", attributes?.MuscleDefinition);
        AddStructured(structured, "Bust", attributes?.BustSize);
        AddStructured(structured, "Waist", attributes?.WaistSize);
        AddStructured(structured, "Hips", attributes?.HipSize);
        AddStructured(structured, "Rear", attributes?.ButtSize);
        AddStructured(structured, "Body hair", attributes?.BodyHair);
        AddStructured(structured, "Tattoos", attributes?.Tattoos);
        AddStructured(structured, "Distinguishing marks", attributes?.DistinguishingMarks);
        AddStructured(structured, "Pubic hair", attributes?.PubicHair);

        return string.Join(
            "\n",
            $"Character: {DisplayName(template)}",
            structured.Count == 0
                ? "Structured attributes: none recorded."
                : "Structured attributes already recorded on the template (use them when the description does not "
                  + "conflict):\n" + string.Join("\n", structured),
            "Character description:",
            template.Content!.Trim());
    }

    /// <summary>Every field is a nullable string, so "the description is silent here" is expressible as JSON.</summary>
    private static JsonElement CreateDraftSchema()
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var definition in CharacterBodyCardFields.All)
        {
            properties[SchemaKey(definition.Field)] = new
            {
                type = new[] { "string", "null" },
                description = definition.RequiresDecision
                    ? $"{definition.Label}. A [DECIDE] item: null unless the description states it, and never \"none\" unless the description says there is none."
                    : definition.Label
            };
        }

        var schema = new
        {
            type = "object",
            additionalProperties = false,
            required = CharacterBodyCardFields.All.Select(definition => SchemaKey(definition.Field)).ToArray(),
            properties
        };

        return JsonSerializer.SerializeToElement(schema, JsonOptions);
    }

    private static CharacterBodyCardPrefill ParseDraft(
        TemplateDefinition template, StructuredTextCompletionResult result)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(result.Content);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException(
                $"The body-card draft model returned content that is not JSON, so nothing can be proposed from it "
                + $"({error.Message}). Configure a model with strict JSON output in Model Manager.");
        }

        using (document)
        {
            var proposals = new List<CharacterBodyCardFieldProposal>();
            var gaps = new List<CharacterBodyCardPrefillGap>();

            foreach (var definition in CharacterBodyCardFields.All)
            {
                var value = ReadDraftValue(document.RootElement, definition.Field);
                if (string.IsNullOrWhiteSpace(value))
                {
                    gaps.Add(new CharacterBodyCardPrefillGap(
                        definition.Field,
                        definition.Label,
                        definition.RequiresDecision,
                        $"the description does not state {definition.Label.ToLowerInvariant()} — "
                        + "this stays your decision"));
                    continue;
                }

                proposals.Add(new CharacterBodyCardFieldProposal(
                    definition.Field,
                    definition.Label,
                    definition.RequiresDecision,
                    value.Trim(),
                    CharacterBodyCardPrefillSource.DescriptionDraft,
                    $"drafted from the template description by {result.ModelIdentifier}"));
            }

            return new CharacterBodyCardPrefill(
                template.Id.ToString(),
                DisplayName(template),
                CharacterBodyCardPrefillSource.DescriptionDraft,
                proposals,
                gaps,
                result.ModelIdentifier);
        }
    }

    private static string? ReadDraftValue(JsonElement root, CharacterBodyCardField field)
    {
        var key = SchemaKey(field);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // A model that answers "none"/"unknown" for an unstated invariant would silently create one. The prompt
        // forbids it; this refuses to pass it through anyway, because the card must not record an invented "none".
        return IsNonAnswer(text.Trim()) ? null : text;
    }

    private static bool IsNonAnswer(string value) => value.ToLowerInvariant() switch
    {
        "none" => false, // An explicit "none" IS a legitimate answer for the [DECIDE] items.
        "unknown" or "not specified" or "not stated" or "n/a" or "na" or "unspecified" or "not mentioned" => true,
        _ => false
    };

    private static string SchemaKey(CharacterBodyCardField field) => field switch
    {
        CharacterBodyCardField.BodyShape => "bodyShape",
        CharacterBodyCardField.HeightBuild => "heightBuild",
        CharacterBodyCardField.Skin => "skin",
        CharacterBodyCardField.BodyHair => "bodyHair",
        CharacterBodyCardField.Tattoos => "tattoos",
        CharacterBodyCardField.ScarsMarks => "scarsMarks",
        CharacterBodyCardField.PubicHair => "pubicHair",
        _ => throw new InvalidOperationException($"Unsupported body card field '{field}'.")
    };

    /// <summary>
    /// Adds one field's proposal from the named attributes, or records why it could not be built. The provenance
    /// lists the attributes that were actually used, and each part keeps the operator's own wording.
    /// </summary>
    private static void AddPart(
        List<CharacterBodyCardFieldProposal> proposals,
        List<CharacterBodyCardPrefillGap> gaps,
        TemplateDefinition template,
        CharacterBodyCardField field,
        PhysicalAttributes? attributes,
        (string Attribute, string? Value, string? Label)[] parts,
        string separator,
        string? caveat = null)
    {
        var definition = CharacterBodyCardFields.Require(field);
        var used = parts.Where(part => !string.IsNullOrWhiteSpace(part.Value)).ToList();
        if (used.Count == 0)
        {
            gaps.Add(new CharacterBodyCardPrefillGap(
                definition.Field,
                definition.Label,
                definition.RequiresDecision,
                $"the character template '{DisplayName(template)}' records no "
                + $"{string.Join(" / ", parts.Select(part => part.Attribute))}, and nothing here may guess it — "
                + "set it on the template or decide it in the card"));
            return;
        }

        var value = string.Join(
            separator,
            used.Select(part => part.Label is null ? part.Value!.Trim() : $"{part.Label} {part.Value!.Trim()}"));
        var provenance = $"character template '{DisplayName(template)}': "
            + string.Join(", ", used.Select(part => part.Attribute));
        if (caveat is not null)
        {
            provenance += $" ({caveat})";
        }

        proposals.Add(new CharacterBodyCardFieldProposal(
            definition.Field,
            definition.Label,
            definition.RequiresDecision,
            value,
            CharacterBodyCardPrefillSource.CharacterAttributes,
            provenance));
    }

    private static void AddStructured(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"- {label}: {value.Trim()}");
        }
    }

    private async Task<TemplateDefinition> RequireTemplateAsync(
        string characterTemplateId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(characterTemplateId, out var parsed))
        {
            throw new InvalidOperationException(
                $"'{characterTemplateId}' is not a character template id, so there is nothing to prefill the body "
                + "card from. Character identity is owned by a character template.");
        }

        var template = await _templates.GetByIdAsync(parsed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The character template '{characterTemplateId}' no longer exists, so its body card cannot be "
                + "prefilled. Re-link the character to an existing template.");

        if (template.TemplateType != TemplateType.Character)
        {
            throw new InvalidOperationException(
                $"Character identity must belong to a character template, but '{characterTemplateId}' is a "
                + $"{template.TemplateType} template. The body card belongs to the character template.");
        }

        return template;
    }

    private static string DisplayName(TemplateDefinition template)
        => string.IsNullOrWhiteSpace(template.Name) ? template.Id.ToString() : template.Name.Trim();
}
