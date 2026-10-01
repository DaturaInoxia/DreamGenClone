using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>One axis the compiled text must NOT describe, because something structural carries it.</summary>
public sealed record ImageCellStructuralAxis(ImageBindingAxis Axis, ImageBindingMode Mode, string? Value);

/// <summary>
/// Everything the compile step is given. Note what is ABSENT: the cell's <c>ExpectedPrompt</c>. The compiler must not
/// see the answer — "compiled ≈ expected" is only evidence if the two were produced independently.
/// </summary>
public sealed record ImageCellCompileInput(
    string UserDirection,
    ImageCompilerProfile Profile,
    ImageCellCompilerLlmSettings Llm,
    IReadOnlyList<ImageCellBinding> Bindings);

/// <summary>What the compiler produced, with the exact messages that produced it.</summary>
public sealed record ImageCellCompileResult(
    string CompiledPrompt,
    string SystemPrompt,
    string UserPrompt,
    string ModelIdentifier,
    double Temperature);

public interface IImageCellPromptCompiler
{
    /// <summary>
    /// Compiles a cell's input into one prompt for the profile's checkpoint. Fails fast rather than degrading: no
    /// compiler LLM, no instructions, or a declared seed the completion path cannot honour are all refusals.
    /// </summary>
    Task<ImageCellCompileResult> CompileAsync(ImageCellCompileInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// The Playground's compile step (B-135 B135-015/B135-016): a cell's user direction becomes one prompt for a specific
/// checkpoint, using that checkpoint's OWN profile.
///
/// <para>
/// <b>What it deliberately does not do.</b> It does not repair its own output. A prompt that comes back wrapped in a
/// code fence or spread over three lines is returned exactly as the model produced it, and the prompt-layer evaluator
/// then fails it — because a compiler silently cleaning up after a model that ignored its instructions hides the very
/// signal the cell exists to measure. Only whitespace is trimmed.
/// </para>
///
/// <para>
/// The system prompt is the checkpoint's <c>SystemPrompt</c> from its profile, which the seed copies from the family's
/// researched text (<see cref="SceneImageCompilerSystemPrompts"/>). Nothing here composes instructions of its own, so
/// there is exactly one place a compiler instruction can come from.
/// </para>
/// </summary>
public sealed class ImageCellPromptCompiler : IImageCellPromptCompiler
{
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolution;

    public ImageCellPromptCompiler(
        ICompletionClient completionClient,
        IModelResolutionService modelResolution)
    {
        _completionClient = completionClient;
        _modelResolution = modelResolution;
    }

    public async Task<ImageCellCompileResult> CompileAsync(
        ImageCellCompileInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.UserDirection))
        {
            throw new InvalidOperationException("The cell carries no user direction, so there is nothing to compile.");
        }

        // One definition of a valid profile; a profile that cannot compile must not reach a model.
        ImageCompilerProfileValidation.Validate(input.Profile);

        if (input.Llm.Seed is { } declaredSeed)
        {
            // A capability gate, not a preference. The completion path has no seed parameter, so a run that declared one
            // would record a seed it never sent - the run would look reproducible and would not be. Fail here, where the
            // declaration is known, instead of at the point where the evidence is believed.
            throw new InvalidOperationException(
                $"The compiler LLM declaration pins seed {declaredSeed}, but the text completion path cannot honour it: "
                + $"'{nameof(ICompletionClient)}' has no seed parameter (seeds exist only on the image clients). Compile "
                + "with temperature 0 and no seed, or add seed support to the completion client and the provider it "
                + "targets. A seed that is recorded but never sent is worse than no seed at all.");
        }

        var resolved = await ResolveCompilerModelAsync(input.Llm, cancellationToken);
        var userPrompt = BuildUserPrompt(input);
        var systemPrompt = input.Profile.SystemPrompt;

        var (content, _) = await _completionClient.GenerateWithReasoningAsync(
            systemPrompt,
            userPrompt,
            resolved,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                $"The compiler LLM '{input.Llm.ModelIdentifier}' returned an empty prompt for cell input "
                + $"'{FirstLine(input.UserDirection)}'.");
        }

