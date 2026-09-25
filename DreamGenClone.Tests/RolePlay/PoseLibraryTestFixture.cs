using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// A throwaway pose-library environment: a temp SQLite database, a temp pose pack and a temp web root, wired
/// to the real importer and service. Real components over fakes, because the behaviours under test are the
/// ones that only exist in the wiring (idempotency, path resolution, fail-fast configuration).
/// </summary>
internal sealed class PoseLibraryTestFixture : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;

    public PoseLibraryTestFixture(bool configurePackRoot = true)
    {
        _root = Path.Combine(Path.GetTempPath(), $"pose-library-{Guid.NewGuid():N}");
        PacksRoot = Path.Combine(_root, "packs");
        PackRoot = Path.Combine(PacksRoot, PoseLibraryIds.BundledPackFolder);
        WebRoot = Path.Combine(_root, "web");
        Directory.CreateDirectory(PackRoot);
        Directory.CreateDirectory(WebRoot);

        // Every pack must declare itself; the importer refuses a pack without a manifest rather than guessing.
        File.WriteAllText(
            Path.Combine(PackRoot, "pack.json"),
            """{ "name": "Test pack", "description": "Fixture pack", "source": "test" }""");

        _dbPath = Path.Combine(_root, "poses.db");
        Repository = new PosePresetRepository(
            Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={_dbPath};Pooling=False" }));

        OptionsValue = new PoseLibraryOptions
        {
            PacksRoot = configurePackRoot ? PacksRoot : null,
            SkeletonFolder = "library",
            DownloadMaxMegabytes = 64
        };

        var environment = new StubHostEnvironment(WebRoot, _root);
        var options = Options.Create(OptionsValue);

        Importer = new PoseLibraryImporter(
            Repository, environment, options, NullLogger<PoseLibraryImporter>.Instance);
        Service = new PoseLibraryService(
            Repository,
            environment,
            options,
            Options.Create(new PoseStudioOptions
            {
                FocalLengthPx = 1600,
                CameraDistance = 4.5,
                Canvas = 1024,
                RotationStepDegrees = 5
            }));
    }

    /// <summary>The folder holding every pack — the configured root the importer walks.</summary>
    public string PacksRoot { get; }

    /// <summary>The one pack folder this fixture imports.</summary>
    public string PackRoot { get; }

    public string WebRoot { get; }

    public PoseLibraryOptions OptionsValue { get; }

    public PosePresetRepository Repository { get; }

    public PoseLibraryImporter Importer { get; }

    public PoseLibraryService Service { get; }

    /// <summary>
    /// A downloader whose HTTP factory fails loudly on use, so a refusal test proves the refusal happened before
    /// any request was made rather than merely that it also refused later.
    /// </summary>
    public PosePackDownloader Downloader() => new(
        new UnusedHttpClientFactory(),
        Importer,
        new StubHostEnvironment(WebRoot, _root),
        Options.Create(OptionsValue),
        NullLogger<PosePackDownloader>.Instance);

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException(
                "This test expected no HTTP call, but the downloader made one.");
    }

    public void WritePose(string relativePath) => WriteRaw(relativePath, PackDocument());

    /// <summary>
    /// Adds another pack under the same packs root, so "a second pack" is a real directory rather than a
    /// second library row that could never occur in practice.
    /// </summary>
    public string AddPack(string packFolder, string name, params string[] relativePaths)
    {
        var directory = Path.Combine(PacksRoot, packFolder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "pack.json"),
            $$"""{ "name": "{{name}}", "source": "test" }""");

        foreach (var relative in relativePaths)
        {
            var path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, PackDocument());
        }

        return directory;
    }

    /// <summary>
    /// Writes one pack file, imports it and returns the resulting preset. The category is asserted rather than
    /// set, because deriving it from the pack's folder name is part of what the importer must get right.
    /// </summary>
    public async Task<PosePreset> ImportPoseAsync(string expectedCategory, string relativePath, string? keywords = null)
    {
        WritePose(relativePath);
        await Importer.ImportAsync();

        var preset = (await Repository.ListAsync())
            .Single(entry => entry.ProvenanceJson?.Contains(relativePath, StringComparison.Ordinal) == true);

        Assert.Equal(expectedCategory, preset.Category);

        if (keywords is not null)
        {
            preset.Keywords = keywords;
            await Repository.UpsertAsync(preset);
        }

        return preset;
    }

    public void WriteRaw(string relativePath, string content)
    {
        var path = Path.Combine(PackRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string PackDocument()
    {
        var body = Enumerable.Range(0, 18)
            .SelectMany(i => new[] { (double)(i * 10), (double)(100 + i), 1.0 })
            .ToArray();

        return $$"""
            { "people": [ { "pose_keypoints_2d": [{{string.Join(", ", body)}}] } ] }
            """;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a test failure.
        }
    }

    private sealed class StubHostEnvironment : IWebHostEnvironment
    {
        public StubHostEnvironment(string webRoot, string contentRoot)
        {
            WebRootPath = webRoot;
            ContentRootPath = contentRoot;
        }

        public string WebRootPath { get; set; }

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ApplicationName { get; set; } = "DreamGenClone.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
