using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Builds the frozen <see cref="BodyReferenceBrief"/> that a body-reference prompt is compiled from (B-122).
///
/// One brief, one body: the body card supplies the body facts, the character template supplies the person, and the
/// request supplies the slot (state and stance). Nothing is inferred from a neighbouring fact — the old path derived
/// the prompt's noun from whether a bust measurement happened to be present, which rendered a flat-chested woman as
/// a man.
///
/// <b>The body parts are the single source of the body.</b> The card's "body shape" line is DERIVED from them by
/// <see cref="CharacterBodyAxes.Compose"/>, so it must still agree with them. A line that has been hand-edited away
/// from the parts is not reconciled here: there would be two answers to "what is this body" and no honest way to
/// pick one, so it fails fast with the one-click remedy instead.
/// </summary>
public interface IBodyReferenceBriefFactory
{
    Task<BodyReferenceBrief> CreateAsync(
        string characterTemplateId,
        CharacterBodyCard card,
        SceneImageReferenceBodyState state,
        BodyReferenceStance stance,
        bool requestIdentity = false,
        string? faceAssetId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The character's identity reference, or the reason there is none. Lets the UI offer identity conditioning only
    /// when it can actually be honoured, instead of offering it and failing at generation time.
    /// </summary>
    Task<BodyIdentityAvailability> ResolveIdentityAvailabilityAsync(
        string characterTemplateId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether a character can be identity-conditioned, and with what. <see cref="Reason"/> explains an unavailable
/// state rather than leaving the operator to guess why the option is missing.
/// </summary>
public sealed record BodyIdentityAvailability(
    bool IsAvailable,
    string? PackId,
    int PackVersion,
    string? FaceAssetId,
    string? Reason)
{
    /// <summary>
    /// Every APPROVED face in the pack, canonical first — the reference ANGLES a render may condition on. Conditioning
    /// on the angle the render actually shows is the same rule the scene editor follows, so the operator picks a view
    /// rather than being pinned to the canonical face.
    /// </summary>
    public IReadOnlyList<BodyIdentityFace> Faces { get; init; } = [];

    /// <summary>
    /// The model's QUALIFIED identity mechanism this render will use — <c>ReferenceConditioning</c> (a configured
    /// IP-Adapter/PuLID graph) or <c>NativeMultiReference</c> (the model takes the approved face as a reference image
    /// in the same call, as Qwen-Image-2.1 does). Empty when identity is unavailable.
    ///
    /// Carried on the answer so the panel can say WHICH mechanism it is offering, and so the render path and the
    /// operator's expectation are the same fact rather than two derivations of it.
    /// </summary>
    public string Strategy { get; init; } = string.Empty;

    public static BodyIdentityAvailability Unavailable(string reason) => new(false, null, 0, null, reason);
}

/// <summary>One approved face reference a render can condition on: its asset, its angle, and how good it is.</summary>
public sealed record BodyIdentityFace(
    string AssetId,
    SceneImageReferenceFaceView FaceView,
    bool IsCanonical,
    SceneImageReferenceQuality Quality)
{
    /// <summary>Operator-facing label: the angle, plus the canonical marker and the quality when they are known.</summary>
    public string Label
    {
        get
        {
            var parts = new List<string>(3) { DescribeFaceView(FaceView) };
            if (IsCanonical)
            {
                parts.Add("canonical");
            }

            if (Quality != SceneImageReferenceQuality.NotRated)
            {
                parts.Add(Quality.ToString());
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The angle in operator words. Unknown values print as-is rather than being guessed at.</summary>
    public static string DescribeFaceView(SceneImageReferenceFaceView view) => view switch
    {
        SceneImageReferenceFaceView.Front => "Front",
        SceneImageReferenceFaceView.ThreeQuarterLeft => "Three-quarter left",
        SceneImageReferenceFaceView.ThreeQuarterRight => "Three-quarter right",
        SceneImageReferenceFaceView.ProfileLeft => "Profile left",
        SceneImageReferenceFaceView.ProfileRight => "Profile right",
        _ => view.ToString()
    };
}

/// <inheritdoc />
public sealed class BodyReferenceBriefFactory : IBodyReferenceBriefFactory
{
    private readonly ITemplateService _templates;
    private readonly ICharacterImageIdentityRepository _identity;
    private readonly ILogger<BodyReferenceBriefFactory> _logger;

    public BodyReferenceBriefFactory(
        ITemplateService templates,
        ICharacterImageIdentityRepository identity,
        ILogger<BodyReferenceBriefFactory> logger)
    {
        _templates = templates;
        _identity = identity;
        _logger = logger;
    }

    public async Task<BodyReferenceBrief> CreateAsync(
        string characterTemplateId,
        CharacterBodyCard card,
        SceneImageReferenceBodyState state,
        BodyReferenceStance stance,
        bool requestIdentity = false,
        string? faceAssetId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (string.IsNullOrWhiteSpace(characterTemplateId))
        {
            throw new InvalidOperationException(
                "A character template id is required to build a body reference brief: the person's appearance is "
                + "owned by the character template.");
        }

        if (!string.Equals(card.CharacterTemplateId, characterTemplateId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The body card belongs to character template '{card.CharacterTemplateId}', not "
                + $"'{characterTemplateId}', so it cannot describe this character's body.");
        }

        // The card must be COMPLETE before anything is read off it. This is the same gate the render path uses, so an
        // unanswered field cannot reach a prompt by going through the brief instead of the direct path.
        card.RequireReadyForGeneration();
        RequireBodyParts(card);

        var template = await RequireCharacterTemplateAsync(characterTemplateId, cancellationToken);
        var attributes = template.PhysicalAttributes;

        // Resolved before the brief is built, so "identity was requested but cannot be honoured" fails here with the
        // remedy rather than producing an unconditioned image that looks like a conditioned one.
        var identity = await ResolveIdentityAsync(characterTemplateId, requestIdentity, faceAssetId, cancellationToken);

        var brief = new BodyReferenceBrief
        {
            BodyCardVersion = card.Version,
            CharacterTemplateId = characterTemplateId,

            // Stated, never inferred: the template owns gender, and the compiler refuses an unstated one rather than
            // picking a count tag or a noun on the operator's behalf.
            Gender = template.Gender,

            BodyState = state,
            Stance = stance,

            // Required. Both target families need an explicit age token or they fall back to their own prior, and
            // that prior is consistently younger than the character.
            Age = Clean(attributes?.Age),
            HairStyle = Clean(attributes?.HairStyle),
            HairColour = Clean(attributes?.HairColour),
            EyeColour = Clean(attributes?.EyeColour),
            SkinTone = Clean(attributes?.SkinTone),
            SkinTexture = Clean(attributes?.SkinTexture),
            Ethnicity = Clean(attributes?.Ethnicity),

            BodyShape = card.BodyShape.Trim(),
            Axes = card.Axes,

            // The four descriptive body fields go through the card's ONE decision-record rule: "none" is a record
            // that a decision was made, not a descriptor, so it is carried as absent rather than as a token the model
            // would try to draw. Re-implementing that check here would let the two drift apart.
            BodyHair = Renderable(card.BodyHair),
            PubicHair = Renderable(card.PubicHair),
            Tattoos = Renderable(card.Tattoos),
            ScarsMarks = Renderable(card.ScarsMarks),

            // One resolver for the character's outfit, shared with the scene builders — not a second opinion.
            Clothing = state == SceneImageReferenceBodyState.Clothed
                ? Clean(PhysicalAttributesFormatter.FormatVisualClothing(attributes))
                : null,

            // Identity conditioning is stated, never assumed: it needs an approved face reference AND a model that
            // declares an identity mechanism, so "off" is the only honest value unless the operator asked for it and
            // the reference was verified to exist.
            RequiresIdentity = identity is not null,
            IdentityFaceAssetId = identity?.FaceAssetId,
            IdentityPackId = identity?.PackId
        };

        brief.Validate();

        _logger.LogInformation(
            "Body reference brief built: Character={CharacterId}, Card={CardVersion}, State={State}, Stance={Stance}, "
            + "Gender={Gender}, Age={Age}, Identity={Identity}",
            characterTemplateId, brief.BodyCardVersion, state, stance, brief.Gender, brief.Age,
            identity is null ? "none" : $"pack {identity.PackId} v{identity.PackVersion} face {identity.FaceAssetId}");

        return brief;
    }

    /// <inheritdoc />
    public async Task<BodyIdentityAvailability> ResolveIdentityAvailabilityAsync(
        string characterTemplateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(characterTemplateId))
        {
            return BodyIdentityAvailability.Unavailable("This character has no identity template, so there is no pack to condition on.");
        }

        var pack = await _identity.GetLatestApprovedPackAsync(characterTemplateId, cancellationToken);
        if (pack is null)
        {
            return BodyIdentityAvailability.Unavailable(
                "This character has no APPROVED identity pack, so there is no face reference to condition on. Approve "
                + "a face reference for the character first.");
        }

        if (string.IsNullOrWhiteSpace(pack.CanonicalFaceAssetId))
        {
            return BodyIdentityAvailability.Unavailable(
                $"Approved pack v{pack.Version} names no canonical face asset, so there is nothing to condition on.");
        }

        var face = await _identity.GetAssetAsync(pack.CanonicalFaceAssetId, cancellationToken);
        if (face is null || face.AssetKind != SceneImageReferenceAssetKind.Face || !face.IsApproved)
        {
            return BodyIdentityAvailability.Unavailable(
                $"Approved pack v{pack.Version}'s canonical face asset is missing, is not a face, or is not approved, "
                + "so it cannot condition a render.");
        }

        // The approved faces of the pack, canonical first: these are the ANGLES the operator can pick between.
        var assets = await _identity.ListAssetsAsync(pack.Id, cancellationToken);
        var faces = assets
            .Where(asset => asset.AssetKind == SceneImageReferenceAssetKind.Face && asset.IsApproved)
            .Where(asset => asset.FaceView is not null)
            .Select(asset => new BodyIdentityFace(
                asset.Id,
                asset.FaceView!.Value,
                string.Equals(asset.Id, pack.CanonicalFaceAssetId, StringComparison.Ordinal),
                asset.QualityRating))
            .OrderByDescending(candidate => candidate.IsCanonical)
            .ThenBy(candidate => candidate.FaceView)
            .ToList();

        return new BodyIdentityAvailability(true, pack.Id, pack.Version, face.Id, null) { Faces = faces };
    }

    /// <summary>
    /// The verified face reference, or null when the operator did not ask for identity. A refusal carries the remedy,
    /// because "no approved pack" and "the pack's face is unapproved" need different actions.
    ///
    /// <paramref name="faceAssetId"/> selects WHICH approved angle conditions the render; null takes the pack's
    /// canonical face. A requested asset that is not an approved face of this pack is refused rather than silently
    /// swapped for the canonical one — that would condition on a different image than the operator picked.
    /// </summary>
    private async Task<BodyIdentityAvailability?> ResolveIdentityAsync(
        string characterTemplateId, bool requestIdentity, string? faceAssetId,
        CancellationToken cancellationToken)
    {
        if (!requestIdentity)
        {
            return null;
        }

        var availability = await ResolveIdentityAvailabilityAsync(characterTemplateId, cancellationToken);
        if (!availability.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Identity conditioning was requested for character '{characterTemplateId}', but {availability.Reason}");
        }

        if (string.IsNullOrWhiteSpace(faceAssetId))
        {
            return availability;
        }

        var chosen = availability.Faces.FirstOrDefault(
            candidate => string.Equals(candidate.AssetId, faceAssetId.Trim(), StringComparison.Ordinal));
        if (chosen is null)
        {
            var available = availability.Faces.Count == 0
                ? "the pack has no approved face assets"
                : string.Join(", ", availability.Faces.Select(candidate => candidate.Label));

            throw new InvalidOperationException(
                $"Face reference '{faceAssetId}' is not an approved face of identity pack '{availability.PackId}' "
                + $"(v{availability.PackVersion}), so it cannot condition this render. Available: {available}.");
        }

        return availability with { FaceAssetId = chosen.AssetId };
    }

    /// <summary>
    /// The card must name its body PARTS, because they — not the shape line — are what the prompt is compiled from:
    /// only the parts carry a per-family rendering, so a body with no parts has nothing to compile.
    ///
    /// The shape LINE is deliberately NOT required to match them. It is the composed, human-readable record of the
    /// parts, and it legitimately drifts: the studio lets the pickers be changed without pressing "Compose into body
    /// shape". Refusing on that drift was a mistake that made EVERY body view of a live card fail — a hard stop on a
    /// stale cosmetic line — so the parts simply win, and the drift is warned about here and shown in the studio.
    /// </summary>
    private void RequireBodyParts(CharacterBodyCard card)
    {
        if (card.Axes.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Character '{card.CharacterTemplateId}' has no body parts picked, so no body reference can be "
                + "compiled. Pick the body parts on the body card (Body parts), then press 'Compose into body shape' "
                + "so the shape line is written from them.");
        }

        if (!string.Equals(card.BodyShape.Trim(), card.Axes.Compose().Trim(), StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "The body card's shape line is out of date for character {CharacterId} (card v{Version}). The picked "
                + "body parts are what generate; press 'Compose into body shape' to rewrite the line. Line: '{Line}'",
                card.CharacterTemplateId, card.Version, card.BodyShape.Trim());
        }
    }

    private async Task<TemplateDefinition> RequireCharacterTemplateAsync(
        string characterTemplateId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(characterTemplateId, out var parsed))
        {
            throw new InvalidOperationException(
                $"'{characterTemplateId}' is not a character template id, so its body reference cannot be prepared. "
                + "Character identity is owned by a character template.");
        }

        var template = await _templates.GetByIdAsync(parsed, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The character template '{characterTemplateId}' no longer exists, so no body reference can be "
                + "prepared for it. Re-link the character to an existing template.");

        if (template.TemplateType != TemplateType.Character)
        {
            throw new InvalidOperationException(
                $"Character identity must belong to a character template, but '{characterTemplateId}' is a "
                + $"{template.TemplateType} template. The body reference belongs to the character template.");
        }

        return template;
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Carries a card value only when it is a descriptor — see <see cref="CharacterBodyCard.IsDecisionOnly"/>.</summary>
    private static string? Renderable(string? value)
        => CharacterBodyCard.IsDecisionOnly(value) ? null : value!.Trim();
}
