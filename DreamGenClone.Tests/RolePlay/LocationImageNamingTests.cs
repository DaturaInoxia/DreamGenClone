using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// A location is a CONTAINER of several accepted images — four elevations, an interior — and the name an operator types
/// is the only thing that tells them apart in a reference picker. An id is not a label anybody can act on.
///
/// <para>
/// These tests hold the two halves of that rule: the name is written by its OWN writer, so no ordinary save of the image
/// row can erase it, and a location image cannot be approved for production without one, because an unnamed approved
/// image cannot be retrieved.
/// </para>
///
/// <para>
/// Reported live 2026-10-02: "i have an approved image and not images show in the location list to add as reference
/// image ... also i need to be able to name them, i am not using the GUID to know front or back."
/// </para>
/// </summary>
public sealed class LocationImageNamingTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>
    /// Which container types require a name. A location does; everything else keeps the contract it already had, so
    /// this change cannot reach into the face, body, wardrobe or pose flows.
    /// </summary>
    [Theory]
    [InlineData(SceneAssetType.Location, true)]
    [InlineData(SceneAssetType.Wardrobe, false)]
    [InlineData(SceneAssetType.Prop, false)]
    [InlineData(SceneAssetType.Style, false)]
    [InlineData(SceneAssetType.CharacterFace, false)]
    [InlineData(SceneAssetType.CharacterBody, false)]
    [InlineData(SceneAssetType.ProductionFrame, false)]
    [InlineData(SceneAssetType.Character, false)]
    [InlineData(SceneAssetType.CharacterPose, false)]
    [InlineData(SceneAssetType.Playground, false)]
    public void NameIsRequiredForALocationAndNothingElse(SceneAssetType type, bool expected)
        => Assert.Equal(expected, SceneAssetImageNaming.IsNameRequiredForApproval(type));

    /// <summary>
    /// The name survives an ordinary save of the row. This is the whole reason the writer is narrow: an edit stage
    /// upserting the image it just produced carries no name in memory, and a save that cleared the column would delete
    /// the operator's label silently — the kind of fact nobody notices going missing until the picker is unreadable again.
    /// </summary>
    [Fact]
    public async Task TheNameIsWrittenByItsOwnWriter_AndAnOrdinarySaveCannotEraseIt()
    {
        var repo = CreateRepo(out var dbPath);
        try
        {
            await SeedImageAsync(repo, "shed", SceneAssetType.Location, "img-1");

            var named = await repo.SetImageDisplayNameAsync("img-1", "  Left side  ");
            Assert.Equal("Left side", named.DisplayName);

            var loaded = (await repo.GetImageAsync("img-1"))!;
            loaded.DisplayName = null;
            await repo.UpsertImageAsync(loaded);

            Assert.Equal("Left side", (await repo.GetImageAsync("img-1"))!.DisplayName);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// A blank name is refused rather than stored as "cleared". An accepted location image with no name IS the state
    /// the column exists to prevent, so there is no path that produces one — and no default is invented to fill the gap.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankNameIsRefused(string blank)
    {
        var repo = CreateRepo(out var dbPath);
        try
        {
            await SeedImageAsync(repo, "shed", SceneAssetType.Location, "img-1");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.SetImageDisplayNameAsync("img-1", blank));

            Assert.Contains("name is required", error.Message, StringComparison.Ordinal);
            Assert.Null((await repo.GetImageAsync("img-1"))!.DisplayName);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// The gate itself: an unnamed location image cannot become a reference. The refusal names the image and says what
    /// to do, and leaves the row unapproved — nothing is approved "and then" fixed up.
    /// </summary>
    [Fact]
    public async Task Approval_RefusesAnUnnamedLocationImage_AndLeavesItUnapproved()
    {
        var repo = CreateRepo(out var dbPath);
        try
        {
            await SeedImageAsync(repo, "shed", SceneAssetType.Location, "img-1");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ApproveAsync(repo, "img-1"));

            Assert.Contains("has no name", error.Message, StringComparison.Ordinal);
            Assert.Contains("location reference", error.Message, StringComparison.Ordinal);
            Assert.Null((await repo.GetImageAsync("img-1"))!.ProductionApprovalStatus);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>A named location image approves exactly as any other image does.</summary>
    [Fact]
    public async Task Approval_AcceptsANamedLocationImage()
    {
        var repo = CreateRepo(out var dbPath);
        try
        {
            await SeedImageAsync(repo, "shed", SceneAssetType.Location, "img-1");
            await repo.SetImageDisplayNameAsync("img-1", "Front");

            var approved = await ApproveAsync(repo, "img-1");

            Assert.Equal(SceneAssetProductionApprovalStatus.Approved, approved.ProductionApprovalStatus);
            Assert.Equal(1, approved.ProductionVersion);
            Assert.Equal("Front", approved.DisplayName);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// A container that is not a location keeps the contract it had: wardrobe, face, body and pose approvals do not
    /// start demanding a name because locations do.
    /// </summary>
    [Fact]
    public async Task Approval_OfANonLocationImageWithoutAName_IsUnchanged()
    {
        var repo = CreateRepo(out var dbPath);
        try
        {
            await SeedImageAsync(repo, "dress", SceneAssetType.Wardrobe, "img-1");

            var approved = await ApproveAsync(repo, "img-1");

            Assert.Equal(SceneAssetProductionApprovalStatus.Approved, approved.ProductionApprovalStatus);
            Assert.Null(approved.DisplayName);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// The picker's own explanation has to mention the name requirement, or "Approve one in the Asset Manager" sends an
    /// operator to a form that then refuses them — the exact dead end this work came from.
    /// </summary>
    [Fact]
    public void TheEmptyPickerTellsALocationOperatorThatANameIsRequired()
    {
        var reason = ReferencePickerEmptyReason.Build(SceneAssetType.Location, characterProfileId: null, assetCount: 3);

        Assert.NotNull(reason);
        Assert.Contains("must be named before it can be approved", reason, StringComparison.Ordinal);
    }

    private static Task<SceneAssetImage> ApproveAsync(SceneAssetRepository repo, string imageId) =>
        repo.ApproveImageForProductionAsync(
            imageId,
            "{\"source\":\"test\"}",
            SceneAssetConsentState.Confirmed,
            SceneAssetLicenseState.Confirmed,
            "internal",
            SceneAssetApprovedUseScope.Location,
            "general",
            "{}");

    private static async Task SeedImageAsync(
        SceneAssetRepository repo, string assetId, SceneAssetType type, string imageId)
    {
        await repo.UpsertAsync(new SceneAsset
        {
            Id = assetId,
            Name = assetId,
            Kind = SceneAssetKind.Uploaded,
            Status = SceneAssetStatus.Complete,
            Type = type,
            IsContainerOnly = true
        });
        await repo.UpsertImageAsync(new SceneAssetImage
        {
            Id = imageId,
            AssetId = assetId,
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Complete,
            FileRelativePath = $"assets/{imageId}.png",
            MediaType = "image/png",
            Width = 1024,
            Height = 1024,
            ByteLength = 1024,
            Sha256 = Sha
        });
    }

    private static SceneAssetRepository CreateRepo(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"location-naming-{Guid.NewGuid():N}.db");
        var repo = new SceneAssetRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        }));
        // Touch the schema by running a read against a missing row (no row is inserted).
        repo.GetAsync("__schema_probe__").GetAwaiter().GetResult();
        return repo;
    }

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { /* best effort */ }
        }
    }
}