        return new ImageCellCompileResult(
            content.Trim(),
            systemPrompt,
            userPrompt,
            resolved.ModelIdentifier,
            resolved.Temperature);
    }

    /// <summary>
    /// Resolves the pinned compiler LLM. The declared model id must be the one that answers: silently compiling with a
    /// different model would make the run's own record of its compiler false.
    /// </summary>
    private async Task<ResolvedModel> ResolveCompilerModelAsync(
        ImageCellCompilerLlmSettings llm,
        CancellationToken cancellationToken)
    {
        var resolved = await _modelResolution.ResolveImagePromptModelAsync(null, cancellationToken);
        if (!string.Equals(resolved.ModelIdentifier, llm.ModelIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The run pins compiler LLM '{llm.ModelIdentifier}' but the app resolves '{resolved.ModelIdentifier}' for "
                + "image prompt generation. Compiling with the resolved model anyway would record a compiler that did not "
                + "draft the prompt. Point the declaration at the model the app is configured to use, or configure that "
                + "model to match.");
        }

        if (Math.Abs(resolved.Temperature - llm.Temperature) > 0.0005)
        {
            throw new InvalidOperationException(
                $"The run pins temperature {llm.Temperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} "
                + $"but '{resolved.ModelIdentifier}' is configured at {resolved.Temperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}. "
                + "Temperature is a declared run variable; a prompt compiled at a temperature nobody chose cannot be "
                + "compared to one that was.");
        }

        return resolved;
    }

    /// <summary>
    /// The user message. Built only from the cell's own input and the checkpoint's declared data — no invented
    /// guidance, because anything added here would be an undeclared variable in the comparison.
    /// </summary>
    private static string BuildUserPrompt(ImageCellCompileInput input)
    {
        var profile = input.Profile;
        var builder = new System.Text.StringBuilder();

        builder.AppendLine("USER DIRECTION (the complete input; compile ONLY this):");
        builder.AppendLine(input.UserDirection.Trim());
        builder.AppendLine();
        builder.AppendLine($"TARGET CHECKPOINT: {profile.DisplayName} ({profile.CheckpointIdentifier})");
        builder.AppendLine(
            $"PROMPT BUDGET: the result must be between {profile.MinChars} and {profile.MaxChars} characters "
            + $"(target {SceneImageCompilerSystemPrompts.OutputTargetChars}).");

        var textAxes = input.Bindings.Where(binding => binding.Mode == ImageBindingMode.Text).Select(binding => binding.Axis).ToList();
        var structuralAxes = input.Bindings
            .Where(binding => binding.Mode != ImageBindingMode.Text)
            .Select(binding => new ImageCellStructuralAxis(binding.Axis, binding.Mode, binding.Value))
            .ToList();

        builder.AppendLine(textAxes.Count == 0
            ? "AXES CARRIED BY THIS TEXT: none declared - describe only what the user direction states."
            : $"AXES CARRIED BY THIS TEXT: {string.Join(", ", textAxes)}.");
        if (structuralAxes.Count > 0)
        {
            builder.AppendLine(
                "AXES CARRIED STRUCTURALLY (do NOT describe these in words - the render supplies them, and saying them "
                + "as well creates a second, conflicting source): "
                + string.Join(", ", structuralAxes.Select(axis => $"{axis.Axis} via {axis.Mode}")) + ".");
        }

        builder.AppendLine($"POSE IN TEXT: {DescribePoseInText(profile.PoseInText)}");
        builder.AppendLine($"REQUIRED COMPONENTS: {string.Join(", ", ReadComponentNames(profile.RequiredComponentsJson))}");
        builder.AppendLine($"NEVER INCLUDE: {string.Join(", ", ReadComponentNames(profile.ForbiddenTokensJson))}");
        builder.AppendLine();
        builder.AppendLine("Return only the final prompt as plain text: one line, no commentary, no quotes, no markdown.");
        return builder.ToString();
    }

    /// <summary>
    /// States the checkpoint's measured pose capability in words the model can act on. This is where the "pose text
    /// essentially does not work" finding becomes an instruction instead of an unwritten expectation.
    /// </summary>
    private static string DescribePoseInText(ImagePoseInText poseInText) => poseInText switch
    {
        ImagePoseInText.Full =>
            "complex and multi-person poses may be described in words.",
        ImagePoseInText.SimpleOnly =>
            "only a simple single-subject pose may be described; if the direction needs anything more, describe the "
            + "people and the framing and leave the pose out rather than attempting it in words.",
        ImagePoseInText.Forbidden =>
            "pose must NOT be described in words at all. This checkpoint cannot hold a body position expressed as text, "
            + "and attempting it produces the wrong pose or merges people. Describe who is present, their appearance, "
            + "their clothing and the framing; leave the body position to the structural source.",
        _ => throw new InvalidOperationException(
            $"The profile declares pose-in-text '{poseInText}', which is not a known capability. Profile validation "
            + "refuses this, so reaching here means a profile was constructed without validation.")
    };

    private static IReadOnlyList<string> ReadComponentNames(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement
                .EnumerateArray()
                .Where(element => element.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(element => element.GetString() ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            // The profile's own validation already refused malformed JSON; treat an unreadable list as "none named"
            // rather than failing a compile over reporting detail.
            return [];
        }
    }

    private static string FirstLine(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        return index < 0 ? text.Trim() : text[..index].Trim();
    }
}
