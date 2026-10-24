using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B135-008 N2 — the edit-prompt compiler is split by <see cref="ImageEditorGraphKind"/>. A merged/split-unet (2511)
/// editor resolves the ordered-reference compiler; a Qwen-Image-2.1 native editor resolves the 2.1 compiler; an
/// unconfigured or unknown graph kind fails fast. The two compilers stamp distinct system-prompt versions so an
/// attempt's persisted version names the compiler that compiled it.
/// </summary>
public sealed class SceneImageEditPromptCompilerResolverTests
{
    private static SceneImageEditPromptCompilerResolver NewResolver() =>
        new(new QwenSceneImageEditPromptCompiler(), new QwenImage21EditPromptCompiler());

    [Theory]
    [InlineData(ImageEditorGraphKind.MergedCheckpoint)]
    [InlineData(ImageEditorGraphKind.SplitUnet)]
    public void Resolve_2511Graphs_ReturnTheOrderedReferenceCompiler(ImageEditorGraphKind kind)
    {
        var resolver = NewResolver();

        var compiler = resolver.Resolve(kind);

        Assert.IsType<QwenSceneImageEditPromptCompiler>(compiler);
    }

    [Fact]
    public void Resolve_QwenImage21Native_ReturnsThe21Compiler()
    {
        var resolver = NewResolver();

        var compiler = resolver.Resolve(ImageEditorGraphKind.QwenImage21Native);

        Assert.IsType<QwenImage21EditPromptCompiler>(compiler);
    }

