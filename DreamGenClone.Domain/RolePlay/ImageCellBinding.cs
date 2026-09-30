using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

/// <summary>Which part of the frame this binding carries (B-135 D17). One entry per axis, never two.</summary>
public enum ImageBindingAxis
{
    Unknown = 0,
    Identity = 1,
    Pose = 2,
    Location = 3,
    Wardrobe = 4,
    Pov = 5,
    Lighting = 6
}

/// <summary>
/// HOW an axis is carried. These are genuinely different mechanisms, not preferences: a Reference is an image the
/// model is conditioned on, an Adapter is a control graph, a Lora is a trained identity, and Text is the prompt.
/// </summary>
public enum ImageBindingMode
{
    Unknown = 0,

    /// <summary>Carried by the prompt text. The axis is then the compiler's responsibility.</summary>
    Text = 1,

    /// <summary>Carried by a bound reference image (a pack view, an asset, a skeleton).</summary>
    Reference = 2,

    /// <summary>Carried by a control adapter (OpenPose / Depth / Canny) whose capability the model must declare.</summary>
    Adapter = 3,

    /// <summary>Carried by a character LoRA artifact at an explicit strength.</summary>
    Lora = 4
}

/// <summary>
/// One axis of a cell's declared bindings. This is the cell's side of the contract the request layer is checked
/// against: the run records what was ACTUALLY resolved, and the evaluator compares the two.
/// </summary>
public sealed record ImageCellBinding(
    ImageBindingAxis Axis,
    ImageBindingMode Mode,
    string? Value = null,

    /// <summary>LoRA weight. Required and positive for <see cref="ImageBindingMode.Lora"/>, mirroring the render
    /// path's own rule that a LoRA applied at a strength nobody chose is a different identity.</summary>
    double? Strength = null,

    /// <summary>Adapter name (OpenPose / Depth / Canny). Required for <see cref="ImageBindingMode.Adapter"/>.</summary>
    string? Strategy = null);

/// <summary>
/// Parsing and validation for a cell's <c>BindingsJson</c>.
///
/// <para>
/// A binding declaration is the only thing that makes a cell reproducible, so it is strict on purpose: an unknown
/// axis, an unknown mode, an unknown PROPERTY (which catches a typo like <c>strenght</c>), a duplicate axis, or a
/// mode whose required detail is missing is refused by name rather than ignored. A silently-dropped binding would
/// produce a render that looks like the declared one and is not.
/// </para>
/// </summary>
public static class ImageCellBindings
{
    private static readonly string[] KnownProperties = ["axis", "mode", "value", "strength", "strategy"];

    public static IReadOnlyList<ImageCellBinding> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Cell bindings are not valid JSON: {exception.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Cell bindings must be a JSON array of binding objects.");
            }

