using System.Text.RegularExpressions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Contract for how a host passes parameters to <c>ImageStepComposer</c>.
///
/// For a component parameter typed <c>string</c>, Razor treats an unquoted attribute value as a STRING LITERAL, not
/// as a C# expression: <c>SelectedModelId="_modelId"</c> hands the composer the nine characters <c>_modelId</c>.
/// That is invisible to the compiler and to every test that does not read the source, and it broke the whole
/// reference step in the field: the composer could never match its selected model, so <c>SelectedChoice</c> stayed
/// null and the reference slots - the entire feature - never rendered, on all six hosts, while the model picker
/// happily listed the models and the operator's choice was even persisted.
///
/// The visible tell was a literal <c>RenderBlockedReason</c> printed in the panel, which is why that parameter is
/// covered here too. Non-string parameters are unaffected (e.g. <c>CanSubmit="CanTestPose()"</c> is an expression).
/// </summary>
public sealed class ImageStepComposerUsageContractTests
{
    /// <summary>
    /// String-typed composer parameters a host passes by identifier. Each must be written <c>Param="@expression"</c>.
    /// The lookbehind keeps <c>@bind-SelectedModelId="…"</c> (<c>@bind</c> always takes an expression) and longer
    /// names that merely end in these words (<c>ShowPrompt</c>, <c>EditablePrompt</c>) out of scope.
    /// </summary>
    private static readonly Regex UnprefixedStringArgument = new(
        @"(?<![\w-])(SelectedModelId|Prompt|PromptError|BindingError|SubmitBlockedReason|GeneratedForSignature)=""[^@""]",
        RegexOptions.CultureInvariant);

    [Fact]
    public void EveryHostPassesStringParametersAsExpressions()
    {
        var hosts = Directory
            .EnumerateFiles(Path.Combine(FindRepositoryRoot(), "DreamGenClone.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("<ImageStepComposer", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(6, hosts.Count);

        var offenders = new List<string>();
        foreach (var host in hosts)
        {
            foreach (Match match in UnprefixedStringArgument.Matches(File.ReadAllText(host)))
            {
                offenders.Add($"{Path.GetFileName(host)}: {match.Value}\"");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These string parameters are passed as literals, so the composer receives the parameter NAME as text "
            + "instead of the host's value. Prefix each with '@': " + string.Join("; ", offenders));
    }

    /// <summary>
    /// Proves the guard is not vacuous: it must flag the exact broken forms that shipped, and must not flag the
    /// correct forms that look similar.
    /// </summary>
    [Fact]
    public void TheGuardFlagsTheBrokenFormAndAcceptsTheCorrectOne()
    {
        Assert.Matches(UnprefixedStringArgument, "<ImageStepComposer SelectedModelId=\"_cellModelId\" />");
        Assert.Matches(UnprefixedStringArgument, "<ImageStepComposer SubmitBlockedReason=\"RenderBlockedReason\" />");
        Assert.Matches(UnprefixedStringArgument, "<ImageStepComposer\n    Prompt=\"_prompt\" />");

        Assert.DoesNotMatch(UnprefixedStringArgument, "<ImageStepComposer SelectedModelId=\"@_cellModelId\" />");
        Assert.DoesNotMatch(UnprefixedStringArgument, "<ModelPicker @bind-SelectedModelId=\"_bodyViewModelId\" />");
        Assert.DoesNotMatch(UnprefixedStringArgument, "<ImageStepComposer ShowPrompt=\"false\" />");
        Assert.DoesNotMatch(UnprefixedStringArgument, "<EditIterateWorkbench @bind-EditablePrompt=\"_editablePrompt\" />");
    }

    /// <summary>
    /// Every public property of the composer is a parameter, because that is the only way a host can set it. This is
    /// not pedantry: an edit that dropped one <c>[Parameter]</c> attribute left <c>Busy</c> a plain property, and the
    /// whole step then threw at render time - "has a property matching the name 'Busy', but it does not have
    /// [Parameter]" - because a host was still passing it. No test here renders a component, so nothing else in the
    /// suite could see it.
    /// </summary>
    [Fact]
    public void EveryComposerPropertyIsAParameter()
    {
        var lines = File.ReadAllLines(Path.Combine(
            FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Shared", "ImageStepComposer.razor"));

        var offenders = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("public ", StringComparison.Ordinal)
                || !line.EndsWith("{ get; set; }", StringComparison.Ordinal))
            {
                continue;
            }

            var j = i - 1;
            while (j >= 0 && (lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(lines[j])))
            {
                j--;
            }

            if (j < 0 || !lines[j].TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                offenders.Add($"line {i + 1}: {line}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These composer properties have no [Parameter] attribute, so a host that passes them makes the step throw "
            + "at render time: " + string.Join("; ", offenders));
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln"))
                && File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find the DreamGenClone repository root from '{AppContext.BaseDirectory}'.");
    }
}
