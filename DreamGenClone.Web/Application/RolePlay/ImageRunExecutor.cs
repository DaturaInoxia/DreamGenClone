using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Evaluation;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>What a caller declares when starting a run. The suite's own version is read, never passed.</summary>
public sealed record ImageRunStart(
    string SuiteId,

    /// <summary>
    /// Which of the cell's two prompts THIS run renders from (D19). Run-wide on purpose: "does the compiled prompt
    /// perform as well as the hand-authored one?" is answered by two runs over the same cells, not by one run that
    /// mixes them. Each cell still records it, so a cell's report is readable on its own.
    /// </summary>
    ImageRenderSource RenderFromPath,

    bool ImageLayerRequested,

    /// <summary>JSON: the compiler LLM pinned for the whole run (D11).</summary>
    string CompilerLlmJson,

    string Notes = "",
    string Provenance = "operator");

public interface IImageRunExecutor
{
    /// <summary>Creates the run and snapshots every cell's declaration into it. Does not evaluate anything.</summary>
    Task<ImageRun> CreateRunAsync(ImageRunStart start, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the free layers over a run's cells: compile, then the prompt layer. Per-cell failure is recorded on the
    /// cell and the run continues, so one bad cell cannot hide the verdicts of the others.
    /// </summary>
    Task<ImageRun> RunFreeLayersAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the request layer against a request the app actually built. Separate from
    /// <see cref="RunFreeLayersAsync"/> because it needs a resolved request, which only exists once something rendered.
    /// </summary>
    Task<ImageRunCell> ApplyRequestLayerAsync(
        string runCellId,
        ImageResolvedRequest resolved,
        ImageRequestExpectation expectation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Drives the free layers over a suite's cells (B-135 B135-015).
///
/// <para>
/// <b>The run owns its evidence.</b> Creating a run copies each cell's declaration into the run at that moment, so a
/// later edit to the cell cannot change what a completed run is reported to have tested. A re-run is a new run.
/// </para>
///
/// <para>
/// <b>Failures are per cell.</b> A cell that cannot compile — a missing profile, a compiler LLM the app does not
/// resolve — is recorded as FAILED with its message and the run carries on, because aborting the whole batch on the
/// first bad cell would hide the verdicts of every cell after it.
/// </para>
/// </summary>
public sealed class ImageRunExecutor : IImageRunExecutor
{
    /// <summary>
    /// The options the run's stored JSON is written with. Lives in <see cref="ImageLayerJson"/> because the Playground is
    /// the reader: one shared contract, not a private copy on each side that can drift.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = ImageLayerJson.Options;

    private readonly IImageSuiteRepository _suites;
    private readonly IImageRunRepository _runs;
    private readonly IImageCompilerProfileRepository _profiles;
    private readonly IImageCellPromptCompiler _compiler;
    private readonly ILogger<ImageRunExecutor> _logger;

    public ImageRunExecutor(
        IImageSuiteRepository suites,
        IImageRunRepository runs,
        IImageCompilerProfileRepository profiles,
        IImageCellPromptCompiler compiler,
        ILogger<ImageRunExecutor> logger)
    {
        _suites = suites;
        _runs = runs;
        _profiles = profiles;
        _compiler = compiler;
        _logger = logger;
    }

    public async Task<ImageRun> CreateRunAsync(ImageRunStart start, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(start);

        if (string.IsNullOrWhiteSpace(start.SuiteId))
        {
            throw new InvalidOperationException("A run must name the suite it executes.");
        }

        if (start.RenderFromPath == ImageRenderSource.Unknown)
        {
            throw new InvalidOperationException(
                "A run must declare which prompt it renders from (CompiledPrompt or ExpectedPrompt). An unrecorded "
                + "source makes every result unattributable to either prompt (D19).");
        }

        if (start.ImageLayerRequested)
        {
            // An explicit capability gate rather than a silent downgrade to a free-layer-only run: a caller who asked for
            // images and got none, with the run marked Complete, would believe images existed.
            throw new InvalidOperationException(
                "This run requested the image layer, which is not wired yet. The bindings-to-settings mapping (which "
                + "identity pack, LoRA artifact, skeleton and render mode a declared binding means) is what is missing, "
                + "and it is B135-023/B135-024. Run the free layers only (ImageLayerRequested = false), or build that "
                + "mapping first.");
        }

        var suite = await _suites.GetSuiteAsync(start.SuiteId, cancellationToken)
            ?? throw new InvalidOperationException($"Suite '{start.SuiteId}' was not found, so it cannot be run.");

        var cells = await _suites.ListCellsAsync(suite.Id, cancellationToken);
        if (cells.Count == 0)
        {
            throw new InvalidOperationException(
                $"Suite '{suite.Name}' has no cells. A run of no cells proves nothing and would look like a passing batch.");
        }

        // Refuses a declaration the compile step could not honour, at the moment the run is created rather than in the
        // middle of a batch.
        _ = ImageCellCompilerLlmSettings.Parse(start.CompilerLlmJson);

        var run = new ImageRun
        {
            SuiteId = suite.Id,
            SuiteVersion = suite.Version,
            Kind = suite.Kind,
            Status = ImageRunStatus.Queued,
            ImageLayerRequested = start.ImageLayerRequested,
            CompilerLlmJson = start.CompilerLlmJson,
            Notes = start.Notes,
            Provenance = start.Provenance,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
        ImageRunValidation.Validate(run);
        await _runs.InsertRunAsync(run, cancellationToken);

        foreach (var cell in cells)
        {
            var runCell = new ImageRunCell
            {
                RunId = run.Id,
                SuiteId = suite.Id,
                CellId = cell.Id,
                Ordinal = cell.Ordinal,
                Name = cell.Name,
                CheckpointProfileId = cell.CheckpointProfileId,
                UserDirection = cell.UserDirection,
                ExpectedPrompt = cell.ExpectedPrompt,
                BindingsJson = cell.BindingsJson,
                SeedJson = cell.SeedJson,
                SettingsJson = cell.SettingsJson,
                GatesJson = cell.GatesJson,
                CompilerLlmJson = start.CompilerLlmJson,
                SimilarityTolerance = cell.SimilarityTolerance,
                RenderFromPath = start.RenderFromPath,
                Status = ImageRunCellStatus.Pending,
                VisualVerdict = ImageVisualVerdict.Unreviewed,
                UpdatedUtc = DateTime.UtcNow
            };

            await _runs.UpsertCellAsync(runCell, cancellationToken);
        }

        _logger.LogInformation(
            "Playground run {RunId} created for suite {Suite} v{Version} with {Cells} cell(s); render source {Source}, image layer {ImageLayer}",
            run.Id, suite.Name, suite.Version, cells.Count, start.RenderFromPath, start.ImageLayerRequested);

        return run;
    }

    public async Task<ImageRun> RunFreeLayersAsync(string runId, CancellationToken cancellationToken = default)
    {
        var run = await _runs.GetRunAsync(runId, cancellationToken)
            ?? throw new InvalidOperationException($"Run '{runId}' was not found.");

        if (run.Status is ImageRunStatus.Complete or ImageRunStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"Run '{run.Id}' is already {run.Status}. Free layers are not re-run in place: a re-run is a new run, so "
                + "the evidence of this one stays as it was.");
        }

        run.Status = ImageRunStatus.Running;
        run.StartedUtc ??= DateTime.UtcNow;
        run.UpdatedUtc = DateTime.UtcNow;
        await _runs.UpdateRunAsync(run, cancellationToken);

        var cells = await _runs.ListCellsAsync(run.Id, cancellationToken);
        foreach (var cell in cells)
        {
            await RunFreeLayersForCellAsync(run, cell, cancellationToken);
        }

        run.Status = ImageRunStatus.Complete;
        run.CompletedUtc = DateTime.UtcNow;
        run.UpdatedUtc = DateTime.UtcNow;
        await _runs.UpdateRunAsync(run, cancellationToken);

        return run;
    }

    private async Task RunFreeLayersForCellAsync(ImageRun run, ImageRunCell cell, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await ResolveProfileAsync(cell, cancellationToken);
            var bindings = ImageCellBindings.Parse(cell.BindingsJson);
            var settings = ImageCellCompilerLlmSettings.Parse(run.CompilerLlmJson);

            var compiled = await _compiler.CompileAsync(
                new ImageCellCompileInput(cell.UserDirection, profile, settings, bindings),
                cancellationToken);

            var conformance = ImagePromptConformanceEvaluator.Evaluate(
                compiled.CompiledPrompt,
                cell.ExpectedPrompt,
                cell.SimilarityTolerance,
                profile);

            cell.ResolvedCheckpoint = profile.CheckpointIdentifier;
            cell.CompiledPrompt = compiled.CompiledPrompt;
            cell.Similarity = conformance.Similarity;
            cell.PromptLayerJson = JsonSerializer.Serialize(conformance.Checks, JsonOptions);
            cell.Status = ImageRunCellStatus.Compiled;
            cell.FailureMessage = string.Empty;
            cell.UpdatedUtc = DateTime.UtcNow;

            ImageRunValidation.ValidateCell(cell);
            await _runs.UpsertCellAsync(cell, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // Recorded, not swallowed: the cell says what refused it, and the run continues so the other cells' verdicts
            // are not lost behind this one.
            _logger.LogInformation(
                "Playground cell {Ordinal} ('{Name}') failed the free layers: {Message}",
                cell.Ordinal, cell.Name, exception.Message);

            cell.Status = ImageRunCellStatus.Failed;
            cell.FailureMessage = exception.Message;
            cell.UpdatedUtc = DateTime.UtcNow;
            await _runs.UpsertCellAsync(cell, cancellationToken);
        }
    }

    public async Task<ImageRunCell> ApplyRequestLayerAsync(
        string runCellId,
        ImageResolvedRequest resolved,
        ImageRequestExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(expectation);

        var cell = await _runs.GetCellAsync(runCellId, cancellationToken)
            ?? throw new InvalidOperationException($"Run cell '{runCellId}' was not found.");

        var profile = await ResolveProfileAsync(cell, cancellationToken);
        var bindings = ImageCellBindings.Parse(cell.BindingsJson);

        var conformance = ImageRequestConformanceEvaluator.Evaluate(bindings, resolved, profile, expectation);

        cell.RequestLayerJson = JsonSerializer.Serialize(conformance.Checks, JsonOptions);
        cell.RequestSnapshotJson = JsonSerializer.Serialize(resolved, JsonOptions);
        cell.ResolvedCheckpoint = profile.CheckpointIdentifier;
        cell.ResolvedProvider = resolved.Provider;
        cell.Seed = resolved.Seed;
        cell.UpdatedUtc = DateTime.UtcNow;

        ImageRunValidation.ValidateCell(cell);
        await _runs.UpsertCellAsync(cell, cancellationToken);

        return cell;
    }

    /// <summary>
    /// Resolves the profile the cell pinned. A cell with no profile cannot be compiled for, and the message says what to
    /// do about it rather than falling back to a family's profile — the same fail-fast posture the resolver takes.
    /// </summary>
    private async Task<ImageCompilerProfile> ResolveProfileAsync(ImageRunCell cell, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cell.CheckpointProfileId))
        {
            throw new InvalidOperationException(
                $"Cell '{cell.Name}' names no checkpoint profile. A cell is compiled for one checkpoint (B-135 D13), so "
                + "choose the profile on the cell before running it.");
        }

        var profile = await _profiles.FindByIdAsync(cell.CheckpointProfileId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Cell '{cell.Name}' pins checkpoint profile '{cell.CheckpointProfileId}', which does not exist. A run "
                + "cannot be judged against instructions nobody wrote.");

        ImageCompilerProfileValidation.Validate(profile);
        return profile;
    }
}
