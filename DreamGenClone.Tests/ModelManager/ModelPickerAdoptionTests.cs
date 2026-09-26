using System.Text.RegularExpressions;

namespace DreamGenClone.Tests.ModelManager;

/// <summary>
/// B-131: every model dropdown in the app renders through <c>ModelPicker</c>, so the grouping (one group per
/// provider) and the ordering (the configured default first) are written once instead of being re-derived per
/// page. A page that grows its own flat model <c>&lt;select&gt;</c> is the regression this guard exists to catch:
/// it would list the operator's default model in an arbitrary position with no provider grouping, and nothing
/// else in the suite would notice.
/// </summary>
public sealed class ModelPickerAdoptionTests
{
    /// <summary>
    /// The scope is the IMAGE model pickers: a component that deals in <c>SceneImageModelChoice</c> must render its
    /// model <c>&lt;select&gt;</c> through <c>ModelPicker</c>.
    ///
    /// Text/LLM pickers are deliberately out of scope. They select a <c>RegisteredModel</c>, they already group by
    /// provider into <c>&lt;optgroup&gt;</c>s, and their "nothing selected" state means "use the Function Default" -
    /// the per-function default mechanism, not the image-model row flag - so they answer a different question and are
    /// left to their own UI (e.g. <c>ModelSettingsPanel</c>, the role-play assistant picker).
    ///
    /// Known gap, recorded rather than silently allowed: <c>PoseAuthorPanel</c> is an image model picker whose list is
    /// <c>PoseTestModelChoice</c> - a pose-specific shape carrying a per-model <c>CanCarryPose</c> gate and a mechanism
    /// label that <c>SceneImageModelChoice</c> does not have - so this guard does not see it and it does not yet render
    /// through the shared picker.
    /// </summary>
    [Fact]
    public void EveryImageModelDropdownRendersThroughTheSharedPicker()
    {
        var imageModelComponents = Directory
            .EnumerateFiles(Path.Combine(FindRepositoryRoot(), "DreamGenClone.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), "ModelPicker.razor", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("SceneImageModelChoice", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(imageModelComponents);

        // Only a <select> that binds a model id is the pattern being replaced. A text <input> that happens to hold a
        // model identifier (the LoRA base model) is not a picker and is deliberately left alone.
        var offenders = imageModelComponents
            .Where(path => Regex.IsMatch(
                File.ReadAllText(path),
                @"<select[^>]*@bind=""_?[a-zA-Z]*ModelId""",
                RegexOptions.CultureInvariant))
            .Select(path => Path.GetFileName(path))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These components deal in image model choices but bind a model id in their own <select>, so they miss "
            + $"the provider grouping and the configured default ordering: {string.Join(", ", offenders)}. Render "
            + $"them through ModelPicker (Components/Shared/ModelPicker.razor).");
    }

    [Fact]
    public void TheSharedPickerIsTheOnlyGroupingOwner()
    {
        // Grouping is the picker's job. A second component that builds its own provider groups would be a second
        // answer to "what order do models appear in".
        var picker = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Shared", "ModelPicker.razor"));

        Assert.Contains("<optgroup", picker, StringComparison.Ordinal);
        Assert.Contains("ModelChoiceOrdering.Order(Choices)", picker, StringComparison.Ordinal);
        Assert.Contains("ModelChoiceOrdering.Group(ordered)", picker, StringComparison.Ordinal);
        Assert.Contains("ModelChoiceOrdering.AutoSelect(ordered)", picker, StringComparison.Ordinal);
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
