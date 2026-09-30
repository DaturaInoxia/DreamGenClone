using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>The lifecycle of one run of a suite's cells.</summary>
public enum ImageRunStatus
{
    Unknown = 0,

    /// <summary>Authored but not started. A run may sit here while its cells are being written.</summary>
    Draft = 1,

    Queued = 2,
    Running = 3,

    /// <summary>Every cell reached a terminal state. Individual cells may still have failed.</summary>
    Complete = 4,

    /// <summary>The run itself could not proceed (a missing profile, a provider refusal).</summary>
    Failed = 5,

    Cancelled = 6
}

/// <summary>Where one cell of a run got to.</summary>
public enum ImageRunCellStatus
{
    Unknown = 0,
    Pending = 1,

    /// <summary>The compiler ran and the prompt layer was evaluated. No render yet.</summary>
    Compiled = 2,

    /// <summary>A render is in flight through the production render path.</summary>
    Rendering = 3,

    Complete = 4,

    /// <summary>Refused or errored — <c>FailureMessage</c> says which.</summary>
    Failed = 5,

    /// <summary>Deliberately not run (the image layer was not requested, or a gate upstream refused it).</summary>
    Skipped = 6
}

/// <summary>
/// Which of the cell's two prompts drove the render (B-135 D19). Recorded per cell because it is the variable that
/// makes "does the compiled prompt perform as well as the hand-authored one?" answerable rather than assumed.
/// </summary>
public enum ImageRenderSource
{
    Unknown = 0,

    /// <summary>The compiler's output from <c>UserDirection</c> — the thing under test.</summary>
    CompiledPrompt = 1,

    /// <summary>The cell's canned <c>ExpectedPrompt</c> — the baseline the compiled one is measured against.</summary>
    ExpectedPrompt = 2
}

/// <summary>The operator's verdict on the produced image. Never inferred from a gate: a gate measures, a person judges.</summary>
public enum ImageVisualVerdict
{
    Unknown = 0,
    Unreviewed = 1,
    Accept = 2,
    Reject = 3
}

