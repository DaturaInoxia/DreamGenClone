using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// How a model variant is APPLIED (B-135 manifest v2).
///
/// <para>
/// This distinction is not cosmetic: a <see cref="Generate"/> variant is a text-to-image prompt, while an
/// <see cref="Edit"/> variant is an instruction that changes an existing image ("keep both people's faces, hair and
/// lighting exactly unchanged"). An edit variant cannot be rendered from nothing, so a suite run against an edit model
/// is a two-stage chain per cell — render the base, then edit it. Without this field the manifest's <c>qwen-edit</c>
/// entries look like ordinary prompts and a run would either drop them or send an edit instruction to a text-to-image
/// checkpoint.
/// </para>
/// </summary>
public enum PromptVariantKind
{
    Unknown = 0,
    Generate = 1,
    Edit = 2
}

/// <summary>
/// One model the suite can be run against: the variant KEY used in every position file, and the CHECKPOINT that key
/// means (B-135 manifest v2).
///
/// <para>
/// The key is data, not an enum, precisely so a new checkpoint is added by editing the manifest legend. The app maps
/// <see cref="Checkpoint"/> to a compiler profile through the existing per-checkpoint lookup, so a legend entry naming
/// a checkpoint with no profile fails fast by name rather than compiling with another checkpoint's instructions.
/// </para>
/// </summary>
public sealed record PromptModelVariant(
    string Key,
    string Checkpoint,
    PromptVariantKind Kind,
    string DisplayName);

/// <summary>One runnable position: the three prompts the design calls for, plus one variant per model.</summary>
public sealed record PromptSuitePosition
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>Cast legend key ("1M1F", "2F1M", …) so a cell's expected person count is declared, not inferred from prose.</summary>
    public string Actors { get; init; } = string.Empty;

    public bool Closeup { get; init; }

    /// <summary>
    /// What a user (or an RP session) would type. The thing the COMPILER is given; it never sees
    /// <see cref="Expected"/>.
    /// </summary>
    public string UserInput { get; init; } = string.Empty;

    /// <summary>
    /// The prompt known to work for this position — the baseline the compiled prompt is measured against, and the
    /// prompt an "expected" run renders from directly. Present ONCE, model-independent: it is the claim, not a variant.
    /// </summary>
    public string Expected { get; init; } = string.Empty;

    /// <summary>The neutral, model-agnostic description of the scene.</summary>
    public string NeutralScene { get; init; } = string.Empty;

    /// <summary>Declared negative for this position, empty when the position needs none.</summary>
    public string Negative { get; init; } = string.Empty;

    /// <summary>Key → prompt, one entry per model in the legend. A missing key is refused when a run needs it.</summary>
    public IReadOnlyDictionary<string, string> Variants { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The per-position sampler settings, as authored. Kept verbatim so a run sends what the file says.</summary>
    public string SettingsJson { get; init; } = "{}";

    /// <summary>
    /// Optional declared bindings for this position (identity / pose / location, and whether each is carried by text or
    /// by a reference). Same contract the cell uses, so a manifest cell and a hand-authored cell cannot disagree.
    /// </summary>
    public string BindingsJson { get; init; } = "[]";

    /// <summary>
    /// What this position got wrong, in words, instead of a refusal that stops the suite loading.
    ///
    /// <para>
    /// The point of a suite is to RUN prompts so the images can be looked at. A manifest that refuses to load because
    /// one position forgot its 'flux' variant blocks every other position too. So a gap is recorded here, shown against
    /// the cell, and the positions that are complete still run — and a model with no variant here is simply skipped for
    /// that model, which is visible in the run rather than fatal to it.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>True when this position has a prompt for the model, so a run can include it.</summary>
    public bool HasVariant(string key) => Variants.ContainsKey(key);

    /// <summary>
    /// The prompt for a variant key, or a refusal naming the position and the keys it does have.
    ///
    /// <para>
    /// Reached when a run targets a model whose checkpoint has no variant here — the crossing of a run's checkpoint with
    /// this position's variants. Failing by name is the point: falling back to another model's variant would render an
    /// image that is filed as this model's result while carrying a different model's prompt.
    /// </para>
    /// </summary>
    public string RequireVariant(string key)
    {
        if (Variants.TryGetValue(key, out var prompt))
        {
            return prompt;
        }

        throw new InvalidOperationException(
            $"Position '{Id}' has no variant for '{key}'. It has [{string.Join(", ", Variants.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}].");
    }
}

