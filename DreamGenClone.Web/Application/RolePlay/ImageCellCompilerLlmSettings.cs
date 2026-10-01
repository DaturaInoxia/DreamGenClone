using System.Globalization;
using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// The pinned compiler LLM for a run or a cell (B-135 B135-016 / D11).
///
/// <para>
/// <b>Why pinning is not optional.</b> The compiled prompt is the thing under test, so a compiler call whose model,
/// temperature or seed nobody wrote down makes every cell non-repeatable: a red cell could be a bad compiler or a
/// different sample, and nothing in the run would say which. D11 therefore makes these DECLARED run variables that are
/// recorded with the run rather than read from whatever the app's current default happens to be.
/// </para>
///
/// <para>
/// <b>The seed is declared but NOT honoured yet, and this type says so rather than pretending.</b>
/// <c>ICompletionClient</c> has no seed parameter at any of its overloads — seeds exist only on the image clients
/// (<c>ComfyUIImageClient</c>, <c>RunPodServerlessImageClient</c>). So <see cref="Seed"/> can be parsed and carried,
/// and <see cref="ImageCellPromptCompiler"/> refuses a declared seed with an explicit message instead of silently
/// compiling without one. A run that recorded a seed it never sent would look reproducible and would not be, which is
/// the precise failure this whole workstream exists to stop.
/// </para>
/// </summary>
public sealed record ImageCellCompilerLlmSettings(
    string ModelIdentifier,
    double Temperature,
    long? Seed = null)
{
    /// <summary>Reads the declared settings. Refuses a shape it cannot honour rather than reading part of it.</summary>
    public static ImageCellCompilerLlmSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "No compiler LLM is declared. A run pins the model it compiles with (B-135 D11); an unpinned compiler "
                + "makes every cell's result unattributable.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The compiler LLM declaration is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("The compiler LLM declaration must be a JSON object.");
            }

            var model = ReadString(root, "model");
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException(
                    "The compiler LLM declaration names no model. Declare {\"model\":\"...\",\"temperature\":0} so the run "
                    + "records which model drafted the prompt it is judged on.");
            }

            if (!root.TryGetProperty("temperature", out var temperatureElement)
                || temperatureElement.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"The compiler LLM declaration for '{model}' declares no temperature. Temperature is a declared run "
                    + "variable, never a default: a prompt compiled at a temperature nobody chose is not comparable to one "
                    + "compiled at the pinned value.");
            }

            var temperature = temperatureElement.GetDouble();
            if (temperature < 0 || temperature > 2)
            {
                throw new InvalidOperationException(
                    $"The compiler LLM declaration for '{model}' declares temperature {temperature.ToString(CultureInfo.InvariantCulture)}, "
                    + "which is outside the 0-2 range providers accept.");
            }

            long? seed = null;
            if (root.TryGetProperty("seed", out var seedElement) && seedElement.ValueKind != JsonValueKind.Null)
            {
                if (seedElement.ValueKind != JsonValueKind.Number || !seedElement.TryGetInt64(out var parsedSeed))
                {
                    throw new InvalidOperationException(
                        $"The compiler LLM declaration for '{model}' has a seed that is not a whole number.");
                }

                seed = parsedSeed;
            }

            return new ImageCellCompilerLlmSettings(model.Trim(), temperature, seed);
        }
    }

    /// <summary>Writes the declaration back, so what a run records is exactly what it declared.</summary>
    public string Serialize()
    {
        var seedFragment = Seed is { } value ? $",\"seed\":{value.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        return $"{{\"model\":{JsonSerializer.Serialize(ModelIdentifier)},\"temperature\":{Temperature.ToString("0.###", CultureInfo.InvariantCulture)}{seedFragment}}}";
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
