using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The LoRA dataset's prompts are app data, not code strings: every one of them is a seeded, editable row
/// in the same store the face and body pipelines use. These tests hold two lines that matter more than the
/// rest — that no key is missing (a missing key must fail loudly, never quietly), and that a caption can
/// never name an invariant feature, because naming one is exactly what stops identity binding to the
/// trigger token.
/// </summary>
public sealed class CharacterLoraCellTemplateSeedTests
{
    [Fact]
    public async Task EveryLoraKey_Resolves()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var missing = new List<string>();
            foreach (var key in LoraCellWorkflowKeys.All)
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

    /// <summary>The twelve render templates are one per angle family × framing, and each encodes its own framing.</summary>
    [Fact]
    public void RenderKeys_AreTheTwelveFamilyFramingCombinations()
    {
        var expected = new[]
        {
            "lora.cell.render.front.close", "lora.cell.render.front.half", "lora.cell.render.front.full",
            "lora.cell.render.threequarter.close", "lora.cell.render.threequarter.half", "lora.cell.render.threequarter.full",
            "lora.cell.render.profile.close", "lora.cell.render.profile.half", "lora.cell.render.profile.full",
            "lora.cell.render.behind.close", "lora.cell.render.behind.half", "lora.cell.render.behind.full"
        };

        Assert.Equal(expected, LoraCellWorkflowKeys.RenderKeys);

        foreach (var family in Enum.GetValues<LoraCoverageAngleFamily>())
        {
            foreach (var distance in Enum.GetValues<LoraCoverageDistance>())
            {
                Assert.Contains(LoraCellWorkflowKeys.RenderKey(family, distance), expected);
            }
        }
    }

