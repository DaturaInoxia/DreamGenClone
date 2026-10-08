using System.Text.RegularExpressions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Source-contract checks for B-156's storage-root discipline. A continuation's anchor frame lives under the
/// SCENE-VIDEO root while every other reference is a scene image, and a reference knows which root it belongs to.
/// Reading one by its bare path silently picked the scene-image root: a real continuation render failed with
/// <c>Could not find a part of the path '...\data\scene-images\&lt;clipId&gt;\frames\last.png'</c> after a full render
/// had been wasted, and the composer showed the anchor as a broken thumbnail. These tests pin the two call sites
/// that must keep using the reference's own root.
/// </summary>
public sealed class SceneVideoReferenceRootContractTests
{
    private static readonly string HandlerSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Application", "RolePlay", "SceneVideoRenderingJobHandler.cs"));

    private static readonly string ComposerSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "VideoStudio.razor"));

    [Fact]
    public void RenderingHandler_ReadsEveryReferenceFromTheRootItNames()
    {
        Assert.Contains("ReadReferenceBytesAsync(reference,", HandlerSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ReadReferenceBytesAsync(reference.FileRelativePath", HandlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderingHandler_MaterialisesTheGuideFromTheSceneVideoRoot()
    {
        // The guide is a continuation's anchor frame plus (for a carried track) the audio extracted beside it.
        Assert.Contains("record.SourceFrameRelativePath", HandlerSource, StringComparison.Ordinal);
        Assert.Contains("ReadSceneVideoFileAsync", HandlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_ServesTheAnchorFromTheSceneVideoRoot()
    {
        // The markup must route every reference through the root-aware helper, and the helper must map the
        // scene-video root to the scene-video URL rather than the scene-image one.
        Assert.Contains("src=\"@ReferenceUrl(reference)\"", ComposerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"@ImageUrl(reference.FileRelativePath)\"", ComposerSource, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"ReferenceUrl\(SceneVideoReference reference\)[\s\S]{0,220}SceneVideoReferenceStorageRoot\.SceneVideo[\s\S]{0,80}VideoUrl",
                RegexOptions.CultureInvariant),
            ComposerSource);
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
