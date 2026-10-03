using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ImageWorkflowTemplateServiceTests
{
    [Fact]
    public async Task Seed_ResolvesGlobalTemplate()
    {
        var (service, repo, dbPath) = CreateService();
        try
        {
            var resolved = await service.ResolveAsync("identity.front.generate", null);
            Assert.Equal(ImageWorkflowPromptTemplateScope.Global, resolved.Scope);
            Assert.Contains("{CharacterName}", resolved.Body, StringComparison.Ordinal);
            Assert.Equal(resolved.Body, resolved.SeedBody);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task CharacterOverride_Wins()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            await service.SaveTemplateAsync(new ImageWorkflowPromptTemplate
            {
                Key = "identity.front.generate",
                Scope = ImageWorkflowPromptTemplateScope.Character,
                CharacterProfileId = "char-1",
                WorkflowStep = "Front",
                Body = "OVERRIDE",
                SeedBody = "OVERRIDE"
            });

            var resolved = await service.ResolveAsync("identity.front.generate", "char-1");
            Assert.Equal("OVERRIDE", resolved.Body);
            Assert.Equal(ImageWorkflowPromptTemplateScope.Character, resolved.Scope);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task CharacterOverride_DoesNotAlterGlobalRow()
    {
        var (service, repo, dbPath) = CreateService();
        try
        {
            var globalBefore = await service.ResolveAsync("identity.front.generate", null);
            await service.SaveTemplateAsync(new ImageWorkflowPromptTemplate
            {
                Key = "identity.front.generate",
                Scope = ImageWorkflowPromptTemplateScope.Character,
                CharacterProfileId = "char-1",
                WorkflowStep = "Front",
                Body = "OVERRIDE",
                SeedBody = "OVERRIDE"
            });

            var globalAfter = await service.ResolveAsync("identity.front.generate", null);
            Assert.Equal(globalBefore.Body, globalAfter.Body);
            Assert.Equal(globalBefore.SeedBody, globalAfter.SeedBody);

            // No fallback to the character row when resolving without a character id.
            Assert.DoesNotContain("OVERRIDE", globalAfter.Body, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task MissingRow_FailsFast_NamingKey()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ResolveAsync("identity.unknown.step", null));
            Assert.Contains("identity.unknown.step", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ResetGlobalToSeed_RestoresSeedBody()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var global = await service.ResolveAsync("identity.angle.profile", null);
            var seed = global.SeedBody;

            global.Body = "USER-EDITED TEXT";
            await service.SaveTemplateAsync(global);

            var reset = await service.ResetToSeedAsync("identity.angle.profile", ImageWorkflowPromptTemplateScope.Global, null);
            Assert.Equal(seed, reset.Body);
            Assert.Equal(seed, reset.SeedBody);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ReSeeding_DoesNotOverwriteEditedBody()
    {
        var (service, repo, dbPath) = CreateService();
        try
        {
            var global = await service.ResolveAsync("identity.garment.remove", null);
            global.Body = "USER-EDITED";
            await service.SaveTemplateAsync(global);

            await repo.EnsureSchemaAsync();

            var reloaded = await service.ResolveAsync("identity.garment.remove", null);
            Assert.Equal("USER-EDITED", reloaded.Body);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Settings_GlobalSeed_ResolvesDefaults()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var settings = await service.ResolveSettingsAsync(null);
            Assert.Equal(1024, settings.EnhanceTargetLongEdge);
            Assert.Equal(1.5, settings.EyeGateMaxAbsIrisDyPercent);
            Assert.Equal(250, settings.QualityGateMinSharpness);
            Assert.Equal(8, settings.CropHeadroomPercent);
            Assert.Equal(1.0, settings.CropTargetAspect);
            Assert.True(settings.DeriveByMirrorThreeQuarterRight);
            Assert.True(settings.DeriveByMirrorProfileRight);
            Assert.Null(settings.EditorModelId);
            Assert.Null(settings.EyeToolPythonPath);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// The region mask's geometry is persisted configuration exactly as the crop's is: it has a seeded starting value,
    /// it survives a round trip, and a row missing it fails fast by key. The bounds match what the graph can honour -
    /// the encode node takes a grow of 0-64, and a negative feather is a mistake rather than a softening. A feather of
    /// zero is a legal ROW (the row is shared with flows that never confine anything) and is refused by a confined edit
    /// instead, because the host rounds the mask it confines with to 0/1 and a zero feather therefore leaves a visible
    /// rectangle edge (CASE-25).
    /// </summary>
    [Fact]
    public async Task Settings_RegionMaskGeometry_IsSeededRoundTripsAndFailsFastWhenMissing()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var seeded = await service.ResolveSettingsAsync(null);
            Assert.Equal(8, seeded.RegionGrowMaskBy);
            Assert.Equal(0, seeded.RegionFeatherPixels);

            await service.SaveSettingsAsync(new ReferenceWorkflowSettings
            {
                CharacterProfileId = "char-region",
                UpscalerModelName = seeded.UpscalerModelName,
                EnhanceTargetLongEdge = seeded.EnhanceTargetLongEdge,
                EyeGateMaxAbsIrisDyPercent = seeded.EyeGateMaxAbsIrisDyPercent,
                AngleYawMinAbsPercent = seeded.AngleYawMinAbsPercent,
                QualityGateMinSharpness = seeded.QualityGateMinSharpness,
                CropHeadroomPercent = seeded.CropHeadroomPercent,
                CropTargetAspect = seeded.CropTargetAspect,
                RegionGrowMaskBy = 16,
                RegionFeatherPixels = 6
            });

            var saved = await service.ResolveSettingsAsync("char-region");
            Assert.Equal(16, saved.RegionGrowMaskBy);
            Assert.Equal(6, saved.RegionFeatherPixels);

            var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
                new ReferenceWorkflowSettings
                {
                    CharacterProfileId = "char-no-region",
                    EnhanceTargetLongEdge = 1024,
                    CropHeadroomPercent = 8,
                    CropTargetAspect = 1.0
                }));
            Assert.Contains("RegionGrowMaskBy", missing.Message, StringComparison.Ordinal);

            var growTooLarge = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
                new ReferenceWorkflowSettings
                {
                    CharacterProfileId = "char-grow-too-large",
                    EnhanceTargetLongEdge = 1024,
                    CropHeadroomPercent = 8,
                    CropTargetAspect = 1.0,
                    RegionGrowMaskBy = 65,
                    RegionFeatherPixels = 0
                }));
            Assert.Contains("RegionGrowMaskBy", growTooLarge.Message, StringComparison.Ordinal);

            var negativeFeather = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
                new ReferenceWorkflowSettings
                {
                    CharacterProfileId = "char-bad-feather",
                    EnhanceTargetLongEdge = 1024,
                    CropHeadroomPercent = 8,
                    CropTargetAspect = 1.0,
                    RegionGrowMaskBy = 8,
                    RegionFeatherPixels = -1
                }));
            Assert.Contains("RegionFeatherPixels", negativeFeather.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// The crop/enhance values have no code default: persisting a row without them must fail fast naming
    /// the missing key, so a value nobody chose can never steer the crop or the enhance size.
    /// </summary>
    [Fact]
    public async Task Settings_WithoutTheRequiredCropAndEnhanceValues_FailsFastNamingTheKey()
    {
        var (service, _, dbPath) = CreateService();
        try
        {
            var enhanceError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SaveSettingsAsync(new ReferenceWorkflowSettings { CharacterProfileId = "char-1" }));
            Assert.Contains("EnhanceTargetLongEdge", enhanceError.Message, StringComparison.Ordinal);

            var headroomError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SaveSettingsAsync(new ReferenceWorkflowSettings
                {
                    CharacterProfileId = "char-1",
                    EnhanceTargetLongEdge = 1024
                }));
            Assert.Contains("CropHeadroomPercent", headroomError.Message, StringComparison.Ordinal);

            var aspectError = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SaveSettingsAsync(new ReferenceWorkflowSettings
                {
                    CharacterProfileId = "char-1",
                    EnhanceTargetLongEdge = 1024,
                    CropHeadroomPercent = 8
                }));
            Assert.Contains("CropTargetAspect", aspectError.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static (ImageWorkflowTemplateService service, ImageWorkflowRepository repo, string dbPath) CreateService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"image-workflow-{Guid.NewGuid():N}.db");
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