    /// <summary>
    /// The render templates must be slot-driven. A template that hard-codes a lighting or background word
    /// would be a prompt decision living in code, and it would silently win over the cell's own axes.
    /// </summary>
    [Fact]
    public async Task RenderTemplates_UseSlotsRatherThanWordsOfTheirOwn()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            foreach (var key in LoraCellWorkflowKeys.RenderKeys)
            {
                var body = (await service.ResolveAsync(key, null)).Body;

                foreach (var slot in new[] { "{BodyCard}", "{Facing}", "{Wardrobe}", "{Pose}", "{Expression}", "{Lighting}", "{Background}" })
                {
                    Assert.Contains(slot, body, StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// A training image is of a person under stated conditions, so the render prompt must not name the
    /// character: identity in a training image comes from the trigger token and the references, and a name
    /// would bind the look to a word the caption is forbidden to contain.
    /// </summary>
    [Fact]
    public async Task RenderTemplates_DoNotNameTheCharacter()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            foreach (var key in LoraCellWorkflowKeys.RenderKeys)
            {
                var body = (await service.ResolveAsync(key, null)).Body;
                Assert.DoesNotContain("{CharacterName}", body, StringComparison.Ordinal);
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// The caption template's first token is the trigger, and it carries no invariant slot at all. If a
    /// future edit adds one, identity stops binding to the trigger and starts depending on a description
    /// the model can vary — the exact failure the invariant/variable split exists to prevent.
    /// </summary>
    [Theory]
    [InlineData("body shape")]
    [InlineData("proportions")]
    [InlineData("skin tone")]
    [InlineData("body hair")]
    [InlineData("pubic")]
    [InlineData("tattoo")]
    [InlineData("scar")]
    [InlineData("piercing")]
    [InlineData("birthmark")]
    [InlineData("{BodyCard}")]
    public async Task CaptionTemplate_NeverNamesAnInvariant(string forbidden)
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var body = (await service.ResolveAsync(LoraCellWorkflowKeys.Caption, null)).Body;

            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task CaptionTemplate_StartsWithTheTriggerToken()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var body = (await service.ResolveAsync(LoraCellWorkflowKeys.Caption, null)).Body;

            Assert.StartsWith("{TriggerToken}", body, StringComparison.Ordinal);
            // Comma-separated, because a trainer can shuffle the tail only if the tags are separate words.
            Assert.Contains(", {Wardrobe}", body, StringComparison.Ordinal);
            Assert.Contains("{Angle}", body, StringComparison.Ordinal);
            Assert.Contains("{Distance}", body, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>The vocabulary is wording, so every row must actually read as a phrase rather than a key.</summary>
    [Fact]
    public async Task VocabularyRows_ArePhrasesNotKeys()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            foreach (var key in LoraCellWorkflowKeys.VocabularyKeys)
            {
                var body = (await service.ResolveAsync(key, null)).Body;

                Assert.False(string.IsNullOrWhiteSpace(body));
                Assert.DoesNotContain("lora.vocabulary", body, StringComparison.Ordinal);
                // A phrase, not a sentence with a full stop: these get pasted into prompts and captions.
                Assert.DoesNotContain(".", body, StringComparison.Ordinal);
            }
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>Re-running the seed must not duplicate rows, and must not overwrite a body somebody edited.</summary>
    [Fact]
    public async Task Reseeding_NeitherDuplicatesNorOverwritesAnEditedBody()
    {
        var (service, repo, dbPath) = CreateService();
        try
        {
            var original = await service.ResolveAsync(LoraCellWorkflowKeys.Caption, null);
            original.Body = "{TriggerToken}, edited by hand";
            await service.SaveTemplateAsync(original);

            // A second EnsureSchemaAsync is what a restart of the app does.
            await repo.EnsureSchemaAsync();

            var afterRestart = await service.ResolveAsync(LoraCellWorkflowKeys.Caption, null);
            Assert.Equal("{TriggerToken}, edited by hand", afterRestart.Body);
            Assert.NotEqual(afterRestart.Body, afterRestart.SeedBody);

            var all = await repo.ListTemplatesAsync();
            var captionRows = all.Where(row => row.Key == LoraCellWorkflowKeys.Caption).ToList();
            Assert.Single(captionRows);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>A character override wins over the seeded global row, and resetting puts the seed back.</summary>
    [Fact]
    public async Task CharacterOverride_WinsAndResetsToTheSeed()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            await service.SaveTemplateAsync(new ImageWorkflowPromptTemplate
            {
                Key = LoraCellWorkflowKeys.VocabularyLightingIndoorDim,
                Scope = ImageWorkflowPromptTemplateScope.Character,
                CharacterProfileId = "char-1",
                WorkflowStep = "LoraCellVocabulary",
                Body = "candlelight",
                SeedBody = "candlelight"
            });

            Assert.Equal("candlelight", (await service.ResolveAsync(LoraCellWorkflowKeys.VocabularyLightingIndoorDim, "char-1")).Body);
            Assert.Equal("dim indoor lighting with soft shadows",
                (await service.ResolveAsync(LoraCellWorkflowKeys.VocabularyLightingIndoorDim, "char-2")).Body);

            var reset = await service.ResetToSeedAsync(
                LoraCellWorkflowKeys.VocabularyLightingIndoorDim,
                ImageWorkflowPromptTemplateScope.Global,
                null);
            Assert.Equal(reset.SeedBody, reset.Body);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>The facing phrase is derived from the cell's face-visibility and yaw, so the two cannot disagree.</summary>
    [Theory]
    [InlineData(true, 0, LoraCellWorkflowKeys.VocabularyFacingCamera)]
    [InlineData(true, -45, LoraCellWorkflowKeys.VocabularyFacingLeft)]
    [InlineData(true, -90, LoraCellWorkflowKeys.VocabularyFacingLeft)]
    [InlineData(true, 45, LoraCellWorkflowKeys.VocabularyFacingRight)]
    [InlineData(true, 90, LoraCellWorkflowKeys.VocabularyFacingRight)]
    [InlineData(false, 180, LoraCellWorkflowKeys.VocabularyFacingAway)]
    public void FacingKey_FollowsFaceVisibilityAndYaw(bool faceVisible, int yaw, string expected)
    {
        Assert.Equal(expected, LoraCellWorkflowKeys.FacingKey(faceVisible, yaw));
    }

    /// <summary>Every axis-value mapping lands on a key that is actually seeded, not on a name that looks right.</summary>
    [Fact]
    public void AxisMappings_AllLandOnSeededKeys()
    {
        foreach (var state in Enum.GetValues<LoraCoverageWardrobeState>())
        {
            Assert.Contains(LoraCellWorkflowKeys.WardrobeKey(state), LoraCellWorkflowKeys.VocabularyKeys);
        }

        foreach (var pose in Enum.GetValues<LoraCoveragePoseClass>())
        {
            Assert.Contains(LoraCellWorkflowKeys.PoseKey(pose), LoraCellWorkflowKeys.VocabularyKeys);
        }

        foreach (var distance in Enum.GetValues<LoraCoverageDistance>())
        {
            Assert.Contains(LoraCellWorkflowKeys.DistanceKey(distance), LoraCellWorkflowKeys.VocabularyKeys);
        }

        foreach (var family in Enum.GetValues<LoraCoverageAngleFamily>())
        {
            Assert.Contains(LoraCellWorkflowKeys.AngleKey(family), LoraCellWorkflowKeys.VocabularyKeys);
        }

        foreach (var split in Enum.GetValues<CharacterLoraDatasetSplit>())
        {
            Assert.Contains(LoraCellWorkflowKeys.SplitKey(split), LoraCellWorkflowKeys.VocabularyKeys);
        }
    }

    /// <summary>An unsupported enum value must be refused by name rather than mapped to some default axis.</summary>
    [Fact]
    public void AxisMappings_RefuseAnUnsupportedValue()
    {
        Assert.Throws<InvalidOperationException>(() => LoraCellWorkflowKeys.WardrobeKey((LoraCoverageWardrobeState)99));
        Assert.Throws<InvalidOperationException>(() => LoraCellWorkflowKeys.PoseKey((LoraCoveragePoseClass)99));
        Assert.Throws<InvalidOperationException>(() => LoraCellWorkflowKeys.DistanceKey((LoraCoverageDistance)99));
        Assert.Throws<InvalidOperationException>(() => LoraCellWorkflowKeys.AngleKey((LoraCoverageAngleFamily)99));
        Assert.Throws<InvalidOperationException>(() => LoraCellWorkflowKeys.SplitKey((CharacterLoraDatasetSplit)99));
        Assert.Throws<InvalidOperationException>(() => LoraCellWorkflowKeys.RenderKey((LoraCoverageAngleFamily)99, LoraCoverageDistance.CloseUp));
        Assert.Throws<InvalidOperationException>(() => LoraCellWorkflowKeys.RenderKey(LoraCoverageAngleFamily.Front, (LoraCoverageDistance)99));
    }

    private static (ImageWorkflowTemplateService service, ImageWorkflowRepository repo, string dbPath) CreateService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"lora-cell-templates-{Guid.NewGuid():N}.db");
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