    [Fact]
    public void Resolve_NullGraphKind_FailsFastNamingTheFix()
    {
        var resolver = NewResolver();

        var error = Assert.Throws<InvalidOperationException>(() => resolver.Resolve((ImageEditorGraphKind?)null));

        Assert.Contains("no image editor graph kind", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Editor Graph", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_UnknownGraphKind_FailsFast()
    {
        var resolver = NewResolver();

        var error = Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve((ImageEditorGraphKind)99));

        Assert.Contains("no edit-prompt compiler", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTwoCompilers_StampDistinctSystemPromptVersions()
    {
        // The persisted SystemPromptVersion on an attempt must name WHICH compiler compiled it, so the two versions
        // can never be equal.
        Assert.NotEqual(
            QwenSceneImageEditPromptCompiler.SystemPromptVersion,
            QwenImage21EditPromptCompiler.SystemPromptVersion);
    }

    [Fact]
    public void EachCompiler_StampsItsOwnVersionOnBuiltMessages()
    {
        var context = new SceneImageEditCompilerContext("remove the glasses", []);

        var messages2511 = new QwenSceneImageEditPromptCompiler().BuildMessages(context);
        var messages21 = new QwenImage21EditPromptCompiler().BuildMessages(context);

        Assert.Equal(QwenSceneImageEditPromptCompiler.SystemPromptVersion, messages2511.SystemPromptVersion);
        Assert.Equal(QwenImage21EditPromptCompiler.SystemPromptVersion, messages21.SystemPromptVersion);
        Assert.NotEqual(messages2511.SystemPromptVersion, messages21.SystemPromptVersion);
    }

    [Fact]
    public void Resolve_EditorModel_QwenImage21Native_ReturnsThe21Compiler()
    {
        var resolver = NewResolver();
        var editor = EditorModel(ImageEditorGraphKind.QwenImage21Native, ImageProtocol.ComfyUi);

        var compiler = resolver.Resolve(editor);

        Assert.IsType<QwenImage21EditPromptCompiler>(compiler);
    }

    [Fact]
    public void Resolve_EditorModel_MergedCheckpoint_ReturnsThe2511Compiler()
    {
        var resolver = NewResolver();
        var editor = EditorModel(ImageEditorGraphKind.MergedCheckpoint, ImageProtocol.ComfyUi);

        var compiler = resolver.Resolve(editor);

        Assert.IsType<QwenSceneImageEditPromptCompiler>(compiler);
    }

    [Fact]
    public void Resolve_ServerlessEditor_NormalizesNullGraphKindToMerged()
    {
        // A serverless editor always runs the merged-checkpoint (2511) graph; its null graph kind is implicit-merged,
        // not "unset".
        var resolver = NewResolver();
        var editor = EditorModel(null, ImageProtocol.ComfyUiServerless);

        var compiler = resolver.Resolve(editor);

        Assert.IsType<QwenSceneImageEditPromptCompiler>(compiler);
    }

    [Theory]
    [InlineData(QwenSceneImageEditPromptCompiler.SystemPromptVersion, typeof(QwenSceneImageEditPromptCompiler))]
    [InlineData(QwenImage21EditPromptCompiler.SystemPromptVersion, typeof(QwenImage21EditPromptCompiler))]
    // Historical: an attempt queued before a 2.1 compiled-prompt contract change still resolves, so it fails with
    // the explicit "contract changed after this attempt was queued" diagnostic instead of "no compiler registered".
    [InlineData("qwen-edit-2.1-rules-v1", typeof(QwenImage21EditPromptCompiler))]
    [InlineData("qwen-edit-2.1-rules-v2", typeof(QwenImage21EditPromptCompiler))]
    [InlineData("qwen-edit-2.1-rules-v3", typeof(QwenImage21EditPromptCompiler))]
    public void ResolveByVersion_ReturnsTheCompilerThatStampedIt(string version, Type expectedType)
    {
        var compiler = NewResolver().ResolveByVersion(version);

        Assert.IsType(expectedType, compiler);
    }

    [Fact]
    public void ResolveByVersion_UnknownVersion_FailsFast()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => NewResolver().ResolveByVersion("qwen-edit-rules-v0"));

        Assert.Contains("no edit-prompt compiler", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ResolvedImageEditorModel EditorModel(ImageEditorGraphKind? graphKind, ImageProtocol protocol) => new(
        "http://localhost:8188", 120, null, "editor-model", "Local ComfyUI", ImageContentPolicy.AdultAllowed,
        "diffusion.safetensors", "text_encoder.safetensors", "vae.safetensors",
        8, 1.0, "euler", "simple", 1.0, 3.1, 1.0,
        ImageProtocol: protocol, GraphKind: graphKind);

    [Fact]
    public void BothCompilers_ShareTheSameSchemaAndParseContract()
    {
        // N2 is the structural split: the schema and response contract are still shared, and both compilers must parse
        // the same ready response to the same STRUCTURED result. What deliberately differs (v5, 2026-10-15) is the
        // assembled instruction: the 2.1 instruction records preservation in `preserve` and the application states it,
        // while the 2511 instruction states it in its own prose and gets nothing appended.
        const string response = """
            {
              "schemaVersion": "scene-image-edit-compiler-v1",
              "status": "ready",
              "sourceSummary": "a woman with glasses",
              "targets": [
                { "key": "person", "visibleLocator": "woman on image left", "headView": "front", "region": null }
              ],
              "requestedChanges": ["remove the glasses"],
              "preserve": ["identity", "setting"],
              "clarificationQuestion": null,
              "invalidReason": null,
              "compiledPrompt": "Remove the glasses from the woman on the left; keep her face and the room unchanged."
            }
            """;

        var via2511 = new QwenSceneImageEditPromptCompiler().Parse(response);
        var via21 = new QwenImage21EditPromptCompiler().Parse(response);

        Assert.Equal(via2511.Status, via21.Status);
        Assert.Single(via2511.Targets);
        Assert.Single(via21.Targets);
        Assert.Equal(via2511.Preserve, via21.Preserve);

        // The 2511 instruction is left exactly as the model wrote it…
        Assert.Equal(
            "Remove the glasses from the woman on the left; keep her face and the room unchanged.",
            via2511.CompiledPrompt);
        // …and the 2.1 instruction gains the sentence built from the same structured `preserve`.
        Assert.Equal(
            "Remove the glasses from the woman on the left; keep her face and the room unchanged. "
            + "Hold the identity and setting at input fidelity. Change nothing else.",
            via21.CompiledPrompt);
    }
}