            var bindings = new List<ImageCellBinding>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                bindings.Add(ReadBinding(element));
            }

            Validate(bindings);
            return bindings;
        }
    }

    public static string Serialize(IEnumerable<ImageCellBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var list = bindings.ToList();
        Validate(list);

        return JsonSerializer.Serialize(list.Select(binding => new Dictionary<string, object?>
        {
            ["axis"] = binding.Axis.ToString(),
            ["mode"] = binding.Mode.ToString(),
            ["value"] = binding.Value,
            ["strength"] = binding.Strength,
            ["strategy"] = binding.Strategy
        }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value)));
    }

    /// <summary>
    /// The rules, in one place. Every write and every read calls this, so there is a single definition of a valid
    /// declaration.
    /// </summary>
    public static void Validate(IReadOnlyList<ImageCellBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var seen = new HashSet<ImageBindingAxis>();
        foreach (var binding in bindings)
        {
            if (binding.Axis == ImageBindingAxis.Unknown)
            {
                throw new InvalidOperationException("A binding must declare which axis it carries.");
            }

            if (!seen.Add(binding.Axis))
            {
                throw new InvalidOperationException(
                    $"Axis '{binding.Axis}' is declared twice. One entry per axis: two entries would leave the axis's "
                    + "mechanism ambiguous, and the render would have to guess which one won.");
            }

            var value = binding.Value?.Trim() ?? string.Empty;
            switch (binding.Mode)
            {
                case ImageBindingMode.Unknown:
                    throw new InvalidOperationException($"Binding '{binding.Axis}' must declare a mode.");

                case ImageBindingMode.Text:
                    // Text means the PROMPT carries this axis. A value here would be a second source for it.
                    if (value.Length > 0 || binding.Strength is not null || !string.IsNullOrWhiteSpace(binding.Strategy))
                    {
                        throw new InvalidOperationException(
                            $"Binding '{binding.Axis}' is Text but carries a value, strength or strategy. Text means the "
                            + "prompt carries this axis; anything else here would be a second, uncompared source.");
                    }

                    break;

                case ImageBindingMode.Reference:
                    if (value.Length == 0)
                    {
                        throw new InvalidOperationException($"Binding '{binding.Axis}' is a Reference but names nothing to bind.");
                    }

                    // A Reference MAY pin its strategy and its strength, and that is deliberate: "which mechanism carries
                    // this identity" (IP-Adapter graph vs native multi-reference) and "how strongly" are exactly the
                    // variables a COMPARISON suite exists to change. Refusing them here would foreclose the baseline-vs-
                    // variant comparison this playground is built for, and the refusal would look like a strictness win.
                    // A Reference that pins neither is still a complete declaration - it says what is bound and leaves
                    // the mechanism to the render.
                    if (binding.Strength is { } referenceStrength && referenceStrength <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Binding '{binding.Axis}' is a Reference at strength {referenceStrength}; a conditioning "
                            + "strength must be positive or omitted.");
                    }

                    break;

                case ImageBindingMode.Lora:
                    if (value.Length == 0)
                    {
                        throw new InvalidOperationException($"Binding '{binding.Axis}' is a LoRA but names no artifact.");
                    }

                    if (binding.Strength is not { } loraStrength || loraStrength <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Binding '{binding.Axis}' selects LoRA '{value}' without a positive strength. A LoRA applied "
                            + "at a strength nobody chose is a different identity from the trained one.");
                    }

                    if (!string.IsNullOrWhiteSpace(binding.Strategy))
                    {
                        throw new InvalidOperationException($"Binding '{binding.Axis}' is a LoRA and must not carry an adapter strategy.");
                    }

                    break;

                case ImageBindingMode.Adapter:
                    if (string.IsNullOrWhiteSpace(binding.Strategy))
                    {
                        throw new InvalidOperationException(
                            $"Binding '{binding.Axis}' is an Adapter but names no adapter strategy (OpenPose / Depth / Canny).");
                    }

                    if (binding.Strength is { } adapterStrength && adapterStrength <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Binding '{binding.Axis}' declares adapter strength {adapterStrength}; an adapter weight is positive.");
                    }

                    break;
            }
        }
    }

    private static ImageCellBinding ReadBinding(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Each cell binding must be a JSON object.");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!KnownProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Cell binding has an unknown property '{property.Name}'. Known properties: {string.Join(", ", KnownProperties)}. "
                    + "A misspelled property would be silently ignored and the binding would not be the declared one.");
            }
        }

        var axis = ReadEnum(element, "axis", ImageBindingAxis.Unknown, name => Enum.TryParse(name, ignoreCase: true, out ImageBindingAxis parsed) ? parsed : ImageBindingAxis.Unknown);
        var mode = ReadEnum(element, "mode", ImageBindingMode.Unknown, name => Enum.TryParse(name, ignoreCase: true, out ImageBindingMode parsed) ? parsed : ImageBindingMode.Unknown);

        return new ImageCellBinding(axis, mode, ReadString(element, "value"), ReadDouble(element, "strength"), ReadString(element, "strategy"));
    }

    private static TEnum ReadEnum<TEnum>(JsonElement element, string property, TEnum unknown, Func<string, TEnum> parse)
        where TEnum : struct, Enum
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Cell binding is missing its '{property}'.");
        }

        var raw = value.GetString() ?? string.Empty;
        var parsedValue = parse(raw);
        if (EqualityComparer<TEnum>.Default.Equals(parsedValue, unknown))
        {
            // 'Unknown' is the refusal sentinel in every one of these enums, so it is never offered as a valid value.
            var validNames = Enum.GetNames<TEnum>()
                .Where(name => !string.Equals(name, "Unknown", StringComparison.Ordinal));

            throw new InvalidOperationException(
                $"Cell binding has an unrecognised '{property}' value '{raw}'. Valid values: {string.Join(", ", validNames)}.");
        }

        return parsedValue;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
