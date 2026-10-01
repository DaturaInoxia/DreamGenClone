using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Who chooses the sampler seed for a run.
///
/// <para>
/// Stated by the caller, never defaulted, because the two answers mean opposite things: one makes a re-run reproduce
/// the same images so a difference can be attributed to a change, the other produces new images so a set can be
/// explored. Neither is a substitute for the other, and a run that did not say which it wanted is not a run whose
/// results can be interpreted.
/// </para>
/// </summary>
public enum ImageSeedSource
{
    Unknown = 0,

    /// <summary>
    /// Each cell's own declared seed, so re-running reproduces the image. A cell that declares no seed is SKIPPED and
    /// reported: rendering it with a fresh random seed would quietly turn a reproducible run into a mixed one.
    /// </summary>
    Declared = 1,

    /// <summary>
    /// A fresh seed per image, drawn by the render and RECORDED on the image. Every result is still reproducible
    /// afterwards, so a lucky accident can be pinned instead of lost.
    /// </summary>
    Random = 2
}

/// <summary>
/// What to render out of a catalog, and where the result should land.
///
/// <para>
/// <b>Two independent choices, deliberately not coupled.</b> <see cref="VariantKey"/> picks WHICH prompt a cell is
/// rendered from (the text authored for one model family), and <see cref="ModelId"/> picks WHICH registered model
/// renders it. They are separate because the interesting questions are cross-model — "how does this set look on
/// BigLust, then on Juggernaut, then on BigLust with a character applied" — and refusing a pairing that is merely
/// unusual would block exactly the experiments this exists for. The UI shows the chosen model's family and dialect
/// beside its name so the correspondence is visible rather than enforced.
/// </para>
/// </summary>
public sealed record ImageSuiteRenderRequest(
    string SuiteId,

    /// <summary>The cells to render. One, many, or all — the caller decides; an empty list is refused, not defaulted to all.</summary>
    IReadOnlyList<string> CellIds,

    /// <summary>The variant key in each cell's <c>VariantsJson</c>, e.g. <c>biglust</c>, <c>pony</c>, <c>qwen-edit-2511</c>.</summary>
    string VariantKey,

    /// <summary>The EXACT registered image model id that renders. Never a family.</summary>
    string ModelId,

    string ImageSize,

    /// <summary>
    /// Who chooses the sampler seed. STATED by the caller rather than defaulted: one mode reproduces, the other
    /// explores, and a run that did not say which would produce results nobody could interpret.
    /// </summary>
    ImageSeedSource SeedSource,

    /// <summary>The run's name, which becomes the container's name in the Asset Manager. Required: an unnamed run is unreadable later.</summary>
    string RunName,

    /// <summary>Optional identity pack applied to every render in the run (the "same character across the catalog" run).</summary>
    string? IdentityPackId = null,

    string? IdentityFaceAssetId = null,

    /// <summary>
    /// The character LoRAs applied to every render in the run, in chain order.
    ///
    /// <para>
    /// Empty is a CONFIGURED state, not a missing one: the render then emits no LoRA node at all and is identical to a
    /// render made before character LoRAs existed. Each artifact binds to the checkpoint it was trained against, so the
    /// surface that fills this can only offer the artifacts qualified for the chosen model's checkpoint - which is why
    /// the choice travels beside <see cref="ModelId"/> rather than being resolved later.
    /// </para>
    /// </summary>
    IReadOnlyList<SceneImageCharacterLoraSelection>? CharacterLoras = null);

/// <summary>One render enqueued with the exact prompt it was given, so the report is readable without opening the DB.</summary>
public sealed record ImageSuiteRenderItem(
    string CellId,
    int Ordinal,
    string CellName,
    string ImageId,
    string Prompt);

/// <summary>
/// One cell that produced no render, with the reason. Reported rather than dropped: a silently skipped cell is
/// indistinguishable from a cell that was never in the run, and the operator would read the run as complete.
/// </summary>
public sealed record ImageSuiteRenderSkip(string CellId, int Ordinal, string CellName, string Reason);

