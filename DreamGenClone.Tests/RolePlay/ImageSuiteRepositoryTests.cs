using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-010 — the suite/cell store.
///
/// <para>
/// The load-bearing behaviour is VERSIONING: a suite's version is frozen by the run that executed it, so a new
/// version must INSERT a new row rather than overwrite the old one. Overwriting would re-label already-recorded
/// evidence with cells that did not produce it, which is the one failure that makes a proof worthless. An empty
/// store is also asserted to be legitimate — suites are authored, not seeded.
/// </para>
/// </summary>
public sealed class ImageSuiteRepositoryTests
{
    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"b135-suite-{Guid.NewGuid():N}.db");

    private static ImageSuiteRepository NewRepository(string dbPath) =>
        new(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" }));

    private static ImageSuite ValidSuite(string name = "baseline-positions") => new()
    {
        Name = name,
        Kind = ImageSuiteKind.Qualification,
        Status = ImageSuiteStatus.Draft,
        Description = "the model-agnostic position catalog",
        Provenance = "test fixture",
    };

    private static ImageSuiteCell ValidCell(string suiteId, int ordinal) => new()
    {
        SuiteId = suiteId,
        Ordinal = ordinal,
        Name = $"cell-{ordinal}",
        UserDirection = "Woman laying on bed, seductive look, hands touching herself",
        ExpectedPrompt = "a photograph of a woman lying on a bed, one hand on her thigh, warm low lamp light, 35mm",
        SimilarityTolerance = 0.7,
        BindingsJson = """[{"axis":"Identity","mode":"Lora","value":"becky-v7","strength":0.8}]""",
        CompilerLlmJson = """{"model":"qwen3.5-vl","temperature":0,"seed":42}""",
    };

    // ---- validation ---------------------------------------------------------------------------------------

    [Fact]
    public void AnUnnamedSuiteIsRefused()
    {
        var suite = ValidSuite();
        suite.Name = "   ";

        var error = Assert.Throws<InvalidOperationException>(() => ImageSuiteValidation.Validate(suite));

        Assert.Contains("name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ASuiteMustDeclareItsKindAndStatus()
    {
        var noKind = ValidSuite();
        noKind.Kind = ImageSuiteKind.Unknown;
        Assert.Throws<InvalidOperationException>(() => ImageSuiteValidation.Validate(noKind));

        var noStatus = ValidSuite();
        noStatus.Status = ImageSuiteStatus.Unknown;
        Assert.Throws<InvalidOperationException>(() => ImageSuiteValidation.Validate(noStatus));
    }

    [Fact]
    public void ASuiteVersionStartsAtOne()
    {
        var suite = ValidSuite();
        suite.Version = 0;

        var error = Assert.Throws<InvalidOperationException>(() => ImageSuiteValidation.Validate(suite));

        Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACellValidationEnforcesIdentityNotContent()
    {
        // A catalog cell carries one prompt per MODEL and may have no user direction at all. Requiring one here meant
        // an agent-authored catalog could not be imported because it lacked a field only the COMPILER needs - and the
        // images were never made. Identity is what the store enforces; content gaps are reported on the cell.
        var cell = ValidCell("suite-1", 0);
        cell.UserDirection = string.Empty;
        cell.ExpectedPrompt = string.Empty;
        cell.SimilarityTolerance = null;

        ImageSuiteValidation.Validate(cell);

        // Identity is still required.
        var unnamed = ValidCell("suite-1", 0);
        unnamed.Name = "  ";
        Assert.Throws<InvalidOperationException>(() => ImageSuiteValidation.Validate(unnamed));

        var withoutSuite = ValidCell("suite-1", 0);
        withoutSuite.SuiteId = string.Empty;
        Assert.Throws<InvalidOperationException>(() => ImageSuiteValidation.Validate(withoutSuite));

        var negativeOrdinal = ValidCell("suite-1", 0);
        negativeOrdinal.Ordinal = -1;
        Assert.Throws<InvalidOperationException>(() => ImageSuiteValidation.Validate(negativeOrdinal));
    }

    // ---- the store -----------------------------------------------------------------------------------------

    [Fact]
    public async Task AnEmptyStoreIsLegitimate()
    {
        // Suites are authored configuration. Seeding a catalog is an explicit import (B135-032), never a side effect
        // of opening the store.
        var repository = NewRepository(NewDbPath());

        Assert.Empty(await repository.ListSuitesAsync());
    }

    [Fact]
    public async Task ASuiteAndItsCellsRoundTrip_OrderedByOrdinal()
    {
        var dbPath = NewDbPath();
        var repository = NewRepository(dbPath);

        var suite = ValidSuite();
        await repository.UpsertSuiteAsync(suite);
        await repository.UpsertCellAsync(ValidCell(suite.Id, 2));
        await repository.UpsertCellAsync(ValidCell(suite.Id, 0));
        await repository.UpsertCellAsync(ValidCell(suite.Id, 1));

        var reloaded = await NewRepository(dbPath).GetSuiteAsync(suite.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("baseline-positions", reloaded!.Name);
        Assert.Equal(ImageSuiteKind.Qualification, reloaded.Kind);

        var cells = await NewRepository(dbPath).ListCellsAsync(suite.Id);
        Assert.Equal([0, 1, 2], cells.Select(cell => cell.Ordinal));

        // Everything the compiler and the evaluators need survives the round trip, including the per-cell tolerance
        // that must never be defaulted.
        var first = cells[0];
        Assert.Contains("seductive look", first.UserDirection, StringComparison.Ordinal);
        Assert.Equal(0.7, first.SimilarityTolerance);
        Assert.Contains("qwen3.5-vl", first.CompilerLlmJson, StringComparison.Ordinal);
        Assert.Contains("\"Lora\"", first.BindingsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVersionBumpInsertsANewSuite_RatherThanOverwritingTheRunRecordedOne()
    {
        var dbPath = NewDbPath();
        var repository = NewRepository(dbPath);

        var v1 = ValidSuite();
        await repository.UpsertSuiteAsync(v1);

        var v2 = ValidSuite();
        v2.Id = Guid.NewGuid().ToString();
        v2.Version = 2;
        await repository.UpsertSuiteAsync(v2);

        var suites = await NewRepository(dbPath).ListSuitesAsync();

        // Both versions exist: a run that recorded version 1 still resolves to the cells that produced it.
        Assert.Equal(2, suites.Count);
        Assert.Equal([1, 2], suites.Select(suite => suite.Version));

        // And the version-1 cells are still reachable under the version-1 suite id.
        Assert.Empty(await NewRepository(dbPath).ListCellsAsync(v1.Id));
    }

    [Fact]
    public async Task ACellIsIdentifiedByItsOrdinalWithinTheSuite()
    {
        var dbPath = NewDbPath();
        var repository = NewRepository(dbPath);

        var suite = ValidSuite();
        await repository.UpsertSuiteAsync(suite);

        var cell = ValidCell(suite.Id, 0);
        await repository.UpsertCellAsync(cell);

        // Same ordinal, edited content: an update in place, not a second row.
        cell.ExpectedPrompt = "a photograph of a woman lying on a bed, both hands behind her head";
        cell.SimilarityTolerance = 0.55;
        await repository.UpsertCellAsync(cell);

        var cells = await NewRepository(dbPath).ListCellsAsync(suite.Id);

        var only = Assert.Single(cells);
        Assert.Equal(0.55, only.SimilarityTolerance);
        Assert.Contains("behind her head", only.ExpectedPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACellCanBeDeleted()
    {
        var dbPath = NewDbPath();
        var repository = NewRepository(dbPath);

        var suite = ValidSuite();
        await repository.UpsertSuiteAsync(suite);
        var cell = ValidCell(suite.Id, 0);
        await repository.UpsertCellAsync(cell);

        await repository.DeleteCellAsync(cell.Id);

        Assert.Empty(await NewRepository(dbPath).ListCellsAsync(suite.Id));
    }

    [Fact]
    public async Task ACatalogCellWithNoCompilerInputStillPersists()
    {
        // The import path depends on this: a manifest position with no userInput becomes a cell that can still be
        // rendered from its per-model variants.
        var dbPath = NewDbPath();
        var repository = NewRepository(dbPath);

        var suite = ValidSuite();
        await repository.UpsertSuiteAsync(suite);

        var cell = ValidCell(suite.Id, 0);
        cell.UserDirection = string.Empty;
        cell.ExpectedPrompt = string.Empty;
        cell.VariantsJson = """{"biglust":"Photorealistic explicit sex scene, cowgirl position."}""";

        await repository.UpsertCellAsync(cell);

        var stored = Assert.Single(await NewRepository(dbPath).ListCellsAsync(suite.Id));
        Assert.Equal(string.Empty, stored.UserDirection);
        Assert.Contains("cowgirl position", stored.VariantsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritingAnInvalidCellDoesNotPersistIt()
    {
        var dbPath = NewDbPath();
        var repository = NewRepository(dbPath);

        var suite = ValidSuite();
        await repository.UpsertSuiteAsync(suite);

        var cell = ValidCell(suite.Id, 0);
        cell.Ordinal = -1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertCellAsync(cell));

        Assert.Empty(await NewRepository(dbPath).ListCellsAsync(suite.Id));
    }
}