/// <summary>
/// One execution of a suite (B-135 B135-014, D14).
///
/// <para>
/// <b>The run owns its evidence.</b> It records the suite VERSION it executed, and each of its cells carries a copy of
/// the declaration it ran against. A later edit to a cell therefore cannot rewrite the history of a run that already
/// happened — which is the failure mode that makes "we tested it last week" unfalsifiable.
/// </para>
/// </summary>
public sealed class ImageRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string SuiteId { get; set; } = string.Empty;

    /// <summary>The suite version this run executed. Frozen: a run is never retargeted to a newer version.</summary>
    public int SuiteVersion { get; set; } = 1;

    /// <summary>Copied from the suite so a comparison view can group runs without a join.</summary>
    public ImageSuiteKind Kind { get; set; } = ImageSuiteKind.Unknown;

    public ImageRunStatus Status { get; set; } = ImageRunStatus.Unknown;

    /// <summary>
    /// Whether this run spends GPU time (D14). A prompt/request-only run is free, so it is always available; the image
    /// layer is opt-in per run and never implied by the presence of a suite.
    /// </summary>
    public bool ImageLayerRequested { get; set; }

    /// <summary>JSON object: the compiler LLM pinned for this run (model, temperature, seed) — D11.</summary>
    public string CompilerLlmJson { get; set; } = "{}";

    public string Notes { get; set; } = string.Empty;

    /// <summary>Where the run came from: an operator, a captured RP session (B135-033), or an import.</summary>
    public string Provenance { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One cell's execution inside a run (B-135 B135-014).
///
/// <para>
/// Three blocks, in the order the layers run: the DECLARATION snapshot (what the cell said), the LAYER VERDICTS (what
/// the free prompt and request layers found), and the RENDER evidence (what the image layer actually produced). The
/// declaration block is a copy rather than a reference on purpose — see <see cref="ImageRun"/>.
/// </para>
/// </summary>
public sealed class ImageRunCell
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string RunId { get; set; } = string.Empty;

    public string SuiteId { get; set; } = string.Empty;

    /// <summary>The <see cref="ImageSuiteCell.Id"/> this ran. Kept for traceability, never for re-reading state.</summary>
    public string CellId { get; set; } = string.Empty;

    public int Ordinal { get; set; }

    public string Name { get; set; } = string.Empty;

    // ---- declaration snapshot ---------------------------------------------------------------------------

    public string? CheckpointProfileId { get; set; }

    public string UserDirection { get; set; } = string.Empty;

    public string ExpectedPrompt { get; set; } = string.Empty;

    public string BindingsJson { get; set; } = "[]";

    public string SeedJson { get; set; } = "{}";

    public string SettingsJson { get; set; } = "{}";

    public string GatesJson { get; set; } = "[]";

    public string CompilerLlmJson { get; set; } = "{}";

    public double? SimilarityTolerance { get; set; }

    // ---- resolved ----------------------------------------------------------------------------------------

    /// <summary>The checkpoint that actually rendered, from the profile lookup.</summary>
    public string ResolvedCheckpoint { get; set; } = string.Empty;

    public string ResolvedProvider { get; set; } = string.Empty;

    /// <summary>The compiler's output. Empty until the compile step runs.</summary>
    public string CompiledPrompt { get; set; } = string.Empty;

    /// <summary>Token-set overlap with <see cref="ExpectedPrompt"/>; the headline number of the prompt layer.</summary>
    public double? Similarity { get; set; }

    /// <summary>JSON: every prompt-layer check with its outcome and detail.</summary>
    public string PromptLayerJson { get; set; } = "[]";

    /// <summary>JSON: every request-layer check with its outcome and detail.</summary>
    public string RequestLayerJson { get; set; } = "[]";

    /// <summary>JSON: the <see cref="ImageResolvedRequest"/> the render actually submitted.</summary>
    public string RequestSnapshotJson { get; set; } = "{}";

    // ---- render evidence ----------------------------------------------------------------------------------

    /// <summary>Which of the two prompts drove the render (D19). Never defaulted.</summary>
    public ImageRenderSource RenderFromPath { get; set; } = ImageRenderSource.Unknown;

    /// <summary>The seed actually used, or null when the render drew a random one.</summary>
    public long? Seed { get; set; }

    public ImageRunCellStatus Status { get; set; } = ImageRunCellStatus.Unknown;

    /// <summary>The produced image record, when the image layer ran.</summary>
    public string? ImageId { get; set; }

    public string? ImagePath { get; set; }

    /// <summary>JSON: the native gates' measurements and verdicts (P3). Empty until they run.</summary>
    public string GateResultsJson { get; set; } = "[]";

    public long? TimingMs { get; set; }

    /// <summary>Recorded in USD because that is what the provider bills. Null when the endpoint is local.</summary>
    public decimal? CostUsd { get; set; }

    public ImageVisualVerdict VisualVerdict { get; set; } = ImageVisualVerdict.Unreviewed;

    public string VisualNote { get; set; } = string.Empty;

    /// <summary>Why the cell failed, verbatim from the refusal. Empty on success — a failure is never summarised away.</summary>
    public string FailureMessage { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The one validation path for runs and their cells. The repository calls it on every write and the executor calls it
/// before doing work, so a malformed run is refused at the edge rather than producing an unreadable history.
/// </summary>
public static class ImageRunValidation
{
    public static void Validate(ImageRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        Require(run.Id, "Run id");
        Require(run.SuiteId, "Run suite id");

        if (run.SuiteVersion < 1)
        {
            throw new InvalidOperationException($"Run '{run.Id}' declares suite version {run.SuiteVersion}; versions start at 1.");
        }

        if (run.Kind == ImageSuiteKind.Unknown)
        {
            throw new InvalidOperationException($"Run '{run.Id}' must record the kind of suite it executed.");
        }

        if (run.Status == ImageRunStatus.Unknown)
        {
            throw new InvalidOperationException($"Run '{run.Id}' must declare a status.");
        }

        RequireJsonObject(run.CompilerLlmJson, nameof(run.CompilerLlmJson), run.Id);
    }

    public static void ValidateCell(ImageRunCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        Require(cell.Id, "Run cell id");
        Require(cell.RunId, "Run cell run id");
        Require(cell.SuiteId, "Run cell suite id");
        Require(cell.CellId, "Run cell cell id");
        Require(cell.Name, "Run cell name");

        if (cell.Ordinal < 0)
        {
            throw new InvalidOperationException($"Run cell '{cell.Id}' has ordinal {cell.Ordinal}; ordinals start at 0.");
        }

        if (cell.Status == ImageRunCellStatus.Unknown)
        {
            throw new InvalidOperationException($"Run cell '{cell.Id}' must declare a status.");
        }

        if (cell.RenderFromPath == ImageRenderSource.Unknown)
        {
            throw new InvalidOperationException(
                $"Run cell '{cell.Id}' must record which prompt drove the render (CompiledPrompt or ExpectedPrompt). "
                + "An unrecorded source makes the result unattributable to either prompt.");
        }

        if (cell.VisualVerdict == ImageVisualVerdict.Unknown)
        {
            throw new InvalidOperationException($"Run cell '{cell.Id}' must declare a visual verdict (Unreviewed is a verdict).");
        }

        // The declaration snapshot is validated through the SAME contract the cell was authored against, so a run
        // cannot hold a declaration the suite store would have refused.
        ImageCellBindings.Parse(cell.BindingsJson);

        RequireJsonObject(cell.SeedJson, nameof(cell.SeedJson), cell.Id);
        RequireJsonObject(cell.SettingsJson, nameof(cell.SettingsJson), cell.Id);
        RequireJsonArray(cell.GatesJson, nameof(cell.GatesJson), cell.Id);
        RequireJsonObject(cell.CompilerLlmJson, nameof(cell.CompilerLlmJson), cell.Id);

        // A cell whose status says it compiled must hold the compile output. An empty prompt behind a "Compiled" or
        // "Complete" status is the state that reads as "ran and passed" when nothing ran at all.
        if (cell.Status is (ImageRunCellStatus.Compiled or ImageRunCellStatus.Rendering or ImageRunCellStatus.Complete)
            && string.IsNullOrWhiteSpace(cell.CompiledPrompt))
        {
            throw new InvalidOperationException(
                $"Run cell '{cell.Id}' is {cell.Status} but holds no compiled prompt, so nothing was actually compiled.");
        }

        if (cell.Status == ImageRunCellStatus.Failed && string.IsNullOrWhiteSpace(cell.FailureMessage))
        {
            throw new InvalidOperationException($"Run cell '{cell.Id}' failed without recording why.");
        }

        if (cell.Status != ImageRunCellStatus.Failed && !string.IsNullOrWhiteSpace(cell.FailureMessage))
        {
            throw new InvalidOperationException(
                $"Run cell '{cell.Id}' is {cell.Status} but carries the failure message '{cell.FailureMessage}'.");
        }
    }

    /// <summary>
    /// The cross-entity rule: a run that asked for images cannot have a completed cell that produced none. Split from
    /// <see cref="ValidateCell"/> because it needs the run, and applied by the executor rather than by every write to
    /// the cell store.
    /// </summary>
    public static void ValidateCellForRun(ImageRun run, ImageRunCell cell)
    {
        ArgumentNullException.ThrowIfNull(run);
        ValidateCell(cell);

        if (run.ImageLayerRequested && cell.Status == ImageRunCellStatus.Complete && string.IsNullOrWhiteSpace(cell.ImagePath))
        {
            throw new InvalidOperationException(
                $"Run '{run.Id}' requested the image layer, but cell '{cell.Id}' is Complete with no image path. A run "
                + "that claims images must be able to show them.");
        }
    }

    private static void Require(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{what} is required.");
        }
    }

    private static void RequireJsonObject(string json, string field, string id)
    {
        RequireJson(json, field, id, JsonValueKind.Object);
    }

    private static void RequireJsonArray(string json, string field, string id)
    {
        RequireJson(json, field, id, JsonValueKind.Array);
    }

    private static void RequireJson(string json, string field, string id, JsonValueKind expected)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                $"'{id}' has an empty {field}; it must be a JSON {expected.ToString().ToLowerInvariant()}.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != expected)
            {
                throw new InvalidOperationException($"'{id}' has a {field} that is not a JSON {expected.ToString().ToLowerInvariant()}.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"'{id}' has a {field} that is not valid JSON: {exception.Message}");
        }
    }
}
