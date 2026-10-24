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

        // Branding + the §5 rules, cited to research/qwen-2-1-prompt-enhancer.md §5/§8.
        Assert.Contains("Qwen-Image-2.1", system, StringComparison.Ordinal);
        Assert.Contains("unmistakable degree", system, StringComparison.Ordinal);          // disentanglement at full strength
        Assert.Contains("Anchor the instruction on the image", system, StringComparison.Ordinal); // anchor on the image
        Assert.Contains("Identity is the hardest invariant", system, StringComparison.Ordinal); // identity by reference
        Assert.Contains("confined to a region", system, StringComparison.Ordinal);         // region-confinement language

        // v5 (2026-10-15): the vendor's FRAMING — an instruction that leads with the operation — and preservation moved
        // out of the prose into the DATA, because asking the model to restate it demonstrably lost it (20 of 22 stored
        // edits were under 200 characters and most stated no preservation, while the same attempts recorded it
        // structurally).
        Assert.Contains("lead with the operation", system, StringComparison.Ordinal);
        Assert.Contains("Preservation is data, not prose", system, StringComparison.Ordinal);
        Assert.Contains("Do NOT write what stays unchanged into the compiled prompt", system, StringComparison.Ordinal);

        // …and the two further vendor rules N3 never picked up.
        Assert.Contains("Text rendered INTO the image is a separate decision", system, StringComparison.Ordinal);
        Assert.Contains("Never state a size, aspect ratio, or resolution", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// Measured 2026-10-13: "have the woman turn fully to the right" compiled to a near-verbatim echo of the user's
    /// own sentence (+2 characters), and the edit left the pose essentially unchanged, while a hand-written,
    /// image-anchored, whole-body instruction of the same intent rendered correctly. The user supplies the intent
    /// only, so the instruction must require a self-sufficient instruction rather than a restatement.
    /// </summary>
    [Fact]
    public void The21Instruction_RequiresASelfSufficientImageAnchoredInstruction()
    {
        var system = new QwenImage21EditPromptCompiler()
            .BuildMessages(new SceneImageEditCompilerContext("turn her to the right", [])).SystemMessage;

        Assert.Contains("standalone edit instruction", system, StringComparison.Ordinal);
        Assert.Contains("merely echoes or lightly rewords the user's sentence is a failed compilation", system, StringComparison.Ordinal);
        Assert.Contains("anchored to the image", system, StringComparison.Ordinal);
        Assert.Contains("toward the right edge of the image", system, StringComparison.Ordinal);
        Assert.Contains("head, face, shoulders, torso, hips, knees, and feet", system, StringComparison.Ordinal);
        Assert.Contains("compiledPrompt value is prose only", system, StringComparison.Ordinal);
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
        Assert.DoesNotContain("An identity application is a valid change", system, StringComparison.Ordinal);
        Assert.DoesNotContain("visibleLocator is a short place", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// Measured 2026-10-13: under v2 — which named identity unconditionally as a preserved category and treated any
    /// short compiled prompt as a failed compilation — every identity analysis of the Scene Image Editor's Identity
    /// tab compiled to <c>invalid</c> (8 attempts, 0 ready), because a request that delegates its detail to a
    /// reference image had no way to be ready. Verified against the configured compiler model on the same source
    /// image and the same intent: v1 3/3 ready, v2 3/3 invalid, v3 3/3 ready.
    /// </summary>
    [Fact]
    public void The21Instruction_AdmitsTheIdentityApplicationPath()
    {
        var system = new QwenImage21EditPromptCompiler()
            .BuildMessages(new SceneImageEditCompilerContext("apply the approved face identity to the visible people", [])).SystemMessage;

        Assert.Contains("An identity application is a valid change", system, StringComparison.Ordinal);
        Assert.Contains("the reference face is supplied to the image editor", system, StringComparison.Ordinal);
        Assert.Contains("delegates its detail to a reference image", system, StringComparison.Ordinal);
        Assert.Contains(
            "record the pose, expression, clothing, the objects they hold, and the setting as the `preserve` categories",
            system,
            StringComparison.Ordinal);

        // The v2 wording that closed the path off must be gone, not merely qualified elsewhere.
        Assert.DoesNotContain("What stays unchanged: the identity,", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// Measured 2026-10-13: the v3 analyses wrote a direction clause into <c>visibleLocator</c> ("on the image left")
    /// where v1 had written a bare place ("center-left"), and the editor identity instruction SPLICES that field into
    /// a sentence — so it read "the woman at on the image left", which the editor could not bind to a face, and the
    /// reference portrait was composited into the frame (the same feature produced a clean swap from a v1 analysis,
    /// with the SAME reference image and model).
    ///
    /// <para>
    /// The field's shape is stated in the instruction so the two halves of the contract cannot drift apart again.
    /// The wording was probed against the configured compiler model: every sample came back with a spliceable place
    /// ("center-left", "left of the frame", "left side of the frame"), the front-facing subject was classified
    /// <c>front</c>, and the pose intent still compiled to an EXPANDED instruction 3/3 — the paragraph form of this
    /// same rule cost that expansion 3/3, which is why it is one appended sentence and not a section.
    /// </para>
    /// </summary>
    [Fact]
    public void The21Instruction_FixesTheTargetingFieldsSurfacesSpliceIntoSentences()
    {
        var system = new QwenImage21EditPromptCompiler()
            .BuildMessages(new SceneImageEditCompilerContext("apply the approved face identity", [])).SystemMessage;

        Assert.Contains("visibleLocator is a short place that reads naturally after the word \"at\"", system, StringComparison.Ordinal);
        Assert.Contains("never a direction or a sentence of its own", system, StringComparison.Ordinal);
        Assert.Contains("region is the box around that person's face, not their whole body", system, StringComparison.Ordinal);
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

        // v5: the model's sentence is the CHANGE, and the application states what is HELD from the structured
        // `preserve` it already recorded — so the instruction the editor receives always carries both halves, whether
        // or not the prose mentioned preservation.
        Assert.Equal(
            "Remove the glasses from the woman on the left; keep her face and the room unchanged. "
            + "Hold the identity and setting at input fidelity. Change nothing else.",
            result.CompiledPrompt);
        Assert.Single(result.Targets);
    }

    /// <summary>
    /// The 2511 instruction states its own preservation in prose, so nothing is appended there: a second sentence
    /// would say it twice, and the two edit compilers must stay honest about which of them the model's prose is
    /// trusted for.
    /// </summary>
    [Fact]
    public void The2511Compiler_IsNotGivenTheAppendedPreservationClause()
    {
        const string response = """
            {
              "schemaVersion": "scene-image-edit-compiler-v1",
              "status": "ready",
              "sourceSummary": "a woman with glasses",
              "targets": [],
              "requestedChanges": ["remove the glasses"],
              "preserve": ["identity", "setting"],
              "clarificationQuestion": null,
              "invalidReason": null,
              "compiledPrompt": "Remove the glasses; keep her identity and the setting unchanged."
            }
            """;

        var result = new QwenSceneImageEditPromptCompiler().Parse(response);

        Assert.Equal("Remove the glasses; keep her identity and the setting unchanged.", result.CompiledPrompt);
    }
}
