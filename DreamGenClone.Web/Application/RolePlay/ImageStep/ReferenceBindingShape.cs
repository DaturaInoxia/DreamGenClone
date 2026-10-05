using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// The store a reference binding's image comes from. Four channels exist and they are NOT aliases: each is looked
/// up in a different place, and a resolver that serves one of them cannot serve another.
/// </summary>
public enum ReferenceChannel
{
    /// <summary>An approved <c>SceneAssets</c> image, addressed by its asset id and image id.</summary>
    ApprovedAsset = 1,

    /// <summary>An approved image out of a character's identity PACK (<c>SceneImageReferenceAsset</c>).</summary>
    IdentityPack = 2,

    /// <summary>A skeleton from the pose library, addressed by its preset id.</summary>
    PoseSkeleton = 3,

    /// <summary>An unapproved render produced earlier in the same chain.</summary>
    ScratchImage = 4
}

/// <summary>
/// The shape facts about a reference binding that more than one component has to agree on: which slot it fills, which
/// store supplied it, and the order it reaches the model in.
///
/// <para>
/// These live together because getting any one of them wrong is silent. The order especially: the Qwen-Image-2.1
/// encoder receives the prompt UNCHANGED — unlike the 2509/2511 nodes, which prepend their own "Picture N:" labels —
/// so the ONLY thing binding a reference image to a description of it is the index the prompt names. Qwen's own
/// prompt-enhancer system prompt calls that tagging "mandatory and non-negotiable" for two or more images. A second
/// copy of the ordering rule that drifted from the render's would re-point every reference in a clause without
/// failing anything, which is why the render path and the clause both read the order from here.
/// </para>
/// </summary>
public static class ReferenceBindingShape
{
    /// <summary>
    /// Which store this binding's image comes from. The CHANNEL is what decides who can resolve it — and a channel
    /// nobody serves is a reference the model never receives, so callers read this rather than each testing the
    /// fields they happen to know about.
    ///
    /// <para>
    /// An explicit <c>Source</c> wins when it names a channel; anything else is classified from the fields actually
    /// present, so a legacy binding written before <c>Source</c> existed still lands in the right store. A binding
    /// that carries no image at all is <see cref="ReferenceChannel.ApprovedAsset"/>-shaped only in the sense that it
    /// has an asset id; callers filter on <see cref="ReferenceApplicationSelection.SuppliesImage"/> first.
    /// </para>
    /// </summary>
    public static ReferenceChannel ChannelOf(ReferenceApplicationSelection application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (Enum.TryParse<ImageStepReferenceSourceKind>(application.Source, ignoreCase: true, out var source))
        {
            switch (source)
            {
                case ImageStepReferenceSourceKind.IdentityPackAsset:
                    return ReferenceChannel.IdentityPack;
                case ImageStepReferenceSourceKind.PoseLibrarySkeleton:
                    return ReferenceChannel.PoseSkeleton;
                case ImageStepReferenceSourceKind.ScratchImage:
                    return ReferenceChannel.ScratchImage;
                case ImageStepReferenceSourceKind.ApprovedSceneAsset:
                    return ReferenceChannel.ApprovedAsset;
                case ImageStepReferenceSourceKind.CharacterPoseAsset:
                    // A character-pose asset IS an approved scene asset of that type; it is stored, approved and
                    // versioned the same way, so it is served by the same resolver rather than a fifth channel.
                    return ReferenceChannel.ApprovedAsset;
            }
        }

        if (IsIdentityPackBinding(application))
        {
            return ReferenceChannel.IdentityPack;
        }

        return string.IsNullOrWhiteSpace(application.SkeletonRelativePath)
            ? ReferenceChannel.ApprovedAsset
            : ReferenceChannel.PoseSkeleton;
    }

    /// <summary>
    /// Whether a binding supplies an image out of a character's approved identity PACK rather than an approved scene
    /// asset. The two stores are not aliases: a pack image is a <c>SceneImageReferenceAsset</c> that only its own pack
    /// can look up, so <c>SceneAssetImageId</c> is null on it and the asset resolver cannot serve it.
    /// </summary>
    public static bool IsIdentityPackBinding(ReferenceApplicationSelection application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return string.Equals(
                application.Source,
                nameof(ImageStepReferenceSourceKind.IdentityPackAsset),
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(application.IdentityPackId)
            && !string.IsNullOrWhiteSpace(application.ReferenceAssetId);
    }

    /// <summary>
    /// Whether the approved-asset resolver can serve this binding: it carries an approved scene-asset image, and its
    /// strategy means an image rather than text.
    ///
    /// <para>
    /// This is the predicate the resolver SELECTS by, stated once so a caller cannot count the resolver's input with a
    /// different rule. The three channels are exclusive: a pack image comes out of the pack store, a pose skeleton out
    /// of the pose library, and only what is left comes out of the approved-asset resolver. Counting "everything that
    /// is not a pack binding" as the resolver's input is what made a bound pose look like a reference the resolver had
    /// silently dropped (reported live 2026-10-03), when the pose was in fact added by its own block.
    /// </para>
    /// </summary>
    public static bool IsAssetBacked(ReferenceApplicationSelection application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.UsesReference
            && !string.Equals(application.Strategy, "TextOnly", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The bindings in the order the model RECEIVES them — which is the order a role clause has to number.
    ///
    /// <para>
    /// Pack-supplied images go first because that is the order the render assembles them in (a pack image is resolved
    /// from the pack store, the rest through the approved-asset resolver), and within that group by their recorded
    /// ordinal. Everything else follows in the order it was stored, which is the planned slot order.
    /// </para>
    ///
    /// <para>
    /// This is deliberately NOT "sort everything by ordinal". The planned order and the sent order differ as soon as
    /// a pack binding and an asset binding are mixed — a step that binds a location at ordinal 1 and a face at
    /// ordinal 2 still sends the face first — and numbering a clause from the planned order would then name the wrong
    /// image for every reference after the first.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ReferenceApplicationSelection> InSendOrder(
        IReadOnlyList<ReferenceApplicationSelection> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);

        var ordered = new List<ReferenceApplicationSelection>(applications.Count);
        ordered.AddRange(applications
            .Where(IsIdentityPackBinding)
            .OrderBy(application => application.Ordinal ?? int.MaxValue)
            .ThenBy(application => application.ElementKey, StringComparer.Ordinal));
        ordered.AddRange(applications.Where(application => !IsIdentityPackBinding(application)));
        return ordered;
    }

    /// <summary>
    /// The slot a binding describes: its explicit <c>Kind</c> when set, otherwise the legacy element key it was
    /// created with. Null when neither is recognised — a value this cannot address is not an error worth refusing a
    /// render over, because the element map is a legacy surface that predates slots.
    /// </summary>
    public static ImageStepSlotKind? SlotKindOf(ReferenceApplicationSelection binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (!string.IsNullOrWhiteSpace(binding.Kind)
            && Enum.TryParse<ImageStepSlotKind>(binding.Kind, ignoreCase: true, out var explicitKind))
        {
            // Returned as parsed even when the value is not a defined member: an unmappable kind is refused by the
            // caller that has to map it, which is the same fail-fast the element map has always had.
            return explicitKind;
        }

        return binding.ElementKey?.Trim() switch
        {
            "Identity" => ImageStepSlotKind.Face,
            "Body" => ImageStepSlotKind.Body,
            "Wardrobe" => ImageStepSlotKind.Wardrobe,
            "Location" => ImageStepSlotKind.Location,
            "Pose" => ImageStepSlotKind.Pose,
            "CharacterPose" => ImageStepSlotKind.CharacterPose,
            _ => null
        };
    }
}
