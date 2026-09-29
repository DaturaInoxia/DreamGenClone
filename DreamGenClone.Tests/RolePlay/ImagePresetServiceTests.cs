using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Editing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The preset resolver turns a picked key into an instruction, using rows the operator can edit (B-133). These tests
/// hold the two things that make it trustworthy: the wording comes from the store - including a character's own
/// override - and the change and condition modes differ in exactly the way the design says (the change carries the
/// preserve clause, the condition clause does not).
/// </summary>
public sealed class ImagePresetServiceTests
{
    [Fact]
    public async Task ChangeInstruction_CarriesTheStoredDetailAndThePreserveClause()
    {
        using var fixture = CreateService();

        var instruction = await fixture.Service.ResolveInstructionAsync(ImagePresetKeys.LightingIndoorDim);

        // From the seeded detail, not paraphrased by anything.
        Assert.Contains("just outside the frame", instruction, StringComparison.Ordinal);
        Assert.Contains("fall into deep shadow", instruction, StringComparison.Ordinal);
        Assert.Contains("Relight this photograph", instruction, StringComparison.Ordinal);
        // And the clause that stops the edit re-rendering the person.
        Assert.Contains("Keep the person identical", instruction, StringComparison.Ordinal);
        Assert.Contains("only the lighting changes", instruction, StringComparison.Ordinal);
    }

    /// <summary>A compose clause conditions a render that has not happened yet: nothing exists to preserve.</summary>
    [Fact]
    public async Task ConditionClause_CarriesTheDetailAndNothingToPreserve()
    {
        using var fixture = CreateService();

        var clause = await fixture.Service.ResolveInstructionAsync(
            ImagePresetKeys.LightingIndoorDim, ImagePresetMode.Condition);

        Assert.StartsWith("The scene is lit by", clause, StringComparison.Ordinal);
        Assert.Contains("just outside the frame", clause, StringComparison.Ordinal);
        Assert.DoesNotContain("Keep the person identical", clause, StringComparison.Ordinal);
    }

    /// <summary>Each axis protects its own things: an expression edit must not be told only the lighting changes.</summary>
    [Fact]
    public async Task ExpressionInstruction_UsesTheExpressionPreserveClause()
    {
        using var fixture = CreateService();

        var instruction = await fixture.Service.ResolveInstructionAsync(ImagePresetKeys.ExpressionAngry);

        Assert.Contains("vertical creases between them", instruction, StringComparison.Ordinal);
        Assert.Contains("only the facial expression changes", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("only the lighting changes", instruction, StringComparison.Ordinal);
    }

    /// <summary>A character can relight its own images in its own words, and the override is what gets used.</summary>
    [Fact]
    public async Task CharacterOverride_WinsOverTheSeededDetail()
    {
        using var fixture = CreateService();

        await fixture.Templates.SaveTemplateAsync(new ImageWorkflowPromptTemplate
        {
            Key = ImagePresetKeys.LightingIndoorDim,
            Scope = ImageWorkflowPromptTemplateScope.Character,
            CharacterProfileId = "becky-template",
            WorkflowStep = "ImagePresetLighting",
            Body = "a single candle on a side table, everything beyond it in shadow",
            SeedBody = "a single candle on a side table, everything beyond it in shadow"
        });

        var forBecky = await fixture.Service.ResolveInstructionAsync(
            ImagePresetKeys.LightingIndoorDim, ImagePresetMode.Change, "becky-template");
        var forAnyone = await fixture.Service.ResolveInstructionAsync(ImagePresetKeys.LightingIndoorDim);

        Assert.Contains("a single candle on a side table", forBecky, StringComparison.Ordinal);
        Assert.DoesNotContain("a single candle on a side table", forAnyone, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_ReturnsBothAxesWithTheirDetails()
    {
        using var fixture = CreateService();

        var lighting = await fixture.Service.ListAsync(ImagePresetAxis.Lighting);
        var expression = await fixture.Service.ListAsync(ImagePresetAxis.Expression);
        var all = await fixture.Service.ListAsync();

        Assert.Equal(6, lighting.Count);
        Assert.Equal(20, expression.Count);
        Assert.Equal(26, all.Count);
        Assert.All(all, choice => Assert.False(string.IsNullOrWhiteSpace(choice.Detail)));
        Assert.All(all, choice => Assert.False(string.IsNullOrWhiteSpace(choice.ShortName)));
        Assert.DoesNotContain(lighting, choice => choice.Axis == ImagePresetAxis.Expression);
        Assert.Contains(expression, choice => choice.ShortName == "angry");
    }

    /// <summary>The host default is a key mapping, and an axis with no presets is refused rather than approximated.</summary>
    [Fact]
    public void DefaultPresetFor_MapsTheMatrixAxesAndRefusesTheOthers()
    {
        using var fixture = CreateService();

        Assert.Equal(
            ImagePresetKeys.LightingHardRim,
            fixture.Service.DefaultPresetFor(LoraCellWorkflowKeys.VocabularyLightingHardRim));
        Assert.Equal(
            ImagePresetKeys.ExpressionSensual,
            fixture.Service.DefaultPresetFor(LoraCellWorkflowKeys.VocabularyExpressionSensual));

        var error = Assert.Throws<InvalidOperationException>(
            () => fixture.Service.DefaultPresetFor(LoraCellWorkflowKeys.VocabularyOutfitCasual));
        Assert.Contains(LoraCellWorkflowKeys.VocabularyOutfitCasual, error.Message, StringComparison.Ordinal);
    }

    private static Fixture CreateService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"image-preset-service-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var repo = new ImageWorkflowRepository(options);
        repo.EnsureSchemaAsync().GetAwaiter().GetResult();
        var templates = new ImageWorkflowTemplateService(repo);
        return new Fixture(new ImagePresetService(templates), templates, dbPath);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _dbPath;

        public Fixture(ImagePresetService service, ImageWorkflowTemplateService templates, string dbPath)
        {
            Service = service;
            Templates = templates;
            _dbPath = dbPath;
        }

        public ImagePresetService Service { get; }

        public ImageWorkflowTemplateService Templates { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = _dbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
