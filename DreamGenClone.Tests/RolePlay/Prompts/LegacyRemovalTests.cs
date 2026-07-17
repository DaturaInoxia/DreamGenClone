using System.Reflection;
using DreamGenClone.Web.Application.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// Negative-assertion tests verifying legacy code has been fully removed.
/// SC-010: BuildPromptAsync is deleted from RolePlayContinuationService.
/// FR-028: IPromptInjector, SceneDirectionCoordinator, and PromptInjectionContext are deleted.
/// </summary>
public sealed class LegacyRemovalTests
{
    // ── SC-010: BuildPromptAsync deleted ───────────────────────

    [Fact]
    public void BuildPromptAsync_DoesNotExist_InContinuationService()
    {
        var type = typeof(RolePlayContinuationService);

        // Assert no method named "BuildPromptAsync" exists.
        var method = type.GetMethod("BuildPromptAsync",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

        Assert.Null(method);
    }

    [Fact]
    public void BuildPromptViaBuilderAsync_Exists_InContinuationService()
    {
        var type = typeof(RolePlayContinuationService);

        var method = type.GetMethod("BuildPromptViaBuilderAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
    }

    // ── FR-028: IPromptInjector deleted ────────────────────────

    [Fact]
    public void IPromptInjector_FileDoesNotExist()
    {
        // The IPromptInjector.cs file should be deleted.
        var filePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "DreamGenClone.Web", "Application", "RolePlay", "IPromptInjector.cs");

        // Normalize to check source path.
        var solutionRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));
        var fullPath = Path.Combine(solutionRoot,
            "DreamGenClone.Web", "Application", "RolePlay", "IPromptInjector.cs");

        Assert.False(File.Exists(fullPath),
            $"IPromptInjector.cs should be deleted but was found at {fullPath}");
    }

    [Fact]
    public void SceneDirectionCoordinator_FileDoesNotExist()
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));
        var fullPath = Path.Combine(solutionRoot,
            "DreamGenClone.Web", "Application", "RolePlay", "SceneDirectionCoordinator.cs");

        Assert.False(File.Exists(fullPath),
            $"SceneDirectionCoordinator.cs should be deleted but was found at {fullPath}");
    }

    [Fact]
    public void IPromptInjector_TypeIsNotLoadable()
    {
        // The IPromptInjector type should not be loadable.
        var injectorType = Type.GetType(
            "DreamGenClone.Web.Application.RolePlay.IPromptInjector, DreamGenClone.Web");

        Assert.Null(injectorType);
    }

    [Fact]
    public void SceneDirectionCoordinator_TypeIsNotLoadable()
    {
        var coordinatorType = Type.GetType(
            "DreamGenClone.Web.Application.RolePlay.SceneDirectionCoordinator, DreamGenClone.Web");

        Assert.Null(coordinatorType);
    }

    // ── Injectors directory is empty ───────────────────────────

    [Fact]
    public void InjectorsDirectory_IsEmpty()
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));
        var dirPath = Path.Combine(solutionRoot,
            "DreamGenClone.Web", "Application", "RolePlay", "Injectors");

        if (Directory.Exists(dirPath))
        {
            var files = Directory.GetFiles(dirPath, "*.cs");
            Assert.Empty(files);
        }
    }
}
