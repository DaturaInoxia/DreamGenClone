using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Shooting one coverage cell (B-123 Phase 2, render half).
///
/// <para>
/// One call renders <b>one</b> image for <b>one</b> cell. There is deliberately no method here that shoots a
/// range of cells: a training set of thirty near-identical frames is the failure this item exists to prevent,
/// and it is exactly what a batch entry point produces. The operator works through the plan cell by cell.
/// </para>
///
/// <para>
/// Renders go through the ordinary asset pipeline (a queued generation job, one asset image per attempt), so a cell
/// attempt is an ordinary Scene Asset with its own id, checksum and prompt — which is what the dataset member
/// registration needs later, and what makes an attempt deletable on its own.
/// </para>
///
/// <para>
/// A cell is rendered <b>as the character</b>, not from text alone. The attempt carries the identity conditioning
/// the cell's own reference rule names — the face reference for that head angle, read from the dataset's approved
/// pack — so a training image is the same person by construction rather than by hope. The reference is resolved here
/// from the stored plan and pack, never passed in by the caller, so the reference a cell is conditioned on can never
/// disagree with the reference the cell says it uses.
/// </para>
/// </summary>
public interface ICharacterLoraCellService
{
    /// <summary>
    /// The candidate batch every attempt of one cell is filed under. Derived, never stored separately, so a cell
    /// can always find its own attempts and can never read another cell's.
    /// </summary>
    string CellBatchIdFor(string datasetId, string cellKey);

    /// <summary>Every attempt shot for this cell, newest first.</summary>
    Task<IReadOnlyList<SceneAssetImage>> ListCellAttemptsAsync(
        string datasetId, string cellKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shoot the cell once, conditioned on the identity reference its plan names. Returns the queued attempt
    /// image; the render itself is performed by the ordinary generation worker.
    /// </summary>
    Task<SceneAssetImage> RenderCellAsync(
        string datasetId,
        string cellKey,
        string prompt,
        string modelId,
        string aspect,
        IReadOnlyList<ReferenceApplicationSelection> referenceApplications,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The ordered reference bindings this cell's rule resolves to, so a host can SHOW them and seed them into a step.
    /// The same two decisions the render used to make privately, exposed as the step's starting state.
    /// </summary>
    /// <param name="characterKey">
    /// The character key the CALLER addresses this character by - the same key it puts in the step's blueprint
    /// actor. It is supplied rather than derived here because a slot is matched on kind AND actor: a binding that
    /// names a different id space is invisible to its own slot, which showed the cell's real face and body
    /// references as unbound (reported live 2026-09-26).
    /// </param>
    Task<IReadOnlyList<ReferenceApplicationSelection>> ResolveCellBindingsAsync(
        string datasetId,
        string cellKey,
        string characterKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The identity reference this cell will be conditioned on, in words, for the workspace to show before
    /// anything is shot. Empty when the cell shows no face and therefore has no face reference.
    /// </summary>
    Task<string> DescribeIdentityReferenceAsync(
        string datasetId, string cellKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The BODY reference this cell will be conditioned on, in words, for the workspace to show before anything is
    /// shot. Every cell states one: the plan records the body slot and the state per cell, so a cell that cannot
    /// resolve its own reference fails here rather than rendering a build nobody asked for. Unlike the face, this is
    /// present even for a view from directly behind — the back view is a canonical slot.
    /// </summary>
    Task<string> DescribeBodyReferenceAsync(
        string datasetId, string cellKey, CancellationToken cancellationToken = default);

    /// <summary>Discard one attempt. Its image goes with it, so a rejected frame leaves nothing behind.</summary>
    Task DiscardAttemptAsync(string imageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a review DECISION against one attempt (B-130 follow-up): the same
    /// <see cref="DreamGenClone.Domain.RolePlay.SceneAssetCandidateDecision"/> the review deck writes, so a verdict taken
    /// on the cell's list and one taken in the deck are one fact rather than two.
    ///
    /// It lives here rather than on the page because the page must not reach into the asset store: the cell service
    /// owns what a cell's attempts are, and a host that wrote decisions directly could write one for an image that is
    /// not this cell's.
    /// </summary>
    Task SetAttemptDecisionAsync(
        string imageId,
        DreamGenClone.Domain.RolePlay.SceneAssetCandidateDecision decision,
        CancellationToken cancellationToken = default);

    /// <summary>The persisted cell model for this character, or null when no model has been chosen yet.</summary>
    Task<string?> ResolveCellModelAsync(string characterId, CancellationToken cancellationToken = default);

    /// <summary>Persist the chosen cell model as this character's setting.</summary>
    Task SaveCellModelAsync(
        string characterId, string modelId, CancellationToken cancellationToken = default);
}
