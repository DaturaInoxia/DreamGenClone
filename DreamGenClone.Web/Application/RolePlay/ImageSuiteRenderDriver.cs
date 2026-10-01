using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Renders a catalog's cells against one model, into one run container (B-135).
///
/// <para>
/// <b>The prompt is sent as written.</b> A catalog variant is already worded for a model family — Pony's variant
/// already leads with the V6 quality string, an SDXL variant is already a photography brief — so the render must not
/// run it through the runtime prompt compilers, which would prepend a SECOND quality string to Pony and push it past
/// its qualified length. The image therefore declares
/// <see cref="CatalogVariantCompilerId"/> as its prompt compiler, which is exactly the fact
/// <c>SceneAssetGenerationJobHandler</c> reads to decide the text is already model-ready.
/// </para>
///
/// <para>
/// <b>The run owns its images.</b> One container per run, and every image in it carries the container's id as its
/// candidate batch, so the set is one browsable group in the Asset Manager and can be pulled back by batch id.
/// </para>
/// </summary>
public sealed class ImageSuiteRenderDriver : IImageSuiteRenderDriver
{
    /// <summary>
    /// The prompt's author, stated on the image. <c>SceneAssetGenerationJobHandler</c> treats a non-empty
    /// <c>PromptCompilerId</c> as "this text is already model-ready, send it verbatim"; naming the CATALOG as the
    /// author is what makes that true here rather than a claim that a runtime compiler shaped the text.
    /// </summary>
    public const string CatalogVariantCompilerId = "image-suite-catalog-variant";

    private readonly IImageSuiteRepository _suites;
    private readonly ISceneAssetService _assets;
    private readonly ILogger<ImageSuiteRenderDriver> _logger;

    public ImageSuiteRenderDriver(
        IImageSuiteRepository suites,
        ISceneAssetService assets,
        ILogger<ImageSuiteRenderDriver> logger)
    {
        _suites = suites;
        _assets = assets;
        _logger = logger;
    }