/// <summary>
/// A whole manifest: the suite envelope plus the positions it lists.
///
/// <para>
/// <b>Agent-authored, never written by the app.</b> The manifest is source control content that an agent edits at design
/// time; the app only READS it and imports it into a suite. That is why every parse rule here fails loudly rather than
/// repairing: a manifest that drifts from this contract must break the import, not silently produce a suite whose cells
/// are missing the prompt that was the point of the cell.
/// </para>
/// </summary>
public sealed record PromptSuiteManifest
{
    public string Suite { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    /// <summary>The model legend: which variant keys exist and what checkpoint each means.</summary>
    public IReadOnlyList<PromptModelVariant> Models { get; init; } = [];

    public IReadOnlyList<PromptSuitePosition> Positions { get; init; } = [];

    /// <summary>Suite-level gaps (a missing or duplicated legend entry). Reported, never fatal.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    public PromptModelVariant? Model(string key) =>
        Models.FirstOrDefault(model => string.Equals(model.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The strict parse and validation path for a manifest and its position files. One definition, called by the import on
/// every read, so a malformed manifest cannot produce a half-populated suite.
/// </summary>
public static class PromptSuiteManifestValidation
{
    private static readonly string[] ManifestProperties = ["suite", "purpose", "models", "positions"];

    private static readonly string[] ModelProperties = ["key", "checkpoint", "kind", "displayName"];

    private static readonly string[] PositionProperties =
        ["id", "title", "actors", "closeup", "userInput", "expected", "neutralScene", "negative", "variants", "settings", "bindings"];

    public static PromptSuiteManifest ParseManifest(string json) =>
        ParseManifest(json, path => throw new FileNotFoundException($"Position file '{path}' was not supplied.", path));

    /// <summary>
    /// Parses the suite envelope and every position file. <paramref name="readPositionFile"/> is supplied by the caller so
    /// the whole import is testable without touching a filesystem.
    /// </summary>
    public static PromptSuiteManifest ParseManifest(string json, Func<string, string> readPositionFile)
    {
        ArgumentNullException.ThrowIfNull(readPositionFile);

        using var document = Parse(json, "manifest");
        var root = document.RootElement;
        RequireObject(root, "manifest");

        var suite = ReadRequiredString(root, "suite", "manifest");
        var purpose = ReadOptionalString(root, "purpose");

        var problems = new List<string>();
        CollectUnknown(root, ManifestProperties, "manifest", problems);

        var models = new List<PromptModelVariant>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("models", out var modelsElement) && modelsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in modelsElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    problems.Add("The models legend has a non-object entry, which was ignored.");
                    continue;
                }

                CollectUnknown(element, ModelProperties, "manifest.models[]", problems);
                var key = ReadOptionalString(element, "key");
                if (string.IsNullOrWhiteSpace(key))
                {
                    problems.Add("The models legend has an entry with no 'key', which was ignored.");
                    continue;
                }

                if (!seenKeys.Add(key))
                {
                    problems.Add($"The models legend declares '{key}' twice; the first entry is used.");
                    continue;
                }

                // A legend entry with no checkpoint is usable for naming the variant but not for resolving a compiler
                // profile, so it is recorded and the run surface says so when it needs the checkpoint.
                var checkpoint = ReadOptionalString(element, "checkpoint") ?? string.Empty;
                var displayName = ReadOptionalString(element, "displayName") ?? key;
                var kind = TryReadEnum<PromptVariantKind>(element, "kind", "manifest.models[]", problems, out var parsedKind)
                    ? parsedKind
                    : PromptVariantKind.Generate;

                models.Add(new PromptModelVariant(key, checkpoint, kind, displayName));
            }
        }
        else
        {
            problems.Add(
                "The manifest declares no 'models' legend, so a variant key cannot be mapped to a checkpoint. Pick the "
                + "variant key by hand when running, or add the legend.");
        }

        if (!root.TryGetProperty("positions", out var positionsElement) || positionsElement.ValueKind != JsonValueKind.Array)
        {
            problems.Add("The manifest has no 'positions' array, so there is nothing to run.");
            return new PromptSuiteManifest { Suite = suite, Purpose = purpose, Models = models, Problems = problems };
        }

        var positions = new List<PromptSuitePosition>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in positionsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                problems.Add("The positions array has a non-object entry, which was ignored.");
                continue;
            }

            var path = ReadOptionalString(element, "path") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                problems.Add("A positions entry names no 'path', so its file could not be read.");
                continue;
            }

            var position = ParsePosition(readPositionFile(path), path, models);
            if (!seenIds.Add(position.Id))
            {
                problems.Add($"The manifest lists position id '{position.Id}' twice; the first entry is used.");
                continue;
            }

