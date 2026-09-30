using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-014 — the run store.
///
/// <para>
/// Two load-bearing behaviours. First, a run owns its evidence: the declaration each cell ran against is COPIED into
/// the run, so a later edit to a cell cannot rewrite the history of a run that already happened. Second, a run cannot
/// be retargeted at a different suite — the update path deliberately cannot touch the suite identity, because that is
/// the same re-labelling failure in a different costume.
/// </para>
/// </summary>
public sealed class ImageRunRepositoryTests
{
    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"b135-run-{Guid.NewGuid():N}.db");

    private static ImageRunRepository NewRepository(string dbPath) =>
        new(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" }));

    private static ImageRun ValidRun(string suiteId = "suite-1", bool imageLayer = true) => new()
    {
        SuiteId = suiteId,
        SuiteVersion = 3,
        Kind = ImageSuiteKind.Comparison,
        Status = ImageRunStatus.Running,
        ImageLayerRequested = imageLayer,
        CompilerLlmJson = """{"model":"qwen3.5-vl","temperature":0,"seed":42}""",
        Notes = "baseline vs becky-v7",
        Provenance = "operator",
    };

    private static ImageRunCell ValidCell(string runId, string suiteId = "suite-1", int ordinal = 0) => new()
    {
        RunId = runId,
        SuiteId = suiteId,
        CellId = $"cell-{ordinal}",
        Ordinal = ordinal,
        Name = $"cell-{ordinal}",
        UserDirection = "Woman laying on bed, seductive look, hands touching herself",
        ExpectedPrompt = "a photograph of a woman lying on a bed, one hand on her thigh, warm low lamp light",
        BindingsJson = """[{"axis":"Identity","mode":"Lora","value":"becky-v7","strength":0.8}]""",
        CompilerLlmJson = """{"model":"qwen3.5-vl","temperature":0,"seed":42}""",
        SimilarityTolerance = 0.7,
        Status = ImageRunCellStatus.Pending,
        RenderFromPath = ImageRenderSource.CompiledPrompt,
        VisualVerdict = ImageVisualVerdict.Unreviewed,
    };

    // ---- validation ---------------------------------------------------------------------------------------

    [Fact]
    public void ARunMustDeclareItsSuiteKindStatusAndVersion()
    {
        var noKind = ValidRun();
        noKind.Kind = ImageSuiteKind.Unknown;
        Assert.Throws<InvalidOperationException>(() => ImageRunValidation.Validate(noKind));

        var noStatus = ValidRun();
        noStatus.Status = ImageRunStatus.Unknown;
        Assert.Throws<InvalidOperationException>(() => ImageRunValidation.Validate(noStatus));

        var noVersion = ValidRun();
        noVersion.SuiteVersion = 0;
        Assert.Contains("version", Assert.Throws<InvalidOperationException>(() => ImageRunValidation.Validate(noVersion)).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACellMustRecordWhichPromptDroveTheRender()
    {
        // D19: the source is a declared variable, not an inference. Without it the result is not attributable to either
        // prompt, and a comparison run would silently be comparing a prompt against itself.
        var cell = ValidCell("run-1");
        cell.RenderFromPath = ImageRenderSource.Unknown;

        var error = Assert.Throws<InvalidOperationException>(() => ImageRunValidation.ValidateCell(cell));

        Assert.Contains("which prompt", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACompiledCellWithNoCompiledPromptIsRefused()
    {
        var cell = ValidCell("run-1");
        cell.Status = ImageRunCellStatus.Compiled;
        cell.CompiledPrompt = string.Empty;

        Assert.Throws<InvalidOperationException>(() => ImageRunValidation.ValidateCell(cell));
    }

    [Fact]
    public void AFailedCellMustSayWhyAndAPassingOneMustNotClaimFailure()
    {
        var failed = ValidCell("run-1");
        failed.Status = ImageRunCellStatus.Failed;
        Assert.Throws<InvalidOperationException>(() => ImageRunValidation.ValidateCell(failed));

        var passing = ValidCell("run-1");
        passing.FailureMessage = "checkpoint not found";
        Assert.Throws<InvalidOperationException>(() => ImageRunValidation.ValidateCell(passing));
    }

    [Fact]
    public void ACellsDeclarationIsValidatedThroughTheSameContractAsTheSuite()
    {
        var cell = ValidCell("run-1");
        cell.BindingsJson = """[{"axis":"Identity","mode":"Lora","value":"becky-v7","strenght":0.8}]""";

        var error = Assert.Throws<InvalidOperationException>(() => ImageRunValidation.ValidateCell(cell));

        Assert.Contains("strenght", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunThatRequestedImagesCannotCompleteACellWithoutOne()
    {
        var run = ValidRun(imageLayer: true);
        var cell = ValidCell(run.Id);
        cell.Status = ImageRunCellStatus.Complete;
        cell.CompiledPrompt = "a photograph of a woman lying on a bed";
        cell.ImagePath = null;

        var error = Assert.Throws<InvalidOperationException>(() => ImageRunValidation.ValidateCellForRun(run, cell));

        Assert.Contains("image", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void APromptOnlyRunMayCompleteWithoutAnImage()
    {
        // The free layers are always available; the image layer is opt-in (D14), so a complete free-layer cell having no
        // image is the normal case rather than a defect.
        var run = ValidRun(imageLayer: false);
        var cell = ValidCell(run.Id);
        cell.Status = ImageRunCellStatus.Complete;
        cell.CompiledPrompt = "a photograph of a woman lying on a bed";

        ImageRunValidation.ValidateCellForRun(run, cell);
    }

    // ---- persistence --------------------------------------------------------------------------------------

    [Fact]
    public async Task ARunRoundTrips()
    {
        var db = NewDbPath();
        var repository = NewRepository(db);
        var run = ValidRun();
        run.StartedUtc = DateTime.UtcNow;

        await repository.InsertRunAsync(run);
        var loaded = await repository.GetRunAsync(run.Id);

        Assert.NotNull(loaded);
        Assert.Equal("suite-1", loaded!.SuiteId);
        Assert.Equal(3, loaded.SuiteVersion);
        Assert.Equal(ImageSuiteKind.Comparison, loaded.Kind);
        Assert.Equal(ImageRunStatus.Running, loaded.Status);
        Assert.True(loaded.ImageLayerRequested);
        Assert.Equal(run.CompilerLlmJson, loaded.CompilerLlmJson);
        Assert.NotNull(loaded.StartedUtc);
    }

    [Fact]
    public async Task ACellRoundTripsEveryLayer()
    {
        var db = NewDbPath();
        var repository = NewRepository(db);
        var run = ValidRun();
        await repository.InsertRunAsync(run);

        var cell = ValidCell(run.Id);
        cell.Status = ImageRunCellStatus.Complete;
        cell.CompiledPrompt = "a photograph of a woman lying on a bed, warm lamp light";
        cell.Similarity = 0.63;
        cell.ResolvedCheckpoint = "juggernautXL_ragnarok.safetensors";
        cell.ResolvedProvider = "runpod-juggernaut";
        cell.Seed = 4242;
        cell.ImageId = "img-1";
        cell.ImagePath = "data/scene-images/session/img-1.png";
        cell.TimingMs = 18_400;
        cell.CostUsd = 0.0123m;
        cell.PromptLayerJson = """[{"name":"budget-characters","outcome":"Pass","detail":"ok"}]""";
        cell.RequestLayerJson = """[{"name":"seed-honoured","outcome":"Pass","detail":"4242"}]""";
        cell.RequestSnapshotJson = """{"checkpoint":"juggernautXL_ragnarok.safetensors","seed":4242}""";
        cell.GateResultsJson = """[{"name":"region-containment","outcome":"Pass"}]""";
        cell.VisualVerdict = ImageVisualVerdict.Accept;
        cell.VisualNote = "hands correct, pose held";

        await repository.UpsertCellAsync(cell);
        var loaded = await repository.GetCellAsync(cell.Id);

        Assert.NotNull(loaded);
        Assert.Equal(0.63, loaded!.Similarity);
        Assert.Equal(0.7, loaded.SimilarityTolerance);
        Assert.Equal(4242, loaded.Seed);
        Assert.Equal(ImageRenderSource.CompiledPrompt, loaded.RenderFromPath);
        Assert.Equal(ImageRunCellStatus.Complete, loaded.Status);
        Assert.Equal(ImageVisualVerdict.Accept, loaded.VisualVerdict);
        Assert.Equal("hands correct, pose held", loaded.VisualNote);
        Assert.Equal("juggernautXL_ragnarok.safetensors", loaded.ResolvedCheckpoint);
        Assert.Equal(18_400, loaded.TimingMs);
        Assert.Equal(0.0123m, loaded.CostUsd);
        Assert.Equal(cell.RequestSnapshotJson, loaded.RequestSnapshotJson);
        Assert.Empty(loaded.FailureMessage);
    }

    [Fact]
    public async Task UpdatingACellInPlaceDoesNotCreateASecondRow()
    {
        var db = NewDbPath();
        var repository = NewRepository(db);
        var run = ValidRun();
        await repository.InsertRunAsync(run);

        var cell = ValidCell(run.Id);
        cell.Status = ImageRunCellStatus.Pending;
        await repository.UpsertCellAsync(cell);

        cell.Status = ImageRunCellStatus.Compiled;
        cell.CompiledPrompt = "a photograph of a woman lying on a bed";
        await repository.UpsertCellAsync(cell);

        var cells = await repository.ListCellsAsync(run.Id);

        var only = Assert.Single(cells);
        Assert.Equal(ImageRunCellStatus.Compiled, only.Status);
    }

    [Fact]
    public async Task CellsComeBackInOrdinalOrder()
    {
        var db = NewDbPath();
        var repository = NewRepository(db);
        var run = ValidRun();
        await repository.InsertRunAsync(run);

        await repository.UpsertCellAsync(ValidCell(run.Id, ordinal: 2));
        await repository.UpsertCellAsync(ValidCell(run.Id, ordinal: 0));
        await repository.UpsertCellAsync(ValidCell(run.Id, ordinal: 1));

        var cells = await repository.ListCellsAsync(run.Id);

        Assert.Equal([0, 1, 2], cells.Select(cell => cell.Ordinal));
    }

    [Fact]
    public async Task TheSameOrdinalInTheSameRunIsOneCellNotTwo()
    {
        // A cell's POSITION is its identity within a run, so a duplicate is a rewrite of that position rather than a
        // second entry - the unique index makes that a database rule instead of a convention the writer must remember.
        var db = NewDbPath();
        var repository = NewRepository(db);
        var run = ValidRun();
        await repository.InsertRunAsync(run);

        var first = ValidCell(run.Id, ordinal: 0);
        await repository.UpsertCellAsync(first);

        var replacement = ValidCell(run.Id, ordinal: 0);
        replacement.Name = "renamed";
        await repository.UpsertCellAsync(replacement);

        var cells = await repository.ListCellsAsync(run.Id);
        Assert.Single(cells);
        Assert.Equal("renamed", cells[0].Name);
    }

    [Fact]
    public async Task ARunCannotBeRetargetedAtAnotherSuite()
    {
        var db = NewDbPath();
        var repository = NewRepository(db);
        var run = ValidRun();
        await repository.InsertRunAsync(run);

        run.SuiteId = "suite-2";
        run.SuiteVersion = 9;
        run.Status = ImageRunStatus.Complete;
        await repository.UpdateRunAsync(run);

        var loaded = await repository.GetRunAsync(run.Id);

        // The status moved; the suite identity did not. Old evidence stays attached to the cells that produced it.
        Assert.Equal("suite-1", loaded!.SuiteId);
        Assert.Equal(3, loaded.SuiteVersion);
        Assert.Equal(ImageRunStatus.Complete, loaded.Status);
    }

    [Fact]
    public async Task UpdatingARunThatDoesNotExistIsRefused()
    {
        var repository = NewRepository(NewDbPath());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateRunAsync(ValidRun()));

        Assert.Contains("does not exist", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunsListNewestFirstAndCanBeScopedToOneSuite()
    {
        var db = NewDbPath();
        var repository = NewRepository(db);

        var older = ValidRun("suite-1");
        older.CreatedUtc = DateTime.UtcNow.AddMinutes(-10);
        var newer = ValidRun("suite-1");
        newer.CreatedUtc = DateTime.UtcNow;
        var other = ValidRun("suite-2");

        await repository.InsertRunAsync(older);
        await repository.InsertRunAsync(newer);
        await repository.InsertRunAsync(other);

        var all = await repository.ListRunsAsync();
        var scoped = await repository.ListRunsAsync("suite-1");

        Assert.Equal(3, all.Count);
        Assert.Equal([newer.Id, older.Id], scoped.Select(run => run.Id));
    }

    [Fact]
    public async Task AnEmptyStoreIsALegitimateState()
    {
        var repository = NewRepository(NewDbPath());

        Assert.Empty(await repository.ListRunsAsync());
        Assert.Null(await repository.GetRunAsync("missing"));
    }

    [Fact]
    public async Task DeletingARunTakesItsCellsWithIt()
    {
        var db = NewDbPath();
        var repository = NewRepository(db);
        var run = ValidRun();
        await repository.InsertRunAsync(run);
        var cell = ValidCell(run.Id);
        await repository.UpsertCellAsync(cell);

        await repository.DeleteRunAsync(run.Id);

        Assert.Null(await repository.GetRunAsync(run.Id));
        Assert.Null(await repository.GetCellAsync(cell.Id));
    }
}
