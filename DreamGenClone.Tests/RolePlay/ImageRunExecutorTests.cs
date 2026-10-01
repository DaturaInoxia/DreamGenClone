using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-015 — the run executor's free path.
///
/// <para>
/// Three behaviours carry this file. <b>The run owns its evidence</b>: creating a run copies each cell's declaration, so
/// a later edit cannot change what a completed run is reported to have tested. <b>One bad cell does not hide the
/// others</b>: a per-cell refusal is recorded on that cell and the batch carries on, because aborting would lose every
/// verdict after the failure. <b>The image layer is gated, not silently skipped</b>: asking for renders this build
/// cannot do is refused by name, since a run that claimed images and produced none would look successful.
/// </para>
/// </summary>
public sealed class ImageRunExecutorTests
{
    /// <summary>
    /// A fixture-only checkpoint, NOT a real one on purpose: the profile store seeds a row per registered checkpoint, so
    /// reusing a real identifier here would collide with the seed on the unique index and the pinned id would genuinely
    /// not exist - a confusing failure in the test rather than in the code under test.
    /// </summary>
    private const string Checkpoint = "test-checkpoint.safetensors";
    private const string ProfileId = "profile-juggernaut-test";
    private const string CompilerLlm = """{"model":"qwen3.5-vl","temperature":0}""";
    private const string CompiledText = "a woman lying on a bed in warm lamp light, 35mm, natural skin texture";

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"b135-exec-{Guid.NewGuid():N}.db");

    private static ImageSuiteRepository NewSuiteRepo(string dbPath) =>
        new(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" }));

    private static ImageRunRepository NewRunRepo(string dbPath) =>
        new(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" }));

    private static ImageCompilerProfileRepository NewProfileRepo(string dbPath) =>
        new(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" }));

    private static ImageCompilerProfile ValidProfile() => new()
    {
        Id = ProfileId,
        CheckpointIdentifier = Checkpoint,
        DisplayName = "Juggernaut XL Ragnarok",
        Family = SceneImageModelFamily.Sdxl,
        PromptDialect = SceneImagePromptDialect.SdxlNaturalLanguage,
        MinChars = 10,
        MaxChars = 600,
        MaxTokens = 100,
        PoseInText = ImagePoseInText.Forbidden,
        Negative = string.Empty,
        SystemPrompt = SceneImageCompilerSystemPrompts.NaturalLanguageBeat,
        RequiredComponentsJson = """["subject","framing"]""",
        ForbiddenTokensJson = """["story-name"]""",
    };

    private static ImageSuite ValidSuite() => new()
    {
        Name = "baseline-positions",
        Kind = ImageSuiteKind.Comparison,
        Status = ImageSuiteStatus.Draft,
        Description = "baseline vs lora",
        Provenance = "operator",
    };

    private static ImageSuiteCell ValidCell(string suiteId, int ordinal, string? profileId = ProfileId) => new()
    {
        SuiteId = suiteId,
        Ordinal = ordinal,
        Name = $"cell-{ordinal}",
        CheckpointProfileId = profileId,
        // Cells differ from each other, as real ones do - and the ordinal marker is what lets a stub refuse ONE cell so
        // the per-cell failure path can be exercised.
        UserDirection = $"Woman laying on bed, seductive look, hands touching herself [{ordinal}]",
        ExpectedPrompt = "a woman lying on a bed in warm lamp light, 35mm, natural skin texture",
        SimilarityTolerance = 0.7,
        BindingsJson = "[]",
        SeedJson = """{"seed":4242}""",
        SettingsJson = """{"aspect":"1:1"}""",
        GatesJson = "[]",
        CompilerLlmJson = CompilerLlm,
    };

    private static ImageRunStart Start(string suiteId, bool imageLayer = false) =>
        new(suiteId, ImageRenderSource.CompiledPrompt, imageLayer, CompilerLlm, "test run", "test");

    /// <summary>
    /// The executor's own collaborators, wired over real test-database stores so the run/cell round trip is exercised
    /// rather than mocked away.
    /// </summary>
    private static ImageRunExecutor NewExecutor(
        string dbPath,
        IImageCellPromptCompiler compiler)
    {
        var runRepo = NewRunRepo(dbPath);
        return new ImageRunExecutor(
            NewSuiteRepo(dbPath),
            runRepo,
            NewProfileRepo(dbPath),
            compiler,
            NullLogger<ImageRunExecutor>.Instance);
    }

    private static async Task<(string SuiteId, string RunId)> SeedSuiteAndRunAsync(
        string dbPath,
        int cellCount = 2,
        IImageCellPromptCompiler? compiler = null,
        string? compilerLlm = null)
    {
        var suites = NewSuiteRepo(dbPath);
        await NewProfileRepo(dbPath).UpsertAsync(ValidProfile());

        var suite = ValidSuite();
        await suites.UpsertSuiteAsync(suite);
        for (var ordinal = 0; ordinal < cellCount; ordinal++)
        {
            await suites.UpsertCellAsync(ValidCell(suite.Id, ordinal));
        }

        var executor = NewExecutor(dbPath, compiler ?? new StubCellCompiler(CompiledText));
        var start = Start(suite.Id) with { CompilerLlmJson = compilerLlm ?? CompilerLlm };
        var run = await executor.CreateRunAsync(start);

        return (suite.Id, run.Id);
    }

    private static IReadOnlyList<(string Name, string Outcome, string Detail)> Checks(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .EnumerateArray()
            .Select(check => (
                Name: check.GetProperty("name").GetString() ?? string.Empty,
                Outcome: check.GetProperty("outcome").GetString() ?? string.Empty,
                Detail: check.GetProperty("detail").GetString() ?? string.Empty))
            .ToList();
    }

    // ---- creating a run -----------------------------------------------------------------------------------

    [Fact]
    public async Task CreateRunSnapshotsEveryCellsDeclaration()
    {
        var db = NewDbPath();
        var (suiteId, runId) = await SeedSuiteAndRunAsync(db);

        var run = await NewRunRepo(db).GetRunAsync(runId);
        var cells = await NewRunRepo(db).ListCellsAsync(runId);

        Assert.NotNull(run);
        Assert.Equal(suiteId, run!.SuiteId);
        Assert.Equal(1, run.SuiteVersion);
        Assert.Equal(ImageSuiteKind.Comparison, run.Kind);
        Assert.Equal(ImageRunStatus.Queued, run.Status);
        Assert.False(run.ImageLayerRequested);
        Assert.Equal(CompilerLlm, run.CompilerLlmJson);

        Assert.Equal(2, cells.Count);
        Assert.All(cells, cell =>
        {
            Assert.Equal(ProfileId, cell.CheckpointProfileId);
            Assert.StartsWith("Woman laying on bed, seductive look, hands touching herself", cell.UserDirection, StringComparison.Ordinal);
            Assert.Equal(0.7, cell.SimilarityTolerance);
            Assert.Equal(ImageRenderSource.CompiledPrompt, cell.RenderFromPath);
            Assert.Equal(ImageRunCellStatus.Pending, cell.Status);
            Assert.Equal(ImageVisualVerdict.Unreviewed, cell.VisualVerdict);
            Assert.Contains("4242", cell.SeedJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CreateRunRecordsTheSuitesOwnVersion()
    {
        // The version is READ from the suite, never passed in: a caller-declared version could label a run with cells it
        // did not execute.
        var db = NewDbPath();
        var suites = NewSuiteRepo(db);
        var suite = ValidSuite();
        suite.Version = 4;
        await suites.UpsertSuiteAsync(suite);
        await suites.UpsertCellAsync(ValidCell(suite.Id, 0));

        var run = await NewExecutor(db, new StubCellCompiler(CompiledText)).CreateRunAsync(Start(suite.Id));

        Assert.Equal(4, run.SuiteVersion);
    }

    [Theory]
    [InlineData("", "must name the suite")]
    [InlineData("missing-suite", "was not found")]
    public async Task CreateRunRefusesAMissingSuite(string suiteId, string expected)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewExecutor(NewDbPath(), new StubCellCompiler(CompiledText)).CreateRunAsync(Start(suiteId)));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateRunRefusesAnUnrecordedRenderSource()
    {
        var db = NewDbPath();
        var suites = NewSuiteRepo(db);
        var suite = ValidSuite();
        await suites.UpsertSuiteAsync(suite);
        await suites.UpsertCellAsync(ValidCell(suite.Id, 0));

        var start = Start(suite.Id) with { RenderFromPath = ImageRenderSource.Unknown };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewExecutor(db, new StubCellCompiler(CompiledText)).CreateRunAsync(start));

        Assert.Contains("unattributable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateRunRefusesASuiteWithNoCells()
    {
        // A run of no cells would complete and look like a passing batch.
        var db = NewDbPath();
        var suites = NewSuiteRepo(db);
        var suite = ValidSuite();
        await suites.UpsertSuiteAsync(suite);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewExecutor(db, new StubCellCompiler(CompiledText)).CreateRunAsync(Start(suite.Id)));

        Assert.Contains("no cells", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateRunRefusesTheImageLayerUntilItIsWired()
    {
        // A capability gate, not a silent downgrade: a caller who asked for images and got none, with the run marked
        // Complete, would believe images existed.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewExecutor(NewDbPath(), new StubCellCompiler(CompiledText))
                .CreateRunAsync(Start("whatever", imageLayer: true)));

        Assert.Contains("image layer", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("B135-023/B135-024", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRunRefusesAnUnpinnedCompilerLlm()
    {
        var db = NewDbPath();
        var suites = NewSuiteRepo(db);
        var suite = ValidSuite();
        await suites.UpsertSuiteAsync(suite);
        await suites.UpsertCellAsync(ValidCell(suite.Id, 0));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewExecutor(db, new StubCellCompiler(CompiledText))
                .CreateRunAsync(Start(suite.Id) with { CompilerLlmJson = """{"model":"qwen3.5-vl"}""" }));

        Assert.Contains("no temperature", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateRunRefusesACompilerDeclarationWithNoModel()
    {
        var db = NewDbPath();
        var suites = NewSuiteRepo(db);
        var suite = ValidSuite();
        await suites.UpsertSuiteAsync(suite);
        await suites.UpsertCellAsync(ValidCell(suite.Id, 0));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewExecutor(db, new StubCellCompiler(CompiledText))
                .CreateRunAsync(Start(suite.Id) with { CompilerLlmJson = "{}" }));

        Assert.Contains("names no model", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the free layers ----------------------------------------------------------------------------------

    [Fact]
    public async Task RunFreeLayersCompilesAndEvaluatesEveryCell()
    {
        var db = NewDbPath();
        var (_, runId) = await SeedSuiteAndRunAsync(db);
        var executor = NewExecutor(db, new StubCellCompiler(CompiledText));

        var run = await executor.RunFreeLayersAsync(runId);

        Assert.Equal(ImageRunStatus.Complete, run.Status);
        Assert.NotNull(run.StartedUtc);
        Assert.NotNull(run.CompletedUtc);

        var cells = await NewRunRepo(db).ListCellsAsync(runId);
        Assert.All(cells, cell =>
        {
            Assert.True(cell.Status == ImageRunCellStatus.Compiled,
                $"Cell {cell.Ordinal} was {cell.Status}, not Compiled: {cell.FailureMessage}");
            Assert.Equal(CompiledText, cell.CompiledPrompt);
            Assert.Equal(Checkpoint, cell.ResolvedCheckpoint);
            Assert.Equal(string.Empty, cell.FailureMessage);

            // The prompt-layer verdicts are recorded per cell, which is the whole reason a free run is worth having.
            var checks = Checks(cell.PromptLayerJson);
            Assert.Contains(checks, check => check.Name == "budget-characters");
            Assert.Contains(checks, check => check.Name == "similarity-to-expected");
        });
    }

    [Fact]
    public async Task APromptThatFailsItsVerdictsIsStillACompletedObservation()
    {
        // Status describes the EXECUTION; the verdicts carry the judgement. Marking the cell Failed would lose the
        // distinction between "we could not measure this" and "we measured it and it is bad" - and the second is exactly
        // what a qualification suite is for.
        var db = NewDbPath();
        var (_, runId) = await SeedSuiteAndRunAsync(db);
        var executor = NewExecutor(db, new StubCellCompiler("x"));

        await executor.RunFreeLayersAsync(runId);

        var cell = (await NewRunRepo(db).ListCellsAsync(runId))[0];
        var checks = Checks(cell.PromptLayerJson);

        Assert.Equal(ImageRunCellStatus.Compiled, cell.Status);
        Assert.Equal(string.Empty, cell.FailureMessage);
        Assert.Contains(checks, check => check.Name == "budget-characters" && check.Outcome == "Fail");
        Assert.Contains(checks, check => check.Name == "similarity-to-expected" && check.Outcome == "Fail");
    }

    [Fact]
    public async Task OneBadCellDoesNotHideTheOthersVerdicts()
    {
        var db = NewDbPath();
        var (_, runId) = await SeedSuiteAndRunAsync(db);

        var run = await NewExecutor(db, new StubCellCompiler(CompiledText, failOn: "[1]")).RunFreeLayersAsync(runId);

        // The run still completes: a batch that aborted here would report nothing about cell-2.
        Assert.Equal(ImageRunStatus.Complete, run.Status);

        var cells = await NewRunRepo(db).ListCellsAsync(runId);
        Assert.Equal(ImageRunCellStatus.Compiled, cells[0].Status);
        Assert.Equal(ImageRunCellStatus.Failed, cells[1].Status);
        Assert.Contains("refused by the stub", cells[1].FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(Checks(cells[0].PromptLayerJson));
    }

    [Fact]
    public async Task ACellWithNoProfileFailsByItsOwnName()
    {
        var db = NewDbPath();
        var suites = NewSuiteRepo(db);
        await NewProfileRepo(db).UpsertAsync(ValidProfile());
        var suite = ValidSuite();
        await suites.UpsertSuiteAsync(suite);
        await suites.UpsertCellAsync(ValidCell(suite.Id, 0, profileId: null));

        var executor = NewExecutor(db, new StubCellCompiler(CompiledText));
        var run = await executor.CreateRunAsync(Start(suite.Id));
        await executor.RunFreeLayersAsync(run.Id);

        var cell = (await NewRunRepo(db).ListCellsAsync(run.Id))[0];
        Assert.Equal(ImageRunCellStatus.Failed, cell.Status);
        Assert.Contains("names no checkpoint profile", cell.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ACellPinningAProfileThatDoesNotExistFailsByName()
    {
        var db = NewDbPath();
        var suites = NewSuiteRepo(db);
        var suite = ValidSuite();
        await suites.UpsertSuiteAsync(suite);
        await suites.UpsertCellAsync(ValidCell(suite.Id, 0, profileId: "profile-gone"));

        var executor = NewExecutor(db, new StubCellCompiler(CompiledText));
        var run = await executor.CreateRunAsync(Start(suite.Id));
        await executor.RunFreeLayersAsync(run.Id);

        var cell = (await NewRunRepo(db).ListCellsAsync(run.Id))[0];
        Assert.Equal(ImageRunCellStatus.Failed, cell.Status);
        Assert.Contains("profile-gone", cell.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("does not exist", cell.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoredVerdictsNameTheirOutcomeRatherThanNumberingIt()
    {
        // The layer JSON is EVIDENCE: "outcome":1 is unreadable in the database and in the Playground, and it silently
        // changes meaning if anyone reorders ImagePromptCheckOutcome. The name is stable and legible.
        var db = NewDbPath();
        var (_, runId) = await SeedSuiteAndRunAsync(db);
        await NewExecutor(db, new StubCellCompiler("x")).RunFreeLayersAsync(runId);

        var cell = (await NewRunRepo(db).ListCellsAsync(runId))[0];

        using var document = JsonDocument.Parse(cell.PromptLayerJson);
        var outcomes = document.RootElement
            .EnumerateArray()
            .Select(check => check.GetProperty("outcome"))
            .ToList();

        Assert.NotEmpty(outcomes);
        Assert.All(outcomes, outcome => Assert.Equal(JsonValueKind.String, outcome.ValueKind));
        Assert.Contains(outcomes, outcome => outcome.GetString() == "Fail");
        Assert.Contains(outcomes, outcome => outcome.GetString() == "Pass");
    }

    [Fact]
    public async Task RunFreeLayersRefusesToReRunACompletedRun()
    {
        // Free layers are not re-run in place: a re-run is a new run, so the evidence of this one stays as it was.
        var db = NewDbPath();
        var (_, runId) = await SeedSuiteAndRunAsync(db);
        var executor = NewExecutor(db, new StubCellCompiler(CompiledText));
        await executor.RunFreeLayersAsync(runId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.RunFreeLayersAsync(runId));

        Assert.Contains("a re-run is a new run", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunFreeLayersRefusesAnUnknownRun()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewExecutor(NewDbPath(), new StubCellCompiler(CompiledText)).RunFreeLayersAsync("no-such-run"));

        Assert.Contains("was not found", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the request layer --------------------------------------------------------------------------------

    private static ImageResolvedRequest Resolved(long? seed = 4242) => new()
    {
        Checkpoint = Checkpoint,
        Provider = "runpod-juggernaut",
        Family = SceneImageModelFamily.Sdxl,
        Dialect = SceneImagePromptDialect.SdxlNaturalLanguage,
        RenderMode = SceneImageRenderMode.PromptOnly,
        Negative = string.Empty,
        Seed = seed,
        Size = "1216x1216",
        Prompt = CompiledText,
        Bindings = [],
    };

    [Fact]
    public async Task ApplyRequestLayerRecordsTheSnapshotAndItsVerdicts()
    {
        var db = NewDbPath();
        var (_, runId) = await SeedSuiteAndRunAsync(db);
        var executor = NewExecutor(db, new StubCellCompiler(CompiledText));
        await executor.RunFreeLayersAsync(runId);

        var cell = (await NewRunRepo(db).ListCellsAsync(runId))[0];
        var evaluated = await executor.ApplyRequestLayerAsync(
            cell.Id, Resolved(), new ImageRequestExpectation(4242, "1216x1216"));

        Assert.Equal(Checkpoint, evaluated.ResolvedCheckpoint);
        Assert.Equal("runpod-juggernaut", evaluated.ResolvedProvider);
        Assert.Equal(4242, evaluated.Seed);

        // The exact request is kept, so a later reader can see what was submitted rather than trusting a verdict.
        Assert.Contains(Checkpoint, evaluated.RequestSnapshotJson, StringComparison.Ordinal);

        var checks = Checks(evaluated.RequestLayerJson);
        Assert.NotEmpty(checks);
        Assert.All(checks, check => Assert.NotEqual("Fail", check.Outcome));
    }

    [Fact]
    public async Task ApplyRequestLayerReportsASeedThatWasNotTheDeclaredOne()
    {
        var db = NewDbPath();
        var (_, runId) = await SeedSuiteAndRunAsync(db);
        var executor = NewExecutor(db, new StubCellCompiler(CompiledText));
        await executor.RunFreeLayersAsync(runId);

        var cell = (await NewRunRepo(db).ListCellsAsync(runId))[0];
        var evaluated = await executor.ApplyRequestLayerAsync(
            cell.Id, Resolved(seed: 99), new ImageRequestExpectation(4242, "1216x1216"));

        var seedCheck = Checks(evaluated.RequestLayerJson).Single(check => check.Name == "seed-honoured");

        Assert.Equal("Fail", seedCheck.Outcome);
        Assert.Contains("99", seedCheck.Detail, StringComparison.Ordinal);
        Assert.Contains("4242", seedCheck.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyRequestLayerRefusesAnUnknownCell()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewExecutor(NewDbPath(), new StubCellCompiler(CompiledText)).ApplyRequestLayerAsync(
                "no-such-cell", Resolved(), new ImageRequestExpectation(4242, "1216x1216")));

        Assert.Contains("was not found", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- doubles ------------------------------------------------------------------------------------------

    /// <summary>
    /// Stands in for the pinned LLM call. Deliberately dumb: the executor's job is to drive the layers and record what
    /// they said, not to compile - <see cref="ImageCellPromptCompilerTests"/> covers the compiler's own contract.
    /// </summary>
    private sealed class StubCellCompiler(string prompt, string? failOn = null) : IImageCellPromptCompiler
    {
        public Task<ImageCellCompileResult> CompileAsync(
            ImageCellCompileInput input, CancellationToken cancellationToken = default)
        {
            // Keyed on the direction, which is the only thing the compiler is given: if it needed the cell's name to
            // behave, that would be a signal the real compile step does not have either.
            if (failOn is not null && input.UserDirection.Contains(failOn, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"'{failOn}' was refused by the stub.");
            }

            return Task.FromResult(new ImageCellCompileResult(
                prompt,
                input.Profile.SystemPrompt,
                $"USER DIRECTION: {input.UserDirection}",
                input.Llm.ModelIdentifier,
                input.Llm.Temperature));
        }
    }
}
