using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay.Editing;

/// <summary>
/// A reference image handed to the editor model alongside the source. The opener keeps the storage
/// service private to the subject writer, so shared execution never learns which store it came from.
/// </summary>
/// <param name="SlotKind">
/// Which slot the binding filled, or null for a legacy binding whose element key is not recognised. The render's own
/// role clause needs it so each reference image can be NAMED in the prompt — an edit's references are numbered from
/// the SECOND image, because the source occupies the first.
/// </param>
public sealed record MediaEditReference(
    int Ordinal,
    string Description,
    string FileName,
    string Sha256,
    Func<CancellationToken, Task<Stream>> OpenAsync,
    ImageStepSlotKind? SlotKind = null,

    /// <summary>
    /// What this image is a view OF — the asset container it lives in, or null when it is not an approved scene asset
    /// (a pack image, say).
    ///
    /// <para>
    /// The role clause needs it to tell "several views of one place" from "several different places in one frame":
    /// two views of the same shed share a container and stay the same room, while two different trailers do not and
    /// must not each be described as the whole room. Without it here the edit surface could never reach that branch,
    /// so the fix would have been silently inert on exactly the surface the operator reaches for first (2026-10-12).
    /// </para>
    /// </summary>
    string? AssetContainerId = null,

    /// <summary>
    /// What the OPERATOR says this reference is doing in this frame (B-159) — side, facing, and their own note. Null
    /// when they said nothing, which is the state every reference starts in.
    /// </summary>
    ReferenceDirections? Directions = null);

/// <summary>What the enqueue-time payload carried that preparation still needs.</summary>
public sealed record MediaEditRunContext(
    string ImageId,
    MediaEditOperation Operation,
    string? ExplicitEditorModelId = null,
    string? ReferenceApplicationsJson = null,
    string? ScopeId = null);

/// <summary>How shared execution must resolve the editor model.</summary>
public sealed record MediaEditEditorResolution(string? ExplicitModelId, bool RequiresAdultContentPolicy);

/// <summary>
/// Everything one run needs, assembled by the subject writer.
///
/// The operation decides which members are meaningful: an <see cref="MediaEditOperationKind.Edit"/> run
/// carries a prompt, its references and an editor resolution; a <see cref="MediaEditOperationKind.Crop"/>
/// run carries none of them, because it never reaches a model. The handler enforces the pairing, so a
/// half-filled plan fails fast instead of running with something invented.
/// </summary>
public sealed record MediaEditRunPlan(
    string ImageId,
    string SourceImageId,
    Func<CancellationToken, Task<Stream>> SourceOpenAsync,
    string SourceSha256,
    MediaEditOperation Operation,
    string? Prompt = null,
    IReadOnlyList<MediaEditReference>? References = null,
    MediaEditEditorResolution? Editor = null,
    string? LogScope = null);

/// <summary>
/// The produced bytes, ready to persist, plus what produced them. For a crop the model fields stay null:
/// the record must not claim a model rendered an operation it never called.
/// </summary>
public sealed record MediaEditRunOutput(
    byte[] Bytes,
    MediaEditOperationKind Operation,
    string? ModelIdentifier = null,
    string? ProviderName = null,
    ImageContentPolicy? ContentPolicy = null,

    /// <summary>
    /// The instruction the editor was ACTUALLY given, when it differs from the prompt the row was queued with. The
    /// send-time reference role clause — the <c>&lt;imageN&gt;</c> tags and the sentences naming each reference — is
    /// appended here and never rewrote the queued prompt, so without this the row cannot say what the model read.
    /// Null for an operation that reaches no model.
    /// </summary>
    string? SentInstruction = null);

/// <summary>
/// The ONE seam of the editing step: how to validate a queued image and assemble its run, and how to
/// persist the result. Editor model resolution, the client call, timing, logging and failure marking
/// are shared, so a fix to them lands once for every subject kind.
/// </summary>
public interface IMediaEditSubjectWriter
{
    MediaEditSubjectKind Kind { get; }

    /// <summary>
    /// Validates the queued image and assembles its run plan, or throws with the exact reason. Returns
    /// null when the queued image is already complete (or cancelled): a redelivered job is then a no-op
    /// instead of paying for the same edit twice.
    /// </summary>
    Task<MediaEditRunPlan?> PrepareAsync(MediaEditRunContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the queued row for this run, after preparation and BEFORE the model is called. Returns false
    /// when the row can no longer be claimed (already complete, cancelled, or otherwise terminal); the run
    /// is then skipped instead of paying for work whose result could not be recorded.
    ///
    /// A scene-image edit MUST be claimed: <c>ISceneImageRepository.TryCompleteImageAsync</c> only completes
    /// a row in 'Generating', because the retired per-stage handlers claimed before they ran. A deterministic
    /// operation is deliberately never claimed — its completion accepts a row that is still 'Pending' — so a
    /// writer reports the claim as satisfied for one. The scene asset store has no claim transition at all
    /// (a scene asset is Pending, Complete or Failed and completes through an unguarded upsert), so that
    /// writer reports the claim as satisfied too, rather than inventing a status its store does not have.
    /// </summary>
    Task<bool> ClaimAsync(MediaEditRunContext context, CancellationToken cancellationToken = default);

    /// <summary>Stores the produced bytes and completes the image row and its edit session.</summary>
    Task CompleteAsync(MediaEditRunPlan plan, MediaEditRunOutput output, CancellationToken cancellationToken = default);

    /// <summary>Marks the image (and its edit session) failed. Safe to call when preparation itself threw.</summary>
    Task FailAsync(MediaEditRunContext context, string error, CancellationToken cancellationToken = default);
}

/// <summary>Selects the subject writer. A missing writer fails fast — never a default.</summary>
public sealed class MediaEditSubjectWriterResolver
{
    private readonly IReadOnlyList<IMediaEditSubjectWriter> _writers;

    public MediaEditSubjectWriterResolver(IEnumerable<IMediaEditSubjectWriter> writers)
        => _writers = writers.ToList();

    public IMediaEditSubjectWriter Resolve(MediaEditSubjectKind kind)
        => _writers.FirstOrDefault(writer => writer.Kind == kind)
            ?? throw new InvalidOperationException($"No media edit subject writer is registered for subject kind '{kind}'.");
}
