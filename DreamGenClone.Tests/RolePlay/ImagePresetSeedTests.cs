using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The lighting and expression presets are wording, so they are seeded rows in the same store every other prompt
/// lives in - and these tests hold the two properties that make them worth having: every key resolves, and a detail
/// actually carries the MECHANICS of its condition rather than its name.
///
/// <para>
/// The second one is the whole point of the feature. "angry" is a word the model guesses at; "the eyebrows pulled
/// down and drawn together with vertical creases between them" is the expression. A preset that degrades into a
/// one-word label is worse than no preset, because it looks like it is doing something and is not.
/// </para>
/// </summary>
public sealed class ImagePresetSeedTests
{
    [Fact]
    public async Task EveryPresetKey_Resolves()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var missing = new List<string>();
            foreach (var key in ImagePresetKeys.All)
            {
                try
                {
                    var resolved = await service.ResolveAsync(key, null);
                    if (string.IsNullOrWhiteSpace(resolved.Body))
                    {
                        missing.Add($"{key} (empty body)");
                    }
                }
                catch (InvalidOperationException)
                {
                    missing.Add(key);
                }
            }

            Assert.Empty(missing);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>The lighting presets the LoRA matrix needs are the six the matrix uses, in its own order.</summary>
    [Fact]
    public void LightingPresets_AreTheSixTheMatrixUses()
    {
        Assert.Equal(
            new[]
            {
                "image.preset.lighting.indoor-bright", "image.preset.lighting.indoor-dim",
                "image.preset.lighting.outdoor-day", "image.preset.lighting.outdoor-golden",
                "image.preset.lighting.outdoor-night", "image.preset.lighting.hard-rim"
            },
            ImagePresetKeys.LightingKeys);
    }

