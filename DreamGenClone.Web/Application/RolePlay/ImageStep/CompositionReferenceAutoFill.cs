using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// Turns a Moment's recorded stance into the FACE and BODY references a Composition step starts with, per character.
/// This is the auto-pick that breaks the "generate a prompt, read where everyone stands, then pick the references,
/// then generate again" loop: the angle is already recorded on the frozen Moment (<see cref="SceneMomentFrozenCharacter"/>),
/// so the matching reference can be bound BEFORE the first prompt is compiled.
/// </summary>
/// <remarks>
/// <para>
/// The angle comes from <see cref="SceneViewRecommender"/> — the same derivation the "Suggested view" hint already
/// used, but here it BINDS the reference instead of only suggesting it. The reference comes from the character's
/// NEWEST APPROVED identity pack through <see cref="IdentityPackReferenceResolver"/>, so the bound image is the exact
/// view the moment shows.
/// </para>
/// <para>
/// Nothing is invented. A character whose stance records no angle, or whose pack has no approved reference at the
/// recommended view, produces a NOTICE and no binding — the operator picks manually. A missing body reference falls
/// back to the other wardrobe STATE within the same view only (a pack may hold only one state for an angle), never to
/// another angle: substituting a front face for a turned-away body is the exact silent failure this exists to prevent.
/// </para>
/// </remarks>
public static class CompositionReferenceAutoFill
{
    /// <summary>One character the auto-fill addresses, with the facts it derives the view from.</summary>
    /// <param name="Actor">The profile key and display name the binding must address.</param>
    /// <param name="Sightline">The character's recorded sightline, or null.</param>
    /// <param name="Position">The character's recorded position/stance, or null.</param>
    /// <param name="Clothing">The character's recorded clothing prose, or null. Decides the body state when non-empty.</param>
    /// <param name="IdentityPackId">The character's newest approved identity pack.</param>
    /// <param name="PackAssets">That pack's approved reference assets, faces and bodies.</param>
    public sealed record Input(
        ImageStepActor Actor,
        string? Sightline,
        string? Position,
        string? Clothing,
        string IdentityPackId,
        IReadOnlyList<SceneImageReferenceAsset> PackAssets);

    /// <summary>
    /// The bindings the auto-fill produced (face first, then body, per character in cast order) and the notices that
    /// explain what it could NOT produce.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<ReferenceApplicationSelection> Bindings,
        IReadOnlyList<string> Notices);

    public static Result Resolve(
        IReadOnlyList<Input> characters,
        SceneImageExplicitness explicitness)
    {
        ArgumentNullException.ThrowIfNull(characters);

        var bindings = new List<ReferenceApplicationSelection>();
        var notices = new List<string>();

        foreach (var input in characters)
        {
            if (input?.Actor is null)
            {
                continue;
            }

            var name = input.Actor.DisplayName;
            var recommendation = SceneViewRecommender.Resolve(input.Sightline, input.Position);
            if (recommendation.IsEmpty)
            {
                notices.Add($"{name}: the moment records no angle, so no face or body reference was auto-picked.");
                continue;
            }

            // A face is bound only when a face is visible: a turned-away character recommends a body and no face.
            if (recommendation.FaceView is { } faceView)
            {
                var face = IdentityPackReferenceResolver.ResolveFace(input.PackAssets, faceView);
                if (face is null)
                {
                    notices.Add(
                        $"{name}: no approved {IdentityPackReferenceLabels.FaceView(faceView)} face reference — approve that "
                        + "view on the Faces tab, then reopen this Composition.");
                }
                else
                {
                    bindings.Add(BindingFor(
                        input.Actor, ImageStepSlotKind.Face, "character identity", input.IdentityPackId, face.Id,
                        IdentityPackReferenceLabels.Describe(face)));
                }
            }

            if (recommendation.BodyView is { } bodyView)
            {
                var preferred = DeriveBodyState(input.Clothing, explicitness);
                var body = IdentityPackReferenceResolver.ResolveBody(input.PackAssets, bodyView, preferred)
                    ?? IdentityPackReferenceResolver.ResolveBody(input.PackAssets, bodyView, OtherState(preferred));
                if (body is null)
                {
                    notices.Add(
                        $"{name}: no approved {IdentityPackReferenceLabels.BodyView(bodyView)} body reference — approve that "
                        + "view on the Body tab, then reopen this Composition.");
                }
                else
                {
                    bindings.Add(BindingFor(
                        input.Actor, ImageStepSlotKind.Body, "character body", input.IdentityPackId, body.Id,
                        IdentityPackReferenceLabels.Describe(body)));
                }
            }
        }

        // Ordinals follow cast order (face then body per character), which is the blueprint's slot order.
        for (var index = 0; index < bindings.Count; index++)
        {
            bindings[index].Ordinal = index + 1;
        }

        return new Result(bindings, notices);
    }

    /// <summary>
    /// The wardrobe state the moment implies. A recorded outfit means clothed; otherwise the operator's explicitness
    /// decides — an Explicit render leans unclothed, everything else leans clothed. A preference the pack cannot serve
    /// in the recommended view falls back to the other state at the SAME angle (see <see cref="Resolve"/>).
    /// </summary>
    private static SceneImageReferenceBodyState DeriveBodyState(string? clothing, SceneImageExplicitness explicitness)
    {
        if (!string.IsNullOrWhiteSpace(clothing))
        {
            return SceneImageReferenceBodyState.Clothed;
        }

        return explicitness == SceneImageExplicitness.Explicit
            ? SceneImageReferenceBodyState.Unclothed
            : SceneImageReferenceBodyState.Clothed;
    }

    private static SceneImageReferenceBodyState OtherState(SceneImageReferenceBodyState state) =>
        state == SceneImageReferenceBodyState.Clothed
            ? SceneImageReferenceBodyState.Unclothed
            : SceneImageReferenceBodyState.Clothed;

    private static ReferenceApplicationSelection BindingFor(
        ImageStepActor actor,
        ImageStepSlotKind slotKind,
        string semanticRole,
        string packId,
        string assetId,
        string referenceLabel) =>
        new()
        {
            ElementKey = ReferenceStrategyCatalogue.ElementKeyForSlot(slotKind),
            SemanticRole = semanticRole,
            Kind = slotKind.ToString(),
            ActorKey = actor.ActorKey,
            Source = ImageStepReferenceSourceKind.IdentityPackAsset.ToString(),
            // The binding says WHICH image; the render path still resolves HOW the selected model carries it.
            Strategy = ReferenceStrategyResolver.IdentityNativeMultiReference,
            IdentityPackId = packId,
            ReferenceAssetId = assetId,
            ReferenceLabel = referenceLabel
        };
}
