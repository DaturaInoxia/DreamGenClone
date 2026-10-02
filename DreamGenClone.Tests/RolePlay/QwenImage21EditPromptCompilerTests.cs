using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B135-008 N3 — the Qwen-Image-2.1 edit compiler carries its own instruction content (route 1, grounded in the
/// vendor's edit prompt shape), distinct from the 2511 ordered-reference instruction. The 2.1 instruction adds
/// disentanglement-at-full-strength, anchor-on-image, affirmative preservation, identity-by-reference, and
/// region-confinement; the 2511 instruction is unchanged.
/// </summary>
public sealed class QwenImage21EditPromptCompilerTests
{
    [Fact]
    public void The21Instruction_ContainsThe21SpecificRules()
    {
        var system = new QwenImage21EditPromptCompiler().BuildMessages(new SceneImageEditCompilerContext("remove the glasses", [])).SystemMessage;

        // Branding + the five §5 rules N3 adds (cited to research/qwen-2-1-prompt-enhancer.md §5).
        Assert.Contains("Qwen-Image-2.1", system, StringComparison.Ordinal);
        Assert.Contains("unmistakable degree", system, StringComparison.Ordinal);          // disentanglement at full strength
        Assert.Contains("Anchor the instruction on the image", system, StringComparison.Ordinal); // anchor on the image
        Assert.Contains("keep X unchanged", system, StringComparison.Ordinal);             // preservation stated affirmatively
        Assert.Contains("do not change X", system, StringComparison.Ordinal);              // ... not as a prohibition
        Assert.Contains("Identity is the hardest invariant", system, StringComparison.Ordinal); // identity by reference
        Assert.Contains("confined to a region", system, StringComparison.Ordinal);         // region-confinement language
    }

    [Fact]
    public void The2511Instruction_DoesNotCarryThe21Rules()
    {
        // N3 is additive to the 2.1 compiler ONLY; the 2511 ordered-reference instruction text is unchanged.
        var system = new QwenSceneImageEditPromptCompiler().BuildMessages(new SceneImageEditCompilerContext("remove the glasses", [])).SystemMessage;

        Assert.DoesNotContain("unmistakable degree", system, StringComparison.Ordinal);
        Assert.DoesNotContain("Identity is the hardest invariant", system, StringComparison.Ordinal);
        Assert.DoesNotContain("confined to a region", system, StringComparison.Ordinal);
        Assert.DoesNotContain("keep X unchanged", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The21Compiler_StillSharesTheSchemaAndParseContract()
    {
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

        var result = new QwenImage21EditPromptCompiler().Parse(response);

        Assert.Equal(SceneImageEditCompilationResultStatus.Ready, result.Status);
        Assert.Equal("Remove the glasses from the woman on the left; keep her face and the room unchanged.", result.CompiledPrompt);
        Assert.Single(result.Targets);
    }
}