    /// <summary>
    /// A detail is a noun phrase: the assembly template owns the verb, so one row serves the edit instruction and the
    /// compose clause without a second copy to keep in sync.
    /// </summary>
    [Fact]
    public async Task PresetDetails_CarryNoVerbOfTheirOwn()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            foreach (var key in ImagePresetKeys.LightingKeys.Concat(ImagePresetKeys.ExpressionKeys))
            {
                var body = (await service.ResolveAsync(key, null)).Body;

                // No full stop: the assembly supplies the sentence, so a detail that ends one would read as
                // "…only this: <detail>. Keep the person identical…" with the sentence already closed.
                Assert.DoesNotContain(".", body, StringComparison.Ordinal);

                // And it has to say something substantial - a two-word detail is a label wearing a preset's clothes.
                Assert.True(body.Length >= 90, $"'{key}' is only {body.Length} characters: {body}");
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// A lighting detail describes the LIGHT, with the mechanics named: where it comes from, what stays lit, what
    /// falls off, and the white balance. This is what "dim" never said, and why the dim cells came back bright.
    /// </summary>
    [Fact]
    public async Task LightingDetails_NameTheMechanicsOfTheLight()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var lightWords = new[]
            {
                "light", "lamp", "key", "shadow", "highlight", "white balance", "catchlight",
                "daylight", "sunlight", "rim", "contrast", "illumination", "fall-off", "falloff",
                "lit", "bright", "dark", "black", "ambient", "raking"
            };
            // Object-level settings that would contradict the preserve clause's "the setting itself unchanged".
            var settingWords = new[] { "wall", "bedroom", "kitchen", "living room", "studio", "backdrop" };

            foreach (var key in ImagePresetKeys.LightingKeys)
            {
                var body = (await service.ResolveAsync(key, null)).Body;

                var named = lightWords.Count(word => body.Contains(word, StringComparison.OrdinalIgnoreCase));
                Assert.True(named >= 3, $"'{key}' names only {named} light properties ({body})");

                foreach (var word in settingWords)
                {
                    Assert.DoesNotContain(word, body, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// An expression detail names the facial action units - brows, lids, lips, jaw, cheeks, chin, nostrils. A detail
    /// that does not is a label, and a label is exactly what the render already failed to act on.
    /// </summary>
    [Fact]
    public async Task ExpressionDetails_NameTheFacialActionUnits()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var actionWords = new[]
            {
                "brow", "eyelid", "lid", "eye", "mouth", "lip", "jaw", "nostril",
                "cheek", "chin", "teeth", "gaze", "nose", "corner", "crease"
            };

            foreach (var key in ImagePresetKeys.ExpressionKeys)
            {
                var body = (await service.ResolveAsync(key, null)).Body;

                var named = actionWords.Count(word => body.Contains(word, StringComparison.OrdinalIgnoreCase));
                Assert.True(named >= 3, $"'{key}' names only {named} facial features ({body})");
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// The assemblies own the verb and the slots: a change assembly must carry BOTH slots, a condition assembly
    /// carries the detail and nothing to preserve (nothing exists yet to preserve).
    /// </summary>
    [Theory]
    [InlineData(ImagePresetKeys.AssemblyLightingChange, true)]
    [InlineData(ImagePresetKeys.AssemblyExpressionChange, true)]
    [InlineData(ImagePresetKeys.AssemblyLightingCondition, false)]
    [InlineData(ImagePresetKeys.AssemblyExpressionCondition, false)]
    public async Task Assemblies_CarryTheSlotsTheirModeNeeds(string key, bool change)
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var body = (await service.ResolveAsync(key, null)).Body;

            Assert.Contains($"{{{ImagePresetKeys.DetailSlot}}}", body, StringComparison.Ordinal);
            Assert.Equal(change, body.Contains($"{{{ImagePresetKeys.PreserveSlot}}}", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>A preserve clause may not weaken with time: it must name what stays identical, not merely ask for it.</summary>
    [Theory]
    [InlineData(ImagePresetKeys.PreserveLighting)]
    [InlineData(ImagePresetKeys.PreserveExpression)]
    public async Task PreserveClauses_NameWhatMustStayIdentical(string key)
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var body = (await service.ResolveAsync(key, null)).Body;

            foreach (var must in new[] { "face", "pose", "framing", "clothing" })
            {
                Assert.Contains(must, body, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// Every lighting and expression value the LoRA matrix can choose must have a preset, or a cell could not be
    /// relit to the condition it was planned for. The mapping is by key suffix, so this test is what keeps the two
    /// namespaces aligned as either side grows.
    /// </summary>
    [Fact]
    public void EveryMatrixAxisValue_HasAPreset()
    {
        foreach (var vocabularyKey in LoraCellWorkflowKeys.VocabularyKeys)
        {
            var isLighting = vocabularyKey.StartsWith("lora.vocabulary.lighting.", StringComparison.Ordinal);
            var isExpression = vocabularyKey.StartsWith("lora.vocabulary.expression.", StringComparison.Ordinal);
            if (!isLighting && !isExpression)
            {
                continue;
            }

            var preset = ImagePresetKeys.PresetKeyFor(vocabularyKey);
            Assert.Contains(
                preset,
                isLighting ? ImagePresetKeys.LightingKeys : ImagePresetKeys.ExpressionKeys);
        }
    }

    /// <summary>An axis with no preset is refused by name: a plausible-looking key would be a hidden fallback.</summary>
    [Theory]
    [InlineData("lora.vocabulary.outfit.casual")]
    [InlineData("lora.vocabulary.wardrobe.unclothed")]
    [InlineData("lora.vocabulary.pose.lying")]
    [InlineData("lora.vocabulary.background.kitchen")]
    public void AnAxisWithoutPresets_IsRefusedByName(string vocabularyKey)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ImagePresetKeys.PresetKeyFor(vocabularyKey));

        Assert.Contains(vocabularyKey, error.Message, StringComparison.Ordinal);
    }

    private static (ImageWorkflowTemplateService service, ImageWorkflowRepository repo, string dbPath) CreateService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"image-presets-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repo = new ImageWorkflowRepository(options);
        repo.EnsureSchemaAsync().GetAwaiter().GetResult();
        return (new ImageWorkflowTemplateService(repo), repo, dbPath);
    }

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
