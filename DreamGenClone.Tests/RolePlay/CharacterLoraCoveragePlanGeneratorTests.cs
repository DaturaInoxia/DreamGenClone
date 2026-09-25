using System.Text.RegularExpressions;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The generator is what turns "make a LoRA for this character" into a checklist an operator can actually
/// work through. These tests pin the matrix it must produce, the diversity it must guarantee, that it is
/// deterministic, and — the one that matters most — that it plans and never runs: there is no batch, sweep
/// or "generate N" path anywhere in it.
/// </summary>
public sealed class CharacterLoraCoveragePlanGeneratorTests
{
    [Fact]
    public async Task Generate_ProducesTheConfiguredMatrix()
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            Assert.Equal(36, plan.Records.Count);
            Assert.Equal(30, plan.Records.Count(record => record.Role == LoraCoverageCellRole.Core));
            Assert.Equal(6, plan.Records.Count(record => record.Role == LoraCoverageCellRole.Variation));
            Assert.Equal(30, world.Policy.ExpectedCoreCellCount);
            Assert.Equal(6, world.Policy.ExpectedVariationCellCount);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>The per-angle, per-distance counts are the capture matrix: profiles and angled views weighted highest.</summary>
    [Theory]
    [InlineData(LoraCoverageAngleFamily.Front, 6)]
    [InlineData(LoraCoverageAngleFamily.ThreeQuarter, 10)]
    [InlineData(LoraCoverageAngleFamily.Profile, 12)]
    [InlineData(LoraCoverageAngleFamily.Behind, 2)]
    public async Task Generate_MatchesTheCaptureMatrixPerAngle(LoraCoverageAngleFamily family, int expected)
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            var core = plan.Records.Where(record => record.Role == LoraCoverageCellRole.Core).ToList();
            Assert.Equal(expected, core.Count(record => record.AngleFamily == family));
        }
        finally
        {
            world.Dispose();
        }
    }

    [Theory]
    [InlineData(LoraCoverageDistance.CloseUp, 8)]
    [InlineData(LoraCoverageDistance.HalfBody, 11)]
    [InlineData(LoraCoverageDistance.FullBody, 11)]
    public async Task Generate_MatchesTheCaptureMatrixPerDistance(LoraCoverageDistance distance, int expected)
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            var core = plan.Records.Where(record => record.Role == LoraCoverageCellRole.Core).ToList();
            Assert.Equal(expected, core.Count(record => record.Distance == distance));
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>Both sides of the compass are present, and only the behind pair has no face reference.</summary>
    [Fact]
    public async Task Generate_CoversEveryFaceReferenceExactlyWhereAFaceExists()
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            foreach (var slot in Enum.GetValues<SceneImageReferenceFaceView>())
            {
                Assert.Contains(plan.Records, record => record.FaceCanonicalSlot == slot);
            }

            var withoutFace = plan.Records.Where(record => !record.FaceVisible).ToList();
            Assert.Equal(2, withoutFace.Count);
            Assert.All(withoutFace, record =>
            {
                Assert.Null(record.FaceCanonicalSlot);
                Assert.Equal(SceneImageReferenceBodyView.Back, record.BodyCanonicalSlot);
                Assert.Equal(LoraCoverageAngleFamily.Behind, record.AngleFamily);
            });

            // Every face-bearing cell names the body reference its wardrobe state needs.
            Assert.All(plan.Records.Where(record => record.FaceVisible), record =>
                Assert.Equal(
                    record.WardrobeState == LoraCoverageWardrobeState.Clothed
                        ? SceneImageReferenceBodyState.Clothed
                        : SceneImageReferenceBodyState.Unclothed,
                    record.BodyState));
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Generate_KeepsOneUniqueSeedPerCellInsideTheConfiguredRange()
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            var seeds = plan.Records.Select(record => record.Seed).ToList();
            Assert.Equal(seeds.Count, seeds.Distinct().Count());
            Assert.Equal(world.Policy.SeedRangeStart, seeds.Min());
            Assert.True(seeds.Max() < world.Policy.SeedRangeStart + world.Policy.SeedRangeLength);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// Wardrobe balance is not cosmetic: too much nude strips clothing from the trained model, too much
    /// clothed and it refuses to undress. The plan lands on 50/50 exactly.
    /// </summary>
    [Fact]
    public async Task Generate_BalancesClothedAndUnclothedHalfAndHalf()
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            var unclothed = plan.Records.Count(record => record.WardrobeState == LoraCoverageWardrobeState.Unclothed);
            Assert.Equal(plan.Records.Count / 2, unclothed);

            // ...and every angle group holds both states, so no view teaches only one.
            foreach (var group in plan.Records.GroupBy(record => record.AngleFamily))
            {
                Assert.Contains(group, record => record.WardrobeState == LoraCoverageWardrobeState.Clothed);
                Assert.Contains(group, record => record.WardrobeState == LoraCoverageWardrobeState.Unclothed);
            }
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Generate_MeetsEveryDiversityMinimum()
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            Assert.True(plan.Records.Select(record => record.OutfitKey).Distinct().Count() >= world.Policy.MinimumDistinctOutfits);
            Assert.True(plan.Records.Select(record => record.BackgroundKey).Distinct().Count() >= world.Policy.MinimumDistinctBackgrounds);
            Assert.True(plan.Records.Select(record => record.LightingKey).Distinct().Count() >= world.Policy.MinimumDistinctLighting);
            Assert.True(plan.Records.Select(record => record.ExpressionKey).Distinct().Count() >= world.Policy.MinimumDistinctExpressions);
            Assert.True(plan.Records.Select(record => record.PoseClass).Distinct().Count() >= world.Policy.MinimumPoseClasses);
            Assert.Empty(plan.DescribeGaps(world.Policy));
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// The set must not be thirty standing frames, and two frames of the same view at the same distance — the
    /// cells an operator shoots back to back — may never share a stance.
    /// </summary>
    [Fact]
    public async Task Generate_VariesThePoseClassAndNeverRepeatsItSideBySide()
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            var used = plan.Records.Select(record => record.PoseClass).Distinct().ToList();
            Assert.True(used.Count >= 4, $"expected at least four pose classes, saw {used.Count}");

            var core = plan.Records.Where(record => record.Role == LoraCoverageCellRole.Core);
            foreach (var series in core.GroupBy(record => new { record.AngleYawDeg, record.Distance }))
            {
                var stances = series.Select(record => record.PoseClass).ToList();
                Assert.Equal(stances.Count, stances.Distinct().Count());
            }
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Generate_HoldsOutTheVariationCellsAndTheBehindPair()
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            var validation = plan.Records
                .Where(record => record.Split == CharacterLoraDatasetSplit.Validation).ToList();

            Assert.Equal(8, validation.Count);
            Assert.All(
                validation,
                record => Assert.True(
                    record.Role == LoraCoverageCellRole.Variation || record.AngleFamily == LoraCoverageAngleFamily.Behind));

            // Everything else trains. A holdout that swallows the whole matrix would prove nothing.
            Assert.Equal(28, plan.Records.Count(record => record.Split == CharacterLoraDatasetSplit.Train));
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Generate_UsesTheConfiguredAspectPerDistance()
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            Assert.All(plan.Records, record => Assert.Equal(
                record.Distance == LoraCoverageDistance.CloseUp ? world.Policy.CloseUpAspect : world.Policy.PortraitAspect,
                record.Aspect));
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>Two runs of the same request are the same plan: a shoot spread over weeks depends on it.</summary>
    [Fact]
    public async Task Generate_IsDeterministicApartFromItsTimestamp()
    {
        var world = await World.CreateAsync();
        try
        {
            var first = await world.GenerateAsync();
            var second = await world.GenerateAsync();

            first.GeneratedUtc = second.GeneratedUtc;
            Assert.Equal(first.ToJson(), second.ToJson());
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>The phrases are snapshotted, and they are the phrases the store holds — not paraphrases.</summary>
    [Fact]
    public async Task Generate_SnapshotsTheVocabularyFromTheStore()
    {
        var world = await World.CreateAsync();
        try
        {
            var plan = await world.GenerateAsync();

            Assert.Equal(
                "completely unclothed, with no clothing at all and nothing covering the body",
                plan.PhraseFor(LoraCellWorkflowKeys.VocabularyOutfitUnclothed));
            Assert.Equal("lying down", plan.PhraseFor(LoraCellWorkflowKeys.VocabularyPoseLying));

            // Every phrase the plan's cells refer to resolves from the snapshot — that is what makes the plan
            // readable on its own after the store is edited.
            foreach (var record in plan.Records)
            {
                foreach (var key in new[] { record.ExpressionKey, record.LightingKey, record.BackgroundKey, record.OutfitKey })
                {
                    Assert.False(string.IsNullOrWhiteSpace(plan.PhraseFor(key)));
                }

                Assert.False(string.IsNullOrWhiteSpace(plan.PhraseFor(LoraCellWorkflowKeys.FacingKey(record.FaceVisible, record.AngleYawDeg))));
            }
        }
        finally
        {
            world.Dispose();
        }
    }

    // ------------------------------------------------------------------ fail-fast cases

    /// <summary>
    /// A face-only pack has no unclothed body reference, so half the matrix would be rendered from text alone.
    /// It must be refused by scope, naming the pack.
    /// </summary>
    [Fact]
    public async Task Generate_RefusesAFaceOnlyPack()
    {
        var world = await World.CreateAsync();
        try
        {
            var request = world.Request() with { IdentityPackScope = CharacterImageIdentityPackScope.FaceOnly };

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Generator.GenerateAsync(request));

            Assert.Contains("BodyComplete", error.Message, StringComparison.Ordinal);
            Assert.Contains(world.PackId, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Generate_RefusesAnEmptyTriggerToken(string token)
    {
        var world = await World.CreateAsync();
        try
        {
            var request = world.Request() with { TriggerToken = token };

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => world.Generator.GenerateAsync(request));

            Assert.Contains("trigger token", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            world.Dispose();
        }
    }

    [Fact]
    public async Task Generate_RefusesAnIdentityPackVersionThatIsNotPositive()
    {
        var world = await World.CreateAsync();
        try
        {
            var request = world.Request() with { IdentityPackVersion = 0 };

            await Assert.ThrowsAsync<InvalidOperationException>(() => world.Generator.GenerateAsync(request));
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// A policy row whose payload is incomplete must stop the plan, naming the value it lacks. The seeded row is
    /// written on every open, so an absent row is no longer reachable in production — but a row that was written by
    /// an older build, or edited by hand, is, and it must refuse rather than let a gate run on a guess.
    /// </summary>
    [Fact]
    public async Task Generate_RefusesToPlanWhenThePolicyRowIsIncomplete()
    {
        var world = await World.CreateAsync();
        try
        {
            world.WriteRawGlobalPolicy("{\"expectedCoreCellCount\":30}");

            var error = await Assert.ThrowsAnyAsync<Exception>(() => world.GenerateAsync());

            // The point of the `required` members: the failure names the policy type and says a value is missing,
            // rather than letting a gate run on a threshold nobody set.
            Assert.Contains("CurationPolicy", error.Message, StringComparison.Ordinal);
            Assert.Contains("required", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// And the thresholds themselves are never invisible: the seeded row arrives with the store, so a fresh
    /// database can plan without anyone having configured anything first.
    /// </summary>
    [Fact]
    public async Task Generate_WorksOnAFreshStore_WithNoExplicitEnsure()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"lora-plan-fresh-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        try
        {
            var templates = new ImageWorkflowRepository(options);
            var repository = new CharacterLoraRepository(options);
            var generator = new CharacterLoraCoveragePlanGenerator(
                repository, new ImageWorkflowTemplateService(templates));

            var plan = await generator.GenerateAsync(new CoveragePlanRequest(
                "character-1", "pack-1", 9, CharacterImageIdentityPackScope.BodyComplete, "ohwx-becky", "biglust"));

            Assert.Equal(36, plan.Records.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    /// <summary>
    /// A policy that allocates fewer seeds than the plan needs is refused rather than wrapping a seed around
    /// and shooting two cells with the same noise. The policy store refuses it, so no plan is ever attempted.
    /// </summary>
    [Fact]
    public async Task Policy_SeedRangeThatCannotCoverThePlan_IsRefusedOnSave()
    {
        var world = await World.CreateAsync();
        try
        {
            var policy = world.Policy;
            policy.SeedRangeLength = 20;

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.SavePolicyAsync(policy));

            Assert.Contains("SeedRangeLength", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>Changing a configured minimum changes the verdict, which is how we know the number is read and not assumed.</summary>
    [Fact]
    public async Task Generate_RefusesWhenTheConfiguredMinimaDisagreeWithTheMatrix()
    {
        var world = await World.CreateAsync();
        try
        {
            var policy = world.Policy;
            policy.MinimumDistinctBackgrounds = 40;
            await world.SavePolicyAsync(policy);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => world.GenerateAsync());

            Assert.Contains("backgrounds", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// The seeded thresholds have to arrive with the store, not only when something calls an explicit "ensure" first.
    /// The 2026-09-25 failure was exactly that asymmetry: a read created the table (and the app never called ensure),
    /// so the row was absent and the LoRA images tab died on it. This test opens the store through a *different*
    /// call first, then reads the policy.
    /// </summary>
    [Fact]
    public async Task Policy_IsSeededByTheFirstOpen_NotOnlyByAnExplicitEnsure()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"lora-policy-seed-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        try
        {
            var repository = new CharacterLoraRepository(options);

            // No EnsureSchemaAsync: the very first touch of this store is an ordinary read.
            Assert.Empty(await repository.ListTrainingProfilesAsync());

            var policy = await repository.ResolveCurationPolicyAsync(null);

            Assert.Equal(30, policy.ExpectedCoreCellCount);
            Assert.Equal(41000, policy.SeedRangeStart);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    /// <summary>A character row overrides the global one, and removing it falls back to global — the only two sources.</summary>
    [Fact]
    public async Task Policy_CharacterRowOverridesGlobal()
    {
        var world = await World.CreateAsync();
        try
        {
            var characterPolicy = world.Policy;
            characterPolicy.SeedRangeStart = 51000;
            await world.SavePolicyAsync(characterPolicy, "character-1");

            var resolved = await world.Repository.ResolveCurationPolicyAsync("character-1");
            Assert.Equal(51000, resolved.SeedRangeStart);

            var other = await world.Repository.ResolveCurationPolicyAsync("character-2");
            Assert.Equal(41000, other.SeedRangeStart);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>Resetting restores the seed, and re-running the schema never overwrites an edited row.</summary>
    [Fact]
    public async Task Policy_ResetRestoresTheSeedAndRestartKeepsTheEdit()
    {
        var world = await World.CreateAsync();
        try
        {
            var edited = world.Policy;
            edited.MinimumPoseClasses = 5;
            await world.SavePolicyAsync(edited);
            await world.Repository.EnsureSchemaAsync();

            var afterRestart = await world.Repository.ResolveCurationPolicyAsync(null);
            Assert.Equal(5, afterRestart.MinimumPoseClasses);

            var reset = await world.Repository.ResetCurationPolicyToSeedAsync(null);
            Assert.Equal(3, reset.MinimumPoseClasses);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// The whole point of the item: the generator plans, it does not shoot. No member of its surface may
    /// offer to create the images, and its assembly must not name a batch primitive.
    /// </summary>
    [Fact]
    public void Generator_ExposesNoWayToGenerateTheImages()
    {
        var surface = typeof(ICharacterLoraCoveragePlanGenerator);
        var forbidden = new[] { "GenerateAll", "GenerateBatch", "Batch", "Sweep", "Run", "Enqueue", "EnqueueAll" };

        foreach (var member in surface.GetMethods())
        {
            Assert.DoesNotContain(forbidden, word => member.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Single(surface.GetMethods(), member => member.Name == nameof(ICharacterLoraCoveragePlanGenerator.GenerateAsync));

        var source = File.ReadAllText(SourcePath("DreamGenClone.Web/Application/RolePlay/CharacterLoraCoveragePlanGenerator.cs"));
        Assert.DoesNotMatch(new Regex(@"foreach\s*\(.*Render|GenerateAll|GenerateBatch|for\s*\(.*;\s*.*render", RegexOptions.IgnoreCase), source);
    }

    private static string SourcePath(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DreamGenClone.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative);
    }

    private sealed class World : IDisposable
    {
        private readonly string _dbPath;

        private World(string dbPath, ImageWorkflowRepository templates, CharacterLoraRepository repository)
        {
            _dbPath = dbPath;
            Repository = repository;
            Generator = new CharacterLoraCoveragePlanGenerator(repository, new ImageWorkflowTemplateService(templates));
        }

        public CharacterLoraRepository Repository { get; }

        public CharacterLoraCoveragePlanGenerator Generator { get; }

        public CurationPolicy Policy { get; private set; } = new()
        {
            ExpectedCoreCellCount = 30,
            ExpectedVariationCellCount = 6,
            SeedRangeStart = 41000,
            SeedRangeLength = 100,
            CloseUpAspect = "1024x1024",
            PortraitAspect = "832x1216",
            MinimumDistinctOutfits = 4,
            MinimumDistinctBackgrounds = 4,
            MinimumDistinctLighting = 4,
            MinimumDistinctExpressions = 4,
            MinimumPoseClasses = 3,
            WardrobeBalanceTolerancePercent = 12,
            MinimumTrainMembers = 24,
            MinimumValidationMembers = 6,
            NearDuplicateMaxSimilarity = 0.96,
            BodyInvariantDriftTolerancePercent = 12,
            PoseAdherenceMaxJointErrorPercent = 8
        };

        public string PackId => "pack-1";

        public static async Task<World> CreateAsync()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"lora-plan-{Guid.NewGuid():N}.db");
            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });

            var templates = new ImageWorkflowRepository(options);
            await templates.EnsureSchemaAsync();

            var repository = new CharacterLoraRepository(options);
            await repository.EnsureSchemaAsync();

            return new World(dbPath, templates, repository);
        }

        public CoveragePlanRequest Request() => new(
            "character-1", PackId, 9, CharacterImageIdentityPackScope.BodyComplete, "ohwx-becky", "biglust");

        public Task<CoveragePlan> GenerateAsync() => Generator.GenerateAsync(Request());

        public Task SavePolicyAsync(CurationPolicy policy, string? characterProfileId = null)
        {
            Policy = policy;
            return Repository.SaveCurationPolicyAsync(policy, characterProfileId);
        }

        public void WriteRawGlobalPolicy(string payload)
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWrite");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE CharacterLoraCurationPolicies SET PayloadJson = $payload WHERE Id = 'global';";
            command.Parameters.AddWithValue("$payload", payload);
            command.ExecuteNonQuery();
        }

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
