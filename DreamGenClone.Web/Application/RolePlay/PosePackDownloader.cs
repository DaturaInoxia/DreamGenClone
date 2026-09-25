using System.IO.Compression;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>What a download produced, so the caller reports facts rather than "done".</summary>
/// <param name="PackFolder">The new folder under the packs root.</param>
/// <param name="FilesExtracted">Pose-pack files written.</param>
/// <param name="FilesIgnored">Entries dropped because they are not pack content (counted, never silent).</param>
/// <param name="Import">The import that ran against the new pack.</param>
public sealed record PosePackDownloadResult(
    string PackFolder, int FilesExtracted, int FilesIgnored, PoseLibraryImportResult Import);

public interface IPosePackDownloader
{
    /// <summary>
    /// Downloads a pack archive and adds it as its own library. Refuses anything it cannot verify: a non-http(s)
    /// URL, an existing pack folder, an oversized archive, a non-zip body, or an entry that tries to escape the
    /// pack folder. A refused download leaves nothing behind.
    /// </summary>
    Task<PosePackDownloadResult> DownloadAsync(
        string url, string name, string? description = null, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PosePackDownloader : IPosePackDownloader
{
    /// <summary>
    /// Only these extensions are pack content. Anything else in an archive is counted and ignored rather than
    /// written, so a pack cannot drop arbitrary files into the web root.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".json", ".png", ".txt", ".md" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPoseLibraryImporter _importer;
    private readonly IWebHostEnvironment _environment;
    private readonly PoseLibraryOptions _options;
    private readonly ILogger<PosePackDownloader> _logger;

    public PosePackDownloader(
        IHttpClientFactory httpClientFactory,
        IPoseLibraryImporter importer,
        IWebHostEnvironment environment,
        IOptions<PoseLibraryOptions> options,
        ILogger<PosePackDownloader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _importer = importer;
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PosePackDownloadResult> DownloadAsync(
        string url, string name, string? description = null, CancellationToken cancellationToken = default)
    {
        var packsRoot = ResolvePacksRoot();
        var packFolder = PoseLibraryService.Slug(
            string.IsNullOrWhiteSpace(name) ? throw new InvalidOperationException("A pack name is required.") : name);
        var packDirectory = Path.Combine(packsRoot, packFolder);

        if (Directory.Exists(packDirectory))
        {
            throw new InvalidOperationException(
                $"A pose pack folder named '{packFolder}' already exists, so this download was not started. "
                + "Remove it first, or download the pack under a different name.");
        }

        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"A pack must be downloaded from an absolute http or https URL; '{url}' is not one.");
        }

        var maxBytes = MaxDownloadBytes();
        var temporaryArchive = Path.Combine(Path.GetTempPath(), $"pose-pack-{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadToFileAsync(uri, temporaryArchive, maxBytes, cancellationToken);

            Directory.CreateDirectory(packDirectory);
            var (extracted, ignored) = ExtractArchive(temporaryArchive, packDirectory, maxBytes);

            await File.WriteAllTextAsync(
                Path.Combine(packDirectory, "pack.json"),
                BuildManifest(name.Trim(), description, uri.ToString()),
                cancellationToken);

            var import = await _importer.ImportPackAsync(packFolder, cancellationToken);

            _logger.LogInformation(
                "Downloaded pose pack '{Pack}' from {Url}: {Extracted} files, {Ignored} ignored, {Imported} presets imported.",
                packFolder, uri, extracted, ignored, import.PresetsImported);

            return new PosePackDownloadResult(packFolder, extracted, ignored, import);
        }
        catch
        {
            // A refused or failed download leaves nothing behind, so a retry is not fighting a half-written pack.
            TryDeleteDirectory(packDirectory);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive);
        }
    }

    private async Task DownloadToFileAsync(
        Uri uri, string destination, long maxBytes, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
        {
            throw new InvalidOperationException(
                $"The pack at {uri} is {declared / (1024 * 1024)} MB, over the configured "
                + $"{_options.DownloadMaxMegabytes} MB limit. Raise PoseLibrary:DownloadMaxMegabytes to allow it.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            written += read;
            if (written > maxBytes)
            {
                throw new InvalidOperationException(
                    $"The download passed the configured {_options.DownloadMaxMegabytes} MB limit and was stopped.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static (int Extracted, int Ignored) ExtractArchive(string archivePath, string packDirectory, long maxBytes)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(archivePath);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(
                "The downloaded file is not a zip archive. Pose packs must be distributed as .zip.", ex);
        }

        var extracted = 0;
        var ignored = 0;
        long written = 0;

        using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue; // a directory entry
                }

                var relative = NormaliseEntryPath(entry.FullName);

                if (!AllowedExtensions.Contains(Path.GetExtension(relative)))
                {
                    ignored++;
                    continue;
                }

                written += entry.Length;
                if (written > maxBytes)
                {
                    throw new InvalidOperationException(
                        $"The archive expands past the configured {maxBytes / (1024 * 1024)} MB limit and was stopped.");
                }

                var target = Path.Combine(
                    packDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                extracted++;
            }
        }

        return (extracted, ignored);
    }

    /// <summary>
    /// Rejects an entry that would land outside the pack folder. A traversal entry is refused outright rather than
    /// skipped, because an archive that tries it is not a pose pack.
    /// </summary>
    private static string NormaliseEntryPath(string entryName)
    {
        var normalised = entryName.Replace('\\', '/').TrimStart('/');
        var segments = normalised.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments.Any(segment => segment == ".."))
        {
            throw new InvalidOperationException(
                $"The archive contains the entry '{entryName}', which would be written outside the pose pack "
                + "folder. The download was refused.");
        }

        return string.Join('/', segments);
    }

    private static string BuildManifest(string name, string? description, string source) =>
        new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = name,
            ["description"] = description?.Trim() ?? string.Empty,
            ["source"] = source,
            ["license"] = "unverified"
        }.ToJsonString();

    private long MaxDownloadBytes()
    {
        if (_options.DownloadMaxMegabytes is not int megabytes || megabytes <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{PoseLibraryOptions.SectionName}:DownloadMaxMegabytes' is required to download a "
                + "pack — an unbounded download is not a default to inherit silently.");
        }

        return (long)megabytes * 1024 * 1024;
    }

    private string ResolvePacksRoot()
    {
        if (string.IsNullOrWhiteSpace(_options.PacksRoot))
        {
            throw new InvalidOperationException(
                $"Configuration '{PoseLibraryOptions.SectionName}:PacksRoot' is required to download a pack.");
        }

        var configured = _options.PacksRoot.Trim();
        return Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Leftover temporary content is not worth masking the real failure with.
        }
    }
}
