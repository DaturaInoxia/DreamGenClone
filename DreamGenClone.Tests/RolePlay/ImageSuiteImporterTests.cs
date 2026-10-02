using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 — importing an agent-authored prompt catalog into a runnable suite.
///
/// <para>
/// The load-bearing test is <see cref="TheRealBaselineCatalogImportsWithItsGapsReported"/>. It runs the importer over
/// the ACTUAL <c>specs/image-generator-tests/baseline</c> catalog, so "can the app run my manifest" is answered by the
/// app rather than by my opinion of it — and it asserts the gaps are REPORTED, not that they are absent, because the
/// catalog is expected to be incomplete right now (no model legend, no userInput/expected, three variants).
/// </para>
/// </summary>
public sealed class ImageSuiteImporterTests
{
    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"b135-import-{Guid.NewGuid():N}.db");

    private static string NewCatalogRoot() =>
        Directory.CreateTempSubdirectory($"b135-catalog-{Guid.NewGuid():N}").FullName;

    private static ImageSuiteRepository NewSuiteRepo(string dbPath) =>
        new(Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" }));

    private static ImageSuiteImporter NewImporter(string dbPath, string root) =>
        new(NewSuiteRepo(dbPath),
            Options.Create(new PlaygroundOptions { ManifestRoot = root }),
            NullLogger<ImageSuiteImporter>.Instance);

    private const string Manifest = """
        {
          "suite": "test-catalog",
          "purpose": "two positions across two models",
          "models": [
            { "key": "biglust", "checkpoint": "bigLust_v16.safetensors", "kind": "Generate", "displayName": "BigLust" },
            { "key": "qwen-edit-2511", "checkpoint": "qwen_image_edit_2511.safetensors", "kind": "Edit", "displayName": "Qwen Edit" }
          ],
          "positions": [
            { "id": "cowgirl", "path": "positions/cowgirl.json" },
            { "id": "doggy", "path": "positions/doggy.json" }
          ]
        }
        """;

    private const string Cowgirl = """
        {
          "id": "cowgirl",
          "title": "cowgirl",
          "actors": "1M1F",
          "closeup": false,
          "userInput": "Woman on top riding him",
          "expected": "a photorealistic couple in cowgirl position",
          "neutralScene": "Two adults on a bed.",
          "negative": "extra limbs, fused legs",
          "variants": {
            "biglust": "Photorealistic explicit sex scene, cowgirl position, biglust wording.",
            "qwen-edit-2511": "Change the scene so the woman straddles the man."
          },
          "settings": { "seed": 11223, "steps": 30 },
          "bindings": []
        }
        """;

    /// <summary>Deliberately missing its qwen-edit variant: the gap must be reported and must not block the cell.</summary>
    private const string Doggy = """
        {
          "id": "doggy",
          "title": "doggy",
          "actors": "1M1F",
          "closeup": false,
          "userInput": "Woman on all fours",
          "expected": "a photorealistic couple in doggy position",
          "neutralScene": "Two adults on a bed.",
          "variants": { "biglust": "Photorealistic explicit sex scene, doggy position." },
          "settings": {},
          "bindings": []
        }
        """;

    private static string WriteCatalog(string root, string manifest = Manifest)
    {
        var positions = Path.Combine(root, "positions");
        Directory.CreateDirectory(positions);
        File.WriteAllText(Path.Combine(root, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(positions, "cowgirl.json"), Cowgirl);
        File.WriteAllText(Path.Combine(positions, "doggy.json"), Doggy);
        return Path.Combine(root, "manifest.json");
    }

    // ---- the import ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ACatalogImportsAsASuiteOfCellsWithTheirVariants()
    {
        var db = NewDbPath();
        var root = NewCatalogRoot();
        var manifestPath = WriteCatalog(root);

        var report = await NewImporter(db, root).ImportAsync(manifestPath);

        Assert.Equal("test-catalog", report.SuiteName);
        Assert.Equal(2, report.CellCount);
        Assert.Equal(ImageSuiteKind.Catalog, (await NewSuiteRepo(db).GetSuiteAsync(report.SuiteId))!.Kind);

        var cells = await NewSuiteRepo(db).ListCellsAsync(report.SuiteId);
        Assert.Equal([0, 1], cells.Select(cell => cell.Ordinal));

        var cowgirl = cells[0];
        Assert.Equal("cowgirl", cowgirl.Name);
        Assert.Equal("Woman on top riding him", cowgirl.UserDirection);
        Assert.Equal("a photorealistic couple in cowgirl position", cowgirl.ExpectedPrompt);

        // Both prompts for both models, which is what a run selects between.
        var variants = JsonSerializer.Deserialize<Dictionary<string, string>>(cowgirl.VariantsJson)!;
        Assert.Equal(2, variants.Count);
        Assert.Contains("biglust wording", variants["biglust"], StringComparison.Ordinal);
        Assert.Contains("straddles", variants["qwen-edit-2511"], StringComparison.Ordinal);

        // The declared negative is recorded as one of this prompt's render settings.
        Assert.Contains("fused legs", cowgirl.SettingsJson, StringComparison.Ordinal);
        Assert.Contains("11223", cowgirl.SettingsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACellMissingAModelsPromptIsImportedWithTheGapReported()
    {
        // The whole point of the permissive reader: the cell still runs for biglust.
        var db = NewDbPath();
        var root = NewCatalogRoot();
        var manifestPath = WriteCatalog(root);

        var report = await NewImporter(db, root).ImportAsync(manifestPath);

        var doggy = (await NewSuiteRepo(db).ListCellsAsync(report.SuiteId))[1];
        var variants = JsonSerializer.Deserialize<Dictionary<string, string>>(doggy.VariantsJson)!;

        Assert.Single(variants);
        Assert.Contains(report.CellProblems, problem => problem.Contains("doggy", StringComparison.Ordinal));
        Assert.Contains(report.CellProblems, problem => problem.Contains("qwen-edit-2511", StringComparison.Ordinal));
        Assert.Contains("qwen-edit-2511", doggy.ProblemsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReImportingUpdatesTheSameSuiteRatherThanPilingUpVersions()
    {
        // "Iterate and improve": edit the manifest, import again, run again. The suite is the app's copy of the catalog.
        var db = NewDbPath();
        var root = NewCatalogRoot();
        var manifestPath = WriteCatalog(root);
        var importer = NewImporter(db, root);

        var first = await importer.ImportAsync(manifestPath);

        File.WriteAllText(Path.Combine(root, "positions", "doggy.json"), Doggy.Replace("Woman on all fours", "Woman kneeling", StringComparison.Ordinal));
        var second = await importer.ImportAsync(manifestPath);

        Assert.Equal(first.SuiteId, second.SuiteId);
        Assert.Single(await NewSuiteRepo(db).ListSuitesAsync());

        var cells = await NewSuiteRepo(db).ListCellsAsync(second.SuiteId);
        Assert.Equal("Woman kneeling", cells[1].UserDirection);
    }

    [Fact]
    public async Task ImportAllSkipsManifestsThatDescribeNoPositions()
    {
        // The proof folders carry a manifest of a different shape. Importing them would fill the store with empty suites.
        var db = NewDbPath();
        var root = NewCatalogRoot();
        WriteCatalog(Path.Combine(root, "baseline"));

        var proof = Path.Combine(root, "sex-slideshow");
        Directory.CreateDirectory(proof);
        File.WriteAllText(Path.Combine(proof, "manifest.json"), """{ "runId": "20260914-02", "completed": true }""");

        var reports = await NewImporter(db, root).ImportAllAsync();

        var report = Assert.Single(reports);
        Assert.Equal("test-catalog", report.SuiteName);
        Assert.Single(await NewSuiteRepo(db).ListSuitesAsync());
    }

    [Fact]
    public async Task ImportAllDoesNotDescendIntoRunFolders()
    {
        // A past run's manifest is a record of what happened, not a catalog to run.
        var db = NewDbPath();
        var root = NewCatalogRoot();
        WriteCatalog(Path.Combine(root, "baseline"));

        var pastRun = Path.Combine(root, "baseline", "runs", "20260914-02-slideshow-hq");
        Directory.CreateDirectory(pastRun);
        File.WriteAllText(Path.Combine(pastRun, "manifest.json"), Manifest);

        var reports = await NewImporter(db, root).ImportAllAsync();

        Assert.Single(reports);
        Assert.Single(await NewSuiteRepo(db).ListSuitesAsync());
    }

    [Fact]
    public async Task OneBrokenCatalogDoesNotStopTheOthers()
    {
        var db = NewDbPath();
        var root = NewCatalogRoot();
        WriteCatalog(Path.Combine(root, "good"));

        var broken = Path.Combine(root, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "manifest.json"), "{ this is not json");

        var reports = await NewImporter(db, root).ImportAllAsync();

        Assert.Single(reports);
        Assert.Equal("test-catalog", reports[0].SuiteName);
    }

    [Fact]
    public void DiscoveryIsShallowByDesign()
    {
        var root = NewCatalogRoot();
        WriteCatalog(Path.Combine(root, "baseline"));
        var nested = Path.Combine(root, "baseline", "runs", "x");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "manifest.json"), "{}");

        var discovered = NewImporter(NewDbPath(), root).DiscoverManifests();

        var only = Assert.Single(discovered);
        Assert.Contains("baseline", only, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingRootImportsNothingAndDoesNotThrow()
    {
        var importer = NewImporter(NewDbPath(), Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

        Assert.Empty(importer.DiscoverManifests());
        Assert.Empty(await importer.ImportAllAsync());
    }

    // ---- the real catalog the work is for ------------------------------------------------------------------

    [Fact]
    public async Task TheRealBaselineCatalogImportsWithItsGapsReported()
    {
        // The actual specs/image-generator-tests/baseline catalog, imported by the importer the app uses. What is
        // asserted is deliberately CONTENT-STABLE: the 49 positions import, and every cell carries at least one model
        // prompt so it can be rendered for some model. The catalog is being populated model by model, so any assertion
        // about which gaps exist today would go stale on the next edit - the gaps themselves are read from the report.
        // (49 = the original 45 plus the four 1M1F woman-receiving-oral cells added 2026-10-01: cunnilingus,
        // cunnilingus-closeup, cunnilingus-from-behind, facesitting. Growing the catalog means updating this anchor.)
        var db = NewDbPath();
        var root = RealCatalogRoot();
        var manifestPath = Path.Combine(root, "manifest.json");
        Assert.True(File.Exists(manifestPath), $"Expected the real catalog at '{manifestPath}'.");

        var report = await NewImporter(db, root).ImportAsync(manifestPath);

        Assert.Equal("baseline-positions", report.SuiteName);
        Assert.Equal(49, report.CellCount);

        var cells = await NewSuiteRepo(db).ListCellsAsync(report.SuiteId);
        Assert.Equal(49, cells.Count);

        // Every cell is renderable for at least one model, and the model legend is declared so a run can map a key to
        // a checkpoint.
        Assert.All(cells, cell =>
        {
            var variants = JsonSerializer.Deserialize<Dictionary<string, string>>(cell.VariantsJson)!;
            Assert.NotEmpty(variants);
        });

        Assert.False(cells[0].ProblemsJson.Contains("no 'models' legend", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The repo's own catalog folder, found from the test output directory.</summary>
    private static string RealCatalogRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("Repository root (DreamGenClone.sln) not found above the test output directory.");
        return Path.Combine(root, "specs", "image-generator-tests", "baseline");
    }
}
