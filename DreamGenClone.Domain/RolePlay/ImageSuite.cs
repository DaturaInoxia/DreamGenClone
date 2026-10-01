namespace DreamGenClone.Domain.RolePlay;

/// <summary>What a suite is for. Decides which comparison surfaces are meaningful for it.</summary>
public enum ImageSuiteKind
{
    Unknown = 0,

    /// <summary>A qualification suite: cells that must keep passing for a capability to stay claimed.</summary>
    Qualification = 1,

    /// <summary>A comparison suite: the same cells run against two variable settings (baseline vs LoRA, checkpoint A vs B).</summary>
    Comparison = 2,

    /// <summary>A regression suite captured from real defects (B135-033 specimen capture).</summary>
    Regression = 3,

    /// <summary>
    /// A prompt CATALOG: a set of positions to survey across several checkpoints, imported from an agent-authored
    /// manifest. Distinct from the other kinds because what it answers is "how does this set look on each model" rather
    /// than "does this capability still work" - which is why a run of one is one checkpoint over all the cells.
    /// </summary>
    Catalog = 4
}

/// <summary>
/// A suite's lifecycle. Only a <see cref="Qualified"/> suite may gate a capability; retiring is how a suite stops
/// being run without being deleted (the run history must stay readable).
/// </summary>
public enum ImageSuiteStatus
{
    Unknown = 0,
    Draft = 1,
    Qualified = 2,
    Retired = 3
}