            positions.Add(position);
        }

        if (positions.Count == 0)
        {
            problems.Add("The manifest lists no positions, so running it would produce nothing.");
        }

        return new PromptSuiteManifest { Suite = suite, Purpose = purpose, Models = models, Positions = positions, Problems = problems };
    }

    /// <summary>Parses one position file. Gaps are collected onto the position; only a missing id is fatal.</summary>
    public static PromptSuitePosition ParsePosition(string json, string path, IReadOnlyList<PromptModelVariant> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        using var document = Parse(json, path);
        var root = document.RootElement;
        RequireObject(root, path);

        var problems = new List<string>();
        CollectUnknown(root, PositionProperties, path, problems);

        // The ONE fatal field: without an id the position cannot be tracked through a run or matched to its images.
        var id = ReadRequiredString(root, "id", path);
        var title = ReadOptionalString(root, "title") ?? id;

        var actors = ReadOptionalString(root, "actors") ?? string.Empty;
        if (actors.Length == 0)
        {
            problems.Add("No 'actors' declared, so the expected person count is unknown.");
        }

        var userInput = ReadOptionalString(root, "userInput") ?? string.Empty;
        if (userInput.Length == 0)
        {
            problems.Add("No 'userInput'; there is nothing for a compiler to compile for this position.");
        }

        var expected = ReadOptionalString(root, "expected") ?? string.Empty;
        if (expected.Length == 0)
        {
            problems.Add("No 'expected' prompt; a compiled prompt has nothing to be measured against.");
        }

        var neutralScene = ReadOptionalString(root, "neutralScene") ?? string.Empty;

        var variants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("variants", out var variantsElement) && variantsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var variant in variantsElement.EnumerateObject())
            {
                if (variant.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(variant.Value.GetString()))
                {
                    problems.Add($"Variant '{variant.Name}' is empty and was ignored.");
                    continue;
                }

                // Checked only when the legend exists: with no legend we cannot know which keys are real, and flagging
                // every key as unknown would bury the gaps that actually matter.
                if (models.Count > 0
                    && models.All(model => !string.Equals(model.Key, variant.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add(
                        $"Variant '{variant.Name}' is not in the model legend (declared: "
                        + $"{string.Join(", ", models.Select(model => model.Key))}), so no run will select it.");
                }

                variants[variant.Name] = variant.Value.GetString()!.Trim();
            }
        }
        else
        {
            problems.Add("No 'variants' object, so there is no prompt to render for any model.");
        }

        // Reported per model rather than thrown: a position missing its 'flux' prompt must still run for biglust.
        foreach (var model in models.Where(model => !variants.ContainsKey(model.Key)))
        {
            problems.Add($"No '{model.Key}' variant; this position is skipped when the run targets {model.DisplayName}.");
        }

        var settings = ReadOptionalObjectJson(root, "settings", path, problems);
        var bindings = ReadOptionalArrayJson(root, "bindings", path, problems);

        return new PromptSuitePosition
        {
            Id = id,
            Title = title,
            Actors = actors,
            Closeup = root.TryGetProperty("closeup", out var closeup) && closeup.ValueKind == JsonValueKind.True,
            UserInput = userInput,
            Expected = expected,
            NeutralScene = neutralScene,
            Negative = ReadOptionalString(root, "negative") ?? string.Empty,
            Variants = variants,
            SettingsJson = settings,
            BindingsJson = bindings,
            Problems = problems
        };
    }

    private static JsonDocument Parse(string json, string path)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"'{path}' is empty.");
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"'{path}' is not valid JSON: {exception.Message}", exception);
        }
    }

    private static void RequireObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"'{path}' must be a JSON object.");
        }
    }

    /// <summary>
    /// Records an unknown property instead of refusing it. A field named slightly differently ('userPrompt' for
    /// 'userInput') must be VISIBLE - it would otherwise be dropped and the cell would run with an empty prompt that
    /// nothing reports - but it must not stop the other positions from running.
    /// </summary>
    private static void CollectUnknown(JsonElement element, IReadOnlyList<string> known, string path, List<string> problems)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                problems.Add($"Unknown property '{property.Name}' (known: {string.Join(", ", known)}).");
            }
        }
    }

    private static string ReadRequiredString(JsonElement element, string property, string path)
    {
        var value = ReadOptionalString(element, property);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"'{path}' is missing the required string '{property}'.");
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool TryReadEnum<TEnum>(
        JsonElement element,
        string property,
        string path,
        List<string> problems,
        out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;
        var raw = ReadOptionalString(element, property);
        if (raw is null)
        {
            return false;
        }

        if (!Enum.TryParse<TEnum>(raw, ignoreCase: true, out parsed) || !Enum.IsDefined(parsed))
        {
            problems.Add($"'{path}' has an unrecognised '{property}' value '{raw}'; Generate is assumed.");
            parsed = default;
            return false;
        }

        return true;
    }

    /// <summary>Re-serialises a sub-object so the value is stored exactly as authored, or "{}" when absent.</summary>
    private static string ReadOptionalObjectJson(JsonElement element, string property, string path, List<string> problems)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return "{}";
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"'{property}' is not a JSON object and was ignored (the render will use the model's own defaults).");
            return "{}";
        }

        return value.GetRawText();
    }

    private static string ReadOptionalArrayJson(JsonElement element, string property, string path, List<string> problems)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return "[]";
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"'{property}' is not a JSON array and was ignored.");
            return "[]";
        }

        return value.GetRawText();
    }
}
