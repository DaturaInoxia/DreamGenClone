using System.Text.Json;
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

    /// <summary>
    /// The prompt's author for a POSE cell. Same fact as <see cref="CatalogVariantCompilerId"/> - the text is already
    /// model-ready and is sent as written - but a different author: a pose cell's prompt is the pose's own stored
    /// wording, which is the exact string the pose proofs sent, so a suite render stays comparable to those results.
    /// </summary>
    public const string PoseLibraryPromptCompilerId = "pose-library-preset";

    private readonly IImageSuiteRepository _suites;
    private readonly ISceneAssetService _assets;
    private readonly ILogger<ImageSuiteRenderDriver> _logger;

    /// <summary>
    /// Required only by a POSE library run, and refused by name when such a run reaches them: the pose's angles are read
    /// from the preset's own metadata, so a pose suite cannot be rendered without the library. A catalog run never
    /// touches either one, which is why they are optional rather than forced on every host and test.
    /// </summary>
    private readonly IPoseLibraryService? _poseLibrary;
    private readonly ICharacterImageIdentityService? _identity;

    public ImageSuiteRenderDriver(
        IImageSuiteRepository suites,
        ISceneAssetService assets,
        ILogger<ImageSuiteRenderDriver> logger,
        IPoseLibraryService? poseLibrary = null,
        ICharacterImageIdentityService? identity = null)
    {
        _suites = suites;
        _assets = assets;
        _logger = logger;
        _poseLibrary = poseLibrary;
        _identity = identity;
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

        // A POSE suite has no per-model variants: its cells name a POSE, and the wording is the pose's own stored
        // prompt. So the variant key is required for a catalog and refused for a pose suite - asking for a variant
        // there would be asking the operator to choose something that does not exist.
        var poseSuite = suite.Kind == ImageSuiteKind.PoseLibrary;
        if (!poseSuite && string.IsNullOrWhiteSpace(request.VariantKey))
        {
            throw new InvalidOperationException(
                "A catalog render must name which variant to render (for example 'biglust'). A cell carries one prompt "
                + "per model, so there is no prompt to render without choosing one.");
        }

        // The character is a RUN choice, so a character that cannot be resolved fails the run rather than every cell:
        // one answer for the whole set, refused once.
        var character = await ResolveCharacterAsync(request.CharacterProfileId, cancellationToken);

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
                // A pose cell's prompt and references come from the POSE it stands for; a catalog cell's come from its
                // variant for the chosen model. One dispatch, stated once, so the two kinds cannot half-apply.
                var poseCell = poseSuite ? await ResolvePoseCellAsync(cell, character, cancellationToken) : null;

                string resolvedPrompt;
                if (poseCell is not null)
                {
                    resolvedPrompt = poseCell.Prompt;
                }
                else
                {
                    var variants = ImageCellVariants.Read(cell.VariantsJson, cell.Name);
                    if (!variants.TryGetValue(request.VariantKey, out var variantPrompt)
                        || string.IsNullOrWhiteSpace(variantPrompt))
                    {
                        skipped.Add(new ImageSuiteRenderSkip(
                            cell.Id,
                            cell.Ordinal,
                            cell.Name,
                            $"This cell has no '{request.VariantKey}' prompt, so it was skipped for this run rather than "
                            + "rendered from another model's wording."));
                        continue;
                    }

                    resolvedPrompt = variantPrompt;
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
                    resolvedPrompt,
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
                        PromptCompilerId = poseCell is null ? CatalogVariantCompilerId : PoseLibraryPromptCompilerId,
                        // Null in Explore mode: the render draws a fresh seed and writes it back to the image.
                        Seed = declaredSeed,
                        // A pose cell carries its own pose and its own character-derived angles; a catalog cell uses
                        // the run-level identity instead.
                        PosePresetId = poseCell?.PosePresetId,
                        PoseSkeletonRelativePath = poseCell?.SkeletonRelativePath,
                        Identity = poseCell is null ? identity : poseCell.Identity,
                        BodyReference = poseCell?.Body,
                        // The run's character LoRAs, applied to EVERY image in the set so the whole run is one
                        // experiment. Null means no LoRA, which is a configured state: no LoRA node is emitted.
                        CharacterLoras = request.CharacterLoras is { Count: > 0 } loras ? loras : null
                    });

                rendered.Add(new ImageSuiteRenderItem(
                    cell.Id,
                    cell.Ordinal,
                    cell.Name,
                    image.Id,
                    resolvedPrompt));
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
            request.CharacterLoras,
            character?.DisplayName);

        _logger.LogInformation(
            "Catalog render '{RunName}' enqueued {Enqueued} image(s) and skipped {Skipped} for suite {Suite} with "
            + "variant '{Variant}' on model {Model} ({SeedSource} seed, {Loras} character LoRA(s), character {Character}); "
            + "container {Container}",
            report.RunName,
            report.EnqueuedCount,
            report.SkippedCount,
            suite.Name,
            report.VariantKey,
            report.ModelId,
            report.SeedSource,
            report.LoraCount,
            character?.DisplayName ?? "none",
            container.Id);

        return report;
    }

    /// <summary>
    /// The character a run conditions on, or null for a run that picked none. A run-level choice, so an unresolvable
    /// one fails the whole run by name: conditioning sixty cells on a character the app cannot find would otherwise
    /// produce sixty images of somebody else.
    /// </summary>
    private async Task<IdentityPackOwner?> ResolveCharacterAsync(
        string? characterProfileId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(characterProfileId))
        {
            return null;
        }

        var identity = _identity ?? throw new InvalidOperationException(
            $"This run conditions on character '{characterProfileId}', but the identity service is not configured for "
            + "catalog runs. Register ICharacterImageIdentityService, or run without a character.");

        var owners = await identity.ListPackOwnersAsync(cancellationToken);
        return owners.FirstOrDefault(owner =>
                string.Equals(owner.CharacterProfileId, characterProfileId.Trim(), StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Character '{characterProfileId}' has no approved identity pack, so this run cannot condition on it. "
                + "Approve a pack in Character Studio, or run without a character.");
    }

    /// <summary>One pose cell, resolved to everything the render needs: the prompt, the pose, and its two angles.</summary>
    private sealed record PoseCellPlan(
        string Prompt,
        string PosePresetId,
        string? SkeletonRelativePath,
        SceneAssetIdentityConditioning? Identity,
        SceneAssetBodyReferenceConditioning? Body);

    /// <summary>
    /// Turns a pose cell into its render inputs, or refuses in words the operator can act on.
    ///
    /// <para>
    /// The angles are resolved HERE, from the preset's CURRENT metadata, rather than read from the cell: the cell
    /// records which pose it stands for, and a direction corrected after the suite was derived therefore takes effect on
    /// the next run instead of leaving a stale angle behind. A character that has not approved an angle the pose needs
    /// refuses THIS cell by name (which angle, for which character, and what to do), because one unapproved back view
    /// must not cancel the other poses in the run.
    /// </para>
    /// </summary>
    private async Task<PoseCellPlan> ResolvePoseCellAsync(
        ImageSuiteCell cell,
        IdentityPackOwner? character,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cell.ExpectedPrompt))
        {
            throw new InvalidOperationException(
                $"Pose cell '{cell.Name}' carries no prompt, which means its pose declares no content rating. Declare "
                + "the pack's rating in its pack.json (a prompt cannot be written without knowing whether the subject "
                + "is clothed), then rebuild the suite.");
        }

        var presetId = ReadPosePresetId(cell);
        var library = _poseLibrary ?? throw new InvalidOperationException(
            $"Pose cell '{cell.Name}' needs the pose library (its skeleton and its declared angles are read from the "
            + "preset), but the pose-library service is not configured for catalog runs. Register IPoseLibraryService.");

        var preset = await library.GetPresetAsync(presetId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Pose preset '{presetId}' (cell '{cell.Name}') is no longer in the library. Rebuild the suite from the "
                + "library so its cells match the poses that exist.");

        if (character is null)
        {
            // No character: the pose renders from its skeleton alone, which is exactly what the pose proofs send. Said
            // rather than implied, because a set of unposed-looking images and a set with no character are otherwise
            // indistinguishable in the container.
            return new PoseCellPlan(cell.ExpectedPrompt, preset.Id, preset.SkeletonPngPath, null, null);
        }

        var plan = PoseMetadataPrompt.ReferencePlan(preset);
        var unplannable = PoseReferenceRequirement.Unplannable(preset.Name, plan);
        if (unplannable is not null)
        {
            throw new InvalidOperationException(unplannable);
        }

        SceneImageReferenceAsset? face = null;
        if (plan.FaceView is { } faceView)
        {
            face = IdentityPackReferenceResolver.ResolveFace(character.ApprovedAssets, faceView)
                ?? throw new InvalidOperationException(
                    PoseReferenceRequirement.MissingFace(character, preset.Name, preset.Direction));
        }

        var body = IdentityPackReferenceResolver.ResolveBody(
                character.ApprovedAssets, plan.BodyView!.Value, plan.BodyState!.Value)
            ?? throw new InvalidOperationException(
                PoseReferenceRequirement.MissingBody(
                    character, preset.Name, preset.Direction, preset.ContentRating, plan.BodyState.Value));

        return new PoseCellPlan(
            cell.ExpectedPrompt,
            preset.Id,
            preset.SkeletonPngPath,
            // A back-facing pose resolves NO face and sends none: substituting the front here is the failure this whole
            // route exists to avoid, and it would put a face where the pose has none.
            face is null ? null : new SceneAssetIdentityConditioning(character.PackId, face.Id),
            new SceneAssetBodyReferenceConditioning(character.PackId, body.Id));
    }

    /// <summary>
    /// The pose a cell stands for, read from its declared bindings. Kept as the POSE ID rather than a skeleton path:
    /// the render reads the bytes by id, so a path that has gone stale cannot make the render read a different file than
    /// the pose it names.
    /// </summary>
    private static string ReadPosePresetId(ImageSuiteCell cell)
    {
        if (string.IsNullOrWhiteSpace(cell.BindingsJson))
        {
            throw new InvalidOperationException(
                $"Pose cell '{cell.Name}' declares no bindings, so which pose it stands for is unknown. Rebuild the "
                + "suite from the pose library.");
        }

        IReadOnlyList<ReferenceApplicationSelection> bindings;
        try
        {
            bindings = JsonSerializer.Deserialize<IReadOnlyList<ReferenceApplicationSelection>>(cell.BindingsJson)
                ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Pose cell '{cell.Name}' has unreadable bindings, so its pose cannot be resolved: {exception.Message}",
                exception);
        }

        var presetId = bindings
            .Select(binding => binding.PosePresetId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        return presetId
            ?? throw new InvalidOperationException(
                $"Pose cell '{cell.Name}' declares no pose preset, so the skeleton to send cannot be resolved. Rebuild "
                + "the suite from the pose library.");
    }
}