/// <summary>
/// A named, versioned set of cells (B-135 D4). Suites and cells are configuration in the database, authored in the
/// Playground — not scripts.
///
/// <para>
/// <b>Versioning is frozen on first run.</b> A run records the suite version it executed, and an edit to any cell
/// creates a new version, so a stored result can always be traced back to the exact cells that produced it. A run is
/// never retargeted to a newer version: that would silently re-label old evidence with new cells.
/// </para>
/// </summary>
public sealed class ImageSuite
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    /// <summary>Starts at 1. Incremented when a cell changes after the suite has been run.</summary>
    public int Version { get; set; } = 1;

    public ImageSuiteKind Kind { get; set; } = ImageSuiteKind.Unknown;

    public ImageSuiteStatus Status { get; set; } = ImageSuiteStatus.Draft;

    public string Description { get; set; } = string.Empty;

    /// <summary>Where the suite came from — a seeded catalog, operator authoring, or a captured defect.</summary>
    public string Provenance { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One testable claim about the app (B-135 §2). It carries TWO prompts — the <see cref="UserDirection"/> that is
/// compiled, and the canned <see cref="ExpectedPrompt"/> that is known to work — and both may drive the render.
///
/// <para>
/// The comparison <c>compiled ≈ expected</c> is what makes the compiler testable. Rendering from BOTH (same cell,
/// same seed) is what makes it answerable whether the compiled prompt performs as well as the hand-authored one,
/// instead of assuming it.
/// </para>
/// </summary>
public sealed class ImageSuiteCell
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string SuiteId { get; set; } = string.Empty;

    /// <summary>Position within the suite. Part of the cell's identity in a run, so it is stable once set.</summary>
    public int Ordinal { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The checkpoint profile this cell is compiled for. Resolved by checkpoint, never by family (B-135 D13).</summary>
    public string? CheckpointProfileId { get; set; }

    /// <summary>
    /// What a user would type, or what an RP session would supply — the raw input the compiler must handle. Deliberately
    /// the RP-session shape so a real moment can be captured into a cell (B135-033).
    /// </summary>
    public string UserDirection { get; set; } = string.Empty;

    /// <summary>The canned prompt that is expected to work for this cell. The compiler's target output shape.</summary>
    public string ExpectedPrompt { get; set; } = string.Empty;

    /// <summary>
    /// JSON: every host binding axis with its mode (Text / Reference / Adapter / Lora) and value (B-135 D17). A cell
    /// that cannot reproduce its bindings is not reproducible, so this is required for any render-bearing cell.
    /// </summary>
    public string BindingsJson { get; set; } = "[]";

    /// <summary>
    /// JSON object: ONE PROMPT PER MODEL, keyed by the manifest's variant key (<c>biglust</c>, <c>juggernaut</c>,
    /// <c>qwen-edit-2511</c>).
    ///
    /// <para>
    /// This is what makes "run this set against BigLust" meaningful: the same position is worded differently for each
    /// checkpoint (dense tags for Pony, prose for SDXL, an edit instruction for Qwen Edit), and a run picks the prompt
    /// matching the model it is running. A cell with no variant for the chosen model is simply skipped for that run -
    /// reported, never fatal.
    /// </para>
    /// </summary>
    public string VariantsJson { get; set; } = "{}";

    /// <summary>
    /// JSON array of what the manifest said was missing or malformed for this cell. Displayed against the cell so a gap
    /// is visible without stopping anything from running.
    /// </summary>
    public string ProblemsJson { get; set; } = "[]";

    /// <summary>JSON: the seed policy — one fixed seed, a list, or a grid.</summary>
    public string SeedJson { get; set; } = "{}";

    /// <summary>JSON: steps / cfg / sampler / scheduler / resolution / reference count.</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>
    /// JSON: the pinned compiler LLM for this cell (model, temperature, seed). Required whenever the assertion depends
    /// on compiler output, because an unpinned LLM makes the cell non-repeatable (B-135 D11).
    /// </summary>
    public string CompilerLlmJson { get; set; } = "{}";

    /// <summary>JSON: the declared gates with their configured thresholds. No threshold is ever defaulted (D12).</summary>
    public string GatesJson { get; set; } = "[]";

    /// <summary>
    /// How close the compiled prompt must be to <see cref="ExpectedPrompt"/>. Per cell and never defaulted: a global
    /// constant would let a cell pass by being judged against someone else's tolerance.
    /// </summary>
    public double? SimilarityTolerance { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The one validation path for a suite and its cells. Every write calls it, so there is a single definition of a
/// valid suite — duplicated validation is how two call sites come to disagree about what is allowed.
///
/// <para>
/// <b>What it enforces is IDENTITY, not content.</b> A cell must be findable and orderable (a suite id, a non-negative
/// ordinal, a name). It does NOT require a user direction, an expected prompt or a tolerance: a catalog cell carries one
/// prompt per MODEL and may legitimately have none of those, and the compile step and the prompt layer simply have
/// nothing to say about such a cell. Refusing it at the store would mean an agent-authored catalog cannot be imported
/// because it lacks a field only the compiler needs — which is the wrong place for the check, and it stops the images
/// from ever being made. Content gaps are reported on the cell instead (see <c>ProblemsJson</c>).
/// </para>
/// </summary>
public static class ImageSuiteValidation
{
    public static void Validate(ImageSuite suite)
    {
        ArgumentNullException.ThrowIfNull(suite);

        if (string.IsNullOrWhiteSpace(suite.Name))
        {
            throw new InvalidOperationException("An image suite must have a name.");
        }

        if (suite.Kind == ImageSuiteKind.Unknown)
        {
            throw new InvalidOperationException(
                $"Image suite '{suite.Name}' must declare a kind (Qualification, Comparison, Regression or Catalog).");
        }

        if (suite.Status == ImageSuiteStatus.Unknown)
        {
            throw new InvalidOperationException(
                $"Image suite '{suite.Name}' must declare a status (Draft, Qualified or Retired).");
        }

        if (suite.Version < 1)
        {
            throw new InvalidOperationException(
                $"Image suite '{suite.Name}' has version {suite.Version}; a suite version starts at 1.");
        }
    }

    public static void Validate(ImageSuiteCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (string.IsNullOrWhiteSpace(cell.SuiteId))
        {
            throw new InvalidOperationException("An image suite cell must name the suite it belongs to.");
        }

        if (cell.Ordinal < 0)
        {
            throw new InvalidOperationException(
                $"Image suite cell '{cell.Name}' has ordinal {cell.Ordinal}; an ordinal is zero or greater.");
        }

        if (string.IsNullOrWhiteSpace(cell.Name))
        {
            throw new InvalidOperationException("An image suite cell must have a name.");
        }
    }
}
