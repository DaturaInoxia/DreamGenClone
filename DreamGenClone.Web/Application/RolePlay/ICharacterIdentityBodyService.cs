using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The body target's acquisition surface (B-122 Phase 0). One request produces exactly one image: a base in a
/// state, or one view of the body. There is deliberately no "generate all" and no sweep — the same one-at-a-time
/// contract the face pipeline uses (FR21-022), on the same build, the same step records and the same container.
/// </summary>
public interface ICharacterIdentityBodyService
{
    /// <summary>The body container for this build, created on first use.</summary>
    Task<SceneAsset> EnsureContainerAsync(string buildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The body view settings in force for this character: the model and the render size every body view uses.
    /// They come from the persisted workflow settings (character row over global), so the studio sets them once
    /// instead of the operator typing a size per view — and an unset value stays unset rather than being guessed.
    /// </summary>
    Task<CharacterBodyViewSettings> ResolveViewSettingsAsync(
        string characterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the character's body view model and render size as a CHARACTER row of the workflow settings,
    /// overriding the global seed for that character only. Both are validated here: a model is required, and the
    /// size must be a <c>widthxheight</c> pair — nothing is defaulted in.
    /// </summary>
    Task SaveViewSettingsAsync(
        string characterId, string modelId, string imageSize, CancellationToken cancellationToken = default);

    /// <summary>Everything the body target has recorded for this build, in a stable order.</summary>
    Task<IReadOnlyList<CharacterIdentityBodyView>> ListViewsAsync(string buildId, CancellationToken cancellationToken = default);

    /// <summary>One view's state, or null when that request has not been started.</summary>
    Task<CharacterIdentityBodyView?> GetViewAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default);

    /// <summary>The character's body card, whose line every body prompt is built from.</summary>
    Task<CharacterBodyCard?> GetBodyCardAsync(string characterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the character's body card under optimistic concurrency (<paramref name="expectedVersion"/> of 0
    /// creates the first row). The card is the ONLY mutable source of the body invariants, so the editor writes
    /// it here rather than to the store, and a stale version is refused instead of overwriting another editor.
    /// </summary>
    Task<CharacterBodyCard> SaveBodyCardAsync(
        CharacterBodyCard card, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// The prompt a GENERATION of this slot would use, compiled for the family of <paramref name="modelId"/>.
    ///
    /// Only a BASE view (the front in either state) can be generated from nothing, so only a base has one. Every
    /// other view is the same body at another angle, produced by editing an accepted view, and its prompt is the
    /// edit instruction instead — see <see cref="ResolveEditInstructionAsync"/>.
    /// </summary>
    Task<string> ResolvePromptAsync(
        string buildId, CharacterIdentityBodyViewKey key, string modelId, string characterName,
        string? promptOverride = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The instruction an EDIT of this slot from its accepted source uses: the store's own rotation instruction.
    /// A rotation is a request to change an image that already exists, not a description of a body, so the
    /// per-family prompt dialect does not apply here.
    /// </summary>
    Task<string> ResolveEditInstructionAsync(
        string buildId, CharacterIdentityBodyViewKey key, string characterName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a configured-model generation of one view — the only route that produces an image without a source,
    /// and what a base uses (the two states of the front). The body card must be complete first.
    ///
    /// <paramref name="pose"/> optionally conditions the render on a verified stance skeleton. It is the operator's
    /// per-request choice, not a default: pose conditioning needs a model that declares the PoseControlNet capability,
    /// and assuming it would fail on every other model.
    /// </summary>
    Task<CharacterIdentityBodyView> GenerateAsync(
        string buildId, CharacterIdentityBodyViewKey key, string modelId, string imageSize, string characterName,
        string? promptOverride = null, SceneAssetPoseConditioning? pose = null, bool useIdentity = false,
        string? identityFaceAssetId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this character can be identity-conditioned, and with which approved reference. Lets the panel offer
    /// the option only when it can be honoured — and say why when it cannot.
    /// </summary>
    Task<BodyIdentityAvailability> ResolveIdentityAvailabilityAsync(
        string buildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the configured body view model can be pose-conditioned, and if not, why not. Same contract as
    /// <see cref="ResolveIdentityAvailabilityAsync"/>: capability is a property of the MODEL, so the panel offers the
    /// option only for a model that declares and qualifies it, and states the reason for every other model instead
    /// of letting the operator find out through a failed render.
    /// </summary>
    Task<BodyPoseAvailability> ResolvePoseAvailabilityAsync(
        string buildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a same-image edit of the view this request is derived from (the accepted front base, or the accepted
    /// three-quarter view on that side for a profile). Refuses — naming what is missing — when that source has not
    /// been accepted, because a body view is always the same body, never a fresh attempt at it.
    /// </summary>
    Task<CharacterIdentityBodyView> EditFromAcceptedSourceAsync(
        string buildId, CharacterIdentityBodyViewKey key, string modelId, string characterName,
        string? promptOverride = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the accepted source image itself as this view's artifact, without an edit. An extended view that
    /// the configured editor model cannot rotate is exactly this case; nothing is invented to fill the slot.
    /// </summary>
    Task<CharacterIdentityBodyView> RecordSourceAsResultAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default);

    /// <summary>Records a user-supplied image as one view's artifact. Nothing is inferred from the file.</summary>
    Task<CharacterIdentityBodyView> UploadAsync(
        string buildId, CharacterIdentityBodyViewKey key, string fileName, Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Records the image a queued edit produced for one view (the completion path).</summary>
    Task<CharacterIdentityBodyView> RecordResultAsync(
        string buildId, CharacterIdentityBodyViewKey key, string outputArtifactId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts a view's current artifact as the reference for that slot. The base must be accepted before any
    /// other view can be produced, because every view is an edit of it.
    /// </summary>
    Task<CharacterIdentityBodyView> AcceptAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts one CANDIDATE image as this view's result and then accepts the view, in ONE operator action.
    ///
    /// Two acts existed and were easy to confuse: deciding an image in the candidate deck (a judgement about that
    /// image) and accepting the VIEW (which is what the next step waits on). Accepting an image this view produced IS
    /// accepting the view — the judgement is about the body in that picture — so they travel together, under the same
    /// body-invariant findings gate. Accepting the clothed front therefore still completes the Front step.
    /// </summary>
    Task<CharacterIdentityBodyView> AcceptCandidateAsync(
        string buildId, CharacterIdentityBodyViewKey key, string imageId, CancellationToken cancellationToken = default);

    /// <summary>Records an explicit, attributed override for a view the checks refuse.</summary>
    Task<CharacterIdentityBodyView> RecordOverrideAsync(
        string buildId, CharacterIdentityBodyViewKey key, string reason, string author,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the reviewer's body-invariant findings for one view. Every check the reviewer does not state keeps
    /// its previous verdict — and an unstated check is <see cref="CharacterIdentityBodyCheckVerdict.NotReviewed"/>,
    /// which acceptance refuses, so silence can never read as a pass.
    /// </summary>
    Task<CharacterIdentityBodyView> RecordFindingsAsync(
        string buildId, CharacterIdentityBodyViewKey key,
        IReadOnlyDictionary<CharacterIdentityBodyCheck, CharacterIdentityBodyCheckVerdict> verdicts,
        string reviewer, string? note = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs B-121's ONE reference-quality analyzer over this view's image and stores its rating and notes as the
    /// view's evidence. It reports; it does not accept or refuse anything (the findings gate does that).
    /// </summary>
    Task<CharacterIdentityBodyView> AnalyzeQualityAsync(
        string buildId, CharacterIdentityBodyViewKey key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether the configured body view model can carry a pose (OpenPose ControlNet) conditioning, and the reason when it
/// cannot. <see cref="Reason"/> is the resolver's own diagnostic, so the operator reads the same message a failed
/// render would have produced — before queueing one.
///
/// <see cref="DefaultStrength"/> is the model's CONFIGURED conditioning strength, carried here so the panel opens on
/// the value that was proven for that model. It is not a UI default: on the FLUX path the strength decides whether the
/// control image's own strokes imprint on the render, and the proven-clean value differs per model.
/// </summary>
public sealed record BodyPoseAvailability(
    bool IsAvailable,
    string? Reason,
    double? DefaultStrength = null,
    string? Strategy = null)
{
    /// <summary>
    /// Available, with the strategy the render will use. <paramref name="defaultStrength"/> is null on the
    /// reference-image route: a pose carried as an image has no ControlNet strength to configure.
    /// </summary>
    public static BodyPoseAvailability Available(double? defaultStrength, string strategy)
        => new(true, null, defaultStrength, strategy);

    public static BodyPoseAvailability Unavailable(string reason) => new(false, reason);
}
