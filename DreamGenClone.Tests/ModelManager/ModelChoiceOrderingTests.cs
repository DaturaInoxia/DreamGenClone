using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Tests.ModelManager;

/// <summary>
/// The order every model picker renders in, and the model it starts on. These are operator-visible facts
/// ("the default is at the top and already selected"), so they are pinned here rather than left to whichever
/// page happens to sort its own list.
/// </summary>
public sealed class ModelChoiceOrderingTests
{
    [Fact]
    public void Order_PutsTheDefaultProvidersGroupFirst()
    {
        var choices = new[]
        {
            Choice("a1", "p-local", "Local ComfyUI", "BigLust v1.6"),
            Choice("b1", "p-runpod", "RunPod ComfyUI", "Pony V6 XL", isDefaultProvider: true),
        };

        var ordered = ModelChoiceOrdering.Order(choices);

        Assert.Equal(["b1", "a1"], ordered.Select(choice => choice.ModelId));
    }

    [Fact]
    public void Order_PutsTheDefaultModelFirstInsideItsGroup()
    {
        var choices = new[]
        {
            Choice("a1", "p-local", "Local ComfyUI", "BigLust v1.6"),
            Choice("a2", "p-local", "Local ComfyUI", "Qwen-Image-2.1", isDefaultModel: true),
            Choice("a3", "p-local", "Local ComfyUI", "Juggernaut XL"),
        };

        var ordered = ModelChoiceOrdering.Order(choices);

        Assert.Equal(["a2", "a1", "a3"], ordered.Select(choice => choice.ModelId));
    }

    [Fact]
    public void Order_SortsGroupsByNameWhenNoProviderIsDefault()
    {
        var choices = new[]
        {
            Choice("b1", "p-runpod", "RunPod ComfyUI", "Pony V6 XL"),
            Choice("a1", "p-local", "Local ComfyUI", "BigLust v1.6"),
        };

        var ordered = ModelChoiceOrdering.Order(choices);

        Assert.Equal(["a1", "b1"], ordered.Select(choice => choice.ModelId));
    }

    [Fact]
    public void Order_KeepsEachProvidersModelsTogether()
    {
        var choices = new[]
        {
            Choice("a1", "p-local", "Local ComfyUI", "BigLust v1.6"),
            Choice("b1", "p-runpod", "RunPod ComfyUI", "Pony V6 XL"),
            Choice("a2", "p-local", "Local ComfyUI", "Qwen-Image-2.1"),
        };

        var grouped = ModelChoiceOrdering.Group(ModelChoiceOrdering.Order(choices));

        Assert.Equal(2, grouped.Count);
        Assert.Equal("Local ComfyUI", grouped[0].ProviderName);
        Assert.Equal(["a1", "a2"], grouped[0].Choices.Select(choice => choice.ModelId));
        Assert.Equal("RunPod ComfyUI", grouped[1].ProviderName);
        Assert.Equal(["b1"], grouped[1].Choices.Select(choice => choice.ModelId));
    }

    [Fact]
    public void Order_DoesNotMutateTheInput()
    {
        var choices = new List<SceneImageModelChoice>
        {
            Choice("a1", "p-local", "Local ComfyUI", "Zeta"),
            Choice("a2", "p-local", "Local ComfyUI", "Alpha"),
        };

        ModelChoiceOrdering.Order(choices);

        Assert.Equal(["a1", "a2"], choices.Select(choice => choice.ModelId));
    }

    [Fact]
    public void ConfiguredDefault_ReturnsNullWhenNothingIsFlagged()
    {
        var ordered = ModelChoiceOrdering.Order(
        [
            Choice("a1", "p-local", "Local ComfyUI", "BigLust v1.6"),
        ]);

        Assert.Null(ModelChoiceOrdering.ConfiguredDefault(ordered));
    }

    [Fact]
    public void AutoSelect_PrefersTheConfiguredDefaultOverTheFirstModel()
    {
        var ordered = ModelChoiceOrdering.Order(
        [
            Choice("a1", "p-local", "Local ComfyUI", "BigLust v1.6"),
            Choice("a2", "p-local", "Local ComfyUI", "Qwen-Image-2.1", isDefaultModel: true),
        ]);

        Assert.Equal("a2", ModelChoiceOrdering.AutoSelect(ordered)?.ModelId);
    }

    [Fact]
    public void AutoSelect_FallsBackToTheFirstModelWhenNoDefaultIsConfigured()
    {
        var ordered = ModelChoiceOrdering.Order(
        [
            Choice("a2", "p-local", "Local ComfyUI", "Qwen-Image-2.1"),
            Choice("a1", "p-local", "Local ComfyUI", "BigLust v1.6"),
        ]);

        // The documented substitution: first in display order, which is the only answer available once the
        // operator has not designated one.
        Assert.Equal("a1", ModelChoiceOrdering.AutoSelect(ordered)?.ModelId);
    }

    [Fact]
    public void Group_CarriesTheDefaultProviderFlagSoTheLabelCanShowIt()
    {
        var grouped = ModelChoiceOrdering.Group(ModelChoiceOrdering.Order(
        [
            Choice("a1", "p-local", "Local ComfyUI", "BigLust v1.6", isDefaultProvider: true),
            Choice("b1", "p-runpod", "RunPod ComfyUI", "Pony V6 XL"),
        ]));

        Assert.True(grouped[0].IsDefaultProvider);
        Assert.False(grouped[1].IsDefaultProvider);
    }

    [Fact]
    public void Group_KeepsUnregisteredProvidersTogetherUnderOneGroup()
    {
        // A model whose provider row is missing still has to render. It must not become one group per model,
        // which would print the same fallback label as many times as there are orphans.
        var choices = new[]
        {
            Choice("a1", string.Empty, "Unknown", "BigLust v1.6"),
            Choice("a2", string.Empty, "Unknown", "Qwen-Image-2.1"),
        };

        var grouped = ModelChoiceOrdering.Group(ModelChoiceOrdering.Order(choices));

        Assert.Single(grouped);
        Assert.Equal(["a1", "a2"], grouped[0].Choices.Select(choice => choice.ModelId));
    }

    private static SceneImageModelChoice Choice(
        string id,
        string providerId,
        string providerName,
        string displayName,
        bool isDefaultModel = false,
        bool isDefaultProvider = false) => new(id, displayName, id, providerName, false)
        {
            ProviderId = providerId,
            IsDefaultModel = isDefaultModel,
            IsDefaultProvider = isDefaultProvider
        };
}