    public async Task<ImageSuiteRenderReport> RenderAsync(
        ImageSuiteRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Every one of these is a declaration the render cannot invent. They fail here, together and by name, rather
        // than after some images have already been queued under a name nobody chose.
        if (string.IsNullOrWhiteSpace(request.SuiteId))
        {
            throw new InvalidOperationException("A catalog render must name the suite it renders.");
        }

        if (string.IsNullOrWhiteSpace(request.VariantKey))
        {
            throw new InvalidOperationException(
                "A catalog render must name which variant to render (for example 'biglust'). A cell carries one prompt "
                + "per model, so there is no prompt to render without choosing one.");
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new InvalidOperationException(
                "A catalog render must name the exact registered image model that renders. A model family is not a model.");
        }

        if (string.IsNullOrWhiteSpace(request.ImageSize))
        {
            throw new InvalidOperationException("A catalog render must declare its image size.");
        }

        if (request.SeedSource == ImageSeedSource.Unknown)
        {
            throw new InvalidOperationException(
                "A catalog render must state whether it reproduces (each cell's declared seed) or explores (a fresh "
                + "seed per image). The two produce results that mean different things, so neither can be assumed.");
        }

        if (string.IsNullOrWhiteSpace(request.RunName))
        {
            throw new InvalidOperationException(
                "A catalog render must name the run. The name becomes the container in the Asset Manager, and an "
                + "unnamed container is unreadable once there are several.");
        }

        if (request.CellIds is null || request.CellIds.Count == 0)
        {
            throw new InvalidOperationException(
                "A catalog render must choose at least one cell. Rendering nothing and reporting success would look "
                + "like a finished run.");
        }

        var suite = await _suites.GetSuiteAsync(request.SuiteId, cancellationToken)
            ?? throw new InvalidOperationException($"Suite '{request.SuiteId}' was not found, so it cannot be rendered.");

        var cells = await _suites.ListCellsAsync(suite.Id, cancellationToken);
        var chosenIds = new HashSet<string>(request.CellIds, StringComparer.Ordinal);
        var chosen = cells
            .Where(cell => chosenIds.Contains(cell.Id))
            .OrderBy(cell => cell.Ordinal)
            .ToList();

        if (chosen.Count == 0)
        {
            throw new InvalidOperationException(
                $"None of the {request.CellIds.Count} chosen cell id(s) belong to suite '{suite.Name}'. Rendering a "
                + "different suite's cells would attribute the images to the wrong set.");
        }

        var identity = string.IsNullOrWhiteSpace(request.IdentityPackId)
            ? null
            : new SceneAssetIdentityConditioning(
                request.IdentityPackId.Trim(),
                request.IdentityFaceAssetId?.Trim() ?? string.Empty);

        var container = await _assets.CreateAssetAsync(
            request.RunName.Trim(),
            SceneAssetType.Playground,
            cancellationToken: cancellationToken);

        var rendered = new List<ImageSuiteRenderItem>();
        var skipped = new List<ImageSuiteRenderSkip>();

        foreach (var cell in chosen)
        {
            try
            {
                var variants = ImageCellVariants.Read(cell.VariantsJson, cell.Name);
                if (!variants.TryGetValue(request.VariantKey, out var prompt) || string.IsNullOrWhiteSpace(prompt))
                {
                    skipped.Add(new ImageSuiteRenderSkip(
                        cell.Id,
                        cell.Ordinal,
                        cell.Name,
                        $"This cell has no '{request.VariantKey}' prompt, so it was skipped for this run rather than "
                        + "rendered from another model's wording."));
                    continue;
                }

                // A reproducible run uses the cell's OWN declared seed. A cell that declares none is skipped rather
                // than given a fresh one, because the run said it wanted a reproduction and silently mixing the two
                // would make the whole set uninterpretable. In Explore mode the seed is left null ON PURPOSE, and the
                // render draws and RECORDS one, so even an accidental keeper can be pinned afterwards.
                long? declaredSeed = null;
                if (request.SeedSource == ImageSeedSource.Declared)
                {
                    declaredSeed = ImageCellSeed.ReadDeclared(cell.SettingsJson, cell.Name);
                    if (declaredSeed is null)
                    {
                        skipped.Add(new ImageSuiteRenderSkip(
                            cell.Id,
                            cell.Ordinal,
                            cell.Name,
                            "This cell declares no seed, so a reproducible run cannot honour it. It was skipped rather "
                            + "than rendered with a random seed this run did not ask for."));
                        continue;
                    }
                }

                var image = await _assets.AddGeneratedImageAsync(
                    container.Id,
                    prompt,
                    request.ModelId.Trim(),
                    request.ImageSize.Trim(),
                    cancellationToken,
                    referenceApplications: null,
                    // The run IS the batch: one container, one batch id, so the set comes back together.
                    candidateBatchId: container.Id,
                    options: new SceneAssetImageGenerationOptions
                    {
                        // States that the catalog authored this text for this model's dialect, so the render sends it
                        // as written instead of compiling it a second time.
                        PromptCompilerId = CatalogVariantCompilerId,
                        // Null in Explore mode: the render draws a fresh seed and writes it back to the image.
                        Seed = declaredSeed,
                        Identity = identity,
                        // The run's character LoRAs, applied to EVERY image in the set so the whole run is one
                        // experiment. Null means no LoRA, which is a configured state: no LoRA node is emitted.
                        CharacterLoras = request.CharacterLoras is { Count: > 0 } loras ? loras : null
                    });

                rendered.Add(new ImageSuiteRenderItem(
                    cell.Id,
                    cell.Ordinal,
                    cell.Name,
                    image.Id,
                    prompt));
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
            {
                // Recorded per cell, never fatal: one cell the app refuses must not cancel the other sixty images the
                // operator asked for. The refusal's OWN message is kept, so the reason is the real one.
                skipped.Add(new ImageSuiteRenderSkip(cell.Id, cell.Ordinal, cell.Name, exception.Message));
            }
        }

        var report = new ImageSuiteRenderReport(
            container.Name,
            container.Id,
            request.VariantKey,
            request.ModelId.Trim(),
            request.SeedSource,
            rendered,
            skipped,
            request.CharacterLoras);

        _logger.LogInformation(
            "Catalog render '{RunName}' enqueued {Enqueued} image(s) and skipped {Skipped} for suite {Suite} with "
            + "variant '{Variant}' on model {Model} ({SeedSource} seed, {Loras} character LoRA(s)); container {Container}",
            report.RunName,
            report.EnqueuedCount,
            report.SkippedCount,
            suite.Name,
            report.VariantKey,
            report.ModelId,
            report.SeedSource,
            report.LoraCount,
            container.Id);

        return report;
    }
}