/// <summary>The outcome of one run: what was enqueued, what was skipped, and where the images live.</summary>
public sealed record ImageSuiteRenderReport(
    string RunName,
    string ContainerAssetId,
    string VariantKey,
    string ModelId,
    ImageSeedSource SeedSource,
    IReadOnlyList<ImageSuiteRenderItem> Rendered,
    IReadOnlyList<ImageSuiteRenderSkip> Skipped,

    /// <summary>
    /// The LoRAs the run was rendered WITH, so the report is readable on its own. A set of images made with a character
    /// applied and a set made without look like two results of one experiment unless the run says which it was.
    /// </summary>
    IReadOnlyList<SceneImageCharacterLoraSelection>? CharacterLoras = null)
{
    public int EnqueuedCount => Rendered.Count;

    public int SkippedCount => Skipped.Count;

    /// <summary>How many character LoRAs conditioned every image in this run; zero means none were applied.</summary>
    public int LoraCount => CharacterLoras?.Count ?? 0;
}

/// <summary>
/// Renders a catalog's cells: picks each cell's prompt for the chosen variant key and enqueues a real image
/// generation into a run container in the Asset Manager.
///
/// <para>
/// This is the path that makes "run the whole set on BigLust" a thing an operator can do, and it deliberately
/// bypasses <c>ImageRunExecutor</c>'s free layers: those compile a user direction into a prompt and judge the result,
/// which is a different question from "what does this authored prompt look like on this model".
/// </para>
/// </summary>
public interface IImageSuiteRenderDriver
{
    /// <summary>
    /// Enqueues one render per selected cell that carries the chosen variant. A cell without it is skipped and
    /// reported; a cell whose enqueue is refused is skipped and reported with the refusal's own message.
    /// </summary>
    Task<ImageSuiteRenderReport> RenderAsync(
        ImageSuiteRenderRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The one reader of a cell's <c>VariantsJson</c>. Lives beside the driver because the driver and the cell list both
/// ask the same question of the same payload, and two readers would eventually disagree about a blank prompt.
/// </summary>
public static class ImageCellVariants
{
    private static readonly Dictionary<string, string> Empty = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The cell's variant prompts, keyed case-insensitively. A blank value is dropped rather than offered, because a
    /// variant that is present but empty would render nothing meaningful and would look like a valid selection.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Read(string variantsJson, string cellName)
    {
        if (string.IsNullOrWhiteSpace(variantsJson))
        {
            return Empty;
        }

        Dictionary<string, string>? parsed;
        try
        {
            parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(variantsJson);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidOperationException(
                $"Cell '{cellName}' has an unreadable variants payload, so no prompt can be chosen for it. "
                + $"{exception.Message}");
        }

        if (parsed is null || parsed.Count == 0)
        {
            return Empty;
        }

        var readable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in parsed)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                readable[pair.Key] = pair.Value;
            }
        }

        return readable;
    }

    /// <summary>
    /// Every variant key the cells offer, in a stable order, so the UI can offer the keys that actually exist in the
    /// suite instead of a list hardcoded from the manifest legend.
    /// </summary>
    public static IReadOnlyList<string> ListKeys(IEnumerable<ImageSuiteCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in cells)
        {
            foreach (var key in Read(cell.VariantsJson, cell.Name).Keys)
            {
                keys.Add(key);
            }
        }

        return keys.ToList();
    }
}

/// <summary>
/// The one reader of the seed a cell declares in its settings. Beside the variant reader because both answer a
/// question about the SAME cell, and a declared seed that cannot be read must say so rather than be skipped silently.
/// </summary>
public static class ImageCellSeed
{
    /// <summary>
    /// The seed the cell declares, or null when it declares none. A value that is present but not a whole number is
    /// REFUSED rather than rounded or ignored: a seed is an integer, and a silently coerced one would render something
    /// other than what the catalog asked for.
    /// </summary>
    public static long? ReadDeclared(string settingsJson, string cellName)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }

        System.Text.Json.Nodes.JsonNode? node;
        try
        {
            node = System.Text.Json.Nodes.JsonNode.Parse(settingsJson);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidOperationException(
                $"Cell '{cellName}' has unreadable settings, so the seed it declares cannot be read. {exception.Message}");
        }

        if (node is not System.Text.Json.Nodes.JsonObject settings)
        {
            return null;
        }

        if (!settings.TryGetPropertyValue("seed", out var seedNode) || seedNode is null)
        {
            return null;
        }

        if (seedNode is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<long>(out var seed))
        {
            return seed;
        }

        throw new InvalidOperationException(
            $"Cell '{cellName}' declares seed {seedNode.ToJsonString()}, which is not a whole number. A seed is an "
            + "integer, so it is refused rather than rounded or ignored.");
    }
}
