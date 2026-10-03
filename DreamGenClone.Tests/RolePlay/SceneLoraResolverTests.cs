using System.Text.Json;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The scene-LoRA resolver is the ONE place a LoRA the operator picked is checked against the model it will be
/// applied to. Every refusal here replaces a silent wrong render: a LoRA from another family, a file the catalog
/// does not carry, or a strength nobody chose would all still produce an image.
/// </summary>
public sealed class SceneLoraResolverTests
{
    private const string NsfwV4 = "krea2_nsfw_v4_v43exp.safetensors";
    private const string Cowgirl = "krea2_act_cowgirl_lokr.safetensors";

    private static readonly SceneLora NsfwV4Row = new()
    {
        Id = "krea2-unlock",
        FileName = NsfwV4,
        DisplayName = "Krea2 NSFW V4",
        SceneImageModelFamily = SceneImageModelFamily.Krea2,
        Category = SceneLoraCategory.Unlock,
        DefaultStrength = 1.0,
        IsEnabled = true
    };

    private static readonly SceneLora CowgirlRow = new()
    {
        Id = "krea2-act-cowgirl",
        FileName = Cowgirl,
        DisplayName = "Cowgirl act LoKr",
        SceneImageModelFamily = SceneImageModelFamily.Krea2,
        Category = SceneLoraCategory.Act,
        DefaultStrength = 1.0,
        IsEnabled = true
    };

    private static ResolvedImageModel Model(SceneImageModelFamily family = SceneImageModelFamily.Krea2) => new(
        ProviderBaseUrl: "http://192.168.0.11:8188",
        ImageGenerationPath: "/prompt",
        ProviderTimeoutSeconds: 300,
        ApiKeyEncrypted: null,
        ModelIdentifier: "krea2_turbo_fp8_scaled.safetensors",
        ContentPolicy: ImageContentPolicy.AdultAllowed,
        ProviderName: "Local ComfyUI (WOOD-GAME-MAIN 5080)",
        IsSessionOverride: false,
        SceneImageModelFamily: family,
        PromptDialect: family == SceneImageModelFamily.Krea2
            ? SceneImagePromptDialect.Krea2NaturalLanguage
            : SceneImagePromptDialect.SdxlNaturalLanguage,
        ImageProtocol: ImageProtocol.ComfyUi,
        ComfyUiUrl: "http://192.168.0.11:8188");

    private static SceneImageStudioSettings Settings(params SceneImageLoraSelection[] selections) =>
        new() { SceneLoras = selections.ToList() };

    private static SceneLoraResolver Resolver(params SceneLora[] catalog) =>
        new(new StubCatalog(catalog), NullLogger<SceneLoraResolver>.Instance);

    [Fact]
    public async Task ResolveAsync_NoSelection_ReturnsEmptyWithoutTouchingTheCatalog()
    {
        var catalog = new StubCatalog([NsfwV4Row]) { FailOnRead = true };

        var resolved = await new SceneLoraResolver(catalog, NullLogger<SceneLoraResolver>.Instance)
            .ResolveAsync(Model(), new SceneImageStudioSettings());

        Assert.Empty(resolved);
        Assert.Equal(0, catalog.Reads);
    }

    [Fact]
    public async Task ResolveAsync_KeepsTheOperatorOrderAndCarriesThePurpose()
    {
        var resolved = await Resolver(NsfwV4Row, CowgirlRow).ResolveAsync(
            Model(),
            Settings(
                new SceneImageLoraSelection { FileName = NsfwV4, Strength = 1.0 },
                new SceneImageLoraSelection { FileName = Cowgirl, Strength = 0.9 }));

        Assert.Equal([NsfwV4, Cowgirl], resolved.Select(lora => lora.FileName));
        Assert.Equal(0.9, resolved[1].Strength);
        Assert.Equal("Krea2 NSFW V4", resolved[0].Purpose);
    }

    [Fact]
    public async Task ResolveAsync_WrongFamily_IsRefusedRatherThanApplied()
    {
        var sdxlRow = new SceneLora
        {
            Id = "sdxl-anatomy",
            FileName = "sdxl_anatomy.safetensors",
            DisplayName = "SDXL anatomy",
            SceneImageModelFamily = SceneImageModelFamily.Sdxl,
            Category = SceneLoraCategory.Anatomy,
            DefaultStrength = 0.6,
            IsEnabled = true
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Resolver(sdxlRow).ResolveAsync(
                Model(),
                Settings(new SceneImageLoraSelection { FileName = sdxlRow.FileName, Strength = 0.6 })));

        Assert.Contains("Sdxl", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Krea2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_FileNameNotInTheCatalog_IsRefused()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Resolver(NsfwV4Row).ResolveAsync(
                Model(),
                Settings(new SceneImageLoraSelection { FileName = "unknown.safetensors", Strength = 1.0 })));

        Assert.Contains("not in the scene-LoRA catalog", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_DisabledRow_IsRefused()
    {
        var disabled = new SceneLora
        {
            Id = "krea2-off",
            FileName = "krea2_off.safetensors",
            DisplayName = "Disabled",
            SceneImageModelFamily = SceneImageModelFamily.Krea2,
            Category = SceneLoraCategory.Style,
            DefaultStrength = 0.7,
            IsEnabled = false
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Resolver(disabled).ResolveAsync(
                Model(),
                Settings(new SceneImageLoraSelection { FileName = disabled.FileName, Strength = 0.7 })));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public async Task ResolveAsync_MissingOrNonPositiveStrength_IsRefused(double? strength)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Resolver(NsfwV4Row).ResolveAsync(
                Model(),
                Settings(new SceneImageLoraSelection { FileName = NsfwV4, Strength = strength })));

        Assert.Contains("strength", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_SameLoraTwice_IsRefused()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Resolver(NsfwV4Row).ResolveAsync(
                Model(),
                Settings(
                    new SceneImageLoraSelection { FileName = NsfwV4, Strength = 1.0 },
                    new SceneImageLoraSelection { FileName = NsfwV4, Strength = 0.5 })));

        Assert.Contains("selected twice", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_UnknownModelFamily_IsRefused()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Resolver(NsfwV4Row).ResolveAsync(
                Model(SceneImageModelFamily.Unknown),
                Settings(new SceneImageLoraSelection { FileName = NsfwV4, Strength = 1.0 })));

        Assert.Contains("scene-image family", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The selection is part of the render's settings snapshot, so it must survive the JSON round-trip the settings
    /// take through the queue - otherwise the LoRA would be picked in the UI and silently absent at render time.
    /// </summary>
    [Fact]
    public void SceneLoras_RoundTripThroughTheSettingsSnapshot()
    {
        var settings = new SceneImageStudioSettings
        {
            SceneLoras =
            [
                new SceneImageLoraSelection { FileName = NsfwV4, Strength = 1.0 },
                new SceneImageLoraSelection { FileName = Cowgirl, Strength = 0.85 }
            ]
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<SceneImageStudioSettings>(json)!;

        Assert.NotNull(restored.SceneLoras);
        Assert.Equal(2, restored.SceneLoras!.Count);
        Assert.Equal(NsfwV4, restored.SceneLoras[0].FileName);
        Assert.Equal(0.85, restored.SceneLoras[1].Strength);
        // An empty selection serializes as an empty list, which the resolver treats as "no scene LoRA".
        Assert.Null(JsonSerializer.Deserialize<SceneImageStudioSettings>("{}")!.SceneLoras);
    }

    private sealed class StubCatalog(IReadOnlyList<SceneLora> rows) : ISceneLoraRepository
    {
        public bool FailOnRead { get; init; }
        public int Reads { get; private set; }

        public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SceneLora>> ListAsync(
            SceneImageModelFamily family, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (FailOnRead)
            {
                throw new InvalidOperationException("The catalog should not be read for an empty selection.");
            }

            return Task.FromResult<IReadOnlyList<SceneLora>>(
                rows.Where(row => row.SceneImageModelFamily == family && row.IsEnabled).ToList());
        }

        public Task<SceneLora?> GetByFileNameAsync(string fileName, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (FailOnRead)
            {
                throw new InvalidOperationException("The catalog should not be read for an empty selection.");
            }

            return Task.FromResult(rows.FirstOrDefault(row =>
                string.Equals(row.FileName, fileName, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
